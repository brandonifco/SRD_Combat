namespace SRDCombat.Core.Characters;

/// <summary>
/// The class features this engine actually implements.
/// </summary>
/// <remarks>
/// <para>
/// A deliberately closed set, mapped from printed feature names by an explicit registry.
/// The extracted class content classifies every feature's prose and counts what it
/// cannot express; this enum is the other half of that bargain — the short list of
/// features that genuinely <em>do</em> something in a fight. A feature missing from here
/// is not implemented, and the character sheet reports that rather than implying it
/// works.
/// </para>
/// <para>
/// Spellcasting is absent from this map even though casting works: slots, spell lists
/// and the casting ability are resolved separately, and the printed feature's remaining
/// content — preparing and changing spells between fights — is not modelled, so the
/// name stays reported rather than claimed.
/// </para>
/// </remarks>
public enum ClassFeature
{
    /// <summary>Attack twice, rather than once, when you take the Attack action.</summary>
    ExtraAttack,

    /// <summary>Barbarian: Bonus Action, damage resistance and bonus melee damage.</summary>
    Rage,

    /// <summary>Barbarian: AC is 10 + Dexterity + Constitution while unarmoured.</summary>
    UnarmoredDefenseBarbarian,

    /// <summary>Barbarian: Advantage on Strength attacks, and Advantage to attackers.</summary>
    RecklessAttack,

    /// <summary>Rogue: extra damage once per turn under the right conditions.</summary>
    SneakAttack,

    /// <summary>Rogue: Dash, Disengage or Hide as a Bonus Action.</summary>
    CunningAction,

    /// <summary>Rogue: Reaction to halve one attack's damage.</summary>
    UncannyDodge,

    /// <summary>Fighter: Bonus Action to regain hit points.</summary>
    SecondWind,

    /// <summary>Fighter: one extra action, once per rest.</summary>
    ActionSurge,

    /// <summary>Barbarian: Advantage on Dexterity saving throws unless Incapacitated.</summary>
    DangerSense,

    /// <summary>Barbarian: Speed +10 feet while not in Heavy armour.</summary>
    FastMovement,

    /// <summary>
    /// Rogue: a Bonus Action for Advantage on the next attack roll this turn, at the
    /// cost of all movement.
    /// </summary>
    SteadyAim,

    /// <summary>
    /// Rogue, Ranger: a doubled proficiency bonus on chosen skills. Granted by the
    /// Rogue's Expertise and by the Ranger's Deft Explorer, which are the same rule
    /// under two printed names.
    /// </summary>
    Expertise,

    /// <summary>Fighter, Ranger: a Fighting Style feat. Archery and Defense are executed.</summary>
    FightingStyle,

    /// <summary>
    /// Rogue: spend Sneak Attack dice on an added effect. Only Trip is executed; the
    /// other printed effects are listed on <c>CunningStrikeEffect</c> with their reasons.
    /// </summary>
    CunningStrike,

    /// <summary>
    /// Fighter: spend a use of Second Wind to add 1d10 to a failed ability check, and
    /// keep the use if it still fails.
    /// </summary>
    TacticalMind,

    /// <summary>
    /// Every class, from level 4: the Ability Score Improvement feat, "or another feat
    /// of your choice" — the choice lives on the draft and the increase is applied by
    /// <see cref="CharacterResolver"/>. No other feat is modelled.
    /// </summary>
    AbilityScoreImprovement,

    /// <summary>
    /// Fighter, Barbarian, Rogue, Ranger, Paladin: unlocks the mastery property of a
    /// chosen number of kinds of weapon. Which properties execute is
    /// <c>WeaponMasteryRules</c>, and a draft mastering a weapon whose property does not
    /// is refused rather than granted silently.
    /// </summary>
    WeaponMastery,

    /// <summary>Champion Fighter: weapon attacks score a Critical Hit on a 19 or 20.</summary>
    ImprovedCritical,

    /// <summary>
    /// Berserker Barbarian: extra d6s equal to the Rage Damage bonus on the first
    /// Strength-based hit each turn while Raging and Reckless.
    /// </summary>
    Frenzy,

    /// <summary>
    /// Life Cleric: a spell cast with a slot that restores hit points restores 2 plus
    /// the slot's level more.
    /// </summary>
    DiscipleOfLife,

    /// <summary>
    /// Cleric: divine energy fuelling magical effects, twice per rest arc — one use
    /// back on a Short Rest, all on a Long, the Second Wind pattern. Both printed
    /// effects execute: Divine Spark whole, and Turn Undead with its three early-outs
    /// expressed via <see cref="Combat.ActiveCondition.EndsEarlyOnDamageOrSourceDown"/>
    /// — see the registry's remarks for how.
    /// </summary>
    ChannelDivinity,

