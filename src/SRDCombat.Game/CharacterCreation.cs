using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>
/// What character creation may offer: every menu a client shows, enumerated here so the
/// clients hold no rules.
/// </summary>
/// <remarks>
/// <para>
/// The same division of labour the fight uses. A client renders options and collects
/// choices; which options exist, and how many may be taken, are rules — so they live
/// here, beside the content, and the resolver stays the final validator of the draft
/// the choices build. Phase 5's charter adds one constraint this class serves
/// directly: every choice carries its printed description, so the enumerations return
/// whole definitions — the SRD's own text is CC-BY and ships verbatim — rather than
/// bare names.
/// </para>
/// <para>
/// Two printed lines are read rather than modelled, with the readings stated here. A
/// class's <b>Weapon Proficiencies</b> line comes in three printed shapes: "Simple
/// weapons", "Simple and Martial weapons", and "Simple weapons and Martial weapons
/// that have the … property" — read as: Simple always; Martial when named; and when a
/// property clause follows, only Martial weapons carrying one of the named properties.
/// The <b>Armor Training</b> line names its categories outright ("Light and Medium
/// armor and Shields", or "None"), and is read by those names. Both readings are
/// checked against the closed set of twelve printed lines by a test, so a rewording in
/// a future extraction fails loudly rather than quietly offering a Wizard a greatsword.
/// </para>
/// </remarks>
public static class CharacterCreation
{
    /// <summary>
    /// The classes creation offers: the six launch classes, a kickoff decision — they
    /// cover every mechanical shape the engine must handle.
    /// </summary>
    private static readonly IReadOnlyList<string> LaunchClassIds =
    [
        "class.fighter",
        "class.rogue",
        "class.cleric",
        "class.wizard",
        "class.barbarian",
        "class.ranger",
    ];

    /// <summary>The fighting styles the engine executes; the rest stay reported unimplemented.</summary>
    public static IReadOnlyList<FightingStyle> ExecutedFightingStyles { get; } =
    [
        FightingStyle.Archery,
        FightingStyle.Defense,
    ];

    public static IReadOnlyList<ClassDefinition> Classes(SrdContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return LaunchClassIds.Select(id => content.ClassesById[id]).ToArray();
    }

