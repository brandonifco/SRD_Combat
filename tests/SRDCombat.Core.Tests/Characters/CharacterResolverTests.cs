using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Characters;

public class CharacterResolverTests
{
    [Theory]
    [InlineData(8, -1)]
    [InlineData(9, -1)]
    [InlineData(10, 0)]
    [InlineData(11, 0)]
    [InlineData(15, 2)]
    [InlineData(20, 5)]
    [InlineData(1, -5)]
    public void ModifierFor_RoundsDown(int score, int expected) =>
        Assert.Equal(expected, AbilityRules.ModifierFor(score));

    [Fact]
    public void TheBackgroundGrantsTheAbilityIncreases_NotTheSpecies()
    {
        // A 2024 change worth pinning: species grant no ability scores at all.
        var sheet = CharacterResolver.Resolve(CharacterTestData.Draft(), CharacterTestData.Content());

        // 15 base + 2 from the background's primary increase.
        Assert.Equal(17, sheet.AbilityScores[Ability.Strength]);
        Assert.Equal(15, sheet.AbilityScores[Ability.Constitution]);
        Assert.Equal(13, sheet.AbilityScores[Ability.Dexterity]);
    }

    [Fact]
    public void TheOneEachIncreaseRaisesAllThree()
    {
        var draft = CharacterTestData.Draft() with { IncreaseChoice = AbilityIncreaseChoice.OneEach };

        var sheet = CharacterResolver.Resolve(draft, CharacterTestData.Content());

        Assert.Equal(16, sheet.AbilityScores[Ability.Strength]);
        Assert.Equal(14, sheet.AbilityScores[Ability.Dexterity]);
        Assert.Equal(15, sheet.AbilityScores[Ability.Constitution]);
    }

    [Fact]
    public void AnIncreaseTheBackgroundDoesNotOfferIsRejected()
    {
        var draft = CharacterTestData.Draft(primary: Ability.Charisma);

        Assert.Throws<ArgumentException>(() =>
            CharacterResolver.Resolve(draft, CharacterTestData.Content()));
    }

    [Fact]
    public void AbilityScoresAreCappedAtTwenty()
    {
        var draft = CharacterTestData.Draft(scores: new Dictionary<Ability, int>
        {
            [Ability.Strength] = 19,
            [Ability.Dexterity] = 10,
            [Ability.Constitution] = 10,
            [Ability.Intelligence] = 10,
            [Ability.Wisdom] = 10,
            [Ability.Charisma] = 10,
        });

        Assert.Equal(20, CharacterResolver.Resolve(draft, CharacterTestData.Content()).AbilityScores[Ability.Strength]);
    }

    [Theory]
    // Level 1 is always the die's maximum plus Constitution: d10 + 2 = 12.
    [InlineData(1, 12)]
    // Each level after adds the fixed value (6 for a d10) plus Constitution: +8 each.
    [InlineData(2, 20)]
    [InlineData(3, 28)]
    [InlineData(5, 44)]
    public void HitPointsUseTheMaximumAtLevelOneAndTheFixedValueAfter(int level, int expected)
    {
        var sheet = CharacterResolver.Resolve(
            CharacterTestData.Draft(level: level),
            CharacterTestData.Content());

        Assert.Equal(expected, sheet.MaximumHitPoints);
    }

    [Fact]
    public void HitPointsCanBeRolledInsteadOfAveraged()
    {
        // d10s rolling 3 and 9 at levels 2 and 3, plus +2 Constitution each.
        var sheet = CharacterResolver.Resolve(
            CharacterTestData.Draft(level: 3),
            CharacterTestData.Content(),
            HitPointMethod.Rolled,
            new ScriptedRandomSource(3, 9));

        Assert.Equal(12 + 5 + 11, sheet.MaximumHitPoints);
    }

    [Fact]
    public void FastMovementAddsTenFeetOutsideHeavyArmor()
    {
        var content = CharacterTestData.Content(
            classDefinition: CharacterTestData.Class(
                featuresByLevel: new Dictionary<int, string[]> { [5] = ["Fast Movement"] }));

        // Not yet granted at level 4; granted at 5; withheld again in Heavy armour,
        // which is the printed gate.
        Assert.Equal(30, CharacterResolver.Resolve(CharacterTestData.Draft(level: 4), content).SpeedFeet);
        Assert.Equal(40, CharacterResolver.Resolve(CharacterTestData.Draft(level: 5), content).SpeedFeet);
        Assert.Equal(
            30,
            CharacterResolver.Resolve(
                CharacterTestData.Draft(level: 5, armorId: "armor.chain-mail"),
                content).SpeedFeet);
    }

