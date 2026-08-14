using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Tests.Characters;

/// <summary>
/// The two features that are character-creation <em>choices</em>: a Fighting Style feat
/// and Expertise.
/// </summary>
/// <remarks>
/// <para>
/// These are the first features the resolver cannot derive on its own — every other
/// number on a sheet follows from species, class, background and level, and these two
/// follow from what the player picked. The draft carries the pick and the resolver
/// refuses one the character was never granted, so a sheet can never claim a benefit
/// the class does not give.
/// </para>
/// <para>
/// The printed rules, verbatim, because both are easy to approximate: Archery is "a +2
/// bonus to attack rolls you make with Ranged weapons"; Defense is "While you're wearing
/// Light, Medium, or Heavy armor, you gain a +1 bonus to Armor Class"; Expertise is
/// granted "in two of your skill proficiencies of your choice" and doubles the
/// proficiency bonus.
/// </para>
/// </remarks>
public class DraftChoiceTests
{
    [Fact]
    public void ArcheryAddsTwoToRangedAttacksAndNothingToMelee()
    {
        var bow = CharacterTestData.Weapon(
            "weapon.longbow",
            "Longbow",
            "1d8",
            WeaponKind.Ranged,
            range: new WeaponRange(150, 600));

        var sheet = Resolve(
            CharacterTestData.Draft(weaponIds: ["weapon.longsword", "weapon.longbow"]) with
            {
                FightingStyle = FightingStyle.Archery,
            },
            weapons: [CharacterTestData.Weapon(), bow]);

        var melee = sheet.Attacks.Single(attack => attack.Name == "Longsword");
        var ranged = sheet.Attacks.Single(attack => attack.Name == "Longbow");

        var withoutStyle = Resolve(
            CharacterTestData.Draft(weaponIds: ["weapon.longsword", "weapon.longbow"]),
            weapons: [CharacterTestData.Weapon(), bow]);

        Assert.Equal(withoutStyle.Attacks.Single(a => a.Name == "Longbow").AttackBonus + 2, ranged.AttackBonus);
        Assert.Equal(withoutStyle.Attacks.Single(a => a.Name == "Longsword").AttackBonus, melee.AttackBonus);
    }

    [Fact]
    public void ArcheryFollowsTheWeaponsKindNotTheAttacksRange()
    {
        // A thrown Dagger is a Melee weapon that happens to have a range, and Archery
        // does not touch it. Reading "ranged attacks" instead of "Ranged weapons" would
        // quietly hand every thrown weapon +2.
        var dagger = CharacterTestData.Weapon(
            "weapon.dagger",
            "Dagger",
            "1d4",
            WeaponKind.Melee,
            WeaponProperty.Finesse,
            new WeaponRange(20, 60));

        var sheet = Resolve(
            CharacterTestData.Draft(weaponIds: ["weapon.dagger"]) with { FightingStyle = FightingStyle.Archery },
            weapons: [dagger]);

        var plain = Resolve(CharacterTestData.Draft(weaponIds: ["weapon.dagger"]), weapons: [dagger]);

        Assert.Equal(plain.Attacks[0].AttackBonus, sheet.Attacks[0].AttackBonus);
    }

