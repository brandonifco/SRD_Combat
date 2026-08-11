using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

public class EncounterTests
{
    [Fact]
    public void Start_RollsInitiativeAndOrdersHighestFirst()
    {
        var quick = CombatTestData.Combatant("quick", stats: CombatTestData.Stats(initiativeBonus: 5));
        var slow = CombatTestData.Combatant("slow", sideId: CombatTestData.Monsters, stats: CombatTestData.Stats(initiativeBonus: 0), x: 5);

        // quick rolls 10 (+5 = 15), slow rolls 12 (+0 = 12).
        var encounter = Encounter.Start(Field(), [quick, slow], new SeededSequence(10, 12));

        Assert.Equal(["quick", "slow"], encounter.TurnOrder.Select(combatant => combatant.Id));
        Assert.Equal(1, encounter.Round);
        Assert.Equal("quick", encounter.ActiveCombatant?.Id);
    }

    [Fact]
    public void Start_BreaksInitiativeTiesDeterministically()
    {
        // Both roll 10 with the same bonus. The tie-break is Dexterity then id, so the
        // order is fixed rather than depending on enumeration — which is what lets the
        // frozen transcripts mean anything.
        var beta = CombatTestData.Combatant("beta", stats: CombatTestData.Stats(initiativeBonus: 2));
        var alpha = CombatTestData.Combatant("alpha", sideId: CombatTestData.Monsters, stats: CombatTestData.Stats(initiativeBonus: 2), x: 5);

        var encounter = Encounter.Start(Field(), [beta, alpha], new SeededSequence(10, 10));

        Assert.Equal(["alpha", "beta"], encounter.TurnOrder.Select(combatant => combatant.Id));
    }

    [Fact]
    public void Attack_OutOfReach_IsRefusedWithoutSpendingTheAction()
    {
        var (encounter, hero, _) = TwoCombatants(heroX: 0, monsterX: 5);

        var refusal = encounter.Attack("Sword", encounter.Combatants.Single(c => c.Id == "monster"));

        Assert.Equal("attack.out_of_range", refusal?.Code);
        Assert.True(hero.Turn.HasAction);
    }

    [Fact]
    public void Attack_TwiceInOneTurn_IsRefused()
    {
        var (encounter, _, monster) = TwoCombatants(heroX: 0, monsterX: 1);

        Assert.Null(encounter.Attack("Sword", monster));
        Assert.Equal("action.spent", encounter.Attack("Sword", monster)?.Code);
    }

    [Fact]
    public void Attack_WithAnUnknownAttack_IsRefused()
    {
        var (encounter, _, monster) = TwoCombatants(heroX: 0, monsterX: 1);

        Assert.Equal("attack.unknown", encounter.Attack("Frying Pan", monster)?.Code);
    }

    [Fact]
    public void Move_BeyondTheMovementBudget_IsRefused()
    {
        var (encounter, _, _) = TwoCombatants(heroX: 0, monsterX: 9);

        Assert.Equal("movement.unreachable", encounter.Move(new GridPosition(9, 9))?.Code);
    }

