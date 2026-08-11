namespace SRDCombat.Core.Definitions;

/// <summary>
/// One row of a class's Features table: what the class gains at that level.
/// </summary>
/// <param name="Level">The class level, 1–20.</param>
/// <param name="ProficiencyBonus">The proficiency bonus at this level.</param>
/// <param name="FeatureNames">
/// The features gained, as printed. An empty list where the table prints "—".
/// </param>
/// <param name="SpellSlots">
/// Spell slots by spell level, from the "Spell Slots per Spell Level" columns. Empty
/// for a non-caster, and levels with no slots are omitted rather than stored as zero.
/// </param>
/// <param name="Resources">
/// The class's own columns, keyed by the printed header — "Rages", "Rage Damage",
/// "Second Wind", "Sneak Attack", "Channel Divinity", "Cantrips", "Prepared Spells".
/// </param>
public sealed record ClassLevel(
    int Level,
    int ProficiencyBonus,
    IReadOnlyList<string> FeatureNames,
    IReadOnlyDictionary<int, int> SpellSlots,
    IReadOnlyDictionary<string, string> Resources)
{
    /// <summary>True when this level grants at least one spell slot.</summary>
    public bool HasSpellSlots => SpellSlots.Count > 0;

    /// <summary>
    /// Reads one of the class's own columns as a number, for the many that are counts —
    /// Rages, Channel Divinity uses, Cantrips known. Null when absent or not a number.
    /// </summary>
    /// <remarks>
    /// The values are kept as printed strings rather than parsed on the way in, because
    /// the columns are not uniformly numeric: Rage Damage is "+2" and Sneak Attack is
    /// "1d6". Interpreting a column is the caller's job, and doing it here would have
    /// meant either a lossy conversion or a bespoke field per class.
    /// </remarks>
    public int? ResourceCount(string column) =>
        Resources.TryGetValue(column, out var value)
        && int.TryParse(value.TrimStart('+'), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var count)
            ? count
            : null;
}

/// <summary>
/// A character class, from the SRD's Classes chapter: its Core Traits table, its
/// Features table, and the prose of each feature.
/// </summary>
public sealed record ClassDefinition
{
    /// <summary>Stable slug — <c>class.barbarian</c>.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// The abilities the class runs on. Several print a choice ("Strength or
    /// Dexterity"), so this is a list rather than one value.
    /// </summary>
    public required IReadOnlyList<Ability> PrimaryAbilities { get; init; }

    /// <summary>The hit die's number of sides — 12 for "D12 per Barbarian level".</summary>
    public required int HitDieSides { get; init; }

    /// <summary>The two abilities the class is proficient in saving throws with.</summary>
    public required IReadOnlyList<Ability> SavingThrowProficiencies { get; init; }

    /// <summary>How many skills the class chooses — the 2 in "Choose 2: ...".</summary>
    public required int SkillChoiceCount { get; init; }

    /// <summary>The skills the class may choose from. Empty when <see cref="ChoosesAnySkill"/>.</summary>
    public required IReadOnlyList<string> SkillChoices { get; init; }

    /// <summary>
    /// True when the class chooses from every skill rather than a printed list — the
    /// Bard's "Choose any 3 skills". Distinct from an empty list caused by a bad parse,
    /// which is why it is a flag rather than an inferred special case.
    /// </summary>
    public bool ChoosesAnySkill { get; init; }

    /// <summary>The Weapon Proficiencies line, as printed.</summary>
    public required string WeaponProficiencies { get; init; }

    /// <summary>The Armor Training line, as printed. Empty for classes that get none.</summary>
    public required string ArmorTraining { get; init; }

    /// <summary>
    /// The Starting Equipment line, as printed. Deliberately unstructured for the same
    /// reason background equipment is: the packages name tools and trinkets this game
    /// has no definitions for, so splitting it would imply a precision it does not have.
    /// </summary>
    public required string StartingEquipment { get; init; }

    /// <summary>Levels 1–20, in order.</summary>
    public required IReadOnlyList<ClassLevel> Levels { get; init; }

    /// <summary>
    /// The prose of each class feature, classified by the same rule as everything else:
    /// a feature the model cannot express is Unmodelled and counted, never silent.
    /// </summary>
    public required IReadOnlyList<TraitEntry> Features { get; init; }

    /// <summary>True when any level grants spell slots.</summary>
    public bool IsSpellcaster => Levels.Any(level => level.HasSpellSlots);

    public required int SourcePage { get; init; }

    /// <summary>The row for a given class level, or null when out of range.</summary>
    public ClassLevel? AtLevel(int level) => Levels.FirstOrDefault(row => row.Level == level);
}
