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
    public void TheCapGuardRefusesAnAlreadyCappedNameWhileBudgetRemainsForAnother()
    {
        // Issue #343's STRICT-EXACT reading: "two Claw attacks and one Sting attack"
        // permits exactly two Claws and one Sting — never a third Claw in the Sting's
        // place, even though the action still has a swing of budget left when the
        // third Claw is attempted. This is the case that pins the rule itself, as
        // opposed to the budget-exhaustion case below, which pins a different, more
        // general rule.
        var (encounter, monster, target) = Fight(
            new MultiattackEffect(
                3,
                ["Claw", "Sting"],
                AnyCombination: false,
                Composition: [new MultiattackComponent("Claw", 2), new MultiattackComponent("Sting", 1)]),
            attackNames: ["Claw", "Sting"]);

        Assert.Null(encounter.Attack("Claw", target));
        Assert.Null(encounter.Attack("Claw", target));

        var attacksRemainingBeforeRefusal = monster.Features.AttacksRemainingThisAction;
        var usesAvailableBeforeRefusal = monster.Uses.IsAvailable("Claw");

        // One swing of budget remains (the Sting), but Claw's own cap of two is spent.
        Assert.Equal("attack.composition_exhausted", encounter.Attack("Claw", target)?.Code);

        // The refusal spent nothing.
        Assert.Equal(attacksRemainingBeforeRefusal, monster.Features.AttacksRemainingThisAction);
        Assert.Equal(usesAvailableBeforeRefusal, monster.Uses.IsAvailable("Claw"));
        Assert.True(attacksRemainingBeforeRefusal > 0, "a swing of budget must still remain for this to be the load-bearing case.");

        // The Sting is still open, and taking it completes the printed composition.
        Assert.Null(encounter.Attack("Sting", target));

        // Now the whole action is spent — a second Sting is refused, but as
        // action.spent rather than composition_exhausted: with the composition's
        // component counts always summing to AttackCount (design's own invariant,
        // pinned in EntryMechanicsTests.EveryEnumeratedCompositionSatisfiesItsInvariants),
        // the action budget and the composition are always exhausted at exactly the
        // same swing, so "capped name, budget still open" never happens here — that is
        // the load-bearing case above, not this one.
        Assert.Equal("action.spent", encounter.Attack("Sting", target)?.Code);
    }

    [Fact]
    public void TheCapGuardTallyResetsAtTheStartOfTheNextTurn()
    {
        // Composition Claw×1, Tail×1: capping out Claw while Tail's swing of budget is
        // still open is the unambiguous composition_exhausted case (the same reasoning
        // as TheCapGuardRefusesAnAlreadyCappedNameWhileBudgetRemainsForAnother) — used
        // here only to isolate the tally's reset behaviour across a turn boundary.
        var (encounter, monster, target) = Fight(
            new MultiattackEffect(
                2,
                ["Claw", "Tail"],
                AnyCombination: false,
                Composition: [new MultiattackComponent("Claw", 1), new MultiattackComponent("Tail", 1)]),
            targetHitPoints: 500);

        Assert.Null(encounter.Attack("Claw", target));
        Assert.Equal("attack.composition_exhausted", encounter.Attack("Claw", target)?.Code);
        Assert.Equal(1, monster.Features.MultiattackSwingsThisAction["Claw"]);

        // Monster's turn ends, target's empty turn ends, monster's next turn begins —
        // a fresh Attack action gets its own full composition.
        encounter.EndTurn();
        encounter.EndTurn();

        Assert.Empty(monster.Features.MultiattackSwingsThisAction);
        Assert.Null(encounter.Attack("Claw", target));
        Assert.Equal("attack.composition_exhausted", encounter.Attack("Claw", target)?.Code);
    }

    [Fact]
    public void ThePolicyCompletesThePrintedCompositionInsteadOfDoublingTheBetterAttack()
    {
        // The load-bearing test for issue #343 (§3.7's named hazard): before the fix,
        // SimpleTacticsPolicy's per-swing executor filtered only on static membership
        // (AllowsInMultiattack), so it would rank Bite above Claw by damage and pick
        // Bite for both swings — the exact bug the STRICT-EXACT reading closes. If the
        // engine guard alone had landed without also making the policy cap-aware, the
        // second swing's Bite would be refused and the attack loop would abort (per
        // TryAttack's "return attack is not null && encounter.Attack(...) is null"),
        // leaving the bear swinging once and stopping — a monster nerf and a stall
        // risk, not a fix.
        var (encounter, monster, target) = FightWithGraduatedAttacks(
            new MultiattackEffect(
                2,
                ["Bite", "Claw"],
                AnyCombination: false,
                Composition: [new MultiattackComponent("Bite", 1), new MultiattackComponent("Claw", 1)]),
            new Dictionary<string, string> { ["Bite"] = "4d6", ["Claw"] = "1d4" },
            targetHitPoints: 500);

        SimpleTacticsPolicy.TakeTurn(encounter);

        var swings = encounter.Log
            .Where(step => step.Kind == CombatStepKind.Attack && step.ActorId == monster.Id)
            .Select(step => step.AttackName)
            .ToList();

        Assert.Equal(["Bite", "Claw"], swings);
    }

    [Fact]
    public void AMissingComponentAttackIsPrunedAndTheCompositionIsRecomputed()
    {
        // Design §2.6: a composition naming an attack the creature does not have (a
        // trait- or spell-granted attack absent from this build) drops that component
        // rather than leaving AttackCount/AttackNames stale.
        var monster = MonsterWith(
            new MultiattackEffect(
                2,
                ["Claw", "Eldritch Blast"],
                AnyCombination: false,
                Composition: [new MultiattackComponent("Claw", 1), new MultiattackComponent("Eldritch Blast", 1)]),
            attackNames: ["Claw"]);

        Assert.NotNull(monster.Multiattack);
        Assert.Equal(1, monster.Multiattack!.AttackCount);
        Assert.Equal(["Claw"], monster.Multiattack.AttackNames);
        Assert.Equal([new MultiattackComponent("Claw", 1)], monster.Multiattack.Composition);
        Assert.Equal(1, monster.AttacksPerAction);
    }

    [Fact]
    public void WhenEveryComponentIsMissingTheMultiattackIsDropped()
    {
        var monster = MonsterWith(
            new MultiattackEffect(
                2,
                ["Bite", "Claw"],
                AnyCombination: false,
                Composition: [new MultiattackComponent("Bite", 1), new MultiattackComponent("Claw", 1)]),
            attackNames: ["Tail"]);

        Assert.Null(monster.Multiattack);
        Assert.Equal(1, monster.AttacksPerAction);
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
    /// <remarks>Every named attack deals the same uniform 1d6; see the overload below
    /// for attacks that must differ in value, such as ranking one above another.</remarks>
    private static CombatantStats MonsterWith(MultiattackEffect multiattack, IReadOnlyList<string> attackNames) =>
        MonsterWith(multiattack, attackNames.ToDictionary(name => name, _ => "1d6"));

    /// <summary>
    /// Builds monster stats from a stat block carrying the given Multiattack, with each
    /// named attack dealing its own damage dice — for tests where one attack must
    /// outrank another, such as the tactics policy preferring the higher-value swing.
    /// </summary>
    private static CombatantStats MonsterWith(
        MultiattackEffect multiattack,
        IReadOnlyDictionary<string, string> attackDamage)
    {
        var entries = new List<MonsterEntry>
        {
            new("Multiattack", MonsterEntrySection.Action, "text", Mechanics: EntryMechanics.Multiattack,
                Multiattack: multiattack),
        };

        entries.AddRange(attackDamage.Select(attack => new MonsterEntry(
            attack.Key,
            MonsterEntrySection.Action,
            "text",
            new MonsterAttack(
                AttackKind.Melee,
                5,
                5,
                null,
                null,
                [new AttackDamage(Core.Dice.DiceExpression.Parse(attack.Value), DamageType.Slashing, 3)]),
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
        int targetHitPoints = 200,
        IReadOnlyList<string>? attackNames = null)
    {
        attackNames ??= ["Claw", "Tail"];

        var stats = multiattack is null
            ? MonsterWith(new MultiattackEffect(1, ["Claw"], false), attackNames)
            : MonsterWith(multiattack, attackNames);

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

    /// <summary>
    /// Like <see cref="Fight"/>, but each named attack deals its own damage dice —
    /// for the tactics-policy test, where one attack must genuinely outrank another.
    /// </summary>
    private static (Encounter Encounter, Combatant Monster, Combatant Target) FightWithGraduatedAttacks(
        MultiattackEffect multiattack,
        IReadOnlyDictionary<string, string> attackDamage,
        int targetHitPoints = 200)
    {
        var stats = MonsterWith(multiattack, attackDamage);
        var monster = new Combatant("monster", "Monster", CombatTestData.Monsters, stats, new GridPosition(0, 0));

        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Heroes,
            x: 1,
            stats: CombatTestData.Stats(armorClass: 5, maximumHitPoints: targetHitPoints));

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [monster, target],
            // Every roll lands (target AC 5) and every damage die is clamped to its own
            // max — SeededSequence falls back to 10 once the scripted list runs out, so
            // no explicit script is needed for a fixed, short turn.
            new SeededSequence());

        return (encounter, monster, target);
    }
}
