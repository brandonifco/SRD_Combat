using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Using a stat block entry by name, and the usage limits that gate one.
/// </summary>
/// <remarks>
/// The rules text these pin, verbatim from the stat block legend:
/// <list type="bullet">
/// <item>
/// "Recharge X–Y" — "a monster can use the stat block part once. At the start of each
/// of the monster's turns, roll 1d6. If the roll is within the number range given in
/// the notation, the monster regains the use of that part."
/// </item>
/// <item>
/// "X/Day" — "the stat block part can be used a certain number of times and a monster
/// must finish a Long Rest to regain expended uses." No fight contains a Long Rest.
/// </item>
/// <item>
/// "Recharge after a Short or Long Rest" — one use per fight, for the same reason.
/// </item>
/// </list>
/// The scripted die is load-bearing here: it throws on any unscripted roll, so these
/// tests also prove the d6 is rolled only while the ability is spent.
/// </remarks>
public class EntryUsageTests
{
    [Fact]
    public void AnAttackEntryLockedOutOfTheMultiattackIsUsedThroughUseEntry()
    {
        // The Ape's shape: Multiattack is "two Fist attacks", so Attack() refuses the
        // Rock — before UseEntry existed, the entry was unreachable entirely.
        var (encounter, hurler, target) = HurlerFight(new ScriptedRandomSource(20, 1, 2));

        Assert.Equal("attack.not_in_multiattack", encounter.Attack("Rock", target)?.Code);

        Assert.Null(encounter.UseEntry("Rock", target));

        Assert.Contains(encounter.Log, step => step.Narration.Contains("with Rock", StringComparison.Ordinal));
        Assert.False(hurler.Turn.HasAction);
        Assert.False(hurler.Uses.IsAvailable("Rock"));
    }

    [Fact]
    public void ASpentRechargeEntryIsRefusedUntilTheDieComesUp()
    {
        // One Rock (a miss), a failed d6 next round, a successful one the round after.
        var (encounter, hurler, target) = HurlerFight(new ScriptedRandomSource(20, 1, 2, 5, 6, 2));

        Assert.Null(encounter.UseEntry("Rock", target));
        Assert.Equal("entry.not_recharged", encounter.UseEntry("Rock", target)?.Code);

        encounter.EndTurn();
        encounter.EndTurn();

        // Rolled 5 against Recharge 6: still spent, and the failure is narrated.
        Assert.False(hurler.Uses.IsAvailable("Rock"));
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Recharge
                && step.Narration.Contains("does not recharge (rolled 5, needs 6+)", StringComparison.Ordinal));

        encounter.EndTurn();
        encounter.EndTurn();

