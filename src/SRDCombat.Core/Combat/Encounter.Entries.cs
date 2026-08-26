using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>
/// Using a stat block entry by name, and the usage limits that gate one.
/// </summary>
/// <remarks>
/// <para>
/// This is the path a monster's non-Multiattack entries resolve through — before it
/// existed there was none: every <see cref="Encounter"/> action was either hardcoded or
/// gated on <c>Stats.Character</c>, so a Breath Weapon, a thrown Rock outside the
/// Multiattack's named attacks, anything that is not the Attack action, was unreachable
/// whatever the extractor captured.
/// </para>
/// <para>
/// The dispatch is on <see cref="EntryMechanics"/>, and anything the engine cannot
/// resolve is refused with a named code, exactly like <c>spell.not_implemented</c>.
/// Attack entries resolve as one swing; saving-throw entries resolve through the same
/// loop as save spells — every creature in the area rolls against the printed DC, damage
/// halves on a success where the text says so, and the riders the engine can execute
/// land on a failure.
/// </para>
/// <para>
/// One judgement call, written down: an attack entry whose name carries a form gate —
/// "Bite (Wolf or Hybrid Form Only)" — is not refused, because the engine has no
/// concept of form and the qualifier is printed in the name a client shows. Which form
/// the creature fights in is the caller's choice; <see cref="SimpleTacticsPolicy"/>
/// deliberately never makes it.
/// </para>
/// </remarks>
public sealed partial class Encounter
{
    /// <summary>Uses a named stat block entry as the active combatant's action.</summary>
    public ActionRefusal? UseEntry(string entryName, Combatant? target = null) =>
        UseEntry(entryName, target?.Position, target);

    /// <summary>
    /// Uses a named stat block entry aimed at a point, for an area effect — where a
    /// breath weapon is exhaled, independent of any one creature.
    /// </summary>
    public ActionRefusal? UseEntry(string entryName, GridPosition point, Combatant? target = null) =>
        UseEntry(entryName, (GridPosition?)point, target);

    private ActionRefusal? UseEntry(string entryName, GridPosition? point, Combatant? target)
    {
        if (ActiveCombatant is not { } actor)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!actor.CanAct)
        {
            return new ActionRefusal("combatant.cannot_act", $"{actor.Name} cannot act.");
        }