    [Fact]
    public void ArmorClassComesFromWhatIsWorn()
    {
        var content = CharacterTestData.Content();

        // Unarmoured: 10 + Dex 1.
        Assert.Equal(11, CharacterResolver.Resolve(CharacterTestData.Draft(), content).ArmorClass);

        // Chain Mail is a flat 16 and ignores Dexterity entirely.
        var heavy = CharacterResolver.Resolve(
            CharacterTestData.Draft(armorId: "armor.chain-mail"),
            content);
        Assert.Equal(16, heavy.ArmorClass);

        // A Shield adds 2 on top.
        var shielded = CharacterResolver.Resolve(
            CharacterTestData.Draft(armorId: "armor.chain-mail", hasShield: true),
            content);
        Assert.Equal(18, shielded.ArmorClass);
    }

    [Fact]
    public void MediumArmorCapsTheDexterityModifier()
    {
        var content = CharacterTestData.Content(
            armor: [CharacterTestData.Armor("armor.half-plate", "Half Plate", ArmorCategory.Medium, 15, true, 2)]);

        var draft = CharacterTestData.Draft(armorId: "armor.half-plate", secondary: Ability.Dexterity) with
        {
            BaseAbilityScores = new Dictionary<Ability, int>
            {
                [Ability.Strength] = 10,
                [Ability.Dexterity] = 18,
                [Ability.Constitution] = 10,
                [Ability.Intelligence] = 10,
                [Ability.Wisdom] = 10,
                [Ability.Charisma] = 10,
            },
            PrimaryIncrease = Ability.Strength,
        };

        // Dexterity 19 is a +4 modifier, but Medium armour caps the contribution at 2.
        var sheet = CharacterResolver.Resolve(draft, content);
        Assert.Equal(17, sheet.ArmorClass);
    }

    [Fact]
    public void BarbarianUnarmoredDefenseUsesConstitution()
    {
        var content = CharacterTestData.Content(
            classDefinition: CharacterTestData.Class(
                "Barbarian",
                hitDie: 12,
                featuresByLevel: new Dictionary<int, string[]> { [1] = ["Rage", "Unarmored Defense"] }));

        var sheet = CharacterResolver.Resolve(CharacterTestData.Draft(), content);

        // 10 + Dex 1 + Con 2.
        Assert.Equal(13, sheet.ArmorClass);
        Assert.Contains("Unarmored Defense", sheet.ArmorClassSource, StringComparison.Ordinal);

        // Wearing armour takes precedence, as the feature only applies while unarmoured.
        var armoured = CharacterResolver.Resolve(
            CharacterTestData.Draft(armorId: "armor.chain-mail"),
            content);
        Assert.Equal(16, armoured.ArmorClass);
    }

    [Fact]
    public void SavingThrowsAddProficiencyOnlyWhereTheClassHasIt()
    {
        var sheet = CharacterResolver.Resolve(
            CharacterTestData.Draft(level: 5),
            CharacterTestData.Content());

        // Strength 17 (+3) with proficiency +3 at level 5.
        Assert.Equal(6, sheet.SavingThrows[Ability.Strength]);
        Assert.Equal(5, sheet.SavingThrows[Ability.Constitution]);

        // Dexterity 13 (+1), not a proficient save.
        Assert.Equal(1, sheet.SavingThrows[Ability.Dexterity]);
    }

    [Fact]
    public void SkillsCombineClassChoicesWithBackgroundProficiencies()
    {
        var sheet = CharacterResolver.Resolve(CharacterTestData.Draft(), CharacterTestData.Content());

        var perception = sheet.Skills.Single(skill => skill.Skill == "Perception");
        Assert.True(perception.IsProficient);

        // Athletics comes from the background rather than the class choice.
        var athletics = sheet.Skills.Single(skill => skill.Skill == "Athletics");
        Assert.True(athletics.IsProficient);
        Assert.Equal(5, athletics.Bonus);

        var arcana = sheet.Skills.Single(skill => skill.Skill == "Arcana");
        Assert.False(arcana.IsProficient);
        Assert.Equal(0, arcana.Bonus);

        Assert.Equal(18, sheet.Skills.Count);
    }

