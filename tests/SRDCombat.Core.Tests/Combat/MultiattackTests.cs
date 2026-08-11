using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Covers Multiattack: a monster's Attack action buying several swings, and the
/// constraint on which attacks those swings may use.
/// </summary>
public class MultiattackTests
{
    [Fact]
    public void OneActionBuysEveryAttackTheMultiattackGrants()
    {
        var (encounter, monster, target) = Fight(new MultiattackEffect(2, ["Claw"], AnyCombination: false));

        Assert.Equal(2, monster.Stats.AttacksPerAction);

        Assert.Null(encounter.Attack("Claw", target));
        Assert.False(monster.Turn.HasAction);
        Assert.Equal(1, monster.Features.AttacksRemainingThisAction);

        Assert.Null(encounter.Attack("Claw", target));
        Assert.Equal(0, monster.Features.AttacksRemainingThisAction);

        Assert.Equal("action.spent", encounter.Attack("Claw", target)?.Code);
    }

    [Fact]
    public void WithoutAMultiattackOneActionBuysOneAttack()
    {
        var (encounter, monster, target) = Fight(multiattack: null);

        Assert.Equal(1, monster.Stats.AttacksPerAction);
        Assert.Null(encounter.Attack("Claw", target));
        Assert.Equal("action.spent", encounter.Attack("Claw", target)?.Code);
    }

    [Fact]
    public void AnAttackOutsideTheMultiattackIsRefused()
    {
        // "The creature makes two Claw attacks" does not license a Tail swing.
        var (encounter, _, target) = Fight(new MultiattackEffect(2, ["Claw"], AnyCombination: false));

        Assert.Equal("attack.not_in_multiattack", encounter.Attack("Tail", target)?.Code);
        Assert.Null(encounter.Attack("Claw", target));
    }

    [Fact]
    public void AFreeCombinationMultiattackAllowsEitherNamedAttack()
    {
        var (encounter, _, target) = Fight(new MultiattackEffect(2, ["Claw", "Tail"], AnyCombination: true));

        Assert.Null(encounter.Attack("Tail", target));
        Assert.Null(encounter.Attack("Claw", target));
    }

    [Fact]
    public void AMultiattackNamingAnAttackTheCreatureLacksIsDropped()
    {
        // Several stat blocks name an attack granted by a trait or a spell. Handing out
        // swings the creature has no way to make would be worse than granting none.
        var monster = MonsterWith(
            new MultiattackEffect(2, ["Spellcasting"], AnyCombination: false),
            attackNames: ["Claw"]);

        Assert.Null(monster.Multiattack);
        Assert.Equal(1, monster.AttacksPerAction);
    }

    [Fact]
    public void AMultiattackKeepsOnlyTheNamedAttacksTheCreatureActuallyHas()
    {
        var monster = MonsterWith(
            new MultiattackEffect(2, ["Claw", "Eldritch Blast"], AnyCombination: true),
            attackNames: ["Claw"]);

        Assert.Equal(["Claw"], monster.Multiattack?.AttackNames);
        Assert.False(monster.AllowsInMultiattack("Eldritch Blast"));
    }

    [Fact]
    public void TheTurnTakerSpendsEverySwing()
    {
        var (encounter, monster, target) = Fight(
            new MultiattackEffect(3, ["Claw"], AnyCombination: false),
            targetHitPoints: 200);

        SimpleTacticsPolicy.TakeTurn(encounter);

        // Three attack lines from one turn, not one.
        Assert.Equal(
            3,
            encounter.Log.Count(step => step.Kind == CombatStepKind.Attack && step.ActorId == monster.Id));

        _ = target;
    }

    /// <summary>Builds monster stats from a stat block carrying the given Multiattack.</summary>
    private static CombatantStats MonsterWith(MultiattackEffect multiattack, IReadOnlyList<string> attackNames)
    {
        var entries = new List<MonsterEntry>
        {
            new("Multiattack", MonsterEntrySection.Action, "text", Mechanics: EntryMechanics.Multiattack,
                Multiattack: multiattack),
        };

        entries.AddRange(attackNames.Select(name => new MonsterEntry(
            name,
            MonsterEntrySection.Action,
            "text",
            new MonsterAttack(
                AttackKind.Melee,
                5,
                5,
                null,
                null,
                [new AttackDamage(Core.Dice.DiceExpression.Parse("1d6"), DamageType.Slashing, 3)]),
            EntryMechanics.Attack)));

        return CombatantStats.FromMonster(new MonsterDefinition
        {
            Id = "monster.test",
            Name = "Test",
            Sizes = [CreatureSize.Medium],
            Type = CreatureType.Monstrosity,
            Alignment = "Unaligned",
            ArmorClass = 12,
            InitiativeBonus = 10,
            HitPoints = 30,
            HitDice = Core.Dice.DiceExpression.Parse("4d10 + 8"),
            Speeds = new Dictionary<MovementMode, int> { [MovementMode.Walk] = 30 },
            Abilities = Enum.GetValues<Ability>().ToDictionary(a => a, _ => new MonsterAbility(14, 2)),
            Skills = new Dictionary<string, int>(),
            DamageResponses = new Dictionary<DamageType, DamageResponse>(),
            ConditionImmunities = [],
            Senses = [],
            PassivePerception = 10,
            Languages = [],
            Gear = [],
            ChallengeRating = 1m,
            ExperiencePoints = 200,
            ProficiencyBonus = 2,
            Entries = entries,
            SourcePage = 1,
        });
    }

    private static (Encounter Encounter, Combatant Monster, Combatant Target) Fight(
        MultiattackEffect? multiattack,
        int targetHitPoints = 200)
    {
        var stats = multiattack is null
            ? MonsterWith(new MultiattackEffect(1, ["Claw"], false), ["Claw", "Tail"])
            : MonsterWith(multiattack, ["Claw", "Tail"]);

        var monster = new Combatant("monster", "Monster", CombatTestData.Monsters, stats, new GridPosition(0, 0));

        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Heroes,
            x: 1,
            stats: CombatTestData.Stats(armorClass: 5, maximumHitPoints: targetHitPoints));

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [monster, target],
            new SeededSequence(20, 1, 15, 3, 15, 3, 15, 3, 15, 3));

        return (encounter, monster, target);
    }
}
