using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The two masteries #81 executed: Cleave's second swing and Slow's speed cut, against
/// their printed text (page 90).
/// </summary>
public class CleaveAndSlowTests
{
    [Fact]
    public void CleaveCarriesIntoASecondCreatureWithoutTheAbilityModifier()
    {
        // Attack d20 18 (hit), damage d12 6 (+3 ability); cleave d20 17 (hit), damage
        // d12 5 with NO +3 — the printed subtraction.
        var (encounter, attacker, first, second) = Fight(
            new ScriptedRandomSource(20, 1, 1, 18, 6, 17, 5));

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, first));

        // First target took 6 + 3 = 9; the second took 5, bare.
        Assert.Equal(30 - 9, first.CurrentHitPoints);
        Assert.Equal(30 - 5, second.CurrentHitPoints);
        Assert.Contains(encounter.Log, step =>
            step.Narration.Contains("Cleave carries through into", StringComparison.Ordinal));
    }

    [Fact]
    public void CleaveNeedsTheSecondCreatureBesideTheFirst()
    {
        // The second enemy stands 3 squares from the first — outside "within 5 feet of
        // the first" — so the axe stops after one body.
        var (encounter, attacker, first, second) = Fight(
            new ScriptedRandomSource(20, 1, 1, 18, 6),
            secondX: 4,
            secondY: 0);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, first));

        Assert.Equal(30, second.CurrentHitPoints);
        Assert.DoesNotContain(encounter.Log, step =>
            step.Narration.Contains("Cleave", StringComparison.Ordinal));
    }

    [Fact]
    public void CleaveHappensOncePerTurnHoweverManyAttacksLand()
    {
        // Two swings from Extra Attack: the first cleaves, the second must not.
        var (encounter, attacker, first, second) = Fight(
            new ScriptedRandomSource(20, 1, 1, 18, 6, 17, 5, 18, 6),
            attacksPerAction: 2);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, first));
        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, first));

        Assert.Equal(
            1,
            encounter.Log.Count(step => step.Narration.Contains("Cleave carries", StringComparison.Ordinal)));
    }

    [Fact]
    public void SlowCutsSpeedByTenOnTheVictimsNextTurnAndOnlyTen()
    {
        var (encounter, attacker, first, _) = Fight(
            new ScriptedRandomSource(20, 1, 1, 18, 6),
            mastery: WeaponMastery.Slow);

        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, first));
        Assert.Contains(attacker.Id, first.Features.SlowedBy);

        // The victim's turn begins with 20 feet instead of 30.
        encounter.EndTurn();

        Assert.Same(first, encounter.ActiveCombatant);
        Assert.Equal(20, first.Turn.MovementFeet);
    }

    [Fact]
    public void SlowExpiresAtTheStartOfItsAuthorsNextTurn()
    {
        var (encounter, attacker, first, _) = Fight(
            new ScriptedRandomSource(20, 1, 1, 18, 6),
            mastery: WeaponMastery.Slow);

        encounter.Attack(attacker.Stats.Attacks[0].Name, first);
        encounter.EndTurn();

        Assert.Contains(attacker.Id, first.Features.SlowedBy);

        // The victim's turn passes, the bystander's passes, and the author's comes
        // round: released.
        encounter.EndTurn();
        encounter.EndTurn();

        Assert.Empty(first.Features.SlowedBy);
    }

    /// <summary>
    /// A masteried attacker at the origin and two enemies: the target beside it and a
    /// second enemy beside the target.
    /// </summary>
    private static (Encounter Encounter, Combatant Attacker, Combatant First, Combatant Second) Fight(
        IRandomSource random,
        int secondX = 1,
        int secondY = 1,
        int attacksPerAction = 1,
        WeaponMastery mastery = WeaponMastery.Cleave)
    {
        var attack = CombatTestData.MeleeAttack(bonus: 5, damage: "1d12 + 3") with
        {
            Mastery = mastery,
            AbilityModifier = 3,
        };

        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: [attack]) with
        {
            Character = new CombatantFeatures(
                [],
                attacksPerAction,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 5),
        };

        var attacker = CombatTestData.Combatant("attacker", stats: stats);

        var first = CombatTestData.Combatant(
            "first",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(armorClass: 12, maximumHitPoints: 30, initiativeBonus: -5),
            x: 1);

        // Diagonal by default, so it stands beside the first target and inside the
        // attacker's 5-foot reach — "within 5 feet of the first that is also within
        // your reach" needs both.
        var second = CombatTestData.Combatant(
            "second",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(armorClass: 12, maximumHitPoints: 30, initiativeBonus: -10),
            x: secondX,
            y: secondY);

        return (
            Encounter.Start(new Battlefield(12, 12), [attacker, first, second], random),
            attacker,
            first,
            second);
    }
}
