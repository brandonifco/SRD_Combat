using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Channel Divinity's Divine Spark: one roll, spent as a heal or as a Constitution
/// save for Necrotic or Radiant damage.
/// </summary>
/// <remarks>
/// The test data's Wisdom is 10, so the rolled d8 stands alone and the fallback DC is
/// 8 + 2 proficiency + 0 = 10 — deliberate, because bare numbers make the halving
/// arithmetic visible.
/// </remarks>
public class DivineSparkTests
{
    [Fact]
    public void HealGetsAFallenAllyBackUp()
    {
        // Initiative 10 / 5 / 1, then the heal's d8 rolls 6.
        var (encounter, cleric, ally, _) = Fight(new SeededSequence(10, 5, 1, 6));

        Assert.Null(encounter.DivineSpark(ally, DivineSparkUse.Heal));

        Assert.Equal(6, ally.CurrentHitPoints);
        Assert.False(ally.HasCondition(ConditionType.Unconscious));
        Assert.False(cleric.Turn.HasAction);
        Assert.Equal(1, cleric.Features.ChannelDivinityRemaining);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("back on their feet", StringComparison.Ordinal));
    }

    [Fact]
    public void HealRefusesTheClericItself()
    {
        var (encounter, cleric, _, _) = Fight(new SeededSequence(10, 5, 1));

        var refusal = encounter.DivineSpark(cleric, DivineSparkUse.Heal);

        Assert.Equal("feature.divine_spark.self", refusal?.Code);
        Assert.True(cleric.Turn.HasAction);
        Assert.Equal(2, cleric.Features.ChannelDivinityRemaining);
    }

    [Fact]
    public void HarmHalvesOnASuccessfulSaveRoundingDown()
    {
        // Initiative, then the save's d20 rolls 15 (+2 Constitution beats DC 10) and
        // the d8 rolls 5: half of 5 rounds down to 2.
        var (encounter, _, _, monster) = Fight(new SeededSequence(10, 5, 1, 15, 5));

        Assert.Null(encounter.DivineSpark(monster, DivineSparkUse.Harm));

        Assert.Equal(18, monster.CurrentHitPoints);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("Constitution saving throw", StringComparison.Ordinal)
                && step.Narration.Contains("success", StringComparison.Ordinal));
    }

    [Fact]
    public void HarmDealsTheWholeRollOnAFailure()
    {
        var (encounter, cleric, _, monster) = Fight(new SeededSequence(10, 5, 1, 1, 5));

        Assert.Null(encounter.DivineSpark(monster, DivineSparkUse.Harm, DamageType.Necrotic));

        Assert.Equal(15, monster.CurrentHitPoints);
        Assert.Equal(1, cleric.Features.ChannelDivinityRemaining);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("Necrotic", StringComparison.Ordinal));
    }

    [Fact]
    public void HarmRefusesADamageTypeTheFeatureDoesNotPrint()
    {
        var (encounter, cleric, _, monster) = Fight(new SeededSequence(10, 5, 1));

        var refusal = encounter.DivineSpark(monster, DivineSparkUse.Harm, DamageType.Bludgeoning);

        Assert.Equal("feature.divine_spark.damage_type", refusal?.Code);
        Assert.True(cleric.Turn.HasAction);
        Assert.Equal(2, cleric.Features.ChannelDivinityRemaining);
    }

    [Fact]
    public void AnExhaustedChannelDivinityIsRefusedBeforeAnythingIsSpent()
    {
        var (encounter, cleric, _, monster) = Fight(new SeededSequence(10, 5, 1), uses: 0);

        var refusal = encounter.DivineSpark(monster, DivineSparkUse.Harm);

        Assert.Equal("feature.channel_divinity.exhausted", refusal?.Code);
        Assert.True(cleric.Turn.HasAction);
    }

    [Fact]
    public void ACreatureBeyondThirtyFeetIsRefused()
    {
        var (encounter, cleric, _, monster) = Fight(new SeededSequence(10, 5, 1), monsterX: 7);

        var refusal = encounter.DivineSpark(monster, DivineSparkUse.Harm);

        Assert.Equal("feature.divine_spark.out_of_range", refusal?.Code);
        Assert.Equal(2, cleric.Features.ChannelDivinityRemaining);
    }

    [Fact]
    public void TheDeadAreRefused()
    {
        var (encounter, cleric, ally, _) = Fight(new SeededSequence(10, 5, 1));
        DamageRulesHelper.Kill(ally);

        var refusal = encounter.DivineSpark(ally, DivineSparkUse.Heal);

        Assert.Equal("feature.divine_spark.dead", refusal?.Code);
        Assert.Equal(2, cleric.Features.ChannelDivinityRemaining);
    }

    [Fact]
    public void AWisdomModifierRidesTheRoll()
    {
        // Wisdom 16 (+3): the d8 rolls 4 and the ally comes up at 7.
        var (encounter, _, ally, _) = Fight(new SeededSequence(10, 5, 1, 4), wisdom: 16);

        Assert.Null(encounter.DivineSpark(ally, DivineSparkUse.Heal));

        Assert.Equal(7, ally.CurrentHitPoints);
    }

    [Fact]
    public void MagicResistanceResistsTheSparkAlthoughItIsNoSpell()
    {
        // "Channel divine energy ... to fuel magical effects" — the printed sentence
        // that makes this the one non-spell path Magic Resistance applies to. The
        // script is exact: two initiative d20s, the Advantage pair (3 and 18), and the
        // d8 — a surplus or missing die throws, which is the proof the save rolled
        // twice.
        var cleric = Cleric("cleric", uses: 2, wisdom: 10);

        var resister = CombatTestData.Combatant(
            "resister",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(maximumHitPoints: 60, initiativeBonus: -10, attacks: []) with
            {
                Entries = [new MonsterEntry("Magic Resistance", MonsterEntrySection.Trait, "...")],
            },
            x: 2);

        var encounter = Encounter.Start(
            new Battlefield(8, 8),
            [cleric, resister],
            new ScriptedRandomSource(10, 1, 3, 18, 4));

        Assert.Null(encounter.DivineSpark(resister, DivineSparkUse.Harm));

        // 18 + 2 beats DC 10: half of the rolled 4 is 2.
        Assert.Equal(58, resister.CurrentHitPoints);
    }

    [Fact]
    public void ThePolicySparksAFallenAllyUpWithoutASlot()
    {
        // A Cleric with no spells at all: the only remedy it carries is Channel
        // Divinity, and the policy reaches for it before swinging.
        var cleric = Cleric("cleric", uses: 2, wisdom: 10);
        var fallen = new Combatant(
            "fallen",
            "fallen",
            CombatTestData.Heroes,
            CombatTestData.Stats(maximumHitPoints: 20, diesAtZeroHitPoints: false),
            new GridPosition(1, 1),
            new CombatantCarryOver(0));

        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10, maximumHitPoints: 30),
            x: 7);

        var encounter = Encounter.Start(
            new Battlefield(8, 8),
            [cleric, fallen, monster],
            new SeededSequence(20, 5, 1));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.True(fallen.CurrentHitPoints > 0, "The policy left its ally on the floor.");
        Assert.Equal(1, cleric.Features.ChannelDivinityRemaining);
    }

    private static Combatant Cleric(string id, int uses, int wisdom, int initiativeBonus = 10)
    {
        var abilities = new Dictionary<Ability, MonsterAbility>
        {
            [Ability.Strength] = new(10, 0),
            [Ability.Dexterity] = new(10, 0),
            [Ability.Constitution] = new(14, 2),
            [Ability.Intelligence] = new(10, 0),
            [Ability.Wisdom] = new(wisdom, (wisdom - 10) / 2),
            [Ability.Charisma] = new(10, 0),
        };

        var stats = CombatTestData.Stats(
            maximumHitPoints: 30,
            initiativeBonus: initiativeBonus,
            diesAtZeroHitPoints: false) with
        {
            Abilities = abilities,
            Character = new CombatantFeatures(
                [ClassFeature.ChannelDivinity],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 3,
                ChannelDivinityUses: uses),
        };

        return new Combatant(id, id, CombatTestData.Heroes, stats, new GridPosition(0, 0));
    }

    /// <summary>A Cleric beside a downed ally, with a monster two squares away.</summary>
    private static (Encounter Encounter, Combatant Cleric, Combatant Ally, Combatant Monster) Fight(
        IRandomSource random,
        int uses = 2,
        int wisdom = 10,
        int monsterX = 2)
    {
        var cleric = Cleric("cleric", uses, wisdom);

        var ally = new Combatant(
            "ally",
            "ally",
            CombatTestData.Heroes,
            CombatTestData.Stats(maximumHitPoints: 20, initiativeBonus: 3, diesAtZeroHitPoints: false),
            new GridPosition(0, 1),
            new CombatantCarryOver(0));

        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10),
            x: monsterX);

        var encounter = Encounter.Start(new Battlefield(8, 8), [cleric, ally, monster], random);

        return (encounter, cleric, ally, monster);
    }
}
