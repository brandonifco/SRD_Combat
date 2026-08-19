using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The Petrified condition, and the two-tier gaze that imposes it — the Basilisk's
/// printed shape: a Bonus Action, a Constitution save, Restrained on the first failure,
/// and "the Petrified condition instead of the Restrained condition" when the repeated
/// save at the end of the target's next turn fails too.
/// </summary>
/// <remarks>
/// The printed Petrified effects these pin (glossary, page 186): Incapacitated, Speed 0,
/// Advantage on attack rolls against it, automatically failed Strength and Dexterity
/// saving throws, Resistance to all damage, and Immunity to the Poisoned condition.
/// Note what is not printed: no Critical Hit clause — Paralyzed and Unconscious carry
/// one, stone does not.
/// </remarks>
public class PetrifiedTests
{
    [Fact]
    public void AGazeIsABonusActionAndLeavesTheActionUnspent()
    {
        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1),
            Gazer(),
            Hero("a", x: 2));

        var gazer = encounter.Combatants.Single(combatant => combatant.Id == "gazer");

        Assert.Null(encounter.UseEntry("Petrifying Gaze", new GridPosition(2, 5)));
        Assert.False(gazer.Turn.HasBonusAction);
        Assert.True(gazer.Turn.HasAction);
    }

    [Fact]
    public void ASecondBonusActionEntryIsRefused()
    {
        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1),
            Gazer(),
            Hero("a", x: 2));

        Assert.Null(encounter.UseEntry("Petrifying Gaze", new GridPosition(2, 5)));

        var refusal = encounter.UseEntry("Petrifying Gaze", new GridPosition(2, 5));

        Assert.Equal("entry.bonus_action_spent", refusal?.Code);
    }

    [Fact]
    public void AReactionEntryIsStillRefused()
    {
        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
        {
            Entries =
            [
                new MonsterEntry("Split", MonsterEntrySection.Reaction, "Split.",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: GazeSave()),
            ],
        };

        var encounter = Fight(
            new ScriptedRandomSource(20, 1),
            CombatTestData.Combatant("gazer", sideId: CombatTestData.Monsters, stats: stats, y: 5),
            Hero("a", x: 2));

        Assert.Equal("entry.not_an_action", encounter.UseEntry("Split", new GridPosition(2, 5))?.Code);
    }

    [Fact]
    public void AFailedGazeRestrainsUntilTheRepeatedSaveResolvesIt()
    {
        // Initiative ×2, then the failed save (1 + 2 vs DC 12).
        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1),
            Gazer(),
            Hero("a", x: 2));

        var victim = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.Null(encounter.UseEntry("Petrifying Gaze", new GridPosition(2, 5)));

        Assert.True(victim.HasCondition(ConditionType.Restrained));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains(
                "a has the Restrained condition until a repeated save ends it — or worsens it",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AFailedRepeatTurnsRestrainedToPetrified()
    {
        // Initiative ×2, the failed save, then the failed repeat at the end of the
        // victim's own turn — "Second Failure: The target has the Petrified condition
        // instead of the Restrained condition."
        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1, 1),
            Gazer(),
            Hero("a", x: 2));

        var victim = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.Null(encounter.UseEntry("Petrifying Gaze", new GridPosition(2, 5)));
        encounter.EndTurn();

        // The victim's turn ends: the repeat is rolled, fails, and the stone takes.
        encounter.EndTurn();

        Assert.False(victim.HasCondition(ConditionType.Restrained));
        Assert.True(victim.HasCondition(ConditionType.Petrified));
        Assert.True(victim.HasCondition(ConditionType.Incapacitated));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains(
                "a has the Petrified condition instead of Restrained",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ASuccessfulRepeatShakesTheGazeOff()
    {
        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1, 20),
            Gazer(),
            Hero("a", x: 2));

        var victim = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.Null(encounter.UseEntry("Petrifying Gaze", new GridPosition(2, 5)));
        encounter.EndTurn();
        encounter.EndTurn();

        Assert.False(victim.HasCondition(ConditionType.Restrained));
        Assert.False(victim.HasCondition(ConditionType.Petrified));
    }

    [Fact]
    public void PetrifiedBringsIncapacitatedAndTakesItAwayAgain()
    {
        var victim = CombatTestData.Combatant("a");

        Assert.True(victim.AddCondition(ConditionType.Petrified));
        Assert.True(victim.HasCondition(ConditionType.Incapacitated));

        Assert.True(victim.RemoveCondition(ConditionType.Petrified));
        Assert.False(victim.HasCondition(ConditionType.Incapacitated));
    }

    [Fact]
    public void PetrifiedIsImmuneToPoisoned()
    {
        var victim = CombatTestData.Combatant("a");

        victim.AddCondition(ConditionType.Petrified);

        Assert.False(victim.AddCondition(ConditionType.Poisoned));
        Assert.False(victim.HasCondition(ConditionType.Poisoned));
    }

    [Fact]
    public void AttackRollsAgainstAPetrifiedTargetHaveAdvantage()
    {
        var attacker = CombatTestData.Combatant("attacker", x: 1, y: 0);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 2, y: 0);

        target.AddCondition(ConditionType.Petrified);

        var circumstances = AttackRules.DescribeCircumstances(
            attacker,
            CombatTestData.MeleeAttack(),
            target);

        Assert.True(circumstances.TargetIsPetrified);
        Assert.Equal(RollMode.Advantage, AttackRules.ResolveRollMode(circumstances, distanceFeet: 5));
    }

    [Fact]
    public void PetrifiedResistsAllDamage()
    {
        var victim = CombatTestData.Combatant("a", stats: CombatTestData.Stats(maximumHitPoints: 60));

        victim.AddCondition(ConditionType.Petrified);

        var result = DamageRules.Apply(victim, 10, DamageType.Slashing);

        Assert.Equal(5, result.Effective);
        Assert.Equal(DamageResponse.Resistance, result.Response);
    }

    [Fact]
    public void TheStoneAndTheCreaturesOwnResistanceHalveOnce()
    {
        // "Multiple instances of Resistance ... count as only one instance."
        var stats = CombatTestData.Stats(
            maximumHitPoints: 60,
            damageResponses: new Dictionary<DamageType, DamageResponse>
            {
                [DamageType.Slashing] = DamageResponse.Resistance,
            });
        var victim = CombatTestData.Combatant("a", stats: stats);

        victim.AddCondition(ConditionType.Petrified);

        Assert.Equal(5, DamageRules.Apply(victim, 10, DamageType.Slashing).Effective);
    }

    [Fact]
    public void ResistanceAppliesBeforeVulnerability()
    {
        // The printed worked example (page 17): 23 damage, Resistance to all, then
        // Vulnerability — halved and rounded down to 11, then doubled to 22.
        var stats = CombatTestData.Stats(
            maximumHitPoints: 60,
            damageResponses: new Dictionary<DamageType, DamageResponse>
            {
                [DamageType.Fire] = DamageResponse.Vulnerability,
            });
        var victim = CombatTestData.Combatant("a", stats: stats);

        victim.AddCondition(ConditionType.Petrified);

        Assert.Equal(22, DamageRules.Apply(victim, 23, DamageType.Fire).Effective);
    }

    [Fact]
    public void AnOwnImmunityStillZeroes()
    {
        var stats = CombatTestData.Stats(
            maximumHitPoints: 60,
            damageResponses: new Dictionary<DamageType, DamageResponse>
            {
                [DamageType.Fire] = DamageResponse.Immunity,
            });
        var victim = CombatTestData.Combatant("a", stats: stats);

        victim.AddCondition(ConditionType.Petrified);

        Assert.Equal(0, DamageRules.Apply(victim, 10, DamageType.Fire).Effective);
    }

    [Fact]
    public void PetrifiedAutoFailsADexteritySaveWithoutADie()
    {
        // A single-target save on purpose: an area's covered-creature sweep only takes
        // the active, and stone is Incapacitated — the auto-fail clause is exercised
        // where a save genuinely reaches the statue. Initiative ×2, then only the
        // damage die: the scripted source throws if the auto-failed save consumes one.
        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 3),
            Breather(),
            Hero("a", x: 2));

        var victim = encounter.Combatants.Single(combatant => combatant.Id == "a");

        victim.AddCondition(ConditionType.Petrified);

        Assert.Null(encounter.UseEntry("Stomp", victim));

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("automatically fails the Dexterity saving throw (Petrified)", StringComparison.Ordinal));

        // And the stone's Resistance halves the fire: 3 rolled, 1 taken.
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("takes 1 Fire damage", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePolicyGazesBesideItsBite()
    {
        // A basilisk-shaped monster spends its Bonus Action on the gaze and its Action
        // on the bite, in one turn. Initiative ×2, the failed save (1), then the bite
        // with Advantage against the now-Restrained victim (15, 15) and its damage (4).
        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1, 15, 15, 4),
            Gazer(usage: new UsageLimit(UsageLimitKind.Recharge, RechargeMinimum: 4), withBite: true),
            Hero("a", x: 1));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("uses Petrifying Gaze", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("with Bite", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePolicyNeverPantomimesAnIneffectualBonusEntry()
    {
        // A bonus-action save whose only rider the engine cannot impose would roll a
        // save and change nothing; the policy leaves it alone and just bites.
        var moan = new SaveEffect(
            Ability.Wisdom,
            13,
            new EffectArea(AreaShape.Cone, 30),
            [],
            SaveSuccessOutcome.NoEffect,
            [new AppliedCondition(ConditionType.Deafened)]);

        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: [Bite()]) with
        {
            Entries =
            [
                new MonsterEntry("Moan", MonsterEntrySection.BonusAction, "Moan.",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: moan,
                    Usage: new UsageLimit(UsageLimitKind.Recharge, RechargeMinimum: 4)),
            ],
        };

        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 15, 4),
            CombatTestData.Combatant("gazer", sideId: CombatTestData.Monsters, stats: stats, y: 5),
            Hero("a", x: 1));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("uses Moan", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("with Bite", StringComparison.Ordinal));
    }

    private static CombatAttack Bite() => CombatTestData.MeleeAttack("Bite", damage: "1d8 + 2");

    private static SaveEffect GazeSave() => new(
        Ability.Constitution,
        12,
        new EffectArea(AreaShape.Cone, 30),
        [],
        SaveSuccessOutcome.NoEffect,
        [
            new AppliedCondition(
                ConditionType.Restrained,
                Duration: ConditionDuration.UntilSavedOrEscalated,
                EscalatesTo: ConditionType.Petrified),
        ]);

    /// <summary>A basilisk-shaped monster at (0,5): the gaze, and optionally the bite.</summary>
    private static Combatant Gazer(UsageLimit? usage = null, bool withBite = false)
    {
        var stats = CombatTestData.Stats(
            initiativeBonus: 10,
            attacks: withBite ? [Bite()] : []) with
        {
            Entries =
            [
                new MonsterEntry("Petrifying Gaze", MonsterEntrySection.BonusAction, "Petrifying Gaze.",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: GazeSave(),
                    Usage: usage),
            ],
        };

        return CombatTestData.Combatant("gazer", sideId: CombatTestData.Monsters, stats: stats, y: 5);
    }

    /// <summary>
    /// A stomper at (0,5) with a single-target Dexterity save, for the auto-fail case —
    /// single-target on purpose, because an area's covered-creature sweep only takes
    /// the active and stone is Incapacitated.
    /// </summary>
    private static Combatant Breather()
    {
        var save = new SaveEffect(
            Ability.Dexterity,
            13,
            null,
            [new AttackDamage(DiceExpression.Parse("1d6"), DamageType.Fire, 3)],
            SaveSuccessOutcome.HalfDamage,
            []);

        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
        {
            Entries =
            [
                new MonsterEntry("Stomp", MonsterEntrySection.Action, "Stomp.",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: save),
            ],
        };

        return CombatTestData.Combatant("breather", sideId: CombatTestData.Monsters, stats: stats, y: 5);
    }

    private static Combatant Hero(string id, int x) =>
        CombatTestData.Combatant(
            id,
            sideId: CombatTestData.Heroes,
            stats: CombatTestData.Stats(maximumHitPoints: 60, initiativeBonus: -10, attacks: []),
            x: x,
            y: 5);

    private static Encounter Fight(IRandomSource random, params Combatant[] combatants) =>
        Encounter.Start(new Battlefield(12, 12), combatants, random);
}
