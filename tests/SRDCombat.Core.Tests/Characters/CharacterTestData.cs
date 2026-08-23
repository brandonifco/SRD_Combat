using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Characters;

/// <summary>
/// Hand-built species, classes and backgrounds for resolver tests.
/// </summary>
/// <remarks>
/// Deliberately not loaded from the SRD content, for the same reason the frozen
/// transcript is not: a resolver test should fail when the resolver changes, not when
/// the bestiary is re-extracted. `SRDCombat.Content.Tests` covers the real content.
/// </remarks>
internal static class CharacterTestData
{
    public static SpeciesDefinition Species(
        int speedFeet = 30,
        CreatureSize size = CreatureSize.Medium,
        IReadOnlyList<TraitEntry>? traits = null) => new()
    {
        Id = "species.test",
        Name = "Testfolk",
        CreatureType = CreatureType.Humanoid,
        Sizes = [size],
        SpeedFeet = speedFeet,
        Traits = traits ?? [],
        SourcePage = 1,
    };

    /// <summary>A species trait with no mechanics — SpeciesTraitRegistry has nothing to resolve it to.</summary>
    public static TraitEntry Trait(string name) => new(name, $"{name} does something on paper only.");

    public static BackgroundDefinition Background(params Ability[] abilities) => new()
    {
        Id = "background.test",
        Name = "Tester",
        AbilityScores = abilities.Length > 0
            ? abilities
            : [Ability.Strength, Ability.Dexterity, Ability.Constitution],
        FeatName = "Savage Attacker",
        SkillProficiencies = ["Athletics", "Intimidation"],
        ToolProficiency = "Gaming Set",
        Equipment = "50 GP",
        SourcePage = 1,
    };

    /// <summary>A class with a level table and whatever features are named per level.</summary>
    public static ClassDefinition Class(
        string name = "Tester",
        int hitDie = 10,
        Ability[]? saves = null,
        IReadOnlyDictionary<int, string[]>? featuresByLevel = null,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>>? spellSlotsByLevel = null,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>>? resourcesByLevel = null)
    {
        var levels = Enumerable.Range(1, 20)
            .Select(level => new ClassLevel(
                level,
                Core.Rules.AdvancementRules.ProficiencyBonusForLevel(level),
                featuresByLevel?.GetValueOrDefault(level) ?? [],
                spellSlotsByLevel?.GetValueOrDefault(level) ?? new Dictionary<int, int>(),
                resourcesByLevel?.GetValueOrDefault(level) ?? new Dictionary<string, string>()))
            .ToArray();

        return new ClassDefinition
        {
            Id = "class." + name.ToLowerInvariant(),
            Name = name,
            PrimaryAbilities = [Ability.Strength],
            HitDieSides = hitDie,
            SavingThrowProficiencies = saves ?? [Ability.Strength, Ability.Constitution],
            SkillChoiceCount = 2,
            SkillChoices = ["Athletics", "Perception", "Survival", "Nature"],
            WeaponProficiencies = "Simple and Martial weapons",
            ArmorTraining = "Light and Medium armor and Shields",
            StartingEquipment = "A Greataxe and 15 GP",
            Levels = levels,
            Features = [],
            SourcePage = 1,
        };
    }

    public static WeaponDefinition Weapon(
        string id = "weapon.longsword",
        string name = "Longsword",
        string damage = "1d8",
        WeaponKind kind = WeaponKind.Melee,
        WeaponProperty properties = WeaponProperty.None,
        WeaponRange? range = null) => new()
    {
        Id = id,
        Name = name,
        Category = WeaponCategory.Martial,
        Kind = kind,
        Damage = DiceExpression.Parse(damage),
        DamageType = DamageType.Slashing,
        Properties = properties,
        Range = range,
        Mastery = WeaponMastery.Sap,
        WeightPounds = 3m,
        CostCopper = 1_500,
    };

    public static ArmorDefinition Armor(
        string id = "armor.chain-mail",
        string name = "Chain Mail",
        ArmorCategory category = ArmorCategory.Heavy,
        int baseArmorClass = 16,
        bool addsDexterity = false,
        int? cap = null) => new()
    {
        Id = id,
        Name = name,
        Category = category,
        BaseArmorClass = baseArmorClass,
        AddsDexterityModifier = addsDexterity,
        MaximumDexterityModifier = cap,
        MinimumStrength = category == ArmorCategory.Heavy ? 13 : null,
        StealthDisadvantage = true,
        WeightPounds = 55m,
        CostCopper = 7_500,
    };

    public static CharacterBuildContent Content(
        SpeciesDefinition? species = null,
        ClassDefinition? classDefinition = null,
        BackgroundDefinition? background = null,
        IEnumerable<WeaponDefinition>? weapons = null,
        IEnumerable<ArmorDefinition>? armor = null) =>
        new(
            species ?? Species(),
            classDefinition ?? Class(),
            background ?? Background(),
            (weapons ?? [Weapon()]).ToDictionary(weapon => weapon.Id, StringComparer.Ordinal),
            (armor ?? [Armor(), Shield()]).ToDictionary(item => item.Id, StringComparer.Ordinal));

    public static ArmorDefinition Shield() => new()
    {
        Id = "armor.shield",
        Name = "Shield",
        Category = ArmorCategory.Shield,
        BaseArmorClass = 2,
        AddsDexterityModifier = false,
        StealthDisadvantage = false,
        WeightPounds = 6m,
        CostCopper = 1_000,
    };

    /// <summary>A draft with the standard array laid out sensibly for a melee character.</summary>
    public static CharacterDraft Draft(
        int level = 1,
        IReadOnlyDictionary<Ability, int>? scores = null,
        Ability primary = Ability.Strength,
        Ability secondary = Ability.Constitution,
        string? armorId = null,
        bool hasShield = false,
        IReadOnlyList<string>? weaponIds = null) => new()
    {
        Name = "Test Character",
        SpeciesId = "species.test",
        ClassId = "class.tester",
        BackgroundId = "background.test",
        Level = level,
        BaseAbilityScores = scores ?? new Dictionary<Ability, int>
        {
            [Ability.Strength] = 15,
            [Ability.Dexterity] = 13,
            [Ability.Constitution] = 14,
            [Ability.Intelligence] = 10,
            [Ability.Wisdom] = 12,
            [Ability.Charisma] = 8,
        },
        PrimaryIncrease = primary,
        SecondaryIncrease = secondary,
        ChosenSkills = ["Perception"],
        WeaponIds = weaponIds ?? ["weapon.longsword"],
        ArmorId = armorId,
        HasShield = hasShield,
    };
}
