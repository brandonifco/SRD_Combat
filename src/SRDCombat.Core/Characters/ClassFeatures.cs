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
