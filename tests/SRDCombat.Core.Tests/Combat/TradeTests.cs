using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Trade: a free once-per-turn transfer of one Potion of Healing from the active
/// combatant to an adjacent ally.
/// </summary>
/// <remarks>
/// <para>
/// #536's design record settles four judgement calls this pins: gear is refused
/// outright in a live fight (<c>trade.gear_in_fight</c>) rather than re-resolving two
/// combatants mid-encounter; one trade a turn is free, spending neither the Action nor
/// the Bonus Action; an Unconscious ally may still receive an item, following
/// <c>DrinkPotion</c>'s own precedent that inability to act does not lock a creature's
/// pack; and the direction is always giver to recipient — unlike <c>DrinkPotion</c>,
/// this never reaches into the recipient's own pack.
/// </para>
/// <para>
/// The refusal order is deliberate and pinned here rather than merely implemented:
/// target legality (present, not the actor, an ally, alive, in reach) is checked before
/// affordability (the actor actually carries the item, the turn's free trade is still
/// unspent), so a bad target is reported before a question that never mattered.
/// </para>
/// </remarks>
public class TradeTests
{
    [Fact]
    public void TradingGivesAPotionAndSpendsOnlyTheTradeInteraction()
    {
        var (encounter, giver, ally, _) = Fight(giverPotions: 1);

        Assert.Null(encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), ally));

        Assert.Equal(0, giver.Inventory.TotalPotions);
        Assert.Equal(1, ally.Inventory.CountOf(HealingPotion.Standard));
        Assert.False(giver.Turn.HasTradeInteraction);

        // Free: neither the Action nor the Bonus Action moved.
        Assert.True(giver.Turn.HasAction);
        Assert.True(giver.Turn.HasBonusAction);
    }

    [Fact]
    public void TheNarrationNamesGiverItemAndRecipient()
    {
        var (encounter, giver, ally, _) = Fight(giverPotions: 1, potency: HealingPotion.Greater);

        Assert.Null(encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Greater), ally));

        var step = Assert.Single(
            encounter.Log,
            entry => entry.Kind == CombatStepKind.Item && entry.ActorId == giver.Id);

        Assert.Equal(ally.Id, step.TargetId);
        Assert.Contains(giver.Name, step.Narration, StringComparison.Ordinal);
        Assert.Contains(ally.Name, step.Narration, StringComparison.Ordinal);
        Assert.Contains(PotionRules.PrintedName(HealingPotion.Greater), step.Narration, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondTradeTheSameTurnIsRefusedAndTheFirstStands()
    {
        var (encounter, giver, ally, _) = Fight(giverPotions: 2);

        Assert.Null(encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), ally));

        var logCountAfterFirst = encounter.Log.Count;
        var refusal = encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), ally);

        Assert.Equal("trade.already_used", refusal?.Code);

        // The first trade's own result stands; the second changed nothing further.
        Assert.Equal(1, giver.Inventory.TotalPotions);
        Assert.Equal(1, ally.Inventory.CountOf(HealingPotion.Standard));
        Assert.Equal(logCountAfterFirst, encounter.Log.Count);
    }

    [Fact]
    public void ANewTurnRestoresTheTradeInteraction()
    {
        var (encounter, giver, ally, _) = Fight(giverPotions: 2);

        Assert.Null(encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), ally));
        Assert.False(giver.Turn.HasTradeInteraction);

        // A fresh turn — BeginTurn is what every round boundary calls.
        giver.Turn.BeginTurn(giver.Stats.SpeedFeet);

        Assert.True(giver.Turn.HasTradeInteraction);
        Assert.Null(encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), ally));
        Assert.Equal(2, ally.Inventory.CountOf(HealingPotion.Standard));
    }

    [Fact]
    public void AnUnconsciousAllyCanReceiveATradedPotion()
    {
        var (encounter, giver, ally, _) = Fight(giverPotions: 1, allyStartingHitPoints: 0);

        Assert.True(ally.HasCondition(ConditionType.Unconscious));

        Assert.Null(encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), ally));

        Assert.Equal(1, ally.Inventory.CountOf(HealingPotion.Standard));
    }

    [Fact]
    public void ADeadRecipientIsRefusedAndNothingMoves()
    {
        var (encounter, giver, ally, _) = Fight(giverPotions: 1);
        DamageRulesHelper.Kill(ally);

        var refusal = encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), ally);

        Assert.Equal("trade.target_dead", refusal?.Code);
        Assert.Equal(1, giver.Inventory.TotalPotions);
        Assert.Equal(0, ally.Inventory.TotalPotions);
        Assert.True(giver.Turn.HasTradeInteraction);
        Assert.True(giver.Turn.HasAction);
        Assert.True(giver.Turn.HasBonusAction);
    }

    [Fact]
    public void ARecipientBeyondFiveFeetIsRefusedWithTheMeasuredDistance()
    {
        var (encounter, giver, ally, _) = Fight(giverPotions: 1, allyX: 4);

        var refusal = encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), ally);

        Assert.Equal("trade.out_of_reach", refusal?.Code);
        Assert.Contains("20 feet away", refusal!.Message, StringComparison.Ordinal);
        Assert.Contains("5 feet", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(1, giver.Inventory.TotalPotions);
        Assert.True(giver.Turn.HasTradeInteraction);
    }

    [Fact]
    public void OutOfReachOutranksAMissingPotion()
    {
        // Target legality is checked before affordability: a distant ally with no
        // potion offered still reports the distance, not the missing flask, because
        // moving into reach is the fact that actually helps the player next.
        var (encounter, _, ally, _) = Fight(giverPotions: 0, allyX: 4);

        Assert.Equal(
            "trade.out_of_reach",
            encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), ally)?.Code);
    }

    [Fact]
    public void SelfTradeIsRefused()
    {
        var (encounter, giver, _, _) = Fight(giverPotions: 1);

        Assert.Equal(
            "trade.same_carrier",
            encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), giver)?.Code);

        Assert.Equal(1, giver.Inventory.TotalPotions);
    }

    [Fact]
    public void EnemyTradeIsRefused()
    {
        var (encounter, giver, _, enemy) = Fight(giverPotions: 1, enemyX: 1);

        var refusal = encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), enemy);

        Assert.Equal("trade.target_not_ally", refusal?.Code);
        Assert.Equal(1, giver.Inventory.TotalPotions);
        Assert.Equal(0, enemy.Inventory.TotalPotions);
    }

    [Fact]
    public void ATargetOutsideTheEncounterIsRefused()
    {
        var (encounter, giver, _, _) = Fight(giverPotions: 1);
        var outsider = CombatTestData.Character("outsider");

        var refusal = encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), outsider);

        Assert.Equal("trade.target_not_present", refusal?.Code);
        Assert.Equal(1, giver.Inventory.TotalPotions);
        Assert.True(giver.Turn.HasTradeInteraction);
    }

    [Fact]
    public void APotionTheGiverDoesNotCarryIsRefusedAndNeverReachesIntoTheRecipientsPack()
    {
        // Unlike DrinkPotion, Trade never falls back to the recipient's own supplies —
        // the direction is always giver to recipient.
        var (encounter, giver, ally, _) = Fight(giverPotions: 0, allyPotions: 1);

        var refusal = encounter.TradeItem(new CombatTradeItem.Potion(HealingPotion.Standard), ally);

        Assert.Equal("trade.item_missing", refusal?.Code);
        Assert.Equal(0, giver.Inventory.TotalPotions);
        Assert.Equal(1, ally.Inventory.CountOf(HealingPotion.Standard));
        Assert.True(giver.Turn.HasTradeInteraction);
    }

    [Fact]
    public void AGearRequestIsRefusedInFight()
    {
        var (encounter, giver, ally, _) = Fight(giverPotions: 1);

        var logCountBefore = encounter.Log.Count;
        var refusal = encounter.TradeItem(new CombatTradeItem.Gear("magic-item.longsword"), ally);

        Assert.Equal("trade.gear_in_fight", refusal?.Code);
        Assert.Equal(1, giver.Inventory.TotalPotions);
        Assert.Equal(0, ally.Inventory.TotalPotions);
        Assert.True(giver.Turn.HasTradeInteraction);
        Assert.Equal(logCountBefore, encounter.Log.Count);
    }

    [Fact]
    public void AGearRequestOutranksEveryTargetCheck()
    {
        // The unsupported-item check is categorical and independent of the target, so
        // it fires even against a recipient that would otherwise be refused first for
        // an entirely different reason (here, outside the encounter).
        var (encounter, _, _, _) = Fight(giverPotions: 1);
        var outsider = CombatTestData.Character("outsider");

        Assert.Equal(
            "trade.gear_in_fight",
            encounter.TradeItem(new CombatTradeItem.Gear("magic-item.longsword"), outsider)?.Code);
    }

    /// <summary>A giver, an ally, and an enemy far enough away that the fight does not end at start.</summary>
    private static (Encounter Encounter, Combatant Giver, Combatant Ally, Combatant Enemy) Fight(
        int giverPotions,
        int allyPotions = 0,
        int allyX = 1,
        int enemyX = 10,
        int allyStartingHitPoints = 20,
        HealingPotion potency = HealingPotion.Standard)
    {
        var giver = new Combatant(
            "giver",
            "Giver",
            CombatTestData.Heroes,
            CombatTestData.Stats(maximumHitPoints: 20, initiativeBonus: 10, diesAtZeroHitPoints: false),
            new GridPosition(0, 0),
            new CombatantCarryOver(
                20,
                Potions: giverPotions > 0
                    ? new Dictionary<HealingPotion, int> { [potency] = giverPotions }
                    : null));

        var ally = new Combatant(
            "ally",
            "Ally",
            CombatTestData.Heroes,
            CombatTestData.Stats(maximumHitPoints: 20, initiativeBonus: -10, diesAtZeroHitPoints: false),
            new GridPosition(allyX, 0),
            new CombatantCarryOver(
                allyStartingHitPoints,
                Potions: allyPotions > 0
                    ? new Dictionary<HealingPotion, int> { [potency] = allyPotions }
                    : null));

        var enemy = CombatTestData.Combatant(
            "enemy",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -20),
            x: enemyX);

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [giver, ally, enemy],
            new SeededRandomSource(1));

        return (encounter, giver, ally, enemy);
    }
}
