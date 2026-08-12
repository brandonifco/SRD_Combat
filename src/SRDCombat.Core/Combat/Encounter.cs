using System.Globalization;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>Why an action could not be taken.</summary>
public sealed record ActionRefusal(string Code, string Message)
{
    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>
/// A running fight: the battlefield, the combatants, whose turn it is, and everything
/// that has happened.
/// </summary>
/// <remarks>
/// <para>
/// The encounter owns its own randomness, which is why a fight is reproducible from a
/// seed and why the frozen-transcript tests can pin an exact narrated sequence.
/// </para>
/// <para>
/// Every action returns either the steps it produced or an <see cref="ActionRefusal"/>
/// naming why it was illegal. Nothing throws for an illegal move: a client offering the
/// player a choice needs to be able to ask "can I?" and get an answer it can show.
/// </para>
/// </remarks>
public sealed partial class Encounter
{
    private readonly List<Combatant> _combatants;
    private readonly List<CombatStep> _log = [];
    private readonly IRandomSource _random;
    private List<Combatant> _order = [];
    private int _turnIndex;

    private Encounter(Battlefield battlefield, IEnumerable<Combatant> combatants, IRandomSource random)
    {
        Battlefield = battlefield;
        _combatants = [.. combatants];
        _random = random;
    }

    public Battlefield Battlefield { get; }

    public IReadOnlyList<Combatant> Combatants => _combatants;

    /// <summary>Initiative order, highest first. Empty until the encounter starts.</summary>
    public IReadOnlyList<Combatant> TurnOrder => _order;

    /// <summary>Everything that has happened, in order.</summary>
    public IReadOnlyList<CombatStep> Log => _log;

    /// <summary>The current round, counting from 1.</summary>
    public int Round { get; private set; }

    /// <summary>Whose turn it is, or null before the fight starts or after it ends.</summary>
    public Combatant? ActiveCombatant =>
        IsComplete || _order.Count == 0 ? null : _order[_turnIndex];

    public bool IsComplete { get; private set; }

    /// <summary>The side still standing, once the fight is over.</summary>
    public string? WinningSide { get; private set; }

    /// <summary>Sets up a fight and rolls initiative.</summary>
    public static Encounter Start(Battlefield battlefield, IEnumerable<Combatant> combatants, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(random);

        var encounter = new Encounter(battlefield, combatants, random);

        if (encounter._combatants.Count == 0)
        {
            throw new ArgumentException("An encounter needs at least one combatant.", nameof(combatants));
        }

        encounter.RollInitiative();
        return encounter;
    }

    /// <summary>The combatants on a side who are still able to fight.</summary>
    public IEnumerable<Combatant> ActiveOn(string sideId) =>
        _combatants.Where(combatant => combatant.SideId == sideId && combatant.IsActive);

    /// <summary>Enemies of the given combatant who are still able to fight.</summary>
    public IEnumerable<Combatant> EnemiesOf(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        return _combatants.Where(other => other.SideId != combatant.SideId && other.IsActive);
    }

    /// <summary>Moves the active combatant to a square, resolving Opportunity Attacks on the way.</summary>
    public ActionRefusal? Move(GridPosition destination)
    {
        if (ActiveCombatant is not { } mover)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!mover.CanAct)
        {
            return new ActionRefusal("combatant.cannot_act", $"{mover.Name} cannot act.");
        }

        if (mover.HasCondition(ConditionType.Prone))
        {
            return new ActionRefusal("combatant.prone", $"{mover.Name} is Prone and must stand up first.");
        }

        if (ConditionRules.ImmobilisedBy(mover) is { } holding)
        {
            return new ActionRefusal(
                "combatant.speed_zero",
                $"{mover.Name} is {holding} and has a Speed of 0.");
        }

        // "You can't willingly move closer to the source of fear." Judged at the
        // destination — nearer the source than the square the mover stands in — not
        // along the path between them. The source is read as always within line of
        // sight; the reading is on ConditionRules.
        if (mover.ConditionState(ConditionType.Frightened) is { SourceId: { } fearSourceId }
            && _combatants.FirstOrDefault(combatant =>
                string.Equals(combatant.Id, fearSourceId, StringComparison.Ordinal)) is { } fearSource
            && destination.DistanceFeetTo(fearSource.Position) < mover.Position.DistanceFeetTo(fearSource.Position))
        {
            return new ActionRefusal(
                "movement.frightened",
                $"{mover.Name} is Frightened of {fearSource.Name} and cannot willingly move closer.");
        }

        var path = MovementRules.FindPath(Battlefield, mover, destination, mover.Turn.MovementFeet, _combatants);

        if (path is null)
        {
            return new ActionRefusal(
                "movement.unreachable",
                $"{destination} is not reachable with {mover.Turn.MovementFeet} ft. of movement.");
        }

        WalkPath(mover, path);

        // Walking away from a grappled creature can be what ends the grapple.
        EndBrokenGrapples();
        CheckForCompletion();
        return null;
    }