    [Fact]
    public void DefenseAddsOneArmorClassInArmour()
    {
        var draft = CharacterTestData.Draft(armorId: "armor.chain-mail");

        var plain = Resolve(draft);
        var defended = Resolve(draft with { FightingStyle = FightingStyle.Defense });

        Assert.Equal(plain.ArmorClass + 1, defended.ArmorClass);
        Assert.Contains("+ 1 (Defense)", defended.ArmorClassSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DefenseDoesNothingUnarmouredEvenWithAShield()
    {
        // "While you're wearing Light, Medium, or Heavy armor" — a Shield is none of the
        // three, so a shield alone does not switch the style on.
        var draft = CharacterTestData.Draft(hasShield: true);

        var plain = Resolve(draft);
        var defended = Resolve(draft with { FightingStyle = FightingStyle.Defense });

        Assert.Equal(plain.ArmorClass, defended.ArmorClass);
        Assert.DoesNotContain("Defense", defended.ArmorClassSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AStyleTheClassNeverGrantedIsRefused()
    {
        // The test class grants no Fighting Style, so asking for one is a mistake in the
        // draft rather than a preference to honour silently.
        var draft = CharacterTestData.Draft() with { FightingStyle = FightingStyle.Defense };

        var error = Assert.Throws<ArgumentException>(() => Resolve(draft, styled: false));

        Assert.Contains("Fighting Style", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpertiseDoublesTheProficiencyBonusOnTheChosenSkill()
    {
        var sheet = Resolve(
            CharacterTestData.Draft() with { ExpertiseSkills = ["Athletics"] },
            expertiseAtLevel1: true);

        var athletics = sheet.Skills.Single(skill => skill.Skill == "Athletics");
        var intimidation = sheet.Skills.Single(skill => skill.Skill == "Intimidation");

        // Both are background proficiencies; only one is doubled.
        Assert.Equal(sheet.Modifier(athletics.Ability) + (sheet.ProficiencyBonus * 2), athletics.Bonus);
        Assert.Equal(sheet.Modifier(intimidation.Ability) + sheet.ProficiencyBonus, intimidation.Bonus);
        Assert.Equal(["Athletics"], sheet.ExpertiseSkills);
    }

    [Fact]
    public void ExpertiseNeedsProficiencyInTheSkillFirst()
    {
        // "You gain Expertise in two of your skill proficiencies" — it doubles something
        // the character has, so it cannot be spent on a skill they are not proficient in.
        var draft = CharacterTestData.Draft() with { ExpertiseSkills = ["Arcana"] };

        var error = Assert.Throws<ArgumentException>(() => Resolve(draft, expertiseAtLevel1: true));

        Assert.Contains("proficiency in Arcana", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRoguesTwoPicksAndTheRangersOneAreCountedFromTheGrant()
    {
        // The allowance comes from the granted features, so Expertise at level 1 (two
        // skills, the Rogue's) and Deft Explorer at level 2 (one skill, the Ranger's)
        // need no special case for the class name.
        var twoPicks = new[] { "Athletics", "Intimidation" };

        Assert.Equal(
            twoPicks,
            Resolve(
                CharacterTestData.Draft() with { ExpertiseSkills = twoPicks },
                expertiseAtLevel1: true).ExpertiseSkills);

        var overLimit = Assert.Throws<ArgumentException>(() => Resolve(
            CharacterTestData.Draft(level: 2) with { ExpertiseSkills = twoPicks },
            deftExplorerAtLevel2: true));

        Assert.Contains("1 skill(s), not 2", overLimit.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpertiseTheClassNeverGrantedIsRefused()
    {
        var draft = CharacterTestData.Draft() with { ExpertiseSkills = ["Athletics"] };

        var error = Assert.Throws<ArgumentException>(() => Resolve(draft));

        Assert.Contains("no feature granting Expertise", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameSkillCannotTakeExpertiseTwice()
    {
        var draft = CharacterTestData.Draft() with { ExpertiseSkills = ["Athletics", "athletics"] };

        var error = Assert.Throws<ArgumentException>(() => Resolve(draft, expertiseAtLevel1: true));

        Assert.Contains("twice in the same skill", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADraftMakingNoChoicesResolvesExactlyAsBefore()
    {
        // The defaults have to be inert: every existing character predates these fields.
        var sheet = Resolve(CharacterTestData.Draft(armorId: "armor.chain-mail"));

        Assert.Equal(FightingStyle.Unspecified, sheet.FightingStyle);
        Assert.Empty(sheet.ExpertiseSkills);
        Assert.DoesNotContain("Defense", sheet.ArmorClassSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ArmorTooHeavyForItsWearerCostsTenFeetOfSpeed()
    {
        // "the armor reduces your Speed by 10 feet unless your Strength is equal to
        // or greater than that score." The test armor is Chain Mail at Strength 13;
        // the draft's Strength is 15 before its background increase.
        var heavy = CharacterTestData.Armor() with { MinimumStrength = 18 };

        var underStrength = CharacterResolver.Resolve(
            CharacterTestData.Draft(armorId: heavy.Id),
            CharacterTestData.Content(armor: [heavy, CharacterTestData.Shield()]));

        var bare = CharacterResolver.Resolve(
            CharacterTestData.Draft(),
            CharacterTestData.Content(armor: [heavy, CharacterTestData.Shield()]));

        Assert.Equal(bare.SpeedFeet - 10, underStrength.SpeedFeet);
    }

    [Fact]
    public void MeetingTheStrengthScoreCostsNothing()
    {
        // Equal is enough — "equal to or greater than" — and the score is checked
        // against the resolved Strength, so the background's increase counts.
        var heavy = CharacterTestData.Armor() with { MinimumStrength = 17 };

        var sheet = CharacterResolver.Resolve(
            CharacterTestData.Draft(armorId: heavy.Id),
            CharacterTestData.Content(armor: [heavy, CharacterTestData.Shield()]));

        Assert.Equal(17, sheet.AbilityScores[Ability.Strength]);

        var bare = CharacterResolver.Resolve(
            CharacterTestData.Draft(),
            CharacterTestData.Content(armor: [heavy, CharacterTestData.Shield()]));

        Assert.Equal(bare.SpeedFeet, sheet.SpeedFeet);
    }

    [Fact]
    public void DivineOrderIsRefusedWithoutTheGrantingFeature()
    {
        // The test class grants Fighting Style, never Divine Order — naming a role is
        // the same shape of illegal draft as a Wizard with a Fighting Style.
        var refusal = Assert.Throws<ArgumentException>(() =>
            Resolve(CharacterTestData.Draft() with { DivineOrder = DivineOrder.Protector }));

        Assert.Contains("Divine Order", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnchosenDivineOrderStaysReportedUnimplemented()
    {
        // The name is in the registry, so without this rule it would vanish from the
        // report while nothing executed — a choice nobody made must stay visible.
        var unchosen = Resolve(CharacterTestData.Draft(), divineOrderAtLevel1: true);

        Assert.Equal(DivineOrder.Unspecified, unchosen.DivineOrder);
        Assert.Contains("Divine Order", unchosen.UnimplementedFeatures);

        var chosen = Resolve(
            CharacterTestData.Draft() with { DivineOrder = DivineOrder.Protector },
            divineOrderAtLevel1: true);

        Assert.Equal(DivineOrder.Protector, chosen.DivineOrder);
        Assert.DoesNotContain("Divine Order", chosen.UnimplementedFeatures);
    }

    [Fact]
    public void ThaumaturgeAddsAWisdomBonusToArcanaAndReligionChecks()
    {
        // "The bonus equals your Wisdom modifier (minimum of +1)." The draft's default
        // Wisdom is 12 (+1); the floor is exercised with an 8 (-1), which still grants
        // +1 rather than subtracting.
        var sheet = Resolve(
            CharacterTestData.Draft() with { DivineOrder = DivineOrder.Thaumaturge },
            divineOrderAtLevel1: true);

        var plain = Resolve(CharacterTestData.Draft(), divineOrderAtLevel1: true);

        int Of(CharacterSheet source, string skill) =>
            source.Skills.Single(candidate => candidate.Skill == skill).Bonus;

        Assert.Equal(Of(plain, "Arcana") + 1, Of(sheet, "Arcana"));
        Assert.Equal(Of(plain, "Religion") + 1, Of(sheet, "Religion"));
        Assert.Equal(Of(plain, "Investigation"), Of(sheet, "Investigation"));

        var lowWisdom = new Dictionary<Ability, int>
        {
            [Ability.Strength] = 15,
            [Ability.Dexterity] = 13,
            [Ability.Constitution] = 14,
            [Ability.Intelligence] = 10,
            [Ability.Wisdom] = 8,
            [Ability.Charisma] = 12,
        };

        var floored = Resolve(
            CharacterTestData.Draft(scores: lowWisdom) with { DivineOrder = DivineOrder.Thaumaturge },
            divineOrderAtLevel1: true);
        var flooredPlain = Resolve(CharacterTestData.Draft(scores: lowWisdom), divineOrderAtLevel1: true);

        Assert.Equal(Of(flooredPlain, "Religion") + 1, Of(floored, "Religion"));
    }

    private static CharacterSheet Resolve(
        CharacterDraft draft,
        IEnumerable<WeaponDefinition>? weapons = null,
        bool styled = true,
        bool expertiseAtLevel1 = false,
        bool deftExplorerAtLevel2 = false,
        bool divineOrderAtLevel1 = false)
    {
        var featuresByLevel = new Dictionary<int, string[]>();

        if (styled)
        {
            featuresByLevel[1] = ["Fighting Style"];
        }

        if (expertiseAtLevel1)
        {
            featuresByLevel[1] = [.. featuresByLevel.GetValueOrDefault(1, []), "Expertise"];
        }

        if (deftExplorerAtLevel2)
        {
            featuresByLevel[2] = ["Deft Explorer"];
        }

        if (divineOrderAtLevel1)
        {
            featuresByLevel[1] = [.. featuresByLevel.GetValueOrDefault(1, []), "Divine Order"];
        }

        return CharacterResolver.Resolve(
            draft,
            CharacterTestData.Content(
                classDefinition: CharacterTestData.Class(featuresByLevel: featuresByLevel),
                weapons: weapons));
    }
}
