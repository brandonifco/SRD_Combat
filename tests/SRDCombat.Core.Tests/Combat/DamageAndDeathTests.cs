using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

public class DamageAndDeathTests
{
    [Theory]
    [InlineData(7, DamageResponse.Resistance, 3)]
    [InlineData(8, DamageResponse.Resistance, 4)]
    [InlineData(7, DamageResponse.Vulnerability, 14)]
    [InlineData(7, DamageResponse.Immunity, 0)]
    [InlineData(7, null, 7)]
    public void ApplyResponse_HalvesRoundingDown(int amount, DamageResponse? response, int expected) =>
        Assert.Equal(expected, DamageRules.ApplyResponse(amount, response));

    [Fact]
    public void TemporaryHitPoints_AbsorbDamageFirstAndDoNotStack()
    {
        var target = CombatTestData.Character(maximumHitPoints: 20);

        DamageRules.GrantTemporaryHitPoints(target, 5);
        DamageRules.GrantTemporaryHitPoints(target, 3);

        // The smaller pool is declined rather than added.
        Assert.Equal(5, target.TemporaryHitPoints);

        var result = DamageRules.Apply(target, 8, DamageType.Slashing);

        Assert.Equal(5, result.AbsorbedByTemporaryHitPoints);
        Assert.Equal(0, target.TemporaryHitPoints);
        Assert.Equal(17, target.CurrentHitPoints);
    }

    [Fact]
    public void AMonsterDiesTheInstantItReachesZero()
    {
        var monster = CombatTestData.Combatant(
            "m",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(maximumHitPoints: 10, diesAtZeroHitPoints: true));

        var result = DamageRules.Apply(monster, 10, DamageType.Slashing);

        Assert.True(result.Died);
        Assert.True(monster.IsDead);
        Assert.False(monster.IsDying);
    }

    [Fact]
    public void ACharacterFallsUnconsciousRatherThanDying()
    {
        var hero = CombatTestData.Character(maximumHitPoints: 20);

        var result = DamageRules.Apply(hero, 20, DamageType.Slashing);

        Assert.True(result.Downed);
        Assert.False(result.Died);
        Assert.False(hero.IsDead);
        Assert.True(hero.IsDying);
        Assert.True(hero.HasCondition(ConditionType.Unconscious));

        // Unconscious brings Incapacitated and Prone with it.
        Assert.True(hero.HasCondition(ConditionType.Incapacitated));
        Assert.True(hero.HasCondition(ConditionType.Prone));
        Assert.False(hero.CanAct);
    }

    [Fact]
    public void MassiveDamage_KillsACharacterOutright()
    {
        // The SRD's own worked example: 12 hit point maximum, currently 6, takes 18.
        // 12 damage remains after reaching 0, which equals the maximum, so they die.
        var hero = CombatTestData.Character(maximumHitPoints: 12);
        DamageRules.Apply(hero, 6, DamageType.Slashing);

        var result = DamageRules.Apply(hero, 18, DamageType.Slashing);

        Assert.True(result.Died);
        Assert.True(hero.IsDead);
    }

    [Fact]
    public void DamageAtZeroHitPoints_CostsADeathSaveFailure()
    {
        var hero = CombatTestData.Character(maximumHitPoints: 20);
        DamageRules.Apply(hero, 20, DamageType.Slashing);

        var result = DamageRules.Apply(hero, 3, DamageType.Slashing);

        Assert.Equal(1, result.DeathSaveFailures);
        Assert.Equal(1, hero.DeathSaveFailures);
    }

    [Fact]
    public void ACriticalHitAtZeroHitPoints_CostsTwoFailures()
    {
        var hero = CombatTestData.Character(maximumHitPoints: 20);
        DamageRules.Apply(hero, 20, DamageType.Slashing);

        var result = DamageRules.Apply(hero, 3, DamageType.Slashing, fromCriticalHit: true);

        Assert.Equal(2, result.DeathSaveFailures);
    }