    /// <summary>Attacks a target with one of the active combatant's attacks.</summary>
    public ActionRefusal? Attack(string attackName, Combatant target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (ActiveCombatant is not { } attacker)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!attacker.CanAct)
        {
            return new ActionRefusal("combatant.cannot_act", $"{attacker.Name} cannot act.");
        }

        // Extra Attack and Multiattack are both modelled as one action buying several
        // attacks, rather than as several actions: the SRD grants "two attacks instead of
        // one" on the Attack action, and treating them as extra actions would also
        // wrongly allow a second Dodge or Dash.
        var attacksLeft = attacker.Features.AttacksRemainingThisAction;

        if (attacksLeft <= 0 && !attacker.Turn.HasAction)
        {
            return new ActionRefusal("action.spent", $"{attacker.Name} has already used its action.");
        }

        var attack = attacker.Stats.Attacks
            .FirstOrDefault(candidate => string.Equals(candidate.Name, attackName, StringComparison.Ordinal));

        if (attack is null)
        {
            return new ActionRefusal("attack.unknown", $"{attacker.Name} has no attack called '{attackName}'.");
        }

        // Only death refuses the attack: an Unconscious creature is a legal target,
        // and hitting one is how death saves fail.
        if (target.IsDead)
        {
            return new ActionRefusal("target.dead", $"{target.Name} is already dead.");
        }

        if (CharmedBy(attacker, target))
        {
            return new ActionRefusal(
                "attack.charmed",
                $"{attacker.Name} is Charmed by {target.Name} and cannot attack them.");
        }

        var distance = attacker.Position.DistanceFeetTo(target.Position);
        if (!attack.CanReach(distance))
        {
            return new ActionRefusal(
                "attack.out_of_range",
                $"{target.Name} is {distance} ft. away, beyond {attack.Name}'s reach.");
        }

        // A Multiattack names which attacks it is made of; anything else is a separate
        // action, reached through UseEntry rather than here.
        if (!attacker.Stats.AllowsInMultiattack(attack.Name))
        {
            return new ActionRefusal(
                "attack.not_in_multiattack",
                $"{attack.Name} is not part of {attacker.Name}'s Multiattack.");
        }

        // "(Recharge 5-6)" and "(3/Day)" gate an attack wherever it is used from — the
        // Minotaur's Gore is a plain attack with a recharge printed on it.
        if (CheckUsage(attacker, attack.Name) is { } unavailable)
        {
            return unavailable;
        }

        if (attacksLeft > 0)
        {
            attacker.Features.AttacksRemainingThisAction--;
        }
        else
        {
            attacker.Turn.SpendAction();
            attacker.Features.AttacksRemainingThisAction = Math.Max(0, attacker.Stats.AttacksPerAction - 1);
        }

