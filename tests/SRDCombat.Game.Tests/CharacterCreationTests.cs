using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The menus creation may offer, and the two printed-line readings behind the gear
/// ones — checked against the closed set of twelve printed lines, so a rewording in a
/// future extraction fails loudly rather than quietly offering a Wizard a greatsword.
/// </summary>
public class CharacterCreationTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void TheLaunchSixAreOfferedInKickoffOrder() =>
        Assert.Equal(
            ["Fighter", "Rogue", "Cleric", "Wizard", "Barbarian", "Ranger"],
            CharacterCreation.Classes(Content).Select(definition => definition.Name).ToArray());

    [Fact]
    public void EverySpeciesAndBackgroundIsOffered()
    {
        Assert.Equal(Content.Species.Count, CharacterCreation.Species(Content).Count);
        Assert.Equal(Content.Backgrounds.Count, CharacterCreation.Backgrounds(Content).Count);
    }

    [Fact]
    public void TheWeaponReadingHoldsAcrossEveryPrintedLine()
    {
        foreach (var @class in Content.Classes)
        {
            var options = CharacterCreation.WeaponOptions(Content, @class);

            // Simple weapons are in every printed line.
            Assert.All(
                Content.Weapons.Where(weapon => weapon.Category == WeaponCategory.Simple),
                simple => Assert.Contains(simple, options));

            var martial = options.Where(weapon => weapon.Category == WeaponCategory.Martial).ToArray();

            switch (@class.Id)
            {
                case "class.barbarian" or "class.fighter" or "class.paladin" or "class.ranger":
                    Assert.Equal(Content.Weapons.Count, options.Count);
                    break;
                case "class.rogue":
                    Assert.NotEmpty(martial);
                    Assert.All(martial, weapon => Assert.True(
                        (weapon.Properties & (WeaponProperty.Finesse | WeaponProperty.Light)) != 0));
                    break;
                case "class.monk":
                    Assert.NotEmpty(martial);
                    Assert.All(martial, weapon => Assert.True(
                        (weapon.Properties & WeaponProperty.Light) != 0));
                    break;
                default:
                    Assert.Empty(martial);
                    break;
            }
        }
    }

    [Fact]
    public void TheArmorReadingHoldsAcrossEveryPrintedLine()
    {
        foreach (var @class in Content.Classes)
        {
            var categories = CharacterCreation.ArmorOptions(Content, @class)
                .Select(armor => armor.Category)
                .Distinct()
                .ToArray();

            switch (@class.Id)
            {
                case "class.fighter" or "class.paladin":
                    Assert.Contains(ArmorCategory.Heavy, categories);
                    Assert.Contains(ArmorCategory.Light, categories);
                    break;
                case "class.barbarian" or "class.cleric" or "class.ranger":
                    Assert.Contains(ArmorCategory.Medium, categories);
                    Assert.DoesNotContain(ArmorCategory.Heavy, categories);
                    break;
                case "class.wizard" or "class.sorcerer" or "class.monk":
                    Assert.Empty(categories);
                    break;
                default:
                    Assert.Contains(ArmorCategory.Light, categories);
                    Assert.DoesNotContain(ArmorCategory.Heavy, categories);
                    break;
            }

            Assert.Equal(
                @class.ArmorTraining.Contains("Shields", StringComparison.Ordinal),
                CharacterCreation.MayCarryShield(@class));
        }
    }

    [Fact]
    public void SkillChoicesComeOffTheCoreTraitsTable()
    {
        var (fighterCount, fighterOptions) = CharacterCreation.SkillChoices(Content.ClassesById["class.fighter"]);
        Assert.Equal(2, fighterCount);
        Assert.NotEmpty(fighterOptions);

        // The Bard's "Choose any 3" expands to the whole skill list.
        var (bardCount, bardOptions) = CharacterCreation.SkillChoices(Content.ClassesById["class.bard"]);
        Assert.Equal(3, bardCount);
        Assert.Equal(SkillRules.AllSkills, bardOptions);
    }

    [Fact]
    public void FightingStylesFollowTheFeatureHeadings()
    {
        Assert.True(CharacterCreation.GrantsFightingStyle(Content.ClassesById["class.fighter"], level: 1));
        Assert.False(CharacterCreation.GrantsFightingStyle(Content.ClassesById["class.ranger"], level: 1));
        Assert.True(CharacterCreation.GrantsFightingStyle(Content.ClassesById["class.ranger"], level: 2));
        Assert.False(CharacterCreation.GrantsFightingStyle(Content.ClassesById["class.barbarian"], level: 5));
    }

    [Fact]
    public void MasteryOptionsAreProficientKindsWhosePropertyExecutes()
    {
        var fighter = Content.ClassesById["class.fighter"];

        Assert.Equal(3, CharacterCreation.MasteryAllowance(fighter, level: 1));
        Assert.Equal(0, CharacterCreation.MasteryAllowance(Content.ClassesById["class.cleric"], level: 1));

        // The Rogue's table prints no Weapon Mastery column — its two kinds live in the
        // feature's prose, the resolver's own fallback. The first version read only the
        // column and quietly offered the Rogue nothing.
        Assert.Equal(2, CharacterCreation.MasteryAllowance(Content.ClassesById["class.rogue"], level: 1));
        Assert.Equal(2, CharacterCreation.MasteryAllowance(Content.ClassesById["class.ranger"], level: 1));

        var options = CharacterCreation.MasteryOptions(Content, fighter);
        Assert.NotEmpty(options);
        Assert.All(options, weapon => Assert.True(WeaponMasteryRules.Executes(weapon.Mastery)));
        Assert.DoesNotContain(options, weapon => weapon.Mastery is WeaponMastery.Push or WeaponMastery.Nick);
    }

    [Fact]
    public void SpellMenusAndAllowancesAgreeWithTheRegistryAndTheTable()
    {
        var cleric = Content.ClassesById["class.cleric"];

        Assert.Equal(8, CharacterCreation.SpellOptions(Content, cleric).Count);

        var (cantrips, prepared) = CharacterCreation.SpellAllowances(cleric, level: 1);
        Assert.Equal(3, cantrips);
        Assert.Equal(4, prepared);

        Assert.Equal((0, 0), CharacterCreation.SpellAllowances(Content.ClassesById["class.fighter"], level: 1));
    }

    [Fact]
    public void DivineOrderFollowsTheFeatureHeadings()
    {
        Assert.True(CharacterCreation.GrantsDivineOrder(Content.ClassesById["class.cleric"], level: 1));
        Assert.False(CharacterCreation.GrantsDivineOrder(Content.ClassesById["class.fighter"], level: 5));

        // The choice is offered with the SRD's own sentence, per the charter.
        Assert.Contains(
            "Protector",
            CharacterCreation.DivineOrderText(Content.ClassesById["class.cleric"]),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryLaunchClassGrantsTheAbilityScoreImprovementAtLevelFour()
    {
        // Every launch class's table grants it at 4 (checked against the printed data
        // in #288's investigation), but the reading stays a rule rather than an
        // assumption, the same way FightingStyle and DivineOrder are.
        foreach (var @class in CharacterCreation.Classes(Content))
        {
            Assert.False(CharacterCreation.GrantsAbilityScoreImprovement(@class, level: 3));
            Assert.True(CharacterCreation.GrantsAbilityScoreImprovement(@class, level: 4));
        }
    }

    [Fact]
    public void ProtectorWidensTheClericsMenusAndNobodyElses()
    {
        var cleric = Content.ClassesById["class.cleric"];

        // The Cleric's printed lines: "Simple weapons", "Light and Medium armor and
        // Shields". Protector supersedes both — "proficiency with Martial weapons and
        // training with Heavy armor".
        Assert.DoesNotContain(
            CharacterCreation.WeaponOptions(Content, cleric),
            weapon => weapon.Id == "weapon.longsword");
        Assert.Contains(
            CharacterCreation.WeaponOptions(Content, cleric, DivineOrder.Protector),
            weapon => weapon.Id == "weapon.longsword");

        Assert.DoesNotContain(
            CharacterCreation.ArmorOptions(Content, cleric),
            armor => armor.Category == ArmorCategory.Heavy);
        Assert.Contains(
            CharacterCreation.ArmorOptions(Content, cleric, DivineOrder.Protector),
            armor => armor.Id == "armor.chain-mail");

        // Thaumaturge widens nothing here — its halves live elsewhere.
        Assert.Equal(
            CharacterCreation.ArmorOptions(Content, cleric).Select(armor => armor.Id),
            CharacterCreation.ArmorOptions(Content, cleric, DivineOrder.Thaumaturge).Select(armor => armor.Id));

        // A class that never grants Divine Order is not widened by naming a role: the
        // Rogue's menus are the Rogue's menus whatever the draft claims.
        var rogue = Content.ClassesById["class.rogue"];

        Assert.Equal(
            CharacterCreation.ArmorOptions(Content, rogue).Select(armor => armor.Id),
            CharacterCreation.ArmorOptions(Content, rogue, DivineOrder.Protector).Select(armor => armor.Id));
    }

    [Fact]
    public void ThaumaturgeGrowsTheCantripsColumnByOne()
    {
        var cleric = Content.ClassesById["class.cleric"];

        Assert.Equal(4, CharacterCreation.SpellAllowances(cleric, level: 1, DivineOrder.Thaumaturge).Cantrips);
        Assert.Equal(3, CharacterCreation.SpellAllowances(cleric, level: 1, DivineOrder.Protector).Cantrips);

        // A class without the feature gains nothing from the claim.
        Assert.Equal(
            CharacterCreation.SpellAllowances(Content.ClassesById["class.wizard"], level: 1).Cantrips,
            CharacterCreation.SpellAllowances(
                Content.ClassesById["class.wizard"],
                level: 1,
                DivineOrder.Thaumaturge).Cantrips);
    }

    [Fact]
    public void EveryOfferedChoiceCarriesItsPrintedText()
    {
        // The Phase 5 charter: no bare names. Species carry their traits' prose,
        // classes their features', spells their full description.
        Assert.All(CharacterCreation.Species(Content), species =>
            Assert.Contains(species.Traits, trait => trait.Text.Length > 0));
        Assert.All(CharacterCreation.Classes(Content), @class =>
            Assert.Contains(@class.Features, feature => feature.Text.Length > 0));
        Assert.All(
            CharacterCreation.SpellOptions(Content, Content.ClassesById["class.cleric"]),
            spell => Assert.True(spell.Text.Length > 0));
    }
}