    [Fact]
    public void TakingDamageWhileStable_EndsStability()
    {
        var hero = CombatTestData.Character(maximumHitPoints: 20);
        DamageRules.Apply(hero, 20, DamageType.Slashing);

        // Three successes to stabilise.
        var die = new ScriptedRandomSource(15, 15, 15);
        DeathSaveRules.Roll(die, hero);
        DeathSaveRules.Roll(die, hero);
        DeathSaveRules.Roll(die, hero);
        Assert.True(hero.IsStable);

        DamageRules.Apply(hero, 2, DamageType.Slashing);

        Assert.False(hero.IsStable);
        Assert.True(hero.IsDying);
    }

    [Fact]
    public void ThreeDeathSaveFailures_Kill()
    {
        var hero = CombatTestData.Character(maximumHitPoints: 20);
        DamageRules.Apply(hero, 20, DamageType.Slashing);

        var die = new ScriptedRandomSource(5, 5, 5);
        DeathSaveRules.Roll(die, hero);
        DeathSaveRules.Roll(die, hero);
        var third = DeathSaveRules.Roll(die, hero);

        Assert.True(third.Died);
        Assert.True(hero.IsDead);
    }

    [Fact]
    public void ANaturalOneOnADeathSave_CostsTwoFailures()
    {
        var hero = CombatTestData.Character(maximumHitPoints: 20);
        DamageRules.Apply(hero, 20, DamageType.Slashing);

        var result = DeathSaveRules.Roll(new ScriptedRandomSource(1), hero);

        Assert.Equal(2, result.Failures);
        Assert.Equal(2, hero.DeathSaveFailures);
        Assert.False(hero.IsDead);
    }

    [Fact]
    public void ANaturalTwentyOnADeathSave_RestoresOneHitPoint()
    {
        var hero = CombatTestData.Character(maximumHitPoints: 20);
        DamageRules.Apply(hero, 20, DamageType.Slashing);

        var result = DeathSaveRules.Roll(new ScriptedRandomSource(20), hero);

        Assert.True(result.RegainedConsciousness);
        Assert.Equal(1, hero.CurrentHitPoints);
        Assert.False(hero.HasCondition(ConditionType.Unconscious));
        Assert.True(hero.CanAct);
    }

    [Fact]
    public void ExactlyTen_SucceedsOnADeathSave()
    {
        var hero = CombatTestData.Character(maximumHitPoints: 20);
        DamageRules.Apply(hero, 20, DamageType.Slashing);

        Assert.True(DeathSaveRules.Roll(new ScriptedRandomSource(10), hero).Succeeded);
    }

    [Fact]
    public void RegainingHitPoints_ResetsDeathSaves()
    {
        var hero = CombatTestData.Character(maximumHitPoints: 20);
        DamageRules.Apply(hero, 20, DamageType.Slashing);
        DeathSaveRules.Roll(new ScriptedRandomSource(5), hero);
        Assert.Equal(1, hero.DeathSaveFailures);

        DamageRules.Heal(hero, 4);

        Assert.Equal(0, hero.DeathSaveFailures);
        Assert.Equal(4, hero.CurrentHitPoints);
        Assert.False(hero.HasCondition(ConditionType.Unconscious));
    }

    [Fact]
    public void HealingDoesNotRaiseTheDead()
    {
        var monster = CombatTestData.Combatant(
            "m",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(maximumHitPoints: 5));
        DamageRules.Apply(monster, 5, DamageType.Slashing);

        Assert.Equal(0, DamageRules.Heal(monster, 10));
        Assert.True(monster.IsDead);
    }

    [Fact]
    public void ConditionImmunity_RefusesTheCondition()
    {
        var target = CombatTestData.Combatant(
            "m",
            stats: CombatTestData.Stats(conditionImmunities: [ConditionType.Poisoned]));

        Assert.False(target.AddCondition(ConditionType.Poisoned));
        Assert.False(target.HasCondition(ConditionType.Poisoned));
    }
}
