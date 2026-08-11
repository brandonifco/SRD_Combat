using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Tests.Combat;

namespace SRDCombat.Core.Tests.Characters;

/// <summary>
/// Covers the class features the engine actually implements, in real fights.
/// </summary>
public class ClassFeatureCombatTests
{
    [Fact]
    public void ExtraAttackLetsOneActionBuyTwoAttacks()
    {
        var (encounter, hero, monster) = Fight(Features([ClassFeature.ExtraAttack], attacksPerAction: 2));

        Assert.Null(encounter.Attack("Sword", monster));

        // The action is spent, but a second attack is still owed.
        Assert.False(hero.Turn.HasAction);
        Assert.Equal(1, hero.Features.AttacksRemainingThisAction);

        Assert.Null(encounter.Attack("Sword", monster));
        Assert.Equal(0, hero.Features.AttacksRemainingThisAction);

        // A third is refused: Extra Attack buys two attacks, not unlimited ones.
        Assert.Equal("action.spent", encounter.Attack("Sword", monster)?.Code);
    }

    [Fact]
    public void WithoutExtraAttackOneActionBuysOneAttack()
    {
        var (encounter, _, monster) = Fight(Features());

        Assert.Null(encounter.Attack("Sword", monster));
        Assert.Equal("action.spent", encounter.Attack("Sword", monster)?.Code);
    }

