using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Tests.Combat;

namespace SRDCombat.Core.Tests.Characters;

/// <summary>
/// Rogue Cunning Strike and Fighter Tactical Mind — the two features in #32 that needed
/// no draft plumbing, only a hook.
/// </summary>
/// <remarks>
/// <para>
/// The printed rules these pin. <b>Cunning Strike</b>: "Each effect has a die cost,
/// which is the number of Sneak Attack damage dice you must forgo to add the effect. You
/// remove the die before rolling ... If a Cunning Strike effect requires a saving throw,
/// the DC equals 8 plus your Dexterity modifier and Proficiency Bonus." Trip: "If the
/// target is Large or smaller, it must succeed on a Dexterity saving throw or have the
/// Prone condition."
/// </para>
/// <para>
/// <b>Tactical Mind</b>: "When you fail an ability check, you can expend a use of your
/// Second Wind ... you roll 1d10 and add the number rolled to the ability check ... If
/// the check still fails, this use of Second Wind isn't expended." That last sentence is
/// the feature's whole character, and it has a test of its own.
/// </para>
/// </remarks>
public class CunningStrikeAndTacticalMindTests
{
    [Fact]
    public void TripKnocksTheTargetProneOnAFailedSave()
    {
        // Initiative for three, the attack roll and its damage, the two remaining Sneak
        // Attack dice, then the target's Dexterity save — a 1, which fails DC 12.
        var (encounter, target) = RogueFight(new ScriptedRandomSource(10, 1, 18, 4, 3, 3, 3, 1));

        Assert.Null(encounter.CunningStrike(CunningStrikeEffect.Trip));
        Assert.Null(encounter.Attack("Sword", target));

        Assert.True(target.HasCondition(ConditionType.Prone));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("goes down", StringComparison.Ordinal));
    }

    [Fact]
    public void TripLeavesTheTargetStandingOnASuccessfulSave()
    {
        var (encounter, target) = RogueFight(new ScriptedRandomSource(10, 1, 18, 4, 3, 3, 3, 20));

        Assert.Null(encounter.CunningStrike(CunningStrikeEffect.Trip));
        Assert.Null(encounter.Attack("Sword", target));

        Assert.False(target.HasCondition(ConditionType.Prone));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("stays up", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDieIsRemovedBeforeRollingRatherThanDeductedAfter()
    {
        // A 3d6 Sneak Attack paying one die rolls 2d6, so the script supplies exactly two
        // sneak dice. ScriptedRandomSource throws on an unscripted roll, which is what
        // proves the third die was never rolled — a deduction from the total would have
        // rolled it.
        var (encounter, target) = RogueFight(new ScriptedRandomSource(10, 1, 18, 4, 3, 3, 3, 20));

        Assert.Null(encounter.CunningStrike(CunningStrikeEffect.Trip));
        Assert.Null(encounter.Attack("Sword", target));

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("reduced by Cunning Strike", StringComparison.Ordinal));
    }

    [Fact]
    public void TripIsSizeGatedLikeAnyPrintedProneRider()
    {
        // "If the target is Large or smaller" — a Huge creature stays up whatever it
        // rolls, and no save is rolled at all, so the script carries no die for one.
        var (encounter, target) = RogueFight(
            new ScriptedRandomSource(10, 1, 18, 4, 3, 3, 3),
            targetSize: CreatureSize.Huge);

        Assert.Null(encounter.CunningStrike(CunningStrikeEffect.Trip));
        Assert.Null(encounter.Attack("Sword", target));

        Assert.False(target.HasCondition(ConditionType.Prone));
    }

    [Fact]
    public void ARogueWithoutTheFeatureCannotDeclareIt()
    {
        var (encounter, _) = RogueFight(new ScriptedRandomSource(10, 1, 18), cunningStrike: false);

        Assert.Equal("feature.absent", encounter.CunningStrike(CunningStrikeEffect.Trip)?.Code);
    }

    [Fact]
    public void ARogueWithOneSneakAttackDieCannotPayForAnEffect()
    {
        // A level 1 Rogue's 1d6 cannot forgo a die and still deal Sneak Attack damage.
        var (encounter, _) = RogueFight(new ScriptedRandomSource(10, 1, 18), sneakAttack: "1d6");

        Assert.Equal(
            "feature.cunning_strike.too_few_dice",
            encounter.CunningStrike(CunningStrikeEffect.Trip)?.Code);
    }

    [Fact]
    public void DeclaringAfterTheSneakAttackHasLandedIsRefused()
    {
        var (encounter, target) = RogueFight(new ScriptedRandomSource(10, 1, 18, 4, 3, 3, 3, 3));

        Assert.Null(encounter.Attack("Sword", target));

        Assert.Equal(
            "feature.cunning_strike.sneak_attack_spent",
            encounter.CunningStrike(CunningStrikeEffect.Trip)?.Code);
    }

    [Fact]
    public void TacticalMindTurnsAFailedEscapeIntoASuccessAndSpendsTheUse()
    {
        // The escape rolls 5 + 5 = 10 against DC 13 and fails; Tactical Mind's 1d10
        // rolls 8, taking it to 18.
        var (encounter, fighter) = GrappledFighter(new ScriptedRandomSource(10, 1, 5, 8));

        Assert.Null(encounter.Escape());

        Assert.False(fighter.HasCondition(ConditionType.Grappled));
        Assert.Equal(0, fighter.Features.SecondWindRemaining);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("Tactical Mind", StringComparison.Ordinal));
    }

    [Fact]
    public void AStillFailedCheckDoesNotExpendTheSecondWindUse()
    {
        // "If the check still fails, this use of Second Wind isn't expended." 5 + 5 = 10,
        // plus a 1, is still short of DC 13 — and the use survives.
        var (encounter, fighter) = GrappledFighter(new ScriptedRandomSource(10, 1, 5, 1));

        Assert.Null(encounter.Escape());

        Assert.True(fighter.HasCondition(ConditionType.Grappled));
        Assert.Equal(1, fighter.Features.SecondWindRemaining);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("not expended", StringComparison.Ordinal));
    }

    [Fact]
    public void TacticalMindDoesNothingWithoutASecondWindUseLeft()
    {
        var (encounter, fighter) = GrappledFighter(new ScriptedRandomSource(10, 1, 5), secondWindUses: 0);

        Assert.Null(encounter.Escape());

        Assert.True(fighter.HasCondition(ConditionType.Grappled));
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("Tactical Mind", StringComparison.Ordinal));
    }

    [Fact]
    public void ASuccessfulEscapeNeverReachesTacticalMind()
    {
        // No 1d10 is scripted, so the test throws if the feature fires on a check that
        // already succeeded.
        var (encounter, fighter) = GrappledFighter(new ScriptedRandomSource(10, 1, 20));

        Assert.Null(encounter.Escape());

        Assert.False(fighter.HasCondition(ConditionType.Grappled));
        Assert.Equal(1, fighter.Features.SecondWindRemaining);
    }

    /// <summary>A level 5 Rogue next to a target it can Sneak Attack, thanks to an ally.</summary>
    private static (Encounter Encounter, Combatant Target) RogueFight(
        IRandomSource random,
        string sneakAttack = "3d6",
        bool cunningStrike = true,
        CreatureSize targetSize = CreatureSize.Medium)
    {
        ClassFeature[] features = cunningStrike
            ? [ClassFeature.SneakAttack, ClassFeature.CunningStrike]
            : [ClassFeature.SneakAttack];

        var rogue = Character("rogue", features, initiative: 10, sneakAttack: sneakAttack);
        var ally = Character("ally", [], initiative: 5, x: 2);

        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Monsters,
            x: 1,
            stats: CombatTestData.Stats(
                armorClass: 5,
                maximumHitPoints: 90,
                initiativeBonus: -10,
                size: targetSize));

        var encounter = Encounter.Start(new Battlefield(8, 8), [rogue, ally, target], random);

        return (encounter, target);
    }

    /// <summary>A Fighter already grappled, whose turn it is.</summary>
    private static (Encounter Encounter, Combatant Fighter) GrappledFighter(
        IRandomSource random,
        int secondWindUses = 1)
    {
        var fighter = Character(
            "fighter",
            [ClassFeature.TacticalMind, ClassFeature.SecondWind],
            initiative: 10,
            secondWindUses: secondWindUses);

        var brute = CombatTestData.Combatant(
            "brute",
            sideId: CombatTestData.Monsters,
            x: 1,
            stats: CombatTestData.Stats(initiativeBonus: -10, maximumHitPoints: 60));

        var encounter = Encounter.Start(new Battlefield(8, 8), [fighter, brute], random);

        fighter.AddCondition(new ActiveCondition(
            ConditionType.Grappled,
            "brute",
            EscapeDifficultyClass: 13));

        return (encounter, fighter);
    }

    private static Combatant Character(
        string id,
        ClassFeature[] features,
        int initiative,
        int x = 0,
        string? sneakAttack = null,
        int secondWindUses = 0)
    {
        var stats = CombatTestData.Stats(
            maximumHitPoints: 40,
            initiativeBonus: initiative,
            diesAtZeroHitPoints: false,
            attacks: [CombatTestData.MeleeAttack(bonus: 10, damage: "1d8")]) with
        {
            // Athletics +5 and Acrobatics 0, so the escape check is the die plus 5.
            SkillBonuses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Athletics"] = 5,
            },
            Character = new CombatantFeatures(
                features,
                AttacksPerAction: 1,
                sneakAttack is null ? null : DiceExpression.Parse(sneakAttack),
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: secondWindUses,
                ActionSurgeUses: 0,
                Level: 5),
        };

        return new Combatant(id, id, CombatTestData.Heroes, stats, new GridPosition(x, 0));
    }
}