        Assert.True(hurler.Uses.IsAvailable("Rock"));
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Recharge
                && step.Narration.Contains("recharges (rolled 6", StringComparison.Ordinal));

        Assert.Null(encounter.UseEntry("Rock", target));
    }

    [Fact]
    public void NoDieIsRolledWhileTheAbilityIsCharged()
    {
        // The script holds exactly the two initiative rolls. A whole round passes with
        // the Rock unthrown, so a d6 rolled for a charged ability would throw here.
        var (encounter, _, _) = HurlerFight(new ScriptedRandomSource(20, 1));

        encounter.EndTurn();
        encounter.EndTurn();

        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.Recharge);
    }

    [Fact]
    public void APerDayAttackSpendsOneUsePerSwing()
    {
        // Two Javelins in one Multiattack action spend both of the day's uses; there is
        // no recharge roll for a per-day ability, which the script also proves.
        var javelin = CombatTestData.MeleeAttack("Javelin", bonus: 4, damage: "1d6 + 2");

        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: [javelin]) with
        {
            Multiattack = new MultiattackEffect(2, ["Javelin"], AnyCombination: false),
            Entries =
            [
                AttackEntry("Javelin", new UsageLimit(UsageLimitKind.PerDay, UsesPerDay: 2)),
            ],
        };

        var (encounter, hunter, target) = Fight(stats, new ScriptedRandomSource(20, 1, 2, 2));

        Assert.Null(encounter.Attack("Javelin", target));
        Assert.Null(encounter.Attack("Javelin", target));
        Assert.Equal(0, hunter.Uses.UsesRemaining("Javelin"));

        encounter.EndTurn();
        encounter.EndTurn();

        Assert.Equal("entry.no_uses_left", encounter.Attack("Javelin", target)?.Code);
    }

    [Fact]
    public void ARechargeAfterRestEntryHasOneUsePerFight()
    {
        // No fight contains a rest, so one use is the whole fight's allowance — and no
        // d6 is ever rolled for it.
        var trample = CombatTestData.MeleeAttack("Trample", bonus: 4, damage: "2d8 + 4");

        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: [trample]) with
        {
            Entries = [AttackEntry("Trample", new UsageLimit(UsageLimitKind.RechargeAfterRest))],
        };

        var (encounter, _, target) = Fight(stats, new ScriptedRandomSource(20, 1, 2));

        Assert.Null(encounter.Attack("Trample", target));

        encounter.EndTurn();
        encounter.EndTurn();

        Assert.Equal("entry.no_uses_left", encounter.Attack("Trample", target)?.Code);
    }

    [Fact]
    public void TheGateAppliesWhereverTheAttackIsUsedFrom()
    {
        // The Minotaur's shape: Gore is a plain attack with "(Recharge 5-6)" printed on
        // it and no Multiattack in the way, so the gate has to hold on the Attack path.
        var gore = CombatTestData.MeleeAttack("Gore", bonus: 6, damage: "2d10 + 4");
        var glaive = CombatTestData.MeleeAttack("Glaive", bonus: 6, damage: "2d6 + 4");

        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: [gore, glaive]) with
        {
            Entries =
            [
                AttackEntry("Gore", new UsageLimit(UsageLimitKind.Recharge, RechargeMinimum: 5)),
                AttackEntry("Glaive"),
            ],
        };

        var (encounter, gorer, target) = Fight(stats, new ScriptedRandomSource(20, 1, 2, 2, 2));

        Assert.Null(encounter.Attack("Gore", target));
        Assert.False(gorer.Uses.IsAvailable("Gore"));

        encounter.EndTurn();
        encounter.EndTurn();

        // The d6 came up 2 against Recharge 5: Gore is refused, the Glaive still swings.
        Assert.Equal("entry.not_recharged", encounter.Attack("Gore", target)?.Code);
        Assert.Null(encounter.Attack("Glaive", target));
    }

    [Fact]
    public void EverythingTheEngineCannotResolveIsRefusedByName()
    {
        var (encounter, hurler, target) = HurlerFight(new ScriptedRandomSource(20, 1));

        Assert.Equal("entry.unknown", encounter.UseEntry("Wing Buffet", target)?.Code);
        Assert.Equal("entry.not_an_action", encounter.UseEntry("Keen Smell", target)?.Code);
        Assert.Equal("entry.is_attack_action", encounter.UseEntry("Multiattack", target)?.Code);
        Assert.Equal("entry.save_not_implemented", encounter.UseEntry("Acid Breath", target)?.Code);
        Assert.Equal("entry.not_implemented", encounter.UseEntry("Weird Aura", target)?.Code);
        Assert.Equal("entry.narrative", encounter.UseEntry("Illumination", target)?.Code);
        Assert.Equal("entry.needs_target", encounter.UseEntry("Rock")?.Code);

        // A refusal spends nothing: not the action, and not the entry's use.
        Assert.True(hurler.Turn.HasAction);
        Assert.True(hurler.Uses.IsAvailable("Acid Breath"));
    }

    [Fact]
    public void ThePolicyThrowsTheRockWhenNothingElseReaches()
    {
        // At 25 feet the Fists reach nothing and the Rock is the right choice — then,
        // with the Rock spent and the d6 failed, the policy closes in and swings rather
        // than stalling on the refused entry.
        var (encounter, hurler, _) = HurlerFight(
            new ScriptedRandomSource(20, 1, 2, 2, 2, 2),
            targetDistanceSquares: 5);

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Contains(encounter.Log, step => step.Narration.Contains("with Rock", StringComparison.Ordinal));
        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.Move);

        encounter.EndTurn();
        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.Move);
        Assert.Equal(
            2,
            encounter.Log.Count(step =>
                step.Kind == CombatStepKind.Attack
                && step.Narration.Contains("with Fist", StringComparison.Ordinal)));
        Assert.Equal(
            1,
            encounter.Log.Count(step =>
                step.Kind == CombatStepKind.Attack
                && step.Narration.Contains("with Rock", StringComparison.Ordinal)));
        Assert.True(hurler.Uses.Tracks("Rock"));
    }

    [Fact]
    public void ThePolicyPicksTheNextBestAttackWhileGoreIsSpent()
    {
        var gore = CombatTestData.MeleeAttack("Gore", bonus: 6, damage: "2d10 + 4");
        var glaive = CombatTestData.MeleeAttack("Glaive", bonus: 6, damage: "2d6 + 4");

        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: [gore, glaive]) with
        {
            Entries =
            [
                AttackEntry("Gore", new UsageLimit(UsageLimitKind.Recharge, RechargeMinimum: 5)),
                AttackEntry("Glaive"),
            ],
        };

        var (encounter, _, _) = Fight(stats, new ScriptedRandomSource(20, 1, 2, 2, 2));

        // Gore averages more, so the policy leads with it.
        SimpleTacticsPolicy.TakeTurn(encounter);
        Assert.Contains(encounter.Log, step => step.Narration.Contains("with Gore", StringComparison.Ordinal));

        encounter.EndTurn();

        // Spent and unrecharged, Gore would be refused — the filter hands the Glaive
        // forward instead of aborting the attack.
        SimpleTacticsPolicy.TakeTurn(encounter);
        Assert.Contains(encounter.Log, step => step.Narration.Contains("with Glaive", StringComparison.Ordinal));
    }

    private static MonsterEntry AttackEntry(string name, UsageLimit? usage = null) =>
        new(name, MonsterEntrySection.Action, $"{name}.", Mechanics: EntryMechanics.Attack, Usage: usage);

    /// <summary>The Ape's shape: a Fist Multiattack, and a Rock on Recharge 6 outside it.</summary>
    private static (Encounter Encounter, Combatant Hurler, Combatant Target) HurlerFight(
        IRandomSource random,
        int targetDistanceSquares = 1)
    {
        var fist = CombatTestData.MeleeAttack("Fist", bonus: 4, damage: "1d6 + 3");
        var rock = CombatTestData.RangedAttack("Rock", bonus: 4, normalFeet: 25, longFeet: 50, damage: "2d6 + 3");

        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: [fist, rock]) with
        {
            Multiattack = new MultiattackEffect(2, ["Fist"], AnyCombination: false),
            Entries =
            [
                new MonsterEntry("Multiattack", MonsterEntrySection.Action, "Two Fist attacks.",
                    Mechanics: EntryMechanics.Multiattack,
                    Multiattack: new MultiattackEffect(2, ["Fist"], AnyCombination: false)),
                AttackEntry("Fist"),
                AttackEntry("Rock", new UsageLimit(UsageLimitKind.Recharge, RechargeMinimum: 6)),
                new MonsterEntry("Keen Smell", MonsterEntrySection.Trait, "Smells keenly.",
                    Mechanics: EntryMechanics.Narrative),
                new MonsterEntry("Acid Breath", MonsterEntrySection.Action, "Breathes acid.",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: new SaveEffect(Ability.Dexterity, 12, null, [], SaveSuccessOutcome.HalfDamage, []),
                    Usage: new UsageLimit(UsageLimitKind.Recharge, RechargeMinimum: 6)),
                new MonsterEntry("Weird Aura", MonsterEntrySection.Action, "Does something strange.",
                    Mechanics: EntryMechanics.Unmodelled),
                new MonsterEntry("Illumination", MonsterEntrySection.Action, "Sheds light.",
                    Mechanics: EntryMechanics.Narrative),
            ],
        };

        return Fight(stats, random, targetDistanceSquares);
    }

    private static (Encounter Encounter, Combatant Actor, Combatant Target) Fight(
        CombatantStats actorStats,
        IRandomSource random,
        int targetDistanceSquares = 1)
    {
        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [
                CombatTestData.Combatant("actor", sideId: CombatTestData.Monsters, stats: actorStats),
                CombatTestData.Combatant(
                    "victim",
                    sideId: CombatTestData.Heroes,
                    stats: CombatTestData.Stats(maximumHitPoints: 60, initiativeBonus: -10, attacks: []),
                    x: targetDistanceSquares),
            ],
            random);

        var actor = encounter.Combatants.Single(combatant => combatant.Id == "actor");
        var target = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        return (encounter, actor, target);
    }
}