    /// <summary>
    /// Cleric: the level 1 choice of sacred role — Protector or Thaumaturge. The
    /// choice lives on the draft as <c>DivineOrder</c>, and a draft that never chose
    /// keeps the printed name reported as unimplemented.
    /// </summary>
    DivineOrder,

    /// <summary>
    /// Cleric, level 5: whenever Turn Undead is used, roll a number of d8s equal to
    /// Wisdom modifier (minimum 1d8) and deal that much Radiant damage to every Undead
    /// that failed the save — without ending the turn effect Turn Undead just
    /// imposed. See <c>Encounter.TurnUndead</c>.
    /// </summary>
    SearUndead,
}

/// <summary>What Divine Spark's rolled total is spent on.</summary>
/// <remarks>
/// "You either restore Hit Points to the creature equal to that total or force the
/// creature to make a Constitution saving throw" — one roll, two uses, the chooser's
/// choice, so the choice is a parameter rather than two features.
/// </remarks>
public enum DivineSparkUse
{
    /// <summary>Restore hit points equal to 1d8 + the Cleric's Wisdom modifier.</summary>
    Heal,

    /// <summary>
    /// Force a Constitution saving throw: the total as Necrotic or Radiant damage on a
    /// failure, half (round down) on a success.
    /// </summary>
    Harm,
}

/// <summary>
/// The Cunning Strike effects this engine executes.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="Trip"/>, and the absences are the point. <b>Poison</b> reads "the
/// target has the Poisoned condition for 1 minute. At the end of each of its turns, the
/// Poisoned target repeats the save, ending the effect on itself on a success" — the
/// minute is modellable since timed durations landed, but the repeated save is an early
/// out the condition model does not express, so imposing it would hold the target a full
/// minute the book lets them escape. It also requires a Poisoner's Kit, which no
/// inventory models. <b>Withdraw</b> moves the Rogue half its Speed without provoking,
/// which needs movement resolved mid-attack. Daze, Knock Out and Obscure are level 14
/// and out of this game's range entirely.
/// </para>
/// <para>
/// Each printed effect costs Sneak Attack dice, and the SRD is precise that "you remove
/// the die before rolling" — so the cost is subtracted from the dice count, never from
/// the rolled total.
/// </para>
/// </remarks>
public enum CunningStrikeEffect
{
    /// <summary>No Cunning Strike effect added to this Sneak Attack.</summary>
    None,

    /// <summary>
    /// "Trip (Cost: 1d6). If the target is Large or smaller, it must succeed on a
    /// Dexterity saving throw or have the Prone condition."
    /// </summary>
    Trip,
}

/// <summary>A class feature a character has, and the level it came from.</summary>
/// <param name="Feature">Which feature.</param>
/// <param name="Level">The class level that granted it.</param>
public sealed record GrantedFeature(ClassFeature Feature, int Level);

