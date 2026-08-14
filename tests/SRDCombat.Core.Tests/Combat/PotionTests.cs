using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Potions of Healing: what they restore, what they cost, and the refusals that stop
/// one being poured away.
/// </summary>
/// <remarks>
/// The printed rules these pin: "Drinking a potion or administering it to another
/// creature requires a Bonus Action" (page 204) — the same cost either way — and the
/// Potions of Healing table's four rows (page 236). The reach on administering is this
/// engine's stated reading, recorded on <c>PotionRules</c>.
/// </remarks>
public class PotionTests
{
    [Fact]
    public void ThePotencyTableMatchesPrint()
    {
        Assert.Equal("2d4 + 2", PotionRules.Healing(HealingPotion.Standard).ToString());
        Assert.Equal("4d4 + 4", PotionRules.Healing(HealingPotion.Greater).ToString());
        Assert.Equal("8d4 + 8", PotionRules.Healing(HealingPotion.Superior).ToString());
        Assert.Equal("10d4 + 20", PotionRules.Healing(HealingPotion.Supreme).ToString());
    }

    [Fact]
    public void DrinkingHealsAndCostsOnlyTheBonusAction()
    {
        var (encounter, drinker, _) = Fight(
            new ScriptedRandomSource(20, 10, 1, 4, 4),
            wounded: 5,
            potions: 1);

        // 2d4 + 2 on scripted fours: 4 + 4 + 2 = 10.
        Assert.Null(encounter.DrinkPotion(HealingPotion.Standard));

        Assert.Equal(15, drinker.CurrentHitPoints);
        Assert.False(drinker.Turn.HasBonusAction);
        Assert.True(drinker.Turn.HasAction);
        Assert.Equal(0, drinker.Inventory.TotalPotions);
    }

    [Fact]
    public void AdministeringPutsAFallenAllyBackOnTheirFeet()
    {
        var (encounter, healer, ally) = Fight(
            new ScriptedRandomSource(20, 10, 1, 3, 3),
            wounded: 20,
            potions: 1,
            allyAt: 0);

        Assert.True(ally.HasCondition(Definitions.ConditionType.Unconscious));

        Assert.Null(encounter.DrinkPotion(HealingPotion.Standard, ally));

        // The potion drinker is the ally; the healer keeps their own hit points.
        Assert.Equal(8, ally.CurrentHitPoints);
        Assert.False(ally.HasCondition(Definitions.ConditionType.Unconscious));
        Assert.Equal(20, healer.CurrentHitPoints);
        Assert.False(healer.Turn.HasBonusAction);
        Assert.True(healer.Turn.HasAction);
    }

    [Fact]
    public void AdministeringBeyondReachIsRefusedAndSpendsNothing()
    {
        var (encounter, healer, ally) = Fight(
            new ScriptedRandomSource(20, 10, 1),
            wounded: 20,
            potions: 1,
            allyAt: 0,
            allyX: 4);

        var refusal = encounter.DrinkPotion(HealingPotion.Standard, ally);

        Assert.Equal("potion.out_of_reach", refusal?.Code);

        // The refusal is the point: neither the potion nor the Bonus Action is gone.
        Assert.Equal(1, healer.Inventory.TotalPotions);
        Assert.True(healer.Turn.HasBonusAction);
        Assert.Equal(0, ally.CurrentHitPoints);
    }

    [Fact]
    public void APotionIsNotWastedOnTheDead()
    {
        var (encounter, healer, ally) = Fight(
            new ScriptedRandomSource(20, 10, 1),
            wounded: 20,
            potions: 1,
            allyAt: 0,
            allyX: 1);

        // Three failed Death Saving Throws kill a character; the potion arrives too late.
        DamageRules.Apply(ally, 100, Definitions.DamageType.Slashing);

        Assert.True(ally.IsDead);

        var refusal = encounter.DrinkPotion(HealingPotion.Standard, ally);

        Assert.Equal("potion.target_dead", refusal?.Code);
        Assert.Equal(1, healer.Inventory.TotalPotions);
        Assert.True(healer.Turn.HasBonusAction);
    }

    [Fact]
    public void APotionNobodyCarriesIsRefused()
    {
        var (encounter, healer, _) = Fight(new ScriptedRandomSource(20, 10, 1), wounded: 5, potions: 0);

        Assert.Equal("potion.none", encounter.DrinkPotion(HealingPotion.Standard)?.Code);
        Assert.True(healer.Turn.HasBonusAction);
    }