    [Fact]
    public void RageCostsABonusActionAndGrantsResistanceAndBonusDamage()
    {
        var (encounter, hero, monster) = Fight(
            Features([ClassFeature.Rage], rageDamageBonus: 2, rageUses: 2));

        Assert.Null(encounter.Rage());

        Assert.True(hero.Features.IsRaging);
        Assert.False(hero.Turn.HasBonusAction);
        Assert.Equal(1, hero.Features.RagesRemaining);

        // Bonus damage lands on the attack: 1d8 rolling 5, +2 Rage.
        Assert.Null(encounter.Attack("Sword", monster));
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Damage && step.Narration.Contains("7 Slashing", StringComparison.Ordinal));
    }

    [Fact]
    public void RageHalvesIncomingPhysicalDamage()
    {
        // The Barbarian rages on its own turn, then the monster swings into it.
        var hero = Character("hero", Features([ClassFeature.Rage], rageDamageBonus: 2, rageUses: 2), initiative: 10);
        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            x: 1,
            stats: CombatTestData.Stats(
                armorClass: 5,
                maximumHitPoints: 90,
                initiativeBonus: 0,
                attacks: [CombatTestData.MeleeAttack(bonus: 10, damage: "1d8")]));

        var encounter = Encounter.Start(
            new Battlefield(8, 8),
            [hero, monster],
            new SeededSequence(10, 1, 15, 5, 15, 7));

        Assert.Null(encounter.Rage());

        // Attacking sustains the Rage past the end of the turn.
        Assert.Null(encounter.Attack("Sword", monster));
        encounter.EndTurn();
        Assert.True(hero.Features.IsRaging);

        Assert.Equal(monster.Id, encounter.ActiveCombatant?.Id);
        Assert.Null(encounter.Attack("Sword", hero));

        // 7 Slashing halved to 3 by Rage.
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("3 Slashing damage (halved by Rage)", StringComparison.Ordinal));
    }

    [Fact]
    public void RageEndsIfTheBarbarianDoesNotFight()
    {
        var (encounter, hero, _) = Fight(Features([ClassFeature.Rage], rageDamageBonus: 2, rageUses: 2));

        Assert.Null(encounter.Rage());
        Assert.True(hero.Features.IsRaging);

        // A turn that lands no attack does not sustain the Rage.
        encounter.EndTurn();

        Assert.False(hero.Features.IsRaging);
        Assert.Contains(encounter.Log, step => step.Narration.Contains("Rage ends", StringComparison.Ordinal));
    }

    [Fact]
    public void RageIsRefusedWhenExhausted()
    {
        var (encounter, hero, _) = Fight(Features([ClassFeature.Rage], rageUses: 0));

        Assert.Equal("feature.rage.exhausted", encounter.Rage()?.Code);
        Assert.False(hero.Features.IsRaging);
    }

    [Fact]
    public void SneakAttackNeedsAdvantageOrAnAdjacentAlly()
    {
        // Alone and without Advantage: no Sneak Attack.
        var (soloEncounter, _, soloTarget) = Fight(
            Features([ClassFeature.SneakAttack], sneakAttack: "1d6"));

        Assert.Null(soloEncounter.Attack("Sword", soloTarget));
        Assert.DoesNotContain(
            soloEncounter.Log,
            step => step.Narration.Contains("Sneak Attack", StringComparison.Ordinal));

        // With an ally beside the target, it applies.
        var rogue = Character("rogue", Features([ClassFeature.SneakAttack], sneakAttack: "1d6"), initiative: 10);
        var ally = Character("ally", Features(), initiative: 5, x: 2);
        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Monsters,
            x: 1,
            stats: CombatTestData.Stats(armorClass: 5, maximumHitPoints: 60));

        var encounter = Encounter.Start(
            new Battlefield(8, 8),
            [rogue, ally, target],
            new SeededSequence(10, 5, 1, 15, 5, 4));

        Assert.Null(encounter.Attack("Sword", target));
        Assert.Contains(encounter.Log, step => step.Narration.Contains("Sneak Attack", StringComparison.Ordinal));
    }

    [Fact]
    public void SneakAttackAppliesOnlyOncePerTurn()
    {
        var rogue = Character(
            "rogue",
            Features([ClassFeature.SneakAttack, ClassFeature.ExtraAttack], sneakAttack: "1d6", attacksPerAction: 2),
            initiative: 10);
        var ally = Character("ally", Features(), initiative: 5, x: 2);
        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Monsters,
            x: 1,
            stats: CombatTestData.Stats(armorClass: 5, maximumHitPoints: 90));

        var encounter = Encounter.Start(
            new Battlefield(8, 8),
            [rogue, ally, target],
            new SeededSequence(10, 5, 1, 15, 5, 4, 15, 5, 4));

        Assert.Null(encounter.Attack("Sword", target));
        Assert.Null(encounter.Attack("Sword", target));

        Assert.Single(
            encounter.Log,
            step => step.Narration.Contains("Sneak Attack", StringComparison.Ordinal));
    }

    [Fact]
    public void SecondWindHealsForABonusAction()
    {
        var (encounter, hero, monster) = Fight(Features([ClassFeature.SecondWind], secondWindUses: 1), heroHitPoints: 40);

        // Take a wound first so the healing has somewhere to go.
        SRDCombat.Core.Rules.DamageRules.Apply(hero, 20, DamageType.Slashing);
        Assert.Equal(20, hero.CurrentHitPoints);

        Assert.Null(encounter.SecondWind());

        Assert.True(hero.CurrentHitPoints > 20);
        Assert.False(hero.Turn.HasBonusAction);
        Assert.Equal(0, hero.Features.SecondWindRemaining);
        Assert.Equal("feature.second_wind.exhausted", encounter.SecondWind()?.Code);

        _ = monster;
    }

    [Fact]
    public void ActionSurgeGivesTheActionBack()
    {
        var (encounter, hero, monster) = Fight(Features([ClassFeature.ActionSurge], actionSurgeUses: 1));

        // It is refused while an action is still available — it would be wasted.
        Assert.Equal("feature.action_surge.action_available", encounter.ActionSurge()?.Code);

        Assert.Null(encounter.Attack("Sword", monster));
        Assert.False(hero.Turn.HasAction);

        Assert.Null(encounter.ActionSurge());
        Assert.True(hero.Turn.HasAction);
        Assert.Null(encounter.Attack("Sword", monster));
    }

    [Fact]
    public void RecklessAttackGrantsAdvantageBothWays()
    {
        var (encounter, hero, monster) = Fight(Features([ClassFeature.RecklessAttack]));

        Assert.Null(encounter.RecklessAttack());
        Assert.True(hero.Features.IsRecklessThisTurn);

        Assert.Null(encounter.Attack("Sword", monster));

        // Two dice were rolled for the attack, which is Advantage.
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack
                && step.Narration.Contains("with Advantage", StringComparison.Ordinal));
    }

    [Fact]
    public void CunningActionDashesOrDisengagesForABonusAction()
    {
        var (encounter, hero, _) = Fight(Features([ClassFeature.CunningAction]));

        Assert.Null(encounter.CunningAction(CunningActionKind.Dash));

        Assert.Equal(60, hero.Turn.MovementFeet);
        Assert.False(hero.Turn.HasBonusAction);

        // The action itself is untouched — that is the whole point of the feature.
        Assert.True(hero.Turn.HasAction);
    }

    [Fact]
    public void UncannyDodgeHalvesDamageForAReaction()
    {
        var hero = Character("hero", Features([ClassFeature.UncannyDodge]), initiative: 0, hitPoints: 40);
        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            x: 1,
            stats: CombatTestData.Stats(initiativeBonus: 10, attacks: [CombatTestData.MeleeAttack(bonus: 10, damage: "1d8")]));

        var encounter = Encounter.Start(new Battlefield(8, 8), [hero, monster], new SeededSequence(1, 20, 15, 7));

        Assert.Null(encounter.Attack("Sword", hero));

        Assert.Contains(encounter.Log, step => step.Narration.Contains("Uncanny Dodge", StringComparison.Ordinal));
        Assert.False(hero.Turn.HasReaction);

        // 7 damage halved to 3.
        Assert.Equal(37, hero.CurrentHitPoints);
    }

    [Fact]
    public void AFeatureTheCharacterDoesNotHaveIsRefused()
    {
        var (encounter, _, _) = Fight(Features());

        Assert.Equal("feature.absent", encounter.Rage()?.Code);
        Assert.Equal("feature.absent", encounter.SecondWind()?.Code);
        Assert.Equal("feature.absent", encounter.ActionSurge()?.Code);
        Assert.Equal("feature.absent", encounter.CunningAction(CunningActionKind.Dash)?.Code);
    }

    private static CombatantFeatures Features(
        ClassFeature[]? features = null,
        int attacksPerAction = 1,
        string? sneakAttack = null,
        int rageDamageBonus = 0,
        int rageUses = 0,
        int secondWindUses = 0,
        int actionSurgeUses = 0) =>
        new(
            features ?? [],
            attacksPerAction,
            sneakAttack is null ? null : DiceExpression.Parse(sneakAttack),
            rageDamageBonus,
            rageUses,
            secondWindUses,
            actionSurgeUses,
            Level: 5);

    private static Combatant Character(
        string id,
        CombatantFeatures features,
        int initiative,
        int x = 0,
        int hitPoints = 40)
    {
        var stats = CombatTestData.Stats(
            maximumHitPoints: hitPoints,
            initiativeBonus: initiative,
            diesAtZeroHitPoints: false,
            attacks: [CombatTestData.MeleeAttack(bonus: 10, damage: "1d8")]) with
        {
            Character = features,
        };

        return new Combatant(id, id, CombatTestData.Heroes, stats, new GridPosition(x, 0));
    }

    /// <summary>A hero who acts first, next to a monster tough enough to survive the test.</summary>
    private static (Encounter Encounter, Combatant Hero, Combatant Monster) Fight(
        CombatantFeatures features,
        int heroHitPoints = 40)
    {
        var hero = Character("hero", features, initiative: 10, hitPoints: heroHitPoints);
        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            x: 1,
            stats: CombatTestData.Stats(armorClass: 5, maximumHitPoints: 90));

        var encounter = Encounter.Start(
            new Battlefield(8, 8),
            [hero, monster],
            new SeededSequence(10, 1, 15, 5, 15, 5, 15, 5, 15, 5));

        return (encounter, hero, monster);
    }
}
