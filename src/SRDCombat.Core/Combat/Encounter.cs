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

    /// <summary>
    /// What ends this fight. <see cref="EncounterObjective.Defeat"/> — last side standing —
    /// unless one was passed to <see cref="Start"/>.
    /// </summary>
    public EncounterObjective Objective { get; private init; } = EncounterObjective.Defeat;

    /// <summary>
    /// The objective as one line, with the marked creature named.
    /// </summary>
    /// <remarks>
    /// Composed here because only the encounter can turn a leader's id into a leader's
    /// name, and two clients each doing that lookup would be two places for it to drift —
    /// the same reason <c>TurnBanner</c> and <c>ShopOffer.Effect</c> live where they do.
    /// </remarks>
    public string ObjectiveDescription => Objective.Describe(
        Objective.LeaderId is { } leaderId
            ? _combatants
                .FirstOrDefault(combatant => string.Equals(combatant.Id, leaderId, StringComparison.Ordinal))
                ?.Name
            : null);

    /// <summary>Sets up a fight and rolls initiative.</summary>
    /// <param name="battlefield">The ground it is fought over.</param>
    /// <param name="combatants">Everyone in it.</param>
    /// <param name="random">The dice, seeded so the fight replays.</param>
    /// <param name="objective">
    /// What ends it. Defaults to last-side-standing, which is what every fight was before
    /// objectives existed, so an unchanged caller gets unchanged behaviour.
    /// </param>
    public static Encounter Start(
        Battlefield battlefield,
        IEnumerable<Combatant> combatants,
        IRandomSource random,
        EncounterObjective? objective = null)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(random);

        var encounter = new Encounter(battlefield, combatants, random)
        {
            Objective = objective ?? EncounterObjective.Defeat,
        };

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
    /// <remarks>
    /// <paramref name="interrupt"/> is a caller's chance to halt the walk mid-route the moment
    /// a step reveals a threat — see <see cref="MovementInterrupt"/> for the full contract.
    /// Null — what every caller passes today — walks the whole path exactly as it always has;
    /// #495 adds the party's own clicked-move closure that supplies one.
    /// </remarks>
    public ActionRefusal? Move(GridPosition destination, MovementInterrupt? interrupt = null)
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
            && mover.SpaceAt(destination).DistanceFeetTo(fearSource.Space) < mover.DistanceFeetTo(fearSource))
        {
            return new ActionRefusal(
                "movement.frightened",
                $"{mover.Name} is Frightened of {fearSource.Name} and cannot willingly move closer.");
        }

        // The square is fine and the body is not: a creature asked into a gap its space
        // does not fit. Refused with its own code rather than folded into "unreachable",
        // because the two are different answers — one says walk further, the other says
        // you will never fit — and because SRD 5.2.1 prints no squeezing rule to fall
        // back on (see MovementRules.SpaceFits). Deliberately narrow: an anchor square
        // that is itself a wall or off the board stays "unreachable", exactly as it was
        // before spaces existed.
        if (Battlefield.IsPassable(destination)
            && !MovementRules.SpaceFits(Battlefield, mover.SpaceAt(destination)))
        {
            return new ActionRefusal(
                "movement.no_room",
                $"{mover.Name} is too big to fit at {destination}.");
        }

        var path = MovementRules.FindPath(Battlefield, mover, destination, mover.Turn.MovementFeet, _combatants);

        if (path is null)
        {
            return new ActionRefusal(
                "movement.unreachable",
                $"{destination} is not reachable with {mover.Turn.MovementFeet} ft. of movement.");
        }

        WalkPath(mover, path, interrupt);

        // Walking away from a grappled creature can be what ends the grapple.
        EndBrokenGrapples();
        ClearSharedSquares();
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

        var distance = attacker.DistanceFeetTo(target);
        if (!attack.CanReach(distance))
        {
            return new ActionRefusal(
                "attack.out_of_range",
                $"{target.Name} is {distance} ft. away, beyond {attack.Name}'s reach.");
        }

        // "Can't be targeted directly." Refused before anything is spent, like every
        // other targeting rule.
        if (CoverRules.AgainstSpace(Battlefield, attacker.Space, target.Space, _combatants) == CoverDegree.Total)
        {
            return new ActionRefusal(
                "attack.total_cover",
                $"{target.Name} has Total Cover from {attacker.Name} and can't be targeted directly.");
        }

        // A Multiattack names which attacks it is made of; anything else is a separate
        // action, reached through UseEntry rather than here.
        if (!attacker.Stats.AllowsInMultiattack(attack.Name))
        {
            return new ActionRefusal(
                "attack.not_in_multiattack",
                $"{attack.Name} is not part of {attacker.Name}'s Multiattack.");
        }

        // A printed enumerated composition ("one Bite attack and one Claw attack",
        // issue #343) binds each name to its own exact count — membership above is not
        // enough, because it says nothing about a second Bite in the Claw's place.
        // Refused before anything is spent, like every other targeting/usage rule here.
        //
        // Deliberately placed after the "attacksLeft <= 0 && !HasAction" action.spent
        // check further up, not before it: MultiattackEffect's own invariant
        // (Composition's component counts sum to AttackCount, which is
        // CombatantStats.AttacksPerAction — see EntryEffects.cs and
        // EntryMechanicsTests.EveryEnumeratedCompositionSatisfiesItsInvariants) makes
        // "a capped name attempted with zero action budget left" unreachable through
        // this branch by construction — the whole action is always spent at the exact
        // moment the composition's last slot fills, so that state hits action.spent
        // first, which is the truer refusal (there is no action left at all,
        // independent of composition). This code exists to refuse one thing only:
        // repeating an already-capped name while budget remains for a name that has
        // not been used yet — the exact substitution the printed enumeration forbids.
        // If this branch is ever observed firing at zero remaining budget, the
        // invariant above has broken, not this guard.
        if (attacker.Stats.MultiattackCapFor(attack.Name) is { } cap
            && attacker.Features.MultiattackSwingsThisAction.GetValueOrDefault(attack.Name) >= cap)
        {
            return new ActionRefusal(
                "attack.composition_exhausted",
                $"{attacker.Name} has already made all its {attack.Name} attacks this turn.");
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
            attacker.Features.MultiattackSwingsThisAction.Clear();
        }

        attacker.Features.MultiattackSwingsThisAction[attack.Name] =
            attacker.Features.MultiattackSwingsThisAction.GetValueOrDefault(attack.Name) + 1;

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
            // Dash grants "extra movement equal to your Speed", and a Slowed creature's
            // Speed is the reduced one — so the mastery's 10 feet costs a Dasher 20.
            combatant.Turn.AddMovement(EffectiveSpeedFeet(combatant));
            Add(
                CombatStepKind.Dash,
                $"{combatant.Name} Dashes, gaining {EffectiveSpeedFeet(combatant)} ft. of movement.",
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

        // Tactical Mind turns a failed ability check around, and this is the only
        // ability check a fight rolls.
        if (!escaped)
        {
            escaped = TryTacticalMind(combatant, roll.Total, difficultyClass);
        }

        if (escaped)
        {
            EndGrapple(combatant, "escapes");
        }

        return null;
    }

    /// <summary>
    /// Fighter Tactical Mind: spend a use of Second Wind to add 1d10 to a failed ability
    /// check, keeping the use if it still fails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Rather than regaining Hit Points, you roll 1d10 and add the number rolled to the
    /// ability check, potentially turning it into a success. If the check still fails,
    /// this use of Second Wind isn't expended." That last sentence is the whole feature —
    /// a use is only spent on a success — and it is why the die is rolled before the
    /// resource is touched.
    /// </para>
    /// <para>
    /// Taken automatically rather than offered, like Uncanny Dodge: a failed escape with
    /// a spare Second Wind is never a case where a player would decline, and the use
    /// costs nothing when it does not work. Narrow by construction — the engine rolls
    /// exactly one ability check in combat, and this hooks it. Any future check should
    /// call this too.
    /// </para>
    /// </remarks>
    private bool TryTacticalMind(Combatant combatant, int checkTotal, int difficultyClass)
    {
        if (!combatant.Stats.Has(ClassFeature.TacticalMind) || combatant.Features.SecondWindRemaining <= 0)
        {
            return false;
        }

        var boost = DiceRoller.Roll(_random, new DiceExpression(1, 10, 0));
        var total = checkTotal + boost.Total;
        var succeeded = total >= difficultyClass;

        if (succeeded)
        {
            combatant.Features.SecondWindRemaining--;
        }

        Add(
            CombatStepKind.Feature,
            $"{combatant.Name} uses Tactical Mind: {boost} takes the check to {total} vs DC " +
            $"{difficultyClass} — " +
            (succeeded
                ? $"success ({combatant.Features.SecondWindRemaining} Second Wind use(s) left)."
                : "still short, so the Second Wind use is not expended."),
            combatant);

        return succeeded;
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
        RollRepeatSaves(combatant);

        // Vex lasts "before the end of your next turn": an unspent one dies at the end
        // of a turn *after* the one that earned it — never at the end of the earning
        // turn itself, which is the off-by-one that starved a single-attack Rogue's
        // Sneak Attack of the Advantage its own bow had just bought (#153).
        if (combatant.Features.VexedTargetId is not null
            && combatant.TurnsBegun > combatant.Features.VexEarnedOnTurn)
        {
            combatant.Features.VexedTargetId = null;
        }

        // Guiding Bolt's light runs on the same stamped clock, measured against its
        // author: it dies at the end of the caster's next turn, wherever it landed.
        foreach (var victim in _combatants.Where(c => c.Features.GuidedBy == combatant.Id
            && combatant.TurnsBegun > c.Features.GuidedOnAuthorTurn))
        {
            victim.Features.GuidedBy = null;
        }

        EndBrokenGrapples();
        EndTurnEffectsWhoseSourceIsDown();
        ClearSharedSquares();
        Add(CombatStepKind.TurnEnded, $"{combatant.Name} ends their turn.", combatant);

        // The objective catch-all, and deliberately *only* the objective half. Move and
        // Attack ask after themselves but the casting and entry paths never did, so a
        // Sacred Flame that kills the marked leader would otherwise leave the fight
        // running until somebody swung or the round rolled over.
        //
        // Asking the whole of CheckForCompletion here instead is wrong, and the test that
        // caught it is worth keeping in mind: a party whose last member is at 0 hit points
        // is already "not standing", so a full check at this boundary ends the fight
        // before that character's turn begins — and their turn is where the Death Saving
        // Throw is rolled, including the natural 20 that puts them back on their feet.
        // The last-side-standing rule keeps its existing call sites and its existing
        // timing; only the objective is asked early.
        CheckObjectiveMet();

        if (IsComplete)
        {
            return;
        }

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
    /// Rolls the saves a condition lets its bearer repeat: "at the end of each of its
    /// turns, the target repeats the save, ending the spell on itself on a success."
    /// </summary>
    /// <remarks>
    /// Only the creature whose turn is ending rolls — the printed wording counts the
    /// bearer's own turns. The auto-failure clause is honoured before any die: a
    /// Paralyzed creature repeating a Strength or Dexterity save fails without a roll,
    /// which is why Hold Person prints a Wisdom save and stays escapable. Beyond that
    /// the repeat is a plain save — none of the situational modifiers the original
    /// effect rolled under (cover, Dodge) apply to a creature standing in its own
    /// square at the end of its own turn.
    /// </remarks>
    private void RollRepeatSaves(Combatant bearer)
    {
        foreach (var condition in bearer.Conditions
                     .Select(bearer.ConditionState)
                     .OfType<ActiveCondition>()
                     .Where(active => active.RepeatSaveDifficultyClass is not null)
                     .ToArray())
        {
            var ability = condition.RepeatSaveAbility!.Value;
            var difficultyClass = condition.RepeatSaveDifficultyClass!.Value;

            if (ConditionRules.AutoFailingSaveCondition(bearer, ability) is { } autoFailing)
            {
                Add(
                    CombatStepKind.Condition,
                    $"{bearer.Name} automatically fails the {ability} saving throw to shake off " +
                    $"{condition.Condition} ({autoFailing}).",
                    bearer);
                Escalate(bearer, condition);
                continue;
            }

            var roll = D20Test.Roll(_random, bearer.Stats.SaveBonusFor(ability));

            if (roll.Total >= difficultyClass && bearer.RemoveCondition(condition.Condition))
            {
                Add(
                    CombatStepKind.Condition,
                    $"{bearer.Name} repeats the {ability} saving throw: {roll} vs DC {difficultyClass} — " +
                    $"success; no longer {condition.Condition}.",
                    bearer);
            }
            else
            {
                Add(
                    CombatStepKind.Condition,
                    $"{bearer.Name} repeats the {ability} saving throw: {roll} vs DC {difficultyClass} — " +
                    $"still {condition.Condition}.",
                    bearer);
                Escalate(bearer, condition);
            }
        }
    }

    /// <summary>
    /// Deepens a condition whose repeated save just failed — the two-tier gaze's
    /// "Second Failure: The target has the Petrified condition instead of the
    /// Restrained condition."
    /// </summary>
    /// <remarks>
    /// "Instead of" is executed literally: the first condition is removed and the
    /// deeper one imposed, keeping the source. The deeper condition carries no expiry
    /// and no repeat of its own — the printed Petrified has no end a fight reaches, so
    /// it lasts until the encounter does, the same reading every outlasting duration
    /// gets. Resolving the repeat either way is also why "at the end of its next turn"
    /// needs no one-shot bookkeeping: success ends the effect, failure replaces it,
    /// and either way there is nothing left to repeat.
    /// </remarks>
    private void Escalate(Combatant bearer, ActiveCondition condition)
    {
        if (condition.EscalatesTo is not { } deeper || !bearer.RemoveCondition(condition.Condition))
        {
            return;
        }

        bearer.AddCondition(new ActiveCondition(deeper, condition.SourceId));

        Add(
            CombatStepKind.Condition,
            $"{bearer.Name} has the {deeper} condition instead of {condition.Condition}.",
            bearer);

        // The deeper condition can be the first thing to bring Incapacitated with it
        // (Restrained escalating to Petrified) — glossary p.186 ends Concentration the
        // instant that lands, not on a save.
        BreakConcentrationOnIncapacitated(bearer);
    }

    /// <summary>
    /// Ends every condition that lived only while this caster concentrated — Hold
    /// Person's Paralyzed goes when the Concentration goes, however it went.
    /// </summary>
    internal void SweepConcentrationConditions(Combatant caster)
    {
        foreach (var bearer in _combatants)
        {
            foreach (var type in bearer.Conditions.ToArray())
            {
                if (bearer.ConditionState(type) is { TiedToConcentration: true } active
                    && string.Equals(active.SourceId, caster.Id, StringComparison.Ordinal)
                    && bearer.RemoveCondition(type))
                {
                    Add(
                        CombatStepKind.Condition,
                        $"{bearer.Name} is no longer {type} — the spell holding it has ended.",
                        bearer);
                }
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
    /// <summary>
    /// Restores the one-able-creature-per-square invariant after somebody comes round
    /// underneath somebody else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The price of the house rule in <see cref="MovementRules.FindPath"/>. A move may
    /// finish on a downed creature, so a downed creature can be healed while another
    /// stands in its square — and two able creatures in one square is the exact state
    /// that took down two of sixty seeded runs when occupancy was last read as "active".
    /// This is what the play session meant by "the character standing on top should just
    /// be moved to the nearest viable location".
    /// </para>
    /// <para>
    /// <b>Who stays is a stated reading.</b> The creature with the fewest hit points
    /// keeps the square, ties broken on identifier so a seed always replays. In practice
    /// that is the one who just came round — a revived creature is at 1 hit point and
    /// the one standing over it is not — which puts the displacement on the character
    /// who chose to stand there, as asked. It is a reading rather than a derivation
    /// because the encounter does not record who entered a square first, and inventing
    /// that bookkeeping to serve one sweep would cost more than the tie-break does.
    /// </para>
    /// <para>
    /// The step is narrated rather than silent: a token moving on its own is otherwise
    /// indistinguishable from a bug, and this engine has no other rule that relocates a
    /// creature outside its own turn. Displacement is free — it spends no movement and
    /// provokes no Opportunity Attack, because the creature did not choose to go.
    /// </para>
    /// <para>
    /// <b>Crowding is overlap, not a shared coordinate.</b> Two spaces that share any
    /// square are crowded, so a Large creature standing on a corner of another's body
    /// counts exactly as two Medium creatures on one square do. The clusters are grown
    /// by scanning the combatants in their own order, which at one square per creature
    /// reproduces the position grouping this used to do — same clusters, same order,
    /// same creature staying — and generalizes to bodies that can overlap partially.
    /// </para>
    /// </remarks>
    private void ClearSharedSquares()
    {
        var crowded = OverlapClusters(_combatants
            .Where(combatant => !combatant.IsDead
                && !combatant.HasCondition(ConditionType.Incapacitated)))
            .Where(cluster => cluster.Count > 1)
            .ToArray();

        foreach (var group in crowded)
        {
            var stays = group
                .OrderBy(combatant => combatant.CurrentHitPoints)
                .ThenBy(combatant => combatant.Id, StringComparer.Ordinal)
                .First();

            foreach (var displaced in group.Where(c => !ReferenceEquals(c, stays)))
            {
                if (NearestFreeAnchor(displaced) is not { } square)
                {
                    // Nowhere to put them. Leaving the square shared is survivable —
                    // FindPath keys its blockers as a lookup precisely so that this is
                    // not fatal — and is better than throwing inside a fight.
                    continue;
                }

                displaced.MoveTo(square);

                Add(
                    CombatStepKind.Move,
                    $"{stays.Name} comes round, and {displaced.Name} steps aside.",
                    displaced);
            }
        }
    }

    /// <summary>
    /// Groups creatures whose spaces overlap, in the order they appear.
    /// </summary>
    /// <remarks>
    /// Grown by scanning rather than by keying, because overlap is not an equivalence
    /// relation once a space can be several squares: A can overlap B and B overlap C
    /// with A and C apart, and all three still have to be sorted out together. At one
    /// square per creature it reduces to grouping by position, clusters and their
    /// contents both in source order — which is what keeps the displacement sweep
    /// byte-identical while every creature is Medium.
    /// </remarks>
    private static IReadOnlyList<List<Combatant>> OverlapClusters(IEnumerable<Combatant> combatants)
    {
        var clusters = new List<List<Combatant>>();

        foreach (var combatant in combatants)
        {
            var joined = clusters
                .Where(cluster => cluster.Any(member => member.Space.Overlaps(combatant.Space)))
                .ToArray();

            if (joined.Length == 0)
            {
                clusters.Add([combatant]);
                continue;
            }

            joined[0].Add(combatant);

            // A creature can bridge two clusters that did not touch each other.
            foreach (var merged in joined.Skip(1))
            {
                joined[0].AddRange(merged);
                clusters.Remove(merged);
            }
        }

        return clusters;
    }

    /// <summary>
    /// The closest square this creature's whole space fits in without overlapping
    /// anybody, searched outward so the displaced creature moves as little as possible.
    /// </summary>
    /// <remarks>
    /// The search itself walks square by square — a candidate anchor is reached through
    /// passable ground — while acceptance asks the whole footprint: in bounds, passable,
    /// and clear of every living creature's space. The creature's own squares are not
    /// counted against it, so a Large creature displaced by one square is not blocked by
    /// the half of its body it is already standing in.
    /// </remarks>
    private GridPosition? NearestFreeAnchor(Combatant displaced)
    {
        var taken = _combatants
            .Where(combatant => !combatant.IsDead && !ReferenceEquals(combatant, displaced))
            .SelectMany(combatant => combatant.Space.Squares())
            .ToHashSet();

        var from = displaced.Position;
        var seen = new HashSet<GridPosition> { from };
        var queue = new Queue<GridPosition>();
        queue.Enqueue(from);

        while (queue.TryDequeue(out var current))
        {
            // Ordered so the search is deterministic whatever the neighbour order is.
            foreach (var next in current.Neighbours()
                .Where(seen.Add)
                .OrderBy(square => square.X)
                .ThenBy(square => square.Y))
            {
                if (!Battlefield.IsPassable(next))
                {
                    continue;
                }

                var space = displaced.SpaceAt(next);

                if (MovementRules.SpaceFits(Battlefield, space)
                    && !space.Squares().Any(taken.Contains))
                {
                    return next;
                }

                queue.Enqueue(next);
            }
        }

        return null;
    }

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
                      && victim.DistanceFeetTo(grappler) > range
                        ? $"{grappler.Name} is too far away"
                        : null;

            if (reason is not null)
            {
                EndGrapple(victim, "is free", reason);
            }
        }
    }

    /// <summary>
    /// Ends every Turn Undead effect whose imposing Cleric is now Incapacitated or
    /// dead — the two source-side early-outs printed alongside the bearer's own damage
    /// out: "This effect ends early on the creature ... if you have the Incapacitated
    /// condition, or if you die" (SRD 5.2.1 p. 37).
    /// </summary>
    /// <remarks>
    /// The same shape <see cref="EndBrokenGrapples"/> already reads off a grapple's
    /// source, generalised from one condition (Grappled) to every condition flagged
    /// <see cref="ActiveCondition.EndsEarlyOnDamageOrSourceDown"/>. Swept from the same
    /// boundaries: <see cref="EndTurn"/>, and after each of the five sites that can
    /// apply damage — a Cleric downed or killed mid-round should free anything it
    /// turned promptly, not merely at its own next turn boundary.
    /// </remarks>
    private void EndTurnEffectsWhoseSourceIsDown()
    {
        foreach (var bearer in _combatants)
        {
            foreach (var type in bearer.Conditions.ToArray())
            {
                if (bearer.ConditionState(type) is not { EndsEarlyOnDamageOrSourceDown: true, SourceId: { } sourceId })
                {
                    continue;
                }

                var source = _combatants.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, sourceId, StringComparison.Ordinal));

                if (source is null || (!source.IsDead && !source.HasCondition(ConditionType.Incapacitated)))
                {
                    continue;
                }

                if (bearer.RemoveCondition(type))
                {
                    Add(
                        CombatStepKind.Condition,
                        $"{bearer.Name} is no longer {type} — {source.Name} can no longer hold the turning.",
                        bearer);
                }
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
            ExpireSapsFrom(combatant);
            EndBrokenGrapples();
        ClearSharedSquares();

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
                    combatant.Turn.BeginTurn(EffectiveSpeedFeet(combatant));
                    return;
                }

                ExpireConditions(combatant, ConditionClock.EndOfTurn);
                RollRepeatSaves(combatant);

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

                // The skipped turn still ends, and "at the end of each of its turns"
                // counts it — this is the canonical case, since the creature this
                // clause frees is exactly the one whose turns are all skips.
                RollRepeatSaves(combatant);

                if (!AdvanceIndex())
                {
                    return;
                }

                continue;
            }

            combatant.Turn.BeginTurn(EffectiveSpeedFeet(combatant));
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

        // A survival objective is met by time passing rather than by anything anybody
        // does, so it is the one completion check no action can trigger: without this the
        // fight would run on until somebody landed a hit and CheckForCompletion was asked
        // for another reason.
        CheckForCompletion();

        if (IsComplete)
        {
            return false;
        }

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
            combatant.RecordDeathRound(Round);
            Add(CombatStepKind.Died, $"{combatant.Name} is dead.", combatant);
        }
    }

    /// <summary>Walks a path one square at a time so Opportunity Attacks land at the right moment.</summary>
    /// <remarks>
    /// <paramref name="interrupt"/> is consulted after each non-final step, once the mover has
    /// actually entered the square (so a client's fog recompute sees the mover's new position)
    /// — see <see cref="MovementInterrupt"/>. When it returns a hostile, the walk stops there:
    /// only the feet actually walked are spent, mirroring the Opportunity-Attack-drop stop
    /// immediately above it, and the remaining planned squares are never travelled.
    /// </remarks>
    private void WalkPath(Combatant mover, MovementPath path, MovementInterrupt? interrupt = null)
    {
        var start = mover.Position;
        var travelled = 0;

        // The squares actually occupied, start first, recorded onto the Move step so a
        // client can show the walk without recomputing the route.
        var walked = new List<GridPosition> { start };

        // A throwing delegate is consulted no further this walk — see the try/catch below.
        var interruptFaulted = false;

        for (var i = 0; i < path.Steps.Count; i++)
        {
            var step = path.Steps[i];
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
                        mover,
                        path: walked);
                    return;
                }
            }

            // `from`, not mover.Position, prices this step — see MovementRules.StepCostFeet's
            // remarks. They agree here (this runs before MoveTo), but the local stays correct
            // if that ever changes.
            travelled += MovementRules.StepCostFeet(Battlefield, mover, from, step, _combatants);
            mover.MoveTo(step);
            walked.Add(step);

            // The reveal check: consulted after the move, and never on the final step (an
            // arrived move has nothing left to interrupt). The delegate is caller-supplied
            // advisory code (a fog query), so a throw must not corrupt the walk: it is
            // caught, treated as "no stop", and not consulted again this walk — the move
            // then completes normally below, leaving the encounter consistent. (qc round 1,
            // PR #622.)
            if (interrupt is not null && !interruptFaulted && i < path.Steps.Count - 1)
            {
                Combatant? spotted;

                try
                {
                    spotted = interrupt(new MovementStep(mover, mover.Position, path.Steps.Skip(i + 1).ToList()));
                }
                catch (Exception) // broad on purpose: this is untrusted advisory code; any fault degrades to no-stop
                {
                    // Core has no I/O to log through; the caller owns the closure and is
                    // where a fault surfaces if it cares. Fail-OPEN (complete the planned
                    // move), not closed: a broken visibility computation cannot be trusted
                    // to stop safely, and stopping the mover mid-field for no on-screen
                    // reason is worse than the pre-#493 fallback.
                    spotted = null;
                    interruptFaulted = true;
                }

                if (spotted is { } revealed)
                {
                    mover.Turn.SpendMovement(travelled); // only the feet actually walked

                    Add(
                        CombatStepKind.Move,
                        $"{mover.Name} stops at {mover.Position}: {revealed.Name} comes into view.",
                        mover,
                        path: walked);
                    return;
                }
            }
        }

        mover.Turn.SpendMovement(path.CostFeet);

        Add(
            CombatStepKind.Move,
            $"{mover.Name} moves from {start} to {mover.Position} ({path.CostFeet} ft.).",
            mover,
            path: walked);
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

        // "Can't be targeted directly" forbids the Opportunity Attack too, and reach
        // weapons make it reachable: a Halberd's reach spans a square, and that square
        // can be a wall. Caught by the regenerated transcript, which printed a hit
        // through Total Cover — the mover slips away unswung-at, like the charmer above.
        if (CoverRules.AgainstSpace(Battlefield, attacker.Space, mover.Space, _combatants) == CoverDegree.Total)
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

    /// <summary>
    /// Graze: "If your attack roll with this weapon misses a creature, you can deal
    /// damage to that creature equal to the ability modifier you used to make the attack
    /// roll."
    /// </summary>
    /// <remarks>
    /// No roll and no dice — the modifier itself, of the weapon's own damage type. A
    /// modifier of zero or less deals nothing, since "damage equal to the modifier" is
    /// not damage when the modifier is not positive.
    /// </remarks>
    /// <summary>
    /// The creature's Speed with any Slow mastery applied: down 10 feet while anybody's
    /// Slow is on it, and exactly 10 however many are — "the Speed reduction doesn't
    /// exceed 10 feet".
    /// </summary>
    private static int EffectiveSpeedFeet(Combatant combatant) =>
        Math.Max(0, combatant.Stats.SpeedFeet - (combatant.Features.SlowedBy.Count > 0 ? 10 : 0));

    /// <summary>
    /// Clears the Saps this creature inflicted: "Disadvantage on its next attack roll
    /// <em>before the start of your next turn</em>" — measured against the sapper's turn,
    /// not the victim's, which is why the victim remembers who sapped it.
    /// </summary>
    private void ExpireSapsFrom(Combatant sapper)
    {
        foreach (var victim in _combatants.Where(c => c.Features.SappedBy == sapper.Id))
        {
            victim.Features.SappedBy = null;
        }

        // Slow reads the same possessive — "until the start of your next turn" — so the
        // author's turn coming round releases it wherever it landed.
        foreach (var victim in _combatants.Where(c => c.Features.SlowedBy.Contains(sapper.Id)))
        {
            victim.Features.SlowedBy.Remove(sapper.Id);
        }
    }

    private void ApplyGraze(Combatant attacker, CombatAttack attack, Combatant target)
    {
        if (attack.Mastery != WeaponMastery.Graze || attack.AbilityModifier <= 0)
        {
            return;
        }

        var applied = DamageRules.Apply(target, attack.AbilityModifier, attack.Damage[0].Type);

        // A CombatStepKind.Damage step, not Feature — this changed hit points, can
        // trigger Concentration, and can down or kill the target, same as any other
        // damage application (#584). The preceding Attack step already recorded the
        // miss (Hit: false); this step needs no hit-or-miss answer of its own.
        Add(
            CombatStepKind.Damage,
            $"{attack.Name}'s Graze deals {applied.Effective} {attack.Damage[0].Type} damage anyway — " +
            $"{DescribeHealth(target)}.",
            attacker,
            target,
            damage: applied.Effective);

        CheckConcentration(target, applied.Effective);

        if (applied.Effective > 0)
        {
            BreakTurnEffectOnDamage(target);
            EndTurnEffectsWhoseSourceIsDown();
        }

        if (applied.Died)
        {
            target.RecordDeathRound(Round);
            Add(CombatStepKind.Died, $"{target.Name} is dead.", target);
        }
        else if (applied.Downed)
        {
            Add(CombatStepKind.Downed, $"{target.Name} drops to 0 hit points and falls Unconscious.", target);
        }
    }

    /// <summary>
    /// Sap and Topple, both of which read "if you hit a creature with this weapon".
    /// </summary>
    /// <remarks>
    /// Topple's DC is "8 plus the ability modifier used to make the attack roll and your
    /// Proficiency Bonus", which is why <see cref="CombatAttack.AbilityModifier"/> is
    /// carried on the attack rather than recomputed from the sheet: a monster's attack
    /// has no sheet to recompute from.
    /// </remarks>
    private void ApplySapAndTopple(Combatant attacker, CombatAttack attack, Combatant target)
    {
        switch (attack.Mastery)
        {
            case WeaponMastery.Sap:
                target.Features.SappedBy = attacker.Id;

                Add(
                    CombatStepKind.Feature,
                    $"{attack.Name}'s Sap leaves {target.Name} with Disadvantage on its next attack roll.",
                    attacker,
                    target);
                break;

            case WeaponMastery.Topple:
                if (!target.IsActive)
                {
                    break;
                }

                var difficultyClass = WeaponMasteryRules.ToppleDifficultyClass(
                    attack.AbilityModifier,
                    attacker.Stats.ProficiencyBonus);

                // Topple forces a saving throw, which is a printed Rage extension in
                // its own right — the Barbarian's Greataxe carries Cleave, but a
                // Topple weapon is a legal draft and this is the path it takes.
                attacker.Features.SustainedRageThisTurn = true;

                var roll = D20Test.Roll(_random, target.Stats.SaveBonusFor(Ability.Constitution));
                var succeeded = roll.Total >= difficultyClass;

                Add(
                    CombatStepKind.Feature,
                    $"{attack.Name}'s Topple forces a Constitution saving throw: {roll} vs DC " +
                    $"{difficultyClass} — {(succeeded ? "stays up." : "goes down.")}",
                    attacker,
                    target);

                if (!succeeded)
                {
                    ImposeConditions(attacker, [new AppliedCondition(ConditionType.Prone)], target, grappleRangeFeet: null);
                }

                break;
        }
    }

    private void ResolveAttack(
        Combatant attacker,
        CombatAttack attack,
        Combatant target,
        bool isOpportunityAttack,
        bool isSpellAttack = false)
    {
        // Total Cover never reaches here: every targeting path refuses it before
        // spending, and MakeOpportunityAttack declines to swing through it — a reach
        // weapon's Opportunity Attack can genuinely cross a wall square, which the
        // regenerated transcript caught. What remains is Half or Three-Quarters,
        // raising the AC to beat.
        var cover = CoverRules.AgainstSpace(Battlefield, attacker.Space, target.Space, _combatants);

        // Wand of the War Mage: "you ignore Half Cover when making a spell attack" —
        // Half exactly, so Three-Quarters still counts.
        if (cover == CoverDegree.Half && isSpellAttack && attacker.Stats.IgnoresHalfCoverOnSpellAttacks)
        {
            cover = CoverDegree.None;
        }

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
                && ally.DistanceFeetTo(target) <= Battlefield.FeetPerSquare);

        // Steady Aim is Advantage on the next attack roll only, so it is consumed here
        // whether the attack hits or not.
        var steadyAim = attacker.Features.SteadyAimedThisTurn;
        attacker.Features.SteadyAimedThisTurn = false;

        // Vex: "you have Advantage on your next attack roll against that creature" — the
        // named creature only, and spent on this roll however it lands. Sap is the
        // mirror image on the defender: Disadvantage on its next attack roll, and this
        // is that roll.
        var vexed = attacker.Features.VexedTargetId == target.Id;
        attacker.Features.VexedTargetId = null;

        var sapped = attacker.Features.SappedBy is not null;
        attacker.Features.SappedBy = null;

        // Guiding Bolt's light: "the next attack roll made against it ... has
        // Advantage" — anyone's roll, spent on this one however it lands.
        var guided = target.Features.GuidedBy is not null;
        target.Features.GuidedBy = null;

        var result = AttackRules.Resolve(
            _random,
            attacker,
            attack,
            target,
            extraAdvantage: recklessAdvantage || targetIsReckless || packTactics || steadyAim || vexed || guided,
            extraDisadvantage: sapped,
            combatants: _combatants,
            cover: cover);

        // "Make an attack roll against an enemy" — the first printed way to extend a
        // Rage, and it is the roll rather than the hit, so it is recorded here before
        // the miss path returns.
        if (target.SideId != attacker.SideId)
        {
            attacker.Features.SustainedRageThisTurn = true;
        }

        var modeNote = result.Roll.Mode switch
        {
            RollMode.Advantage => " with Advantage",
            RollMode.Disadvantage => " with Disadvantage",
            _ => string.Empty,
        };

        // The AC narrated already includes the cover bonus — this names why it grew.
        var coverNote = result.Cover == CoverDegree.None
            ? string.Empty
            : $" ({CoverRules.Describe(result.Cover)})";

        var verb = isOpportunityAttack ? "swings at" : "attacks";

        // Whether something crossed the distance, recorded for the client the way a
        // Move's route is: the engine's own predicate, so nothing downstream has to
        // guess from the gap and get a reach weapon wrong. A spell attack counts —
        // a Guiding Bolt crosses the room exactly as an arrow does.
        var ranged = isSpellAttack
            ? RangedAttackKind.Spell
            : attack.IsRangedAttackRoll(attacker.DistanceFeetTo(target))
                ? RangedAttackKind.Weapon
                : RangedAttackKind.None;

        if (!result.Hit)
        {
            var reason = result.Roll.IsNatural1 ? " — a natural 1, an automatic miss" : string.Empty;

            Add(
                CombatStepKind.Attack,
                $"{attacker.Name} {verb} {target.Name} with {attack.Name}{modeNote}: {result.Roll} vs AC " +
                $"{result.TargetArmorClass}{coverNote} — miss{reason}.",
                attacker,
                target,
                ranged: ranged,
                attackName: attack.Name,
                hit: false);

            ApplyGraze(attacker, attack, target);
            return;
        }

        var criticalNote = result.Critical ? " — a Critical Hit!" : string.Empty;

        Add(
            CombatStepKind.Attack,
            $"{attacker.Name} {verb} {target.Name} with {attack.Name}{modeNote}: {result.Roll} vs AC " +
            $"{result.TargetArmorClass}{coverNote} — hit{criticalNote}",
            attacker,
            target,
            ranged: ranged,
            attackName: attack.Name,
            hit: true);

        // Sap and Topple both read "if you hit a creature with this weapon", so they
        // land on the hit itself rather than on damage being dealt.
        ApplySapAndTopple(attacker, attack, target);

        // Guiding Bolt's rider is "On a hit" too — the light lands here, before the
        // damage, and unlike Vex it does not care whether the damage gets through.
        if (attack.GrantsAdvantageAgainstTargetOnHit)
        {
            target.Features.GuidedBy = attacker.Id;
            target.Features.GuidedOnAuthorTurn = attacker.TurnsBegun;

            Add(
                CombatStepKind.Feature,
                $"{attack.Name}'s light clings to {target.Name}: the next attack roll against it has Advantage.",
                attacker,
                target);
        }

        // Uncanny Dodge is decided once for the attack, not once per damage component.
        var halvings = TryUncannyDodge(target) ? 1 : 0;

        var components = AttackRules.RollDamage(_random, attack, result, attacker, target).ToList();

        // Rage adds its bonus to Strength melee attacks. Applied to the first component
        // only: it is one bonus on the attack, not one per damage type.
        var rageBonus = attacker.Features.IsRaging && attack.Kind == AttackKind.Melee
            ? attacker.Stats.Character?.RageDamageBonus ?? 0
            : 0;

        // Frenzy: "If you use Reckless Attack while your Rage is active, you deal extra
        // damage to the first target you hit on your turn with a Strength-based attack
        // ... a number of d6s equal to your Rage Damage bonus ... the same type as the
        // weapon". A melee weapon attack is the Strength-based case this engine has —
        // the same reading Rage's own damage bonus uses.
        if (attacker.Stats.Has(ClassFeature.Frenzy)
            && attacker.Features.IsRaging
            && attacker.Features.IsRecklessThisTurn
            && !attacker.Features.FrenzyUsedThisTurn
            && attack.Kind == AttackKind.Melee)
        {
            attacker.Features.FrenzyUsedThisTurn = true;

            var dice = new DiceExpression(attacker.Stats.Character!.RageDamageBonus, 6, 0);
            var frenzy = DiceRoller.Roll(_random, dice, result.Critical);

            components.Add((new AttackDamage(dice, attack.Damage[0].Type, frenzy.Total), frenzy));

            Add(
                CombatStepKind.Feature,
                $"{attacker.Name}'s Frenzy adds {frenzy.Total} damage [{frenzy}].",
                attacker,
                target);
        }

        var cunningStrike = false;

        if (SneakAttackApplies(attacker, attack, target, result))
        {
            attacker.Features.SneakAttackUsedThisTurn = true;

            // A declared Cunning Strike is paid for in dice removed before rolling, so
            // the reduced expression is what gets rolled — never the full one with the
            // cost taken off the total afterwards.
            cunningStrike = attacker.Features.CunningStrike != CunningStrikeEffect.None;
            var damage = SneakAttackDamageAfterCunningStrike(attacker);

            var sneak = DiceRoller.Roll(_random, damage, result.Critical);

            components.Add((new AttackDamage(damage, attack.Damage[0].Type, sneak.Total), sneak));

            Add(
                CombatStepKind.Feature,
                $"{attacker.Name} lands a Sneak Attack for an extra {sneak.Total} damage [{sneak}]" +
                (cunningStrike ? " (reduced by Cunning Strike)" : string.Empty) + ".",
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
                target,
                damage: applied.Effective);

            CheckConcentration(target, applied.Effective);

            if (applied.Effective > 0)
            {
                BreakTurnEffectOnDamage(target);
                EndTurnEffectsWhoseSourceIsDown();
            }

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
                target.RecordDeathRound(Round);
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

        // Vex: "if you hit a creature with this weapon and deal damage to the creature",
        // so it is set here rather than on the hit - a hit absorbed entirely by Immunity
        // deals no damage and vexes nobody.
        if (attack.Mastery == WeaponMastery.Vex && components.Any(pair => pair.Result.Total > 0))
        {
            attacker.Features.VexedTargetId = target.Id;
            attacker.Features.VexEarnedOnTurn = attacker.TurnsBegun;

            Add(
                CombatStepKind.Feature,
                $"{attack.Name}'s Vex gives {attacker.Name} Advantage on its next attack against {target.Name}.",
                attacker,
                target);
        }

        // Slow shares Vex's trigger — "hit ... and deal damage to it" — and Sap's
        // expiry, the author's next turn. The set caps itself: any number of entries is
        // still one 10-foot reduction.
        if (attack.Mastery == WeaponMastery.Slow && components.Any(pair => pair.Result.Total > 0))
        {
            target.Features.SlowedBy.Add(attacker.Id);

            Add(
                CombatStepKind.Feature,
                $"{attack.Name}'s Slow cuts {target.Name}'s Speed by 10 feet.",
                attacker,
                target);
        }

        ImposeRiders(attacker, attack, target);

        // The embedded saving throw — the Ghast's Claw — rolls after the damage and
        // the attack's own riders, against the printed DC, gated on the printed
        // creature type. A target the blow already finished rolls nothing.
        if (attack.EmbeddedSave is { } embedded
            && target.IsActive
            && (embedded.ExcludedTargetType is not { } exempt || target.Stats.Type != exempt))
        {
            ResolveSaveEffect(
                attacker,
                attack.Name,
                embedded.Save,
                embedded.Save.DifficultyClass ?? 10,
                target.Position,
                target,
                CombatStepKind.Entry,
                embedded.Save.AppliedConditions.Where(ConditionRules.CanBeImposed).ToArray());
        }

        // "The effect occurs immediately after the attack's damage is dealt" — after the
        // riders, which are part of the attack itself.
        if (cunningStrike)
        {
            ResolveCunningStrike(attacker, target);
        }

        // The blow may have dropped a grappler, in this fight or another one.
        EndBrokenGrapples();
        ClearSharedSquares();

        // Cleave last, once everything belonging to the first blow has landed — it is
        // its own attack roll against a second creature, and it must not recurse.
        TryCleave(attacker, attack, target);
    }

    /// <summary>
    /// Cleave: "If you hit a creature with a melee attack roll using this weapon, you
    /// can make a melee attack roll with the weapon against a second creature within 5
    /// feet of the first that is also within your reach. On a hit, the second creature
    /// takes the weapon's damage, but don't add your ability modifier to that damage
    /// unless that modifier is negative. You can make this extra attack only once per
    /// turn."
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The engine chooses the second creature</b>, because declining a free swing is
    /// never right and the choice among candidates is the same judgement the policy
    /// already makes everywhere: an enemy, the most wounded first. A future client
    /// wanting the choice would add a parameter, not a rule.
    /// </para>
    /// <para>
    /// The second swing is a plain attack roll with the weapon's full attack bonus —
    /// the printed text adjusts only the damage — and it carries none of the first
    /// blow's riders, Sneak Attack or Frenzy: those belong to an attack action's blow,
    /// and this is the axe carrying through. It cannot Cleave again, by construction
    /// rather than by flag: this method resolves the swing itself.
    /// </para>
    /// </remarks>
    private void TryCleave(Combatant attacker, CombatAttack attack, Combatant target)
    {
        if (attack.Mastery != WeaponMastery.Cleave
            || attack.Kind != AttackKind.Melee
            || attacker.Features.CleaveUsedThisTurn
            || !attacker.IsActive)
        {
            return;
        }

        var reach = attack.ReachFeet ?? Battlefield.FeetPerSquare;

        var second = _combatants
            .Where(candidate => candidate.IsActive
                && candidate.SideId != attacker.SideId
                && candidate.Id != target.Id
                && candidate.DistanceFeetTo(target) <= Battlefield.FeetPerSquare
                && candidate.DistanceFeetTo(attacker) <= reach)
            .OrderBy(candidate => candidate.CurrentHitPoints)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        if (second is null)
        {
            return;
        }

        attacker.Features.CleaveUsedThisTurn = true;

        var result = AttackRules.Resolve(_random, attacker, attack, second, combatants: _combatants);

        var swing = result.Hit
            ? $"hit{(result.Critical ? " — a Critical Hit!" : string.Empty)}"
            : "miss";

        Add(
            CombatStepKind.Feature,
            $"{attack.Name}'s Cleave carries through into {second.Name}: {result.Roll} vs AC " +
            $"{result.TargetArmorClass} — {swing}.",
            attacker,
            second);

        if (!result.Hit)
        {
            return;
        }

        // "The weapon's damage, but don't add your ability modifier ... unless that
        // modifier is negative": the positive modifier alone comes back out, leaving
        // any magic weapon bonus in.
        var first = attack.Damage[0];
        var amount = first.Amount with
        {
            Modifier = first.Amount.Modifier - Math.Max(0, attack.AbilityModifier),
        };

        var rolled = DiceRoller.Roll(_random, amount, result.Critical);
        var applied = DamageRules.Apply(second, rolled.Total, first.Type, result.Critical);

        Add(
            CombatStepKind.Damage,
            $"{second.Name} takes {applied.Effective} {first.Type} damage [{rolled}] — {DescribeHealth(second)}.",
            attacker,
            second,
            damage: applied.Effective);

        CheckConcentration(second, applied.Effective);

        if (applied.Effective > 0)
        {
            BreakTurnEffectOnDamage(second);
            EndTurnEffectsWhoseSourceIsDown();
        }

        if (applied.Died)
        {
            second.RecordDeathRound(Round);
            Add(CombatStepKind.Died, $"{second.Name} is dead.", second);
        }
        else if (applied.Downed)
        {
            Add(CombatStepKind.Downed, $"{second.Name} drops to 0 hit points and falls Unconscious.", second);
        }

        EndBrokenGrapples();
        ClearSharedSquares();
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
        int? grappleRangeFeet,
        (Ability Ability, int DifficultyClass)? repeatSave = null)
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

            // "until the grapple ends" rides only while the same creature's grapple
            // holds this target. The Grappled rider is printed first and imposed first,
            // so this sees it — and when the grapple itself was refused (a size gate, an
            // immunity), the dependent condition never lands, exactly as a condition
            // whose whole duration is the grapple should not.
            if (rider.Duration is { WhileGrappleHolds: true }
                && !string.Equals(
                    target.ConditionState(ConditionType.Grappled)?.SourceId,
                    source.Id,
                    StringComparison.Ordinal))
            {
                continue;
            }

            // A repeat-save duration repeats *the* save — the ability and DC the
            // effect rolled — so a rider carrying the flag with no save to repeat has
            // nothing printed to roll and must not land at all.
            if (rider.Duration is { RepeatSaveAtTurnEnd: true } && repeatSave is null)
            {
                continue;
            }

            var expiry = ConditionRules.ExpiryFor(rider.Duration, source, target);

            var imposed = new ActiveCondition(
                rider.Condition,
                source.Id,
                expiry,
                rider.EscapeDifficultyClass,
                rider.Condition == ConditionType.Grappled ? grappleRangeFeet : null,
                rider.Duration is { RepeatSaveAtTurnEnd: true } ? repeatSave!.Value.Ability : null,
                rider.Duration is { RepeatSaveAtTurnEnd: true } ? repeatSave!.Value.DifficultyClass : null,
                TiedToConcentration: rider.Duration is { WhileConcentrating: true },
                EscalatesTo: rider.EscalatesTo);

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

            // A rider can bring Incapacitated with no damage attached at all — Hold
            // Person's Paralyzed, say — so nothing on the damage path would ever see
            // it. Glossary p.186 ends Concentration the instant Incapacitated lands,
            // not on a save.
            BreakConcentrationOnIncapacitated(target);
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
    /// callers do not agree on what they pass: a stat-block entry's own save action
    /// (<c>Encounter.Entries.cs</c>) hands over its save's <c>AppliedConditions</c>
    /// unfiltered — the content still carries riders this engine cannot execute (Deafened,
    /// Invisible), and this caller does not screen them out — while that same entry's
    /// embedded save, a spell's save and a class feature's save each filter their own
    /// through <see cref="ConditionRules.CanBeImposed"/> first (Divine Spark passes none
    /// at all, having no rider of its own). Pre-filtering here is a courtesy, not the
    /// guarantee: <see cref="ImposeConditions"/> is the one gate every rider actually
    /// passes through, whichever caller and however much it screened beforehand —
    /// <see cref="ConditionRules.CanImpose"/> re-tests each rider, superset of
    /// <c>CanBeImposed</c>, before any of them is applied. A rider lands on a failure — or
    /// either way when the printed outcome is "Failure or Success" — and carries no
    /// grapple range, because a save effect prints no reach to measure a grapple against.
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
        IReadOnlyList<AppliedCondition> riders,
        bool? magicalEffect = null)
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

        // "Force an enemy to make a saving throw" — the Rage's second printed
        // extension, and the reason this is not simply an attack-roll flag.
        if (affected.Any(victim => victim.SideId != source.SideId))
        {
            source.Features.SustainedRageThisTurn = true;
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
                // Advantage on Dexterity saves unless the Barbarian is Incapacitated, and
                // Dodge is the same Advantage unless its printed exception applies —
                // RetainsDodgeBenefits carries it, shared with the attack-roll half. A
                // Restrained dodger shows why sharing matters: Restrained is Speed 0, so
                // Dodge is lost entirely and the save is at plain Disadvantage rather
                // than the two cancelling. Combined, so Advantage and Disadvantage cancel
                // rather than either winning.
                var restrained = save.Ability == Ability.Dexterity && victim.HasCondition(ConditionType.Restrained);

                // Magic Resistance defaults to the step kind — a spell is magical, a
                // stat block's save entry is read as not (the registry's reading) — and
                // magicalEffect overrides it for the effects that are neither: Divine
                // Spark is printed as divine energy fuelling a magical effect, so it
                // is resisted although it is no spell.
                var magicResistance = (magicalEffect ?? kind == CombatStepKind.Spell)
                    && victim.HasTrait(MonsterTrait.MagicResistance);
                var dangerSense = save.Ability == Ability.Dexterity
                    && victim.Stats.Has(ClassFeature.DangerSense)
                    && !victim.HasCondition(ConditionType.Incapacitated);
                var dodging = save.Ability == Ability.Dexterity
                    && victim.Turn.IsDodging
                    && ConditionRules.RetainsDodgeBenefits(victim);

                // Shatter: "A Construct has Disadvantage on the save." The printed
                // sentence names the type, and the type is on the stats for exactly
                // this rule.
                var construct = save.ConstructsSaveAtDisadvantage
                    && victim.Stats.Type == CreatureType.Construct;

                var mode = D20Test.Combine(magicResistance || dangerSense || dodging, restrained || construct);

                // Cover's other half: "+2/+5 bonus to AC and Dexterity saving throws",
                // judged from the effect's point of origin — the erupting point for a
                // Sphere or Cube, the creature for everything else. Total never reaches
                // here: an area excludes those squares and single-target paths refuse
                // them. Sacred Flame prints "gains no benefit from Half Cover or
                // Three-Quarters Cover for this save", carried as Save.CoverIgnored.
                // The point of origin is a point even when a creature produces it, so it
                // is one square; the victim is judged as its whole body, which is the
                // same reading the attack path takes — the least covered square of the
                // target decides, because a body that sticks out is a body the blast
                // reaches.
                var cover = save.Ability == Ability.Dexterity && !save.CoverIgnored
                    ? CoverRules.AgainstSpace(
                        Battlefield,
                        CreatureSpace.Of(AreaTargeting.PointOfOrigin(save.Area, source.Position, point)),
                        victim.Space,
                        _combatants)
                    : CoverDegree.None;
                var coverNote = CoverRules.Bonus(cover) > 0
                    ? $" ({CoverRules.Describe(cover)})"
                    : string.Empty;

                var roll = D20Test.Roll(
                    _random,
                    victim.Stats.SaveBonusFor(save.Ability) + CoverRules.Bonus(cover),
                    mode);
                succeeded = roll.Total >= difficultyClass;

                Add(
                    kind,
                    $"{victim.Name} makes a {save.Ability} saving throw{coverNote}: {roll} vs DC {difficultyClass} — " +
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
                    victim,
                    damage: applied.Effective);

                CheckConcentration(victim, applied.Effective);

                if (applied.Effective > 0)
                {
                    BreakTurnEffectOnDamage(victim);
                    EndTurnEffectsWhoseSourceIsDown();
                }

                if (applied.Died)
                {
                    victim.RecordDeathRound(Round);
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
                ImposeConditions(source, riders, victim, grappleRangeFeet: null, (save.Ability, difficultyClass));
            }
        }

        // The damage may have dropped a grappler, in this fight or another one.
        EndBrokenGrapples();
        ClearSharedSquares();
    }

    /// <summary>Narrates a duration the way the SRD prints it.</summary>
    private static string DescribeDuration(ConditionDuration? duration, Combatant source, Combatant bearer) =>
        duration switch
        {
            null => string.Empty,
            // The two-tier gaze: no calendar, and naming the repeat is what tells a
            // reader the condition is not simply permanent.
            { OutlastsFight: true, RepeatSaveAtTurnEnd: true } => " until a repeated save ends it — or worsens it",
            { OutlastsFight: true } => " for the rest of the fight",
            { WhileGrappleHolds: true } => " until the grapple ends",
            { TurnsAhead: 1 } =>
                $" until the {(duration.Clock == ConditionClock.StartOfTurn ? "start" : "end")} of " +
                $"{(duration.Owner == ConditionDurationOwner.Bearer ? bearer.Name : source.Name)}'s next turn",
            // Only ConditionDuration.ForMinutes produces the multi-turn shape, so the
            // printed wording is recoverable from the count.
            { TurnsAhead: 10 } => " for 1 minute",
            _ => $" for {duration.TurnsAhead / 10} minutes",
        };

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

        // Still in the fight means alive and above 0 hit points — not "able to act
        // this instant". A creature Paralyzed or Stunned at full health can be freed
        // by an expiry, a repeated save or its captor's broken Concentration, so a
        // side of held creatures has not lost yet; before repeat saves existed this
        // read IsActive, and Hold Person turned that reading into an instant-victory
        // button the printed rules do not sell. What ends the fight for a held
        // creature is the enemy walking over and finishing it, which the policy's
        // stuck-turn rule already does.
        var standing = _combatants
            .Where(combatant => !combatant.IsDead && combatant.CurrentHitPoints > 0)
            .Select(combatant => combatant.SideId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (standing.Length <= 1)
        {
            IsComplete = true;
            WinningSide = standing.Length == 1 ? standing[0] : null;

            Add(
                CombatStepKind.EncounterEnded,
                WinningSide is null
                    ? "The fight ends with nobody left standing."
                    : $"The fight ends in victory for {WinningSide}.");

            return;
        }

        // Both sides still stand, so only an objective can end it here. Asked *after* the
        // last-side-standing question on purpose: being wiped out loses whatever the
        // objective says, and a side with nobody left cannot meet one.
        CheckObjectiveMet();
    }

    /// <summary>
    /// Ends the fight if the objective's own side has met it. Never ends it any other
    /// way — the last-side-standing rule is <see cref="CheckForCompletion"/>'s, and
    /// keeping the two separate is what lets a turn boundary ask about the objective
    /// without disturbing when a downed character rolls its Death Saving Throw.
    /// </summary>
    private void CheckObjectiveMet()
    {
        if (IsComplete || Objective.SideId is not { } side)
        {
            return;
        }

        var standing = _combatants
            .Where(combatant => !combatant.IsDead && combatant.CurrentHitPoints > 0)
            .Select(combatant => combatant.SideId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (!standing.Contains(side, StringComparer.Ordinal))
        {
            return;
        }

        var (met, ending) = Objective.Kind switch
        {
            // Inclusive: surviving 3 rounds means the fourth never begins. Round has
            // already been incremented past the one just played when this is asked at a
            // round boundary, which is what makes the comparison a bare greater-than.
            ObjectiveKind.SurviveRounds => (
                Round > Objective.Rounds,
                $"The fight ends: {side} held out for "
                    + $"{Objective.Rounds.ToString(CultureInfo.InvariantCulture)} rounds."),

            // A leader that is not on the field at all counts as down: the marked
            // combatant can only leave the list by dying, and reading a missing id as
            // "not yet" would make the objective unwinnable rather than already won.
            ObjectiveKind.KillLeader => (
                !_combatants.Any(combatant =>
                    string.Equals(combatant.Id, Objective.LeaderId, StringComparison.Ordinal)
                    && !combatant.IsDead),
                $"The fight ends: the leader is down and the rest break off. Victory for {side}."),

            _ => (false, string.Empty),
        };

        if (!met)
        {
            return;
        }

        IsComplete = true;
        WinningSide = side;
        Add(CombatStepKind.EncounterEnded, ending);
    }

    private void Add(
        CombatStepKind kind,
        string narration,
        Combatant? actor = null,
        Combatant? target = null,
        IReadOnlyList<GridPosition>? path = null,
        RangedAttackKind ranged = RangedAttackKind.None,
        string? attackName = null,
        bool? hit = null,
        int? damage = null) =>
        _log.Add(new CombatStep(kind, narration, actor?.Id, target?.Id, path, ranged, attackName, hit, damage));
}