    [Fact]
    public void Move_SpendsMovementAndRecordsTheRoute()
    {
        var (encounter, hero, _) = TwoCombatants(heroX: 0, monsterX: 9);

        Assert.Null(encounter.Move(new GridPosition(3, 0)));

        Assert.Equal(new GridPosition(3, 0), hero.Position);
        Assert.Equal(15, hero.Turn.MovementFeet);
        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.Move);
    }

    [Fact]
    public void Dash_GrantsAnotherSpeedOfMovement()
    {
        var (encounter, hero, _) = TwoCombatants(heroX: 0, monsterX: 9);

        Assert.Null(encounter.Dash());

        Assert.Equal(60, hero.Turn.MovementFeet);
        Assert.False(hero.Turn.HasAction);
    }

    [Fact]
    public void Dodge_LastsUntilTheStartOfTheDodgersNextTurn()
    {
        var (encounter, hero, monster) = TwoCombatants(heroX: 0, monsterX: 1);

        Assert.Null(encounter.Dodge());
        Assert.True(hero.Turn.IsDodging);

        // Through the monster's whole turn the benefit persists.
        encounter.EndTurn();
        Assert.Equal(monster.Id, encounter.ActiveCombatant?.Id);
        Assert.True(hero.Turn.IsDodging);

        // It falls away the moment the hero's own next turn starts.
        encounter.EndTurn();
        Assert.Equal(hero.Id, encounter.ActiveCombatant?.Id);
        Assert.False(hero.Turn.IsDodging);
    }

    [Fact]
    public void MovingOutOfReach_ProvokesAnOpportunityAttack()
    {
        var hero = CombatTestData.Combatant("hero", x: 1, y: 0, stats: CombatTestData.Stats(initiativeBonus: 10));
        var monster = CombatTestData.Combatant("monster", sideId: CombatTestData.Monsters, x: 0, y: 0);

        var encounter = Encounter.Start(Field(), [hero, monster], new SeededSequence(
            10, 1,          // initiative
            18, 4));        // the opportunity attack, then its damage die

        Assert.Null(encounter.Move(new GridPosition(5, 0)));

        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.OpportunityAttack);
        Assert.False(monster.Turn.HasReaction);
        Assert.True(hero.CurrentHitPoints < hero.Stats.MaximumHitPoints);
    }

    [Fact]
    public void Disengaging_AvoidsTheOpportunityAttack()
    {
        var hero = CombatTestData.Combatant("hero", x: 1, y: 0, stats: CombatTestData.Stats(initiativeBonus: 10));
        var monster = CombatTestData.Combatant("monster", sideId: CombatTestData.Monsters, x: 0, y: 0);

        var encounter = Encounter.Start(Field(), [hero, monster], new SeededSequence(10, 1));

        Assert.Null(encounter.Disengage());
        Assert.Null(encounter.Move(new GridPosition(5, 0)));

        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.OpportunityAttack);
        Assert.Equal(hero.Stats.MaximumHitPoints, hero.CurrentHitPoints);
    }

    [Fact]
    public void AProneCombatant_MustStandBeforeMoving()
    {
        var (encounter, hero, _) = TwoCombatants(heroX: 0, monsterX: 9);
        hero.AddCondition(ConditionType.Prone);

        Assert.Equal("combatant.prone", encounter.Move(new GridPosition(1, 0))?.Code);

        Assert.Null(encounter.StandUp());
        Assert.False(hero.HasCondition(ConditionType.Prone));

        // Standing cost half the 30 ft. Speed.
        Assert.Equal(15, hero.Turn.MovementFeet);
    }

    [Fact]
    public void TheFightEndsWhenOnlyOneSideIsStanding()
    {
        var hero = CombatTestData.Combatant("hero", x: 0, stats: CombatTestData.Stats(initiativeBonus: 10));
        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            x: 1,
            stats: CombatTestData.Stats(maximumHitPoints: 1, armorClass: 1));

        // Initiative, then a natural 20 (auto-hit, auto-crit), then two damage dice.
        var encounter = Encounter.Start(Field(), [hero, monster], new SeededSequence(10, 1, 20, 4, 4));

        Assert.Null(encounter.Attack("Sword", monster));

        Assert.True(encounter.IsComplete);
        Assert.Equal(CombatTestData.Heroes, encounter.WinningSide);
        Assert.Null(encounter.ActiveCombatant);
        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.EncounterEnded);
    }

    [Fact]
    public void ADyingCharacterRollsADeathSaveAtTheStartOfItsTurn()
    {
        var hero = CombatTestData.Character("hero", maximumHitPoints: 20, x: 0);
        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            x: 5,
            stats: CombatTestData.Stats(initiativeBonus: 10));

        // Monster goes first; the hero is downed before its own turn comes round.
        var encounter = Encounter.Start(Field(), [hero, monster], new SeededSequence(1, 20, 5));
        DamageRulesHelper.Down(hero);

        encounter.EndTurn();

        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.DeathSave);
        Assert.Equal(1, hero.DeathSaveFailures);
    }

    [Fact]
    public void ADeadCombatantsTurnIsSkippedEntirely()
    {
        var hero = CombatTestData.Combatant("hero", x: 0, stats: CombatTestData.Stats(initiativeBonus: 10));
        var first = CombatTestData.Combatant("m1", sideId: CombatTestData.Monsters, x: 5, stats: CombatTestData.Stats(initiativeBonus: 5));
        var second = CombatTestData.Combatant("m2", sideId: CombatTestData.Monsters, x: 6, stats: CombatTestData.Stats(initiativeBonus: 0));

        var encounter = Encounter.Start(Field(), [hero, first, second], new SeededSequence(10, 10, 10, 5, 5, 5, 5, 5, 5));
        DamageRulesHelper.Kill(first);

        encounter.EndTurn();

        Assert.Equal("m2", encounter.ActiveCombatant?.Id);
    }

    private static Battlefield Field() => new(12, 12);

    private static (Encounter Encounter, Combatant Hero, Combatant Monster) TwoCombatants(int heroX, int monsterX)
    {
        var hero = CombatTestData.Combatant("hero", x: heroX, stats: CombatTestData.Stats(initiativeBonus: 10));
        var monster = CombatTestData.Combatant("monster", sideId: CombatTestData.Monsters, x: monsterX);

        // Enough scripted dice for initiative plus whatever the test then does.
        var encounter = Encounter.Start(Field(), [hero, monster], new SeededSequence(10, 1, 15, 4, 15, 4, 15, 4));

        return (encounter, hero, monster);
    }
}

/// <summary>
/// A die that plays a script and then falls back to a fixed value, for tests that care
/// about the first few rolls and not the rest.
/// </summary>
internal sealed class SeededSequence(params int[] scripted) : IRandomSource
{
    private readonly int[] _scripted = scripted;
    private int _index;

    public int Roll(int sides)
    {
        var value = _index < _scripted.Length ? _scripted[_index++] : 10;

        return Math.Clamp(value, 1, sides);
    }
}

/// <summary>Shortcuts for putting a combatant into a particular state in a test.</summary>
internal static class DamageRulesHelper
{
    public static void Down(Combatant combatant) =>
        SRDCombat.Core.Rules.DamageRules.Apply(
            combatant,
            combatant.Stats.MaximumHitPoints,
            DamageType.Slashing);

    public static void Kill(Combatant combatant)
    {
        Down(combatant);

        if (!combatant.IsDead)
        {
            SRDCombat.Core.Rules.DamageRules.Apply(
                combatant,
                combatant.Stats.MaximumHitPoints,
                DamageType.Slashing);
        }
    }
}
