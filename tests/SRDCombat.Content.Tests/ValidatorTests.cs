using SRDCombat.Content.Validation;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Content.Tests;

/// <summary>
/// Covers the checks that exist specifically to catch a bad extraction. Each one is
/// asserted by deliberately breaking a known-good monster in one place, so a passing
/// test means the check actually fires rather than that the fixture happened to be
/// clean.
/// </summary>
public class ValidatorTests
{
    [Fact]
    public void HitPointsDisagreeingWithHitDice_IsAnError()
    {
        // The single strongest check available: the SRD prints "HP 11 (2d8 + 2)" and 11
        // is exactly that expression's average, so a mismatch means one was misread.
        var monster = Monster() with { HitPoints = 12 };

        AssertHasCode(monster, "monster.hit_points.disagree_with_dice", ValidationSeverity.Error);
    }

    [Fact]
    public void ADamageAverageDisagreeingWithItsDice_IsAnError()
    {
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Scimitar",
                    MonsterEntrySection.Action,
                    "Melee Attack Roll: +3, reach 5 ft. Hit: 9 (1d6 + 1) Slashing damage.",
                    new MonsterAttack(
                        AttackKind.Melee,
                        3,
                        5,
                        null,
                        null,
                        [new AttackDamage(DiceExpression.Parse("1d6 + 1"), DamageType.Slashing, 9)])),
            ],
        };

        AssertHasCode(monster, "monster.attack.damage_disagrees_with_average", ValidationSeverity.Error);
    }

    [Fact]
    public void AProficiencyBonusDisagreeingWithChallengeRating_IsAnError()
    {
        var monster = Monster() with { ProficiencyBonus = 5 };

        AssertHasCode(monster, "monster.proficiency.disagrees_with_challenge_rating", ValidationSeverity.Error);
    }

    [Fact]
    public void AnExperienceValueDisagreeingWithChallengeRating_IsOnlyAWarning()
    {
        // Deliberately not an error: the SRD itself is inconsistent here — the Archmage
        // prints CR 12 with XP 8,000 against its own table's 8,400 — and the printed
        // value is kept rather than silently overridden.
        var monster = Monster() with { ExperiencePoints = 30 };

        AssertHasCode(monster, "monster.experience.disagrees_with_challenge_rating", ValidationSeverity.Warning);
        Assert.Empty(MonsterValidator.Validate([monster]).Errors);
    }

    [Fact]
    public void ASaveThatIsNeitherTheModifierNorProficient_IsAWarning()
    {
        // How a lost minus sign in the PDF's text layer surfaces.
        var abilities = Monster().Abilities.ToDictionary(pair => pair.Key, pair => pair.Value);
        abilities[Ability.Intelligence] = new MonsterAbility(Score: 6, SaveBonus: 2);

        AssertHasCode(
            Monster() with { Abilities = abilities },
            "monster.ability.save_unexplained",
            ValidationSeverity.Warning);
    }

    [Fact]
    public void DuplicateIds_AreAnError()
    {
        var result = MonsterValidator.Validate([Monster(), Monster()]);

        Assert.Contains(result.Errors, issue => issue.Code == "monster.id.duplicate");
    }

    [Fact]
    public void AKnownGoodMonster_ProducesNothing() =>
        Assert.Empty(MonsterValidator.Validate([Monster()]).Issues);

    [Fact]
    public void SpellcastingWithoutAUsageTier_IsAnError()
    {
        var monster = Monster() with
        {
            Entries = [new MonsterEntry("Spellcasting", MonsterEntrySection.Action, "The priest casts spells.")],
        };

        var issues = MonsterValidator.Validate([monster], [Spell("Light")]).Issues;

        Assert.Contains(issues, issue => issue.Code == "monster.spellcasting.usage_tier_missing");
    }

    [Fact]
    public void SpellcastingTierNamingAnUnknownSpell_IsAnError()
    {
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Spellcasting",
                    MonsterEntrySection.Action,
                    "The priest casts one of the following spells: At Will: Light, Longstrider."),
            ],
        };

        var issues = MonsterValidator.Validate([monster], [Spell("Light")]).Issues;

        Assert.Contains(issues, issue => issue.Code == "monster.spellcasting.spell_unknown"
            && issue.Message.Contains("Longstrider", StringComparison.Ordinal));
    }

    [Fact]
    public void SpellcastingSpellNotesWithCommas_AreNotMistakenForSpellNames()
    {
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Spellcasting",
                    MonsterEntrySection.Action,
                    "The dragon casts one of the following spells: At Will: Shapechange " +
                    "(Beast or Humanoid form only, no Temporary Hit Points gained), Speak with Animals."),
            ],
        };

        Assert.Empty(MonsterValidator.Validate([monster], [Spell("Shapechange"), Spell("Speak with Animals")]).Issues);
    }

    [Fact]
    public void LegendaryActionEntriesWithoutAUsesCount_IsAnError()
    {
        // The Uses count is a separate extracted field (#423) — an entry present with
        // no count means the preamble was lost rather than merely misplaced.
        var monster = Monster() with
        {
            Entries = [new MonsterEntry("Lash", MonsterEntrySection.LegendaryAction, "The dragon makes one attack.")],
        };

        AssertHasCode(monster, "monster.legendary_action_uses.missing", ValidationSeverity.Error);
    }

    [Fact]
    public void ALegendaryActionUsesCountWithoutAnyLegendaryActionEntries_IsAnError()
    {
        var monster = Monster() with { LegendaryActionUses = 3 };

        AssertHasCode(monster, "monster.legendary_action_uses.unexpected", ValidationSeverity.Error);
    }

    [Fact]
    public void AnEntryStillCarryingTheLegendaryActionsPreamble_IsAnError()
    {
        // The regression guard #423 asks for directly: nothing extracted after this fix
        // should ever fold "Legendary Action Uses" prose into an entry's own text.
        var monster = Monster() with
        {
            Entries =
            [
                new MonsterEntry(
                    "Dominate Mind",
                    MonsterEntrySection.Action,
                    "Wisdom Saving Throw: DC 16. Legendary Action Uses: 3 (4 in Lair). Immediately after " +
                    "another creature's turn, the aboleth can expend a use to take one of the following " +
                    "actions. The aboleth regains all expended uses at the start of each of its turns."),
                new MonsterEntry("Lash", MonsterEntrySection.LegendaryAction, "The aboleth makes one attack."),
            ],
            LegendaryActionUses = 3,
            LegendaryActionUsesInLair = 4,
        };

        AssertHasCode(monster, "monster.entry.legendary_preamble_embedded", ValidationSeverity.Error);
    }

    [Fact]
    public void AWellFormedLegendaryActionsSection_ProducesNothing()
    {
        var monster = Monster() with
        {
            Entries = [new MonsterEntry("Lash", MonsterEntrySection.LegendaryAction, "The dragon makes one attack.")],
            LegendaryActionUses = 3,
            LegendaryActionUsesInLair = 4,
        };

        Assert.Empty(MonsterValidator.Validate([monster]).Issues);
    }

    [Fact]
    public void AVersatileWeaponWithoutTwoHandedDamage_IsAnError()
    {
        var weapon = Weapon() with { Properties = WeaponProperty.Versatile, VersatileDamage = null };

        var result = EquipmentValidator.ValidateWeapons([weapon]);

        Assert.Contains(result.Errors, issue => issue.Code == "weapon.versatile.inconsistent");
    }

    [Fact]
    public void AnAmmunitionWeaponWithoutARangeBand_IsAnError()
    {
        var weapon = Weapon() with
        {
            Kind = WeaponKind.Ranged,
            Properties = WeaponProperty.Ammunition,
            AmmunitionKind = "Bolt",
            Range = null,
        };

        var result = EquipmentValidator.ValidateWeapons([weapon]);

        Assert.Contains(result.Errors, issue => issue.Code == "weapon.range.inconsistent");
    }

    [Fact]
    public void MediumArmorWithoutADexterityCap_IsAnError()
    {
        var armor = new ArmorDefinition
        {
            Id = "armor.test",
            Name = "Test",
            Category = ArmorCategory.Medium,
            BaseArmorClass = 13,
            AddsDexterityModifier = true,
            MaximumDexterityModifier = null,
            StealthDisadvantage = false,
            WeightPounds = 20m,
            CostCopper = 5_000,
        };

        var result = EquipmentValidator.ValidateArmor([armor]);

        Assert.Contains(result.Errors, issue => issue.Code == "armor.dexterity.missing_cap");
    }

    [Fact]
    public void ThrowIfInvalid_NamesEveryError()
    {
        var result = MonsterValidator.Validate([Monster() with { HitPoints = 12, ProficiencyBonus = 5 }]);

        var exception = Assert.Throws<ContentValidationException>(() => result.ThrowIfInvalid("monsters.json"));

        Assert.Contains("monster.hit_points.disagree_with_dice", exception.Message, StringComparison.Ordinal);
        Assert.Contains("monster.proficiency.disagrees_with_challenge_rating", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertHasCode(MonsterDefinition monster, string code, ValidationSeverity severity)
    {
        var issues = MonsterValidator.Validate([monster]).Issues;

        Assert.Contains(issues, issue => issue.Code == code && issue.Severity == severity);
    }

    /// <summary>A valid monster, modelled on the printed Bandit stat block.</summary>
    private static MonsterDefinition Monster() => new()
    {
        Id = "monster.bandit",
        Name = "Bandit",
        Sizes = [CreatureSize.Medium],
        Type = CreatureType.Humanoid,
        Alignment = "Neutral",
        ArmorClass = 12,
        InitiativeBonus = 1,
        HitPoints = 11,
        HitDice = DiceExpression.Parse("2d8 + 2"),
        Speeds = new Dictionary<MovementMode, int> { [MovementMode.Walk] = 30 },
        Abilities = new Dictionary<Ability, MonsterAbility>
        {
            [Ability.Strength] = new(11, 0),
            [Ability.Dexterity] = new(12, 1),
            [Ability.Constitution] = new(12, 1),
            [Ability.Intelligence] = new(10, 0),
            [Ability.Wisdom] = new(10, 0),
            [Ability.Charisma] = new(10, 0),
        },
        Skills = new Dictionary<string, int>(),
        DamageResponses = new Dictionary<DamageType, DamageResponse>(),
        ConditionImmunities = [],
        Senses = [],
        PassivePerception = 10,
        Languages = ["Common"],
        Gear = [],
        ChallengeRating = 0.125m,
        ExperiencePoints = 25,
        ProficiencyBonus = 2,
        Entries = [],
        SourcePage = 261,
    };

    private static SpellDefinition Spell(string name) => new()
    {
        Id = "spell." + name.ToLowerInvariant(),
        Name = name,
        Level = 0,
        School = MagicSchool.Evocation,
        Classes = [],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        RangeText = "Self",
        Components = SpellComponents.Verbal,
        DurationText = "Instantaneous",
        Text = string.Empty,
        Mechanics = EntryMechanics.Unmodelled,
        SourcePage = 1,
    };

    /// <summary>A valid weapon, modelled on the printed Longsword row.</summary>
    private static WeaponDefinition Weapon() => new()
    {
        Id = "weapon.longsword",
        Name = "Longsword",
        Category = WeaponCategory.Martial,
        Kind = WeaponKind.Melee,
        Damage = DiceExpression.Parse("1d8"),
        DamageType = DamageType.Slashing,
        Properties = WeaponProperty.Versatile,
        VersatileDamage = DiceExpression.Parse("1d10"),
        Mastery = WeaponMastery.Sap,
        WeightPounds = 3m,
        CostCopper = 1_500,
    };
}
