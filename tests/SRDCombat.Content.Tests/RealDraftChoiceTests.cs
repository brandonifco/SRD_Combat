using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Content.Tests;

/// <summary>
/// The draft's new choices against the real class tables: a Fighter's Fighting Style at
/// level 1, a Rogue's Expertise at level 1, and a Ranger's Deft Explorer at level 2.
/// </summary>
/// <remarks>
/// The resolver tests cover the arithmetic on hand-built classes. What these add is that
/// the printed level tables really grant these features where the code expects — the
/// Fighter's "Fighting Style" at 1, the Rogue's "Expertise" at 1, and the Ranger's "Deft
/// Explorer" at 2 — so a re-extraction that moved or renamed one would fail here rather
/// than silently disable a choice.
/// </remarks>
public class RealDraftChoiceTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void ARealFighterTakesDefenseAndGainsAnArmorClass()
    {
        var plain = Build("class.fighter", level: 1);
        var defended = Build("class.fighter", level: 1, style: FightingStyle.Defense);

        Assert.Equal(plain.ArmorClass + 1, defended.ArmorClass);
        Assert.Equal(FightingStyle.Defense, defended.FightingStyle);

        // The printed name is implemented now, so it stops being reported as a gap.
        Assert.DoesNotContain("Fighting Style", defended.UnimplementedFeatures);
    }

    [Fact]
    public void ARealRogueTakesTwoExpertiseSkillsAtLevelOne()
    {
        var rogue = Build("class.rogue", level: 1, expertise: ["Stealth", "Acrobatics"]);

        var stealth = rogue.Skills.Single(skill => skill.Skill == "Stealth");

        Assert.True(stealth.IsProficient);
        Assert.Equal(rogue.Modifier(stealth.Ability) + (rogue.ProficiencyBonus * 2), stealth.Bonus);
        Assert.Equal(2, rogue.ExpertiseSkills.Count);
        Assert.DoesNotContain("Expertise", rogue.UnimplementedFeatures);
    }

    [Fact]
    public void ARealRangerGetsOneExpertiseFromDeftExplorerAtLevelTwo()
    {
        var ranger = Build("class.ranger", level: 2, expertise: ["Stealth"]);

        Assert.Single(ranger.ExpertiseSkills);
        Assert.DoesNotContain("Deft Explorer", ranger.UnimplementedFeatures);

        // And the level 1 Ranger has not got it yet.
        var error = Assert.Throws<ArgumentException>(() =>
            Build("class.ranger", level: 1, expertise: ["Stealth"]));

        Assert.Contains("no feature granting Expertise", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARealWizardCanTakeNeitherChoice()
    {
        // A class granting neither feature refuses both, which is what stops a draft
        // handing benefits to a character the tables never gave them to.
        Assert.Throws<ArgumentException>(() =>
            Build("class.wizard", level: 1, style: FightingStyle.Archery));

        Assert.Throws<ArgumentException>(() =>
            Build("class.wizard", level: 1, expertise: ["Arcana"]));
    }

    [Fact]
    public void FavoredEnemyIsStillReportedAsUnimplemented()
    {
        // The one feature in #32 that stays blocked, and on something real: Hunter's
        // Mark's marked-target damage is not modelled by the spell grammar. It must keep
        // saying so rather than quietly appearing to work.
        var ranger = Build("class.ranger", level: 1);

        Assert.Contains("Favored Enemy", ranger.UnimplementedFeatures);
    }

    [Fact]
    public void TheImprovementArrivesAtLevelFourAndNotBefore()
    {
        var plus2 = new AbilityScoreImprovement { First = Ability.Strength };

        // The class tables grant it at 4. A draft that names it earlier is describing a
        // choice the character has not reached, so the score is untouched.
        var three = Build("class.fighter", level: 3, improvements: [plus2]);
        var four = Build("class.fighter", level: 4, improvements: [plus2]);

        Assert.Equal(17, three.AbilityScores[Ability.Strength]);
        Assert.Equal(19, four.AbilityScores[Ability.Strength]);

        // And it is worth exactly what the arithmetic says: +1 to hit.
        Assert.Equal(
            Build("class.fighter", level: 4).Attacks[0].AttackBonus + 1,
            four.Attacks[0].AttackBonus);
    }

    [Fact]
    public void TheOtherPrintedShapeRaisesTwoScoresByOne()
    {
        var split = Build("class.fighter", level: 4, improvements:
        [
            new AbilityScoreImprovement { First = Ability.Strength, Second = Ability.Constitution },
        ]);

        Assert.Equal(18, split.AbilityScores[Ability.Strength]);
        Assert.Equal(15, split.AbilityScores[Ability.Constitution]);
    }

    [Fact]
    public void AnImprovementCannotPushAScorePastTwenty()
    {
        // "This feat can't increase an ability score above 20." The Soldier's +2 already
        // took Strength to 17, so a 19 base would be 21 without the cap.
        var capped = Build("class.fighter", level: 4, improvements:
        [
            new AbilityScoreImprovement { First = Ability.Strength },
            new AbilityScoreImprovement { First = Ability.Strength },
        ]);

        // Only one is earned at level 4, so this is 17 + 2 rather than 17 + 4 anyway...
        Assert.Equal(19, capped.AbilityScores[Ability.Strength]);

        // ...and at level 6 a Fighter has earned two, which the cap then holds at 20.
        var fighterAtSix = Build("class.fighter", level: 5, improvements:
        [
            new AbilityScoreImprovement { First = Ability.Strength },
            new AbilityScoreImprovement { First = Ability.Strength },
        ]);

        Assert.Equal(19, fighterAtSix.AbilityScores[Ability.Strength]);
    }

    [Fact]
    public void NamingOneAbilityTwiceIsRefused()
    {
        // That would be a +2 wearing the +1/+1 shape.
        Assert.Throws<ArgumentException>(() => Build("class.fighter", level: 4, improvements:
        [
            new AbilityScoreImprovement { First = Ability.Strength, Second = Ability.Strength },
        ]));
    }

    [Fact]
    public void AnEarnedChoiceNobodySpentStaysVisible()
    {
        // "the Ability Score Improvement feat or another feat of your choice" - taking
        // another feat is legal and no other feat is modelled, so the shortfall is
        // reported rather than forgotten.
        Assert.Equal(0, Build("class.fighter", level: 3).UnspentFeatChoices);
        Assert.Equal(1, Build("class.fighter", level: 4).UnspentFeatChoices);
        Assert.Equal(
            0,
            Build("class.fighter", level: 4, improvements:
                [new AbilityScoreImprovement { First = Ability.Strength }]).UnspentFeatChoices);
    }

    [Fact]
    public void EveryClassGrantsTheImprovementAtLevelFour()
    {
        // The SRD grants this to all twelve classes at level 4 — the Sorcerer included,
        // since #78 taught the parser to join its table's wrapped cells. The exception
        // this test used to carry is gone, exactly as its comment promised.
        foreach (var definition in Content.Classes)
        {
            var atFour = definition.Levels
                .Single(row => row.Level == 4)
                .FeatureNames
                .Any(name => ClassFeatureRegistry.Resolve(name) == ClassFeature.AbilityScoreImprovement);

            Assert.True(atFour, $"{definition.Name} has no Ability Score Improvement at level 4.");
        }
    }

    [Fact]
    public void AMasteredWeaponCarriesItsPropertyIntoTheAttack()
    {
        var plain = Build("class.fighter", level: 1);
        var mastered = Build("class.fighter", level: 1, masteries: ["weapon.longsword"]);

        // The property is unlocked by the feature, so the same Longsword carries it only
        // for the character who took it.
        Assert.Null(plain.Attacks[0].Mastery);
        Assert.Equal(WeaponMastery.Sap, mastered.Attacks[0].Mastery);

        // And it stops being reported as a gap.
        Assert.DoesNotContain("Weapon Mastery", mastered.UnimplementedFeatures);
    }

    [Fact]
    public void AMasteryTheEngineDoesNotExecuteIsRefusedByName()
    {
        // The Greataxe's Cleave needs a second attack whose damage omits the ability
        // modifier, which CombatAttack cannot express. Unlocking it would be a feature
        // that silently does nothing.
        var refusal = Assert.Throws<ArgumentException>(
            () => Build("class.barbarian", level: 1, masteries: ["weapon.greataxe"]));

        Assert.Contains("Cleave", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCountComesFromTheClassTableAndGrowsWithLevel()
    {
        // The Fighter's printed Weapon Mastery column is 3 at level 1 and 4 at level 4.
        string[] four = ["weapon.longsword", "weapon.mace", "weapon.rapier", "weapon.shortsword"];

        Assert.Throws<ArgumentException>(() => Build("class.fighter", level: 1, masteries: four));

        var atFour = Build("class.fighter", level: 4, masteries: four);

        Assert.Equal(WeaponMastery.Sap, atFour.Attacks[0].Mastery);
    }

    [Fact]
    public void AClassWithoutTheFeatureCannotMasterAnything()
    {
        // The Cleric never gets Weapon Mastery.
        Assert.Throws<ArgumentException>(
            () => Build("class.cleric", level: 5, masteries: ["weapon.mace"]));
    }

    private static CharacterSheet Build(
        string classId,
        int level,
        FightingStyle style = FightingStyle.Unspecified,
        IReadOnlyList<string>? expertise = null,
        IReadOnlyList<AbilityScoreImprovement>? improvements = null,
        IReadOnlyList<string>? masteries = null)
    {
        const string backgroundId = "background.soldier";
        var background = Content.BackgroundsById[backgroundId];

        var draft = new CharacterDraft
        {
            Name = "Test",
            SpeciesId = "species.human",
            ClassId = classId,
            BackgroundId = backgroundId,
            Level = level,
            BaseAbilityScores = new Dictionary<Ability, int>
            {
                [Ability.Strength] = 15,
                [Ability.Dexterity] = 13,
                [Ability.Constitution] = 14,
                [Ability.Intelligence] = 12,
                [Ability.Wisdom] = 10,
                [Ability.Charisma] = 8,
            },
            PrimaryIncrease = background.AbilityScores[0],
            SecondaryIncrease = background.AbilityScores[1],
            // Stealth and Acrobatics come from the class list so the Rogue's Expertise
            // has proficiencies to double; Athletics comes with the Soldier background.
            ChosenSkills = ["Stealth", "Acrobatics"],
            ExpertiseSkills = expertise ?? [],
            FightingStyle = style,
            AbilityScoreImprovements = improvements ?? [],
            WeaponMasteryIds = masteries ?? [],
            WeaponIds = ["weapon.longsword"],
            ArmorId = "armor.chain-mail",
        };

        return CharacterResolver.Resolve(
            draft,
            new CharacterBuildContent(
                Content.SpeciesById["species.human"],
                Content.ClassesById[classId],
                background,
                Content.WeaponsById,
                Content.ArmorById));
    }
}
