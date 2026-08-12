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