    [Fact]
    public void WeaponAttacksUseTheRightAbility()
    {
        var content = CharacterTestData.Content(weapons:
        [
            CharacterTestData.Weapon(),
            CharacterTestData.Weapon("weapon.dagger", "Dagger", "1d4", WeaponKind.Melee, WeaponProperty.Finesse),
            CharacterTestData.Weapon(
                "weapon.shortbow",
                "Shortbow",
                "1d6",
                WeaponKind.Ranged,
                WeaponProperty.Ammunition,
                new WeaponRange(80, 320)),
        ]);

        var draft = CharacterTestData.Draft(
            weaponIds: ["weapon.longsword", "weapon.dagger", "weapon.shortbow"]);

        var sheet = CharacterResolver.Resolve(draft, content);

        // Strength 17 (+3), Dexterity 13 (+1), proficiency +2.
        var longsword = sheet.Attacks.Single(attack => attack.Name == "Longsword");
        Assert.Equal(5, longsword.AttackBonus);
        Assert.Equal("1d8 + 3", longsword.Damage[0].Amount.ToString());

        // Finesse takes the better of Strength and Dexterity.
        Assert.Equal(5, sheet.Attacks.Single(attack => attack.Name == "Dagger").AttackBonus);

        // A ranged weapon uses Dexterity regardless.
        var bow = sheet.Attacks.Single(attack => attack.Name == "Shortbow");
        Assert.Equal(3, bow.AttackBonus);
        Assert.Equal(80, bow.NormalRangeFeet);
    }

    [Fact]
    public void FeaturesAreGrantedAtTheLevelThatPrintsThem()
    {
        var content = CharacterTestData.Content(
            classDefinition: CharacterTestData.Class(
                "Fighter",
                featuresByLevel: new Dictionary<int, string[]>
                {
                    [1] = ["Fighting Style", "Second Wind"],
                    [2] = ["Action Surge (one use)"],
                    [5] = ["Extra Attack"],
                }));

        var level1 = CharacterResolver.Resolve(CharacterTestData.Draft(level: 1), content);
        Assert.True(level1.Has(ClassFeature.SecondWind));
        Assert.False(level1.Has(ClassFeature.ExtraAttack));
        Assert.Equal(1, level1.AttacksPerAction);

        var level5 = CharacterResolver.Resolve(CharacterTestData.Draft(level: 5), content);
        Assert.True(level5.Has(ClassFeature.ExtraAttack));
        Assert.Equal(2, level5.AttacksPerAction);

        // "Action Surge (one use)" resolves despite the qualifier the table prints.
        Assert.True(level5.Has(ClassFeature.ActionSurge));
    }

    [Fact]
    public void UnimplementedFeaturesAreListedRatherThanSilentlyDropped()
    {
        // The same rule as the content model: a gap is carried on the object and
        // countable, never an absence nobody can see.
        var content = CharacterTestData.Content(
            classDefinition: CharacterTestData.Class(
                "Cleric",
                featuresByLevel: new Dictionary<int, string[]>
                {
                    [1] = ["Spellcasting", "Divine Order"],
                    [2] = ["Channel Divinity"],
                    [3] = ["Cleric Subclass"],
                }));

        var sheet = CharacterResolver.Resolve(CharacterTestData.Draft(level: 3), content);

        Assert.Equal(
            ["Channel Divinity", "Divine Order", "Spellcasting"],
            sheet.UnimplementedFeatures);

        // A subclass placeholder is not a feature in its own right.
        Assert.DoesNotContain("Cleric Subclass", sheet.UnimplementedFeatures);
    }

    [Fact]
    public void SpellSlotsComeFromTheClassTable()
    {
        var content = CharacterTestData.Content(
            classDefinition: CharacterTestData.Class(
                "Wizard",
                hitDie: 6,
                spellSlotsByLevel: new Dictionary<int, IReadOnlyDictionary<int, int>>
                {
                    [5] = new Dictionary<int, int> { [1] = 4, [2] = 3, [3] = 2 },
                }));

        var sheet = CharacterResolver.Resolve(CharacterTestData.Draft(level: 5), content);

        Assert.Equal(new Dictionary<int, int> { [1] = 4, [2] = 3, [3] = 2 }, sheet.SpellSlots);
    }

    [Fact]
    public void ALevelBeyondWhatTheGameSupportsIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterResolver.Resolve(CharacterTestData.Draft(level: 6), CharacterTestData.Content()));

    [Fact]
    public void AnUnknownWeaponIsRejectedRatherThanSkipped() =>
        Assert.Throws<ArgumentException>(() =>
            CharacterResolver.Resolve(
                CharacterTestData.Draft(weaponIds: ["weapon.not-a-thing"]),
                CharacterTestData.Content()));
}
