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

    private static CharacterSheet Build(
        string classId,
        int level,
        FightingStyle style = FightingStyle.Unspecified,
        IReadOnlyList<string>? expertise = null)
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