/// <summary>
/// Maps printed SRD feature names onto the features this engine implements.
/// </summary>
/// <remarks>
/// Curated by hand and deliberately small, exactly like the inert-entry list in the
/// extractor. Adding a name here is a claim that the engine really does the thing, so
/// it must be added alongside the code that does it — never speculatively.
/// </remarks>
public static class ClassFeatureRegistry
{
    private static readonly IReadOnlyDictionary<string, ClassFeature> ByPrintedName =
        new Dictionary<string, ClassFeature>(StringComparer.OrdinalIgnoreCase)
        {
            ["Extra Attack"] = ClassFeature.ExtraAttack,
            ["Rage"] = ClassFeature.Rage,
            ["Unarmored Defense"] = ClassFeature.UnarmoredDefenseBarbarian,
            ["Reckless Attack"] = ClassFeature.RecklessAttack,
            ["Sneak Attack"] = ClassFeature.SneakAttack,
            ["Cunning Action"] = ClassFeature.CunningAction,
            ["Uncanny Dodge"] = ClassFeature.UncannyDodge,
            ["Second Wind"] = ClassFeature.SecondWind,
            ["Action Surge"] = ClassFeature.ActionSurge,
            ["Danger Sense"] = ClassFeature.DangerSense,
            ["Fast Movement"] = ClassFeature.FastMovement,
            ["Steady Aim"] = ClassFeature.SteadyAim,
            ["Expertise"] = ClassFeature.Expertise,

            // Deft Explorer's Expertise is the same rule under another name. Its other
            // halves — two languages, and the level 9 and 13 benefits — do nothing in a
            // fight, so mapping the name claims only what the engine does at levels 1-5.
            ["Deft Explorer"] = ClassFeature.Expertise,
            ["Fighting Style"] = ClassFeature.FightingStyle,
            ["Cunning Strike"] = ClassFeature.CunningStrike,
            ["Tactical Mind"] = ClassFeature.TacticalMind,
            ["Ability Score Improvement"] = ClassFeature.AbilityScoreImprovement,
            ["Weapon Mastery"] = ClassFeature.WeaponMastery,

            // Channel Divinity's two starting effects both execute (#369). Divine Spark
            // executes whole at levels 1-5 (its dice first grow at Cleric level 7).
            // Turn Undead's Frightened-plus-Incapacitated rider prints three early
            // outs — "ends early on the creature if it takes any damage, if you have
            // the Incapacitated condition, or if you die" — that used to have no hook:
            // ActiveCondition.EndsEarlyOnDamageOrSourceDown is that hook, read by
            // Encounter.BreakTurnEffectOnDamage (the bearer-side out, called from
            // every site that can drop a creature's hit points) and
            // Encounter.EndTurnEffectsWhoseSourceIsDown (the two source-side outs, the
            // same shape EndBrokenGrapples already reads off a grapple's source). Sear
            // Undead (level 5) maps to its own feature below and rides the same Turn
            // Undead call.
            //
            // One printed clause of Turn Undead is unimplemented, and it is accounted
            // rather than left in prose alone: "For that duration, it tries to move as
            // far from you as it can on its turns" is a movement compulsion — a rule,
            // not AI tactics — and Encounter.BeginTurn's own "cannot act" gate skips an
            // Incapacitated creature's turn outright before any tactics policy runs,
            // so a turned creature never reaches a turn to move on. Nothing in this
            // engine before Turn Undead needed a turn for a creature that cannot act
            // but is not immobile. This is not a refusal — the feature is claimed, and
            // Frightened and Incapacitated both land — and it is not merely a doc-
            // comment claim either: both conditions Turn Undead imposes carry the gap
            // on ActiveCondition.UnmodelledBehaviour, the same accounting
            // AppliedCondition.UnmodelledRequirement gives a stat-block rider. The seam
            // this needs — letting an Incapacitated-but-mobile creature take a turn at
            // all — is filed as #615, with the flee code's shape kept there rather than
            // left as dead code here. Read the gap precisely: the turned creature's
            // body stays exactly where it was turned rather than retreating, which is
            // not simply "weaker than print" — a frozen Undead can still block a
            // doorway or grant cover the book's fleeing version would not have offered.
            ["Channel Divinity"] = ClassFeature.ChannelDivinity,

            // Divine Order executes whole once a role is chosen: Protector grows the
            // printed proficiency lines the creation menus and the shop read (worn
            // proficiency is assumed at resolution — the stated reading on the attack
            // builder), and Thaumaturge grows the Cantrips column by one and adds its
            // Wisdom-based bonus to the sheet's Arcana and Religion skills. A draft
            // that chose neither keeps the name reported: the resolver re-adds it to
            // UnimplementedFeatures while the choice is Unspecified, because a mapped
            // name would otherwise vanish from the report without anything executing.
            ["Divine Order"] = ClassFeature.DivineOrder,

            // Level 5: rides Turn Undead (Channel Divinity, above) rather than being a
            // use of its own — Encounter.TurnUndead reads this feature to decide
            // whether to roll it.
            ["Sear Undead"] = ClassFeature.SearUndead,

            // Subclass features. The SRD prints exactly one subclass per class, so a
            // level 3+ character simply has it — there is no choice for a draft to
            // carry, and the reading is on CharacterResolver.ResolveFeatures.
            ["Improved Critical"] = ClassFeature.ImprovedCritical,
            ["Frenzy"] = ClassFeature.Frenzy,
            ["Disciple of Life"] = ClassFeature.DiscipleOfLife,
        };

    /// <summary>The implemented feature for a printed name, or null when not implemented.</summary>
    public static ClassFeature? Resolve(string printedName)
    {
        ArgumentNullException.ThrowIfNull(printedName);

        // The SRD qualifies some names in the level table — "Action Surge (one use)",
        // "Indomitable (two uses)" — while the feature's own heading is unqualified.
        var bare = printedName.Split('(')[0].Trim();

        return ByPrintedName.TryGetValue(bare, out var feature) ? feature : null;
    }

    /// <summary>Every printed name this engine implements, for reporting coverage.</summary>
    public static IReadOnlyCollection<string> ImplementedNames => (IReadOnlyCollection<string>)ByPrintedName.Keys;
}