    [Fact]
    public void ASpentBonusActionRefusesAndKeepsThePotion()
    {
        var (encounter, drinker, _) = Fight(
            new ScriptedRandomSource(20, 10, 1, 4, 4),
            wounded: 5,
            potions: 2);

        Assert.Null(encounter.DrinkPotion(HealingPotion.Standard));

        // The second one this turn is refused, and the potion stays in the pack.
        Assert.Equal("bonus_action.spent", encounter.DrinkPotion(HealingPotion.Standard)?.Code);
        Assert.Equal(1, drinker.Inventory.TotalPotions);
    }

    [Fact]
    public void TheWeakestPotionIsTheOneAClientReachesFor()
    {
        var combatant = CombatTestData.Character("hero");

        Assert.Null(combatant.Inventory.Weakest);

        var carrying = new Combatant(
            "hero",
            "hero",
            CombatTestData.Heroes,
            CombatTestData.Stats(diesAtZeroHitPoints: false),
            new GridPosition(0, 0),
            new CombatantCarryOver(
                20,
                Potions: new Dictionary<HealingPotion, int>
                {
                    [HealingPotion.Greater] = 1,
                    [HealingPotion.Standard] = 2,
                }));

        Assert.Equal(HealingPotion.Standard, carrying.Inventory.Weakest);
        Assert.Equal(3, carrying.Inventory.TotalPotions);
    }

    [Fact]
    public void ACasualtysOwnPotionCanBeAdministeredByAnybodyInReach()
    {
        // The bug a played run found: a potion carried by the character who goes down
        // could only be drunk by the one person who could not act. Whoever is in reach
        // can use it now.
        var (encounter, healer, ally) = Fight(
            new SeededSequence(20, 1, 3, 3), wounded: 20, potions: 0, allyPotions: 1);
        DamageRulesHelper.Down(ally);

        Assert.Equal(0, healer.Inventory.TotalPotions);
        Assert.Null(encounter.DrinkPotion(HealingPotion.Standard, ally));

        Assert.True(ally.CurrentHitPoints > 0, "the casualty's own potion did not get them up");
        Assert.Equal(0, ally.Inventory.TotalPotions);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("administers their own Potion of Healing", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDrinkersOwnFlaskIsSpentBeforeTheHelpersPack()
    {
        // Spending someone's own potion on them before opening your pack is what a
        // person does, and it leaves the rescuer's supplies intact.
        var (encounter, healer, ally) = Fight(
            new SeededSequence(20, 1, 3, 3), wounded: 20, potions: 2, allyPotions: 1);
        DamageRulesHelper.Down(ally);

        Assert.Null(encounter.DrinkPotion(HealingPotion.Standard, ally));

        Assert.Equal(0, ally.Inventory.TotalPotions);
        Assert.Equal(2, healer.Inventory.TotalPotions);
    }

    [Fact]
    public void AdministeringIsRefusedWhenNeitherPartyCarriesOne()
    {
        var (encounter, _, ally) = Fight(new SeededSequence(20, 1), wounded: 20, potions: 0);
        DamageRulesHelper.Down(ally);

        var refusal = encounter.DrinkPotion(HealingPotion.Standard, ally);

        Assert.Equal("potion.none", refusal?.Code);
        Assert.Equal(0, ally.CurrentHitPoints);
    }

    /// <summary>
    /// A healer and an ally, both characters, with the healer acting first. The scripted
    /// dice open with the initiative rolls.
    /// </summary>
    private static (Encounter Encounter, Combatant Healer, Combatant Ally) Fight(
        IRandomSource random,
        int wounded,
        int potions,
        int allyAt = 20,
        int allyX = 1,
        int allyPotions = 0)
    {
        var healer = new Combatant(
            "healer",
            "Healer",
            CombatTestData.Heroes,
            CombatTestData.Stats(maximumHitPoints: 20, initiativeBonus: 10, diesAtZeroHitPoints: false),
            new GridPosition(0, 0),
            new CombatantCarryOver(
                wounded,
                Potions: potions > 0
                    ? new Dictionary<HealingPotion, int> { [HealingPotion.Standard] = potions }
                    : null));

        var ally = new Combatant(
            "ally",
            "Ally",
            CombatTestData.Heroes,
            CombatTestData.Stats(maximumHitPoints: 20, initiativeBonus: -10, diesAtZeroHitPoints: false),
            new GridPosition(allyX, 0),
            new CombatantCarryOver(
                allyAt,
                Potions: allyPotions > 0
                    ? new Dictionary<HealingPotion, int> { [HealingPotion.Standard] = allyPotions }
                    : null));

        // An enemy far away, so the fight does not end the moment it starts.
        var enemy = CombatTestData.Combatant(
            "enemy",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -20),
            x: 10);

        return (Encounter.Start(new Battlefield(12, 12), [healer, ally, enemy], random), healer, ally);
    }
}