        var entry = actor.Stats.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, entryName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return new ActionRefusal("entry.unknown", $"{actor.Name} has no entry called '{entryName}'.");
        }

        // An Action entry spends the Action, a Bonus Action entry the Bonus Action —
        // the Basilisk's Petrifying Gaze is printed under Bonus Actions, and for as
        // long as this gate stopped at Action, no monster could use its signature
        // ability at all (#230). Reactions, Legendary Actions and Traits stay refused:
        // each needs a trigger or an economy the engine does not model.
        if (entry.Section is not (MonsterEntrySection.Action or MonsterEntrySection.BonusAction))
        {
            return new ActionRefusal(
                "entry.not_an_action",
                $"{entry.Name} is a {entry.Section} entry, not an action.");
        }

        if (CheckUsage(actor, entry.Name) is { } unavailable)
        {
            return unavailable;
        }

        return entry.Mechanics switch
        {
            EntryMechanics.Attack => UseAttackEntry(actor, entry, target),
            EntryMechanics.Multiattack => new ActionRefusal(
                "entry.is_attack_action",
                $"{entry.Name} is the Attack action; make its attacks with Attack."),
            EntryMechanics.SavingThrow => UseSaveEntry(actor, entry, point, target),
            EntryMechanics.Narrative => new ActionRefusal(
                "entry.narrative",
                $"{entry.Name} has no effect on a fight."),
            _ => new ActionRefusal(
                "entry.not_implemented",
                $"{entry.Name}'s mechanics are not modelled."),
        };
    }

    /// <summary>
    /// Resolves an attack-shaped entry as its own action: one swing, not the
    /// Multiattack's several.
    /// </summary>
    /// <remarks>
    /// The SRD prints these as separate entries under Actions, so using one is a
    /// different action from Multiattack — the Ape throws one Rock or makes two Fist
    /// attacks, never both. That is why this spends the action without granting the
    /// extra swings <see cref="Attack"/> grants, and why <c>attack.not_in_multiattack</c>
    /// over there is not a contradiction but the other half of the same rule.
    /// </remarks>
    private ActionRefusal? UseAttackEntry(Combatant actor, MonsterEntry entry, Combatant? target)
    {
        if (target is null)
        {
            return new ActionRefusal("entry.needs_target", $"{entry.Name} needs a creature to attack.");
        }

        if (CostUnavailable(actor, entry) is { } spent)
        {
            return spent;
        }

        // FromMonster builds an attack from every entry carrying attack data, so this
        // lookup cannot fail for content; it guards hand-authored stats.
        var attack = actor.Stats.Attacks.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, entry.Name, StringComparison.OrdinalIgnoreCase));

        if (attack is null)
        {
            return new ActionRefusal(
                "entry.attack_missing",
                $"{entry.Name} has no resolvable attack behind it.");
        }

        // Only death refuses the attack: an Unconscious creature is a legal target,
        // and hitting one is how death saves fail.
        if (target.IsDead)
        {
            return new ActionRefusal("target.dead", $"{target.Name} is already dead.");
        }

        // "You can't attack the charmer" — an attack-shaped entry is still an attack.
        if (CharmedBy(actor, target))
        {
            return new ActionRefusal(
                "entry.charmed",
                $"{actor.Name} is Charmed by {target.Name} and cannot attack them.");
        }

        var distance = actor.DistanceFeetTo(target);

        if (!attack.CanReach(distance))
        {
            return new ActionRefusal(
                "attack.out_of_range",
                $"{target.Name} is {distance} ft. away, beyond {attack.Name}'s reach.");
        }

        // "Can't be targeted directly" — an attack-shaped entry is still an attack.
        if (CoverRules.AgainstSpace(Battlefield, actor.Space, target.Space, _combatants) == CoverDegree.Total)
        {
            return new ActionRefusal(
                "attack.total_cover",
                $"{target.Name} has Total Cover from {actor.Name} and can't be targeted directly.");
        }

        SpendCost(actor, entry);
        actor.Uses.Spend(entry.Name);
        ResolveAttack(actor, attack, target, isOpportunityAttack: false);
        CheckForCompletion();
        return null;
    }

    /// <summary>
    /// Resolves a saving-throw entry: every creature it reaches rolls against the printed
    /// DC, exactly as a save spell does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The riders passed through are the save's own — every one the engine can execute
    /// lands on a failed save, and the rest stay counted in the entry's
    /// <c>UnmodelledClauses</c>. A Grappled rider carries no range: an engulf-style
    /// grapple has no printed reach to measure, so it ends only by escape or the
    /// grappler's incapacity.
    /// </para>
    /// <para>
    /// <b>Range is enforced; sight is not (#386).</b> A printed distance —
    /// <see cref="SaveEffect.RangeFeet"/> — refuses a save aimed beyond it, the same
    /// way <c>attack.out_of_range</c> and <c>spell.out_of_range</c> already gate their
    /// own targeting paths. Extraction now structures that field for a single-target
    /// save and for a point-aimed area whose shape itself parsed (#386's second half),
    /// so this check is live: a Mummy's 60-foot Dreadful Glare (p.309) can no longer
    /// land from anywhere on the board. It stays null — deliberately, not by gap — for
    /// a point-aimed <b>Sphere</b> until #420 lands: every corpus Sphere prints its
    /// radius as "N-foot-radius Sphere", which <c>AreaPattern</c> cannot read yet, so
    /// <see cref="SaveEffect.Area"/> comes back null for one, and claiming its range
    /// without its area would let this check narrow an area effect down to a single
    /// target rather than refuse it honestly (see
    /// <c>EntryMechanicsParser.ReadRange</c>'s own remarks for the full reasoning).
    /// A printed sight qualifier — "a creature the mummy can see" — is a different
    /// question and stays permanently unmodelled: the standing reading on
    /// <see cref="ConditionRules"/> is that this engine has no line-of-sight model, and
    /// that gap is not this issue's to close. The area-shape gate above (<see
    /// cref="AreaTargeting"/>) still decides who an area save reaches; the range check
    /// only governs how far the save itself may be aimed.
    /// </para>
    /// </remarks>
    private ActionRefusal? UseSaveEntry(Combatant actor, MonsterEntry entry, GridPosition? point, Combatant? target)
    {
        if (entry.Save is not { } save)
        {
            // The extractor always pairs SavingThrow mechanics with the effect; this
            // guards hand-authored stats.
            return new ActionRefusal(
                "entry.save_missing",
                $"{entry.Name} has no structured saving throw behind it.");
        }

        // A monster's stat block always prints its DC; only hand-authored stats can omit
        // one, and guessing at a DC is not an option.
        if (save.DifficultyClass is not { } difficultyClass)
        {
            return new ActionRefusal(
                "entry.save_missing_dc",
                $"{entry.Name} prints no save difficulty class.");
        }

        if (save.Area is { } area && !AreaTargeting.CanResolve(area.Shape))
        {
            return new ActionRefusal(
                "entry.area_not_modelled",
                $"{entry.Name} uses a {area.Shape}, which is not modelled.");
        }

        if (save.Area is null && target is null)
        {
            return new ActionRefusal("entry.needs_target", $"{entry.Name} needs a creature to target.");
        }

        if (save.Area is null && target is { IsDead: true })
        {
            return new ActionRefusal("target.dead", $"{target.Name} is already dead.");
        }

        // The printed range, structured onto the save by extraction (#386) for a
        // single-target save and for a point-aimed area whose own shape parsed — null,
        // and so unenforced, for a point-aimed Sphere until #420 lands (this method's
        // own remarks above have the reasoning). Slotted here, immediately before
        // Total Cover with nothing between, the same relative position
        // attack.out_of_range and feature.divine_spark.out_of_range hold against their
        // own Total Cover checks. Two checks, mirroring CastSpell's pair: whenever a
        // target creature is given — a single-target save, or an area save merely
        // aimed via a target reference — the distance to that creature is what
        // counts, the same rule regardless of area; only a bare point aim with no
        // creature reference (necessarily an area effect — a single-target save
        // already refused above without one) falls to the second check. Sight ("a
        // creature the mummy can see") stays unenforced — the standing no-sight-model
        // reading on ConditionRules — this checks distance alone.
        if (target is not null && save.RangeFeet is { } range)
        {
            var rangeDistance = actor.DistanceFeetTo(target);

            if (rangeDistance > range)
            {
                return new ActionRefusal(
                    "entry.out_of_range",
                    $"{target.Name} is {rangeDistance} ft. away, beyond {entry.Name}'s {range} ft. range.");
            }
        }

        if (target is null && save.Area is not null && point is { } aimedPoint
            && save.RangeFeet is { } pointRange
            && actor.DistanceFeetTo(aimedPoint) > pointRange)
        {
            return new ActionRefusal(
                "entry.out_of_range",
                $"That square is beyond {entry.Name}'s {pointRange} ft. range.");
        }

        // A single-target save effect targets directly, so Total Cover refuses it; an
        // area effect is aimed at a point, and AreaTargeting's exclusion decides who is
        // caught behind what.
        if (save.Area is null && target is not null
            && CoverRules.AgainstSpace(Battlefield, actor.Space, target.Space, _combatants) == CoverDegree.Total)
        {
            return new ActionRefusal(
                "entry.total_cover",
                $"{target.Name} has Total Cover from {actor.Name} and can't be targeted directly.");
        }

        if ((point ?? target?.Position) is not { } aim)
        {
            return new ActionRefusal(
                "entry.needs_target",
                $"{entry.Name} needs a creature or a point to aim at.");
        }

        if (CharmedHarmRefusal(actor, "entry.charmed", entry.Name, save, aim, target) is { } charmed)
        {
            return charmed;
        }

        if (CostUnavailable(actor, entry) is { } spent)
        {
            return spent;
        }

        SpendCost(actor, entry);
        actor.Uses.Spend(entry.Name);

        Add(
            CombatStepKind.Entry,
            $"{actor.Name} uses {entry.Name}" +
            (save.Area is null && target is not null ? $" on {target.Name}." : "."),
            actor,
            target);

        ResolveSaveEffect(
            actor,
            entry.Name,
            save,
            difficultyClass,
            aim,
            target,
            CombatStepKind.Entry,
            save.AppliedConditions);

        CheckForCompletion();
        return null;
    }

    /// <summary>
    /// Whether the turn still holds what this entry costs — the Action for an Action
    /// entry, the Bonus Action for a Bonus Action one.
    /// </summary>
    private static ActionRefusal? CostUnavailable(Combatant actor, MonsterEntry entry)
    {
        if (entry.Section == MonsterEntrySection.BonusAction)
        {
            return actor.Turn.HasBonusAction
                ? null
                : new ActionRefusal(
                    "entry.bonus_action_spent",
                    $"{actor.Name} has already used its Bonus Action.");
        }

        return actor.Turn.HasAction
            ? null
            : new ActionRefusal("action.spent", $"{actor.Name} has already used its action.");
    }

    /// <summary>The other half of <see cref="CostUnavailable"/>: spends what it checked.</summary>
    private static void SpendCost(Combatant actor, MonsterEntry entry)
    {
        if (entry.Section == MonsterEntrySection.BonusAction)
        {
            actor.Turn.SpendBonusAction();
        }
        else
        {
            actor.Turn.SpendAction();
        }
    }

    /// <summary>Whether a limited-use entry has a use left, as a refusal when it has not.</summary>
    private static ActionRefusal? CheckUsage(Combatant actor, string entryName)
    {
        if (actor.Uses.IsAvailable(entryName))
        {
            return null;
        }

        return actor.Uses.LimitFor(entryName)?.Kind == UsageLimitKind.Recharge
            ? new ActionRefusal("entry.not_recharged", $"{actor.Name}'s {entryName} has not recharged.")
            : new ActionRefusal("entry.no_uses_left", $"{actor.Name} has no uses of {entryName} left.");
    }

    /// <summary>
    /// Rolls the d6 for every spent Recharge entry at the start of the creature's turn.
    /// </summary>
    /// <remarks>
    /// The SRD says to roll "at the start of each of the monster's turns"; the roll is
    /// made only while the ability is spent, because a roll for a charged ability could
    /// change nothing and would still consume a die — and the dice stream is what the
    /// frozen transcripts pin. Both outcomes are narrated: every roll visible is a
    /// commitment this project has made, and a client showing "does not recharge" is
    /// showing the player real information about next turn.
    /// </remarks>
    private void RollRecharges(Combatant combatant)
    {
        foreach (var (name, minimum) in combatant.Uses.AwaitingRecharge())
        {
            var roll = _random.Roll(6);
            var recharged = roll >= minimum;

            if (recharged)
            {
                combatant.Uses.Recharge(name);
            }

            Add(
                CombatStepKind.Recharge,
                $"{combatant.Name}'s {name} {(recharged ? "recharges" : "does not recharge")} " +
                $"(rolled {roll}, needs {minimum}+).",
                combatant);
        }
    }
}