        attacker.Uses.Spend(attack.Name);
        ResolveAttack(attacker, attack, target, isOpportunityAttack: false);
        CheckForCompletion();
        return null;
    }

    /// <summary>The Dodge action.</summary>
    public ActionRefusal? Dodge() => SpendActionOn(
        combatant =>
        {
            combatant.Turn.StartDodging();
            Add(CombatStepKind.Dodge, $"{combatant.Name} takes the Dodge action.", combatant);
        });

    /// <summary>The Dash action: movement equal to the creature's Speed, again.</summary>
    public ActionRefusal? Dash() => SpendActionOn(
        combatant =>
        {
            combatant.Turn.AddMovement(combatant.Stats.SpeedFeet);
            Add(
                CombatStepKind.Dash,
                $"{combatant.Name} Dashes, gaining {combatant.Stats.SpeedFeet} ft. of movement.",
                combatant);
        });

    /// <summary>The Disengage action: movement no longer provokes Opportunity Attacks this turn.</summary>
    public ActionRefusal? Disengage() => SpendActionOn(
        combatant =>
        {
            combatant.Turn.Disengage();
            Add(CombatStepKind.Disengage, $"{combatant.Name} Disengages.", combatant);
        });

    /// <summary>Stands up from Prone, spending half the creature's Speed.</summary>
    public ActionRefusal? StandUp()
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        // Without this a Prone creature that is Paralyzed — Incapacitated, but carrying
        // its own movement — could stand up while unable to act.
        if (!combatant.CanAct)
        {
            return new ActionRefusal("combatant.cannot_act", $"{combatant.Name} cannot act.");
        }

        if (!combatant.HasCondition(ConditionType.Prone))
        {
            return new ActionRefusal("combatant.not_prone", $"{combatant.Name} is not Prone.");
        }

        // Standing up costs half your Speed, and half of 0 is 0 — so without this the
        // arithmetic would let a grappled creature stand for free.
        if (ConditionRules.ImmobilisedBy(combatant) is { } holding)
        {
            return new ActionRefusal(
                "combatant.speed_zero",
                $"{combatant.Name} is {holding} and has a Speed of 0.");
        }

        var cost = MovementRules.StandUpCostFeet(combatant);

        if (combatant.Turn.MovementFeet < cost)
        {
            return new ActionRefusal(
                "movement.insufficient",
                $"{combatant.Name} needs {cost} ft. of movement to stand up.");
        }

        combatant.Turn.SpendMovement(cost);
        combatant.RemoveCondition(ConditionType.Prone);
        Add(CombatStepKind.Move, $"{combatant.Name} stands up.", combatant);
        return null;
    }

    /// <summary>
    /// Escapes a grapple: an action, and a Strength (Athletics) or Dexterity (Acrobatics)
    /// check against the grapple's escape DC.
    /// </summary>
    /// <remarks>
    /// The SRD lets the creature choose which of the two checks to make, so the engine
    /// takes the better one — the choice a player would always make, and stating it here
    /// beats leaving a coin-flip in the rules. This is the first ability check the engine
    /// rolls in a fight, which is why Poisoned's Disadvantage on ability checks finally
    /// has somewhere to apply.
    /// </remarks>
    public ActionRefusal? Escape()
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.CanAct)
        {
            return new ActionRefusal("combatant.cannot_act", $"{combatant.Name} cannot act.");
        }

        if (combatant.ConditionState(ConditionType.Grappled) is not { } grapple)
        {
            return new ActionRefusal("combatant.not_grappled", $"{combatant.Name} is not Grappled.");
        }

        if (grapple.EscapeDifficultyClass is not { } difficultyClass)
        {
            return new ActionRefusal(
                "grapple.no_escape_dc",
                $"The grapple on {combatant.Name} prints no escape DC, so it cannot be escaped.");
        }

        if (!combatant.Turn.HasAction)
        {
            return new ActionRefusal("action.spent", $"{combatant.Name} has already used its action.");
        }

        combatant.Turn.SpendAction();

        var athletics = SkillRules.BonusFor(combatant, "Athletics");
        var acrobatics = SkillRules.BonusFor(combatant, "Acrobatics");
        var useAthletics = athletics >= acrobatics;

        // Poisoned and Frightened both impose Disadvantage on ability checks —
        // Frightened's "while the source of fear is within line of sight", which the
        // engine reads as always; the reading is on ConditionRules.
        var hampered = combatant.HasCondition(ConditionType.Poisoned)
            || combatant.HasCondition(ConditionType.Frightened);
        var mode = hampered ? RollMode.Disadvantage : RollMode.Normal;
        var roll = D20Test.Roll(_random, Math.Max(athletics, acrobatics), mode);
        var escaped = roll.Total >= difficultyClass;

        Add(
            CombatStepKind.Condition,
            $"{combatant.Name} tries to escape the grapple with " +
            $"{(useAthletics ? "Strength (Athletics)" : "Dexterity (Acrobatics)")}: {roll} vs DC " +
            $"{difficultyClass} — {(escaped ? "free!" : "still held.")}",
            combatant);

        if (escaped)
        {
            EndGrapple(combatant, "escapes");
        }

        return null;
    }

    /// <summary>Ends the current turn and begins the next one, rolling any Death Saving Throws due.</summary>
    public void EndTurn()
    {
        if (IsComplete || ActiveCombatant is not { } combatant)
        {
            return;
        }

        EndRageIfUnsustained(combatant);
        ExpireConditions(combatant, ConditionClock.EndOfTurn);
        EndBrokenGrapples();
        Add(CombatStepKind.TurnEnded, $"{combatant.Name} ends their turn.", combatant);
        AdvanceTurn();
    }

    /// <summary>
    /// Ends every condition in the fight due to expire at this boundary of this
    /// combatant's turn.
    /// </summary>
    /// <remarks>
    /// Swept across every combatant rather than just the one whose turn it is, because a
    /// duration is measured against whoever the SRD names — "until the start of the
    /// devil's next turn" is a clock on the devil that runs out on somebody else.
    /// </remarks>
    private void ExpireConditions(Combatant owner, ConditionClock clock)
    {
        foreach (var bearer in _combatants)
        {
            foreach (var ended in bearer.ExpireConditions(owner.Id, clock, owner.TurnsBegun))
            {
                Add(
                    CombatStepKind.Condition,
                    $"{bearer.Name} is no longer {ended}.",
                    bearer);
            }
        }
    }

    /// <summary>
    /// Ends every grapple the SRD says has stopped holding: the grappler is Incapacitated
    /// or dead, or the two have been pulled further apart than the grapple's range.
    /// </summary>
    /// <remarks>
    /// Swept rather than raised as an event, and called from every point where either
    /// could have changed — a turn boundary, a blow landing, a creature walking away. A
    /// grapple that survives its grappler is the failure this exists to prevent, and it
    /// would be invisible: the victim simply never moves again.
    /// </remarks>
    private void EndBrokenGrapples()
    {
        foreach (var victim in _combatants)
        {
            if (victim.ConditionState(ConditionType.Grappled) is not { SourceId: { } grapplerId } grapple)
            {
                continue;
            }

            var grappler = _combatants.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, grapplerId, StringComparison.Ordinal));

            if (grappler is null)
            {
                continue;
            }

            var reason =
                grappler.IsDead || grappler.HasCondition(ConditionType.Incapacitated)
                    ? $"{grappler.Name} can no longer hold on"
                    : grapple.GrappleRangeFeet is { } range
                      && victim.Position.DistanceFeetTo(grappler.Position) > range
                        ? $"{grappler.Name} is too far away"
                        : null;

            if (reason is not null)
            {
                EndGrapple(victim, "is free", reason);
            }
        }
    }

    /// <summary>
    /// Releases a grapple and everything the SRD hangs off it lasting.
    /// </summary>
    /// <remarks>
    /// Restrained goes with it. Several stat blocks read "it has the Grappled condition
    /// ... and it has the Restrained condition until the grapple ends", so a grapple that
    /// ends while leaving the target Restrained would be worse than never grappling.
    /// </remarks>
    private void EndGrapple(Combatant victim, string verb, string? reason = null)
    {
        var grapplerId = victim.ConditionState(ConditionType.Grappled)?.SourceId;

        victim.RemoveCondition(ConditionType.Grappled);

        if (victim.ConditionState(ConditionType.Restrained) is { } restrained
            && string.Equals(restrained.SourceId, grapplerId, StringComparison.Ordinal))
        {
            victim.RemoveCondition(ConditionType.Restrained);
        }

        Add(
            CombatStepKind.Condition,
            reason is null
                ? $"{victim.Name} {verb} the grapple."
                : $"{victim.Name} {verb}: {reason}.",
            victim);
    }

    private ActionRefusal? SpendActionOn(Action<Combatant> apply)
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.CanAct)
        {
            return new ActionRefusal("combatant.cannot_act", $"{combatant.Name} cannot act.");
        }

        if (!combatant.Turn.HasAction)
        {
            return new ActionRefusal("action.spent", $"{combatant.Name} has already used its action.");
        }

        combatant.Turn.SpendAction();
        apply(combatant);
        return null;
    }

    /// <summary>
    /// Rolls initiative and fixes the turn order.
    /// </summary>
    /// <remarks>
    /// Ties are broken by the initiative bonus and then by combatant id, rather than by
    /// a further roll or by player choice. That is a divergence from the SRD, taken
    /// deliberately: the order has to be reproducible from the seed alone for the frozen
    /// transcripts to mean anything.
    /// </remarks>
    private void RollInitiative()
    {
        foreach (var combatant in _combatants)
        {
            combatant.SetInitiative(D20Test.Roll(_random, combatant.Stats.InitiativeBonus).Total);
        }

        _order = [.. _combatants
            .OrderByDescending(combatant => combatant.Initiative)
            .ThenByDescending(combatant => combatant.Stats.ModifierFor(Ability.Dexterity))
            .ThenBy(combatant => combatant.Id, StringComparer.Ordinal)];

        Add(CombatStepKind.EncounterStarted, "A fight breaks out!");

        foreach (var combatant in _order)
        {
            Add(
                CombatStepKind.EncounterStarted,
                $"{combatant.Name} rolls initiative: {combatant.Initiative}.",
                combatant);
        }

        Round = 1;
        _turnIndex = 0;
        Add(CombatStepKind.RoundStarted, "Round 1 begins.");
        BeginTurn();
    }

    /// <summary>
    /// Starts the active combatant's turn, resolving anything automatic first — a Death
    /// Saving Throw, or skipping a creature that cannot act at all.
    /// </summary>
    private void BeginTurn()
    {
        while (!IsComplete)
        {
            var combatant = _order[_turnIndex];

            // The clock ticks before anything else, and for every creature whose turn
            // comes round — dead, unconscious or fighting fit. A condition that reads
            // "until the start of the devil's next turn" has to end even if the devil is
            // in no state to take that turn, or it never ends at all.
            combatant.BeginTurnClock();
            ExpireConditions(combatant, ConditionClock.StartOfTurn);
            EndBrokenGrapples();

            // "At the start of each of the monster's turns, roll 1d6" — its turn starts
            // even when it cannot act, so the roll comes before those branches. A dead
            // creature's skipped turn rolls nothing: it will never use the ability again.
            if (!combatant.IsDead)
            {
                RollRecharges(combatant);
            }

            if (combatant.IsDead)
            {
                // A skipped turn begins and ends in the same instant.
                ExpireConditions(combatant, ConditionClock.EndOfTurn);

                if (!AdvanceIndex())
                {
                    return;
                }

                continue;
            }

            if (DeathSaveRules.MustRoll(combatant))
            {
                Add(CombatStepKind.TurnStarted, $"{combatant.Name}'s turn begins, at 0 hit points.", combatant);
                RollDeathSave(combatant);
                CheckForCompletion();

                if (IsComplete)
                {
                    return;
                }

                // A natural 20 puts the creature back on its feet with 1 hit point, and
                // it takes a normal turn from there. Anything else leaves it prone and
                // unconscious, so its turn is over.
                if (combatant.CanAct)
                {
                    combatant.Turn.BeginTurn(combatant.Stats.SpeedFeet);
                    return;
                }

                ExpireConditions(combatant, ConditionClock.EndOfTurn);

                if (!AdvanceIndex())
                {
                    return;
                }

                continue;
            }

            if (!combatant.CanAct)
            {
                Add(CombatStepKind.TurnStarted, $"{combatant.Name} cannot act.", combatant);
                ExpireConditions(combatant, ConditionClock.EndOfTurn);

                if (!AdvanceIndex())
                {
                    return;
                }

                continue;
            }

            combatant.Turn.BeginTurn(combatant.Stats.SpeedFeet);
            combatant.Features.BeginTurn();
            Add(CombatStepKind.TurnStarted, $"{combatant.Name}'s turn begins.", combatant);
            return;
        }
    }

    private void AdvanceTurn()
    {
        if (!AdvanceIndex())
        {
            return;
        }

        BeginTurn();
    }

    /// <summary>Moves to the next combatant, starting a new round when the order wraps.</summary>
    private bool AdvanceIndex()
    {
        _turnIndex++;

        if (_turnIndex < _order.Count)
        {
            return true;
        }

        _turnIndex = 0;
        Round++;
        Add(CombatStepKind.RoundStarted, $"Round {Round.ToString(CultureInfo.InvariantCulture)} begins.");
        return true;
    }

    private void RollDeathSave(Combatant combatant)
    {
        var result = DeathSaveRules.Roll(_random, combatant);

        var outcome = result switch
        {
            { RegainedConsciousness: true } => "a natural 20 — they regain 1 hit point and are conscious again!",
            { Died: true } => $"failure ({combatant.DeathSaveFailures}/3) — {combatant.Name} dies.",
            { BecameStable: true } => "success (3/3) — they are Stable.",
            { Succeeded: true } => $"success ({combatant.DeathSaveSuccesses}/3).",
            { Failures: 2 } => $"a natural 1 — two failures ({combatant.DeathSaveFailures}/3).",
            _ => $"failure ({combatant.DeathSaveFailures}/3).",
        };

        Add(
            CombatStepKind.DeathSave,
            $"{combatant.Name} makes a Death Saving Throw: {result.Roll} — {outcome}",
            combatant);

        if (result.BecameStable)
        {
            Add(CombatStepKind.Stabilized, $"{combatant.Name} is Stable.", combatant);
        }

        if (result.Died)
        {
            Add(CombatStepKind.Died, $"{combatant.Name} is dead.", combatant);
        }
    }

    /// <summary>Walks a path one square at a time so Opportunity Attacks land at the right moment.</summary>
    private void WalkPath(Combatant mover, MovementPath path)
    {
        var start = mover.Position;
        var travelled = 0;

        foreach (var step in path.Steps)
        {
            var from = mover.Position;

            // The SRD is precise that the Opportunity Attack "occurs right before it
            // leaves your reach", so the attack resolves while the mover is still in the
            // square it is leaving.
            var attackers = MovementRules.FindOpportunityAttackers(mover, from, step, _combatants);

            foreach (var attacker in attackers)
            {
                MakeOpportunityAttack(attacker, mover);

                if (!mover.CanAct)
                {
                    // Dropped mid-move: the rest of the route is not travelled.
                    Add(
                        CombatStepKind.Move,
                        $"{mover.Name} stops at {mover.Position}.",
                        mover);
                    return;
                }
            }

            travelled += Battlefield.EnterCostFeet(step);
            mover.MoveTo(step);
        }

        mover.Turn.SpendMovement(path.CostFeet);

        Add(
            CombatStepKind.Move,
            $"{mover.Name} moves from {start} to {mover.Position} ({path.CostFeet} ft.).",
            mover);

        _ = travelled;
    }

    /// <summary>Whether this creature is Charmed by that one, off the condition's source.</summary>
    private static bool CharmedBy(Combatant creature, Combatant other) =>
        creature.ConditionState(ConditionType.Charmed) is { SourceId: { } charmerId }
        && string.Equals(charmerId, other.Id, StringComparison.Ordinal);

    /// <summary>
    /// Refuses a damaging effect that would catch the actor's charmer — "you can't ...
    /// target the charmer with damaging abilities or magical effects". The clause
    /// heading is "Can't Harm the Charmer", so a non-damaging effect is allowed; the
    /// reading is on <c>ConditionRules</c>. A creature in a save's area is a target —
    /// the glossary defines a target as, among other things, a creature "forced to make
    /// a saving throw by an effect".
    /// </summary>
    private ActionRefusal? CharmedHarmRefusal(
        Combatant actor,
        string code,
        string effectName,
        SaveEffect save,
        GridPosition aim,
        Combatant? target)
    {
        if (save.FailureDamage.Count == 0
            || actor.ConditionState(ConditionType.Charmed) is not { SourceId: { } charmerId })
        {
            return null;
        }

        var victims = save.Area is { } area
            ? CreaturesIn(AreaTargeting.Cover(area, actor.Position, aim, Battlefield))
            : target is null ? [] : [target];

        var charmer = victims.FirstOrDefault(victim =>
            string.Equals(victim.Id, charmerId, StringComparison.Ordinal));

        return charmer is null
            ? null
            : new ActionRefusal(
                code,
                $"{actor.Name} is Charmed by {charmer.Name} and cannot catch them with {effectName}.");
    }

    private void MakeOpportunityAttack(Combatant attacker, Combatant mover)
    {
        // "You can't attack the charmer" — the printed rule names the attack, not the
        // action, so it forbids the Opportunity Attack too. The charmer walks away
        // unswung-at rather than the attack being refused: nothing was asked for.
        if (CharmedBy(attacker, mover))
        {
            return;
        }

        var attack = attacker.Stats.Attacks
            .Where(candidate => candidate.Kind == AttackKind.Melee)
            .Where(candidate => attacker.Uses.IsAvailable(candidate.Name))
            .OrderByDescending(candidate => candidate.Damage.Sum(damage => damage.Amount.Average))
            .FirstOrDefault();

        if (attack is null)
        {
            return;
        }

        attacker.Turn.SpendReaction();
        attacker.Uses.Spend(attack.Name);

        Add(
            CombatStepKind.OpportunityAttack,
            $"{mover.Name} leaves {attacker.Name}'s reach, provoking an Opportunity Attack.",
            attacker,
            mover);

        ResolveAttack(attacker, attack, mover, isOpportunityAttack: true);
    }

    private void ResolveAttack(Combatant attacker, CombatAttack attack, Combatant target, bool isOpportunityAttack)
    {
        // Reckless Attack cuts both ways: Advantage on the Barbarian's own melee attacks
        // this turn, and Advantage to anyone attacking the Barbarian until its next turn.
        var recklessAdvantage = attacker.Features.IsRecklessThisTurn && attack.Kind == AttackKind.Melee;
        var targetIsReckless = target.Features.IsRecklessThisTurn;

        // Pack Tactics: an ally able to fight within 5 feet of the target. On any attack
        // roll, Opportunity Attacks included — the printed rule names the roll, not the
        // action. Combined rather than overriding, so it still cancels Disadvantage.
        var packTactics = attacker.HasTrait(MonsterTrait.PackTactics)
            && _combatants.Any(ally => ally.SideId == attacker.SideId
                && ally != attacker
                && ally.IsActive
                && ally.Position.DistanceFeetTo(target.Position) <= Battlefield.FeetPerSquare);

        // Steady Aim is Advantage on the next attack roll only, so it is consumed here
        // whether the attack hits or not.
        var steadyAim = attacker.Features.SteadyAimedThisTurn;
        attacker.Features.SteadyAimedThisTurn = false;

        var result = AttackRules.Resolve(
            _random,
            attacker,
            attack,
            target,
            extraAdvantage: recklessAdvantage || targetIsReckless || packTactics || steadyAim);

        var modeNote = result.Roll.Mode switch
        {
            RollMode.Advantage => " with Advantage",
            RollMode.Disadvantage => " with Disadvantage",
            _ => string.Empty,
        };

        var verb = isOpportunityAttack ? "swings at" : "attacks";

        if (!result.Hit)
        {
            var reason = result.Roll.IsNatural1 ? " — a natural 1, an automatic miss" : string.Empty;

            Add(
                CombatStepKind.Attack,
                $"{attacker.Name} {verb} {target.Name} with {attack.Name}{modeNote}: {result.Roll} vs AC " +
                $"{result.TargetArmorClass} — miss{reason}.",
                attacker,
                target);
            return;
        }

        var criticalNote = result.Critical ? " — a Critical Hit!" : string.Empty;

        Add(
            CombatStepKind.Attack,
            $"{attacker.Name} {verb} {target.Name} with {attack.Name}{modeNote}: {result.Roll} vs AC " +
            $"{result.TargetArmorClass} — hit{criticalNote}",
            attacker,
            target);

        attacker.Features.AttackedThisTurn = true;

        // Uncanny Dodge is decided once for the attack, not once per damage component.
        var halvings = TryUncannyDodge(target) ? 1 : 0;

        var components = AttackRules.RollDamage(_random, attack, result).ToList();

        // Rage adds its bonus to Strength melee attacks. Applied to the first component
        // only: it is one bonus on the attack, not one per damage type.
        var rageBonus = attacker.Features.IsRaging && attack.Kind == AttackKind.Melee
            ? attacker.Stats.Character?.RageDamageBonus ?? 0
            : 0;

        if (SneakAttackApplies(attacker, attack, target, result))
        {
            attacker.Features.SneakAttackUsedThisTurn = true;

            var sneak = DiceRoller.Roll(
                _random,
                attacker.Stats.Character!.SneakAttackDamage!,
                result.Critical);

            components.Add((
                new AttackDamage(attacker.Stats.Character.SneakAttackDamage!, attack.Damage[0].Type, sneak.Total),
                sneak));

            Add(
                CombatStepKind.Feature,
                $"{attacker.Name} lands a Sneak Attack for an extra {sneak.Total} damage [{sneak}].",
                attacker,
                target);
        }

        var first = true;

        foreach (var (component, roll) in components)
        {
            var raw = roll.Total + (first ? rageBonus : 0);
            first = false;

            var rageHalving = RageResists(target, component.Type) ? 1 : 0;

            var applied = DamageRules.Apply(
                target,
                raw,
                component.Type,
                result.Critical,
                halvings + rageHalving);

            var responseNote = applied.Response switch
            {
                DamageResponse.Resistance => $" (halved by Resistance from {applied.Raw})",
                DamageResponse.Vulnerability => $" (doubled by Vulnerability from {applied.Raw})",
                DamageResponse.Immunity => " (Immune)",
                _ => RageResists(target, component.Type) ? " (halved by Rage)" : string.Empty,
            };

            Add(
                CombatStepKind.Damage,
                $"{target.Name} takes {applied.Effective} {component.Type} damage{responseNote} " +
                $"[{roll}] — {DescribeHealth(target)}.",
                attacker,
                target);

            CheckConcentration(target, applied.Effective);

            if (applied.DeathSaveFailures > 0)
            {
                Add(
                    CombatStepKind.DeathSave,
                    $"{target.Name} is hit while down: " +
                    $"{applied.DeathSaveFailures} Death Saving Throw failure(s) " +
                    $"({target.DeathSaveFailures}/3).",
                    target);
            }

            if (applied.Died)
            {
                Add(CombatStepKind.Died, $"{target.Name} is dead.", target);
                break;
            }

            if (applied.Downed)
            {
                Add(
                    CombatStepKind.Downed,
                    $"{target.Name} drops to 0 hit points and falls Unconscious.",
                    target);
                break;
            }
        }

        ImposeRiders(attacker, attack, target);

        // The blow may have dropped a grappler, in this fight or another one.
        EndBrokenGrapples();
    }

    /// <summary>
    /// Applies the conditions a hit imposes — "If the target is a Large or smaller
    /// creature, it has the Prone condition."
    /// </summary>
    /// <remarks>
    /// Only riders the model expresses in full reach this far: <see cref="CombatantStats"/>
    /// filters the rest out when it builds the attack, so what is left is a condition the
    /// engine executes and, at most, a size gate. The gate is checked here because it is
    /// the only part that depends on who was hit.
    /// </remarks>
    private void ImposeRiders(Combatant attacker, CombatAttack attack, Combatant target) =>
        // The grapple's range is the reach of the attack that made it, which is what
        // the SRD measures against when it asks whether the two have come apart.
        ImposeConditions(attacker, attack.AppliedConditions, target, attack.MaximumRangeFeet);

    /// <summary>
    /// Imposes every rider the engine can, from an attack's hit or a failed save.
    /// </summary>
    /// <param name="source">The creature imposing the conditions.</param>
    /// <param name="riders">The printed riders. Ones the engine cannot impose are skipped.</param>
    /// <param name="target">The creature they land on.</param>
    /// <param name="grappleRangeFeet">
    /// The range a Grappled rider is measured against when asking whether the grapple has
    /// broken by distance. Null when the effect prints none — an engulf-style grapple from
    /// a saving throw has no reach to measure, and ends only by escape or the grappler's
    /// incapacity.
    /// </param>
    private void ImposeConditions(
        Combatant source,
        IReadOnlyList<AppliedCondition> riders,
        Combatant target,
        int? grappleRangeFeet)
    {
        // A creature already down takes nothing further from the blow; Unconscious has
        // brought Prone with it already.
        if (riders.Count == 0 || !target.IsActive)
        {
            return;
        }

        foreach (var rider in riders)
        {
            if (!ConditionRules.CanImpose(rider, target))
            {
                continue;
            }

            var expiry = ConditionRules.ExpiryFor(rider.Duration, source, target);

            var imposed = new ActiveCondition(
                rider.Condition,
                source.Id,
                expiry,
                rider.EscapeDifficultyClass,
                rider.Condition == ConditionType.Grappled ? grappleRangeFeet : null);

            if (!target.AddCondition(imposed))
            {
                continue;
            }

            var escape = rider.EscapeDifficultyClass is { } dc ? $" (escape DC {dc})" : string.Empty;

            Add(
                CombatStepKind.Condition,
                $"{target.Name} has the {rider.Condition} condition{escape}" +
                $"{DescribeDuration(rider.Duration, source, target)}.",
                source,
                target);
        }
    }

    /// <summary>
    /// Resolves a saving-throw effect — a spell's or a stat block entry's — against every
    /// creature it reaches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One loop for both callers, deliberately: the roll, the halving, Restrained's
    /// Disadvantage on Dexterity saves, Concentration and dying are the same rules
    /// wherever the save came from, and two copies would drift.
    /// </para>
    /// <para>
    /// The riders are a parameter rather than read from <paramref name="save"/> because
    /// the two callers have decided differently: an entry imposes every rider the engine
    /// can execute, a spell passes none until executing spell conditions is its own
    /// decided piece of work. A rider lands on a failure — or either way when the printed
    /// outcome is "Failure or Success" — and carries no grapple range, because a save
    /// effect prints no reach to measure a grapple against.
    /// </para>
    /// </remarks>
    private void ResolveSaveEffect(
        Combatant source,
        string effectName,
        SaveEffect save,
        int difficultyClass,
        GridPosition point,
        Combatant? target,
        CombatStepKind kind,
        IReadOnlyList<AppliedCondition> riders)
    {
        var affected = save.Area is { } area
            ? CreaturesIn(AreaTargeting.Cover(area, source.Position, point, Battlefield))
            : target is null ? [] : [target];

        if (save.Area is { } shape)
        {
            Add(
                kind,
                $"{effectName} fills a {shape.SizeFeet}-foot {shape.Shape}, catching " +
                $"{affected.Count} creature(s).",
                source);
        }

        foreach (var victim in affected)
        {
            bool succeeded;

            // Paralyzed, Stunned and Unconscious print "You automatically fail Strength
            // and Dexterity saving throws". No die is rolled and none is consumed — the
            // clause replaces the roll rather than penalising it.
            if (ConditionRules.AutoFailingSaveCondition(victim, save.Ability) is { } autoFailing)
            {
                succeeded = false;

                Add(
                    kind,
                    $"{victim.Name} automatically fails the {save.Ability} saving throw ({autoFailing}).",
                    source,
                    victim);
            }
            else
            {
                // Restrained imposes Disadvantage on Dexterity saving throws, and on nothing
                // else — the ability matters, not just the condition. Magic Resistance is
                // Advantage against spells only: a stat block's save entry is read as not
                // magical, a reading recorded on MonsterTraitRegistry. Danger Sense is
                // Advantage on Dexterity saves unless the Barbarian is Incapacitated.
                // Combined, so Advantage and Disadvantage cancel rather than either winning.
                var restrained = save.Ability == Ability.Dexterity && victim.HasCondition(ConditionType.Restrained);
                var magicResistance = kind == CombatStepKind.Spell && victim.HasTrait(MonsterTrait.MagicResistance);
                var dangerSense = save.Ability == Ability.Dexterity
                    && victim.Stats.Has(ClassFeature.DangerSense)
                    && !victim.HasCondition(ConditionType.Incapacitated);
                var mode = D20Test.Combine(magicResistance || dangerSense, restrained);

                var roll = D20Test.Roll(_random, victim.Stats.SaveBonusFor(save.Ability), mode);
                succeeded = roll.Total >= difficultyClass;

                Add(
                    kind,
                    $"{victim.Name} makes a {save.Ability} saving throw: {roll} vs DC {difficultyClass} — " +
                    (succeeded ? "success." : "failure."),
                    source,
                    victim);
            }

            if (succeeded && save.SuccessOutcome == SaveSuccessOutcome.NoEffect)
            {
                continue;
            }

            foreach (var component in save.FailureDamage)
            {
                var rolled = DiceRoller.Roll(_random, component.Amount);

                // A successful save against a damaging effect halves it.
                var halvings = succeeded && save.SuccessOutcome == SaveSuccessOutcome.HalfDamage ? 1 : 0;
                var rageHalving = RageResists(victim, component.Type) ? 1 : 0;

                var applied = DamageRules.Apply(
                    victim,
                    rolled.Total,
                    component.Type,
                    fromCriticalHit: false,
                    halvings + rageHalving);

                Add(
                    CombatStepKind.Damage,
                    $"{victim.Name} takes {applied.Effective} {component.Type} damage" +
                    (halvings > 0 ? " (halved by a successful save)" : string.Empty) +
                    $" [{rolled}] — {DescribeHealth(victim)}.",
                    source,
                    victim);

                CheckConcentration(victim, applied.Effective);

                if (applied.Died)
                {
                    Add(CombatStepKind.Died, $"{victim.Name} is dead.", victim);
                    break;
                }

                if (applied.Downed)
                {
                    Add(CombatStepKind.Downed, $"{victim.Name} drops to 0 hit points and falls Unconscious.", victim);
                    break;
                }
            }

            if (!succeeded || save.SuccessOutcome == SaveSuccessOutcome.SameAsFailure)
            {
                ImposeConditions(source, riders, victim, grappleRangeFeet: null);
            }
        }

        // The damage may have dropped a grappler, in this fight or another one.
        EndBrokenGrapples();
    }

    /// <summary>Narrates a duration the way the SRD prints it.</summary>
    private static string DescribeDuration(ConditionDuration? duration, Combatant source, Combatant bearer) =>
        duration is null
            ? string.Empty
            : $" until the {(duration.Clock == ConditionClock.StartOfTurn ? "start" : "end")} of " +
              $"{(duration.Owner == ConditionDurationOwner.Bearer ? bearer.Name : source.Name)}'s next turn";

    private static string DescribeHealth(Combatant combatant) =>
        combatant.IsDead
            ? "dead"
            : $"{combatant.CurrentHitPoints}/{combatant.Stats.MaximumHitPoints} hit points";

    private void CheckForCompletion()
    {
        if (IsComplete)
        {
            return;
        }

        var standing = _combatants
            .Where(combatant => combatant.IsActive)
            .Select(combatant => combatant.SideId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (standing.Length > 1)
        {
            return;
        }

        IsComplete = true;
        WinningSide = standing.Length == 1 ? standing[0] : null;

        Add(
            CombatStepKind.EncounterEnded,
            WinningSide is null
                ? "The fight ends with nobody left standing."
                : $"The fight ends in victory for {WinningSide}.");
    }

    private void Add(CombatStepKind kind, string narration, Combatant? actor = null, Combatant? target = null) =>
        _log.Add(new CombatStep(kind, narration, actor?.Id, target?.Id));
}