    public static IReadOnlyList<SpeciesDefinition> Species(SrdContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return content.Species.OrderBy(species => species.Name, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<BackgroundDefinition> Backgrounds(SrdContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return content.Backgrounds.OrderBy(background => background.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// The skills this class chooses from, and how many — "Choose 2: …" off the Core
    /// Traits table, with the Bard-shaped "Choose any" expanding to every skill.
    /// </summary>
    public static (int Count, IReadOnlyList<string> Options) SkillChoices(ClassDefinition @class)
    {
        ArgumentNullException.ThrowIfNull(@class);

        return (@class.SkillChoiceCount, @class.ChoosesAnySkill ? SkillRules.AllSkills : @class.SkillChoices);
    }

    /// <summary>
    /// Whether the class has a Fighting Style feature by this level, read from its own
    /// printed feature headings — the same place the resolver checks the choice against.
    /// </summary>
    public static bool GrantsFightingStyle(ClassDefinition @class, int level)
    {
        ArgumentNullException.ThrowIfNull(@class);

        return @class.Features.Any(feature =>
            string.Equals(feature.Name, "Fighting Style", StringComparison.Ordinal)
            && feature.GrantedAtLevel is { } granted
            && granted <= level);
    }

    /// <summary>
    /// Whether the class has the Divine Order feature by this level — the same
    /// heading-based reading <see cref="GrantsFightingStyle"/> makes.
    /// </summary>
    public static bool GrantsDivineOrder(ClassDefinition @class, int level)
    {
        ArgumentNullException.ThrowIfNull(@class);

        return @class.Features.Any(feature =>
            string.Equals(feature.Name, "Divine Order", StringComparison.Ordinal)
            && feature.GrantedAtLevel is { } granted
            && granted <= level);
    }

    /// <summary>
    /// The printed text of the class's Divine Order feature, for a client offering the
    /// choice — the charter's "every choice carries its description", served verbatim.
    /// </summary>
    public static string DivineOrderText(ClassDefinition @class)
    {
        ArgumentNullException.ThrowIfNull(@class);

        return @class.Features
            .FirstOrDefault(feature => string.Equals(feature.Name, "Divine Order", StringComparison.Ordinal))
            ?.Text ?? string.Empty;
    }

    /// <summary>
    /// The weapons this character may use, per the printed line's reading — grown by
    /// Protector's "proficiency with Martial weapons" when that role was taken.
    /// </summary>
    public static IReadOnlyList<WeaponDefinition> WeaponOptions(
        SrdContent content,
        ClassDefinition @class,
        DivineOrder divineOrder = DivineOrder.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(@class);

        var line = @class.WeaponProficiencies;
        var protector = divineOrder == DivineOrder.Protector && GrantsDivineOrder(@class, level: 1);
        var martial = protector || line.Contains("Martial", StringComparison.Ordinal);

        // Protector's grant is unconditional — "proficiency with Martial weapons" —
        // so a property-restricted printed line is superseded rather than intersected.
        var restricted = !protector && line.Contains("that have the", StringComparison.Ordinal);

        var restriction = WeaponProperty.None;

        if (restricted)
        {
            if (line.Contains("Finesse", StringComparison.Ordinal))
            {
                restriction |= WeaponProperty.Finesse;
            }

            if (line.Contains("Light", StringComparison.Ordinal))
            {
                restriction |= WeaponProperty.Light;
            }
        }

        return content.Weapons
            .Where(weapon => weapon.Category == WeaponCategory.Simple
                || (martial && !restricted)
                || (martial && (weapon.Properties & restriction) != 0))
            .OrderBy(weapon => weapon.Category)
            .ThenBy(weapon => weapon.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The armor this character has training in, per the printed line's names — grown
    /// by Protector's "training with Heavy armor" when that role was taken.
    /// </summary>
    public static IReadOnlyList<ArmorDefinition> ArmorOptions(
        SrdContent content,
        ClassDefinition @class,
        DivineOrder divineOrder = DivineOrder.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(@class);

        var protector = divineOrder == DivineOrder.Protector && GrantsDivineOrder(@class, level: 1);

        return content.Armor
            .Where(armor => armor.Category switch
            {
                ArmorCategory.Light => @class.ArmorTraining.Contains("Light", StringComparison.Ordinal),
                ArmorCategory.Medium => @class.ArmorTraining.Contains("Medium", StringComparison.Ordinal),
                ArmorCategory.Heavy => protector || @class.ArmorTraining.Contains("Heavy", StringComparison.Ordinal),
                _ => false,
            })
            .OrderBy(armor => armor.Category)
            .ThenBy(armor => armor.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Whether the class's Armor Training extends to Shields.</summary>
    public static bool MayCarryShield(ClassDefinition @class)
    {
        ArgumentNullException.ThrowIfNull(@class);

        return @class.ArmorTraining.Contains("Shields", StringComparison.Ordinal);
    }

    /// <summary>
    /// How many kinds of weapon this class may master at this level — the resolver's
    /// own rule, mirrored: the printed Weapon Mastery column where one exists, and the
    /// feature prose's two for a class whose table prints none (the Rogue, Ranger and
    /// Paladin all say two). Zero without the feature at all.
    /// </summary>
    /// <remarks>
    /// The first version read only the column and quietly offered the Rogue no
    /// masteries — caught by the creation flow's scripted smoke session, not by a unit
    /// test, because the unit test had only asserted the classes with columns.
    /// </remarks>
    public static int MasteryAllowance(ClassDefinition @class, int level)
    {
        ArgumentNullException.ThrowIfNull(@class);

        if (@class.Levels.SingleOrDefault(row => row.Level == level)
            ?.ResourceCount("Weapon Mastery") is { } printed)
        {
            return printed;
        }

        var granted = @class.Features.Any(feature =>
            string.Equals(feature.Name, "Weapon Mastery", StringComparison.Ordinal)
            && feature.GrantedAtLevel is { } at
            && at <= level);

        return granted ? 2 : 0;
    }

    /// <summary>
    /// The weapons whose mastery this character could unlock: proficient kinds whose
    /// property the engine executes — Push and Nick stay off the menu for the reasons
    /// on <see cref="WeaponMasteryRules"/>, exactly as the resolver would refuse them.
    /// </summary>
    public static IReadOnlyList<WeaponDefinition> MasteryOptions(
        SrdContent content,
        ClassDefinition @class,
        DivineOrder divineOrder = DivineOrder.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(@class);

        return WeaponOptions(content, @class, divineOrder)
            .Where(weapon => WeaponMasteryRules.Executes(weapon.Mastery))
            .ToArray();
    }

    /// <summary>The spells creation may offer this class — the curated, verified set.</summary>
    public static IReadOnlyList<SpellDefinition> SpellOptions(SrdContent content, ClassDefinition @class)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(@class);

        return PreparableSpells.For(content, @class.Id);
    }

    /// <summary>
    /// The printed Cantrips and Prepared Spells columns at a level — how many of each
    /// kind the plan will actually prepare there. Thaumaturge's "one extra cantrip
    /// from the Cleric spell list" grows the first number by one.
    /// </summary>
    public static (int Cantrips, int Prepared) SpellAllowances(
        ClassDefinition @class,
        int level,
        DivineOrder divineOrder = DivineOrder.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(@class);

        var row = @class.Levels.SingleOrDefault(candidate => candidate.Level == level);

        var thaumaturge = divineOrder == DivineOrder.Thaumaturge && GrantsDivineOrder(@class, level)
            ? 1
            : 0;

        return ((row?.ResourceCount("Cantrips") ?? 0) + thaumaturge, row?.ResourceCount("Prepared Spells") ?? 0);
    }
}
