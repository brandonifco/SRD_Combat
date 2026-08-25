using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// A stat block entry resolving through a saving throw: the area, the roll against the
/// printed DC, the halving, and the riders a failed save imposes.
/// </summary>
/// <remarks>
/// The rules text these pin, verbatim from the stat block legend and the entries
/// themselves: <c>Dexterity Saving Throw: DC 12, each creature in a 30-foot Cone.
/// Failure: 14 (4d6) Acid damage. Success: Half damage.</c> — every creature in the area
/// rolls once against the printed DC, damage halves where a Success line says so, and a
/// condition printed on the failure lands exactly when the engine executes it. The
/// scripted die is load-bearing throughout: it throws on any unscripted roll, so a test
/// that passes also proves nobody outside the area rolled a save.
/// </remarks>
public class EntrySaveTests
{
    [Fact]
    public void AnAreaSaveRollsForEveryCreatureTheConeCatches()
    {
        // Two heroes stand in the cone; the one who fails takes 2, the one who succeeds
        // takes half of 2. The breather itself, at the cone's origin, never rolls — the
        // script would throw if it did.
        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1, 1, 1, 1, 20, 1, 1),
            Breather("Fire Breath", ConeSave()),
            Hero("a", x: 2),
            Hero("b", x: 3));

        Assert.Null(encounter.UseEntry("Fire Breath", new GridPosition(3, 5)));

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Entry
                && step.Narration.Contains("Fire Breath fills a 15-foot Cone, catching 2 creature(s)", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("a takes 2 Fire damage", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("b takes 1 Fire damage (halved by a successful save)", StringComparison.Ordinal));
    }

    [Fact]
    public void TheBreathersOwnSquareIsOutsideItsLine()
    {
        // A Line extends from its user: only the hero in front of the breather rolls,
        // and "catching 1 creature(s)" says the breather's own square was not covered.
        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1, 1),
            Breather("Lightning Breath", LineSave()),
            Hero("a", x: 3));

        Assert.Null(encounter.UseEntry("Lightning Breath", new GridPosition(3, 5)));

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("catching 1 creature(s)", StringComparison.Ordinal));
        Assert.Equal(
            1,
            encounter.Log.Count(step => step.Narration.Contains("saving throw", StringComparison.Ordinal)));
    }

    [Fact]
    public void ARiderLandsOnlyOnAFailedSave()
    {
        // "Failure: ... the target has the Prone condition" — a success against a
        // NoEffect save takes nothing and falls nowhere.
        var bash = new SaveEffect(
            Ability.Strength,
            15,
            null,
            [new AttackDamage(DiceExpression.Parse("1d4"), DamageType.Bludgeoning, 2)],
            SaveSuccessOutcome.NoEffect,
            [new AppliedCondition(ConditionType.Prone, MaximumTargetSize: CreatureSize.Medium)]);

        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1, 1, 1, 20),
            Breather("Bash", bash),
            Hero("a", x: 1),
            Hero("b", x: 2));

        var first = encounter.Combatants.Single(combatant => combatant.Id == "a");
        var second = encounter.Combatants.Single(combatant => combatant.Id == "b");

        Assert.Null(encounter.UseEntry("Bash", first));

        Assert.True(first.HasCondition(ConditionType.Prone));
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Condition
                && step.Narration.Contains("a has the Prone condition", StringComparison.Ordinal));

        encounter.EndTurn();
        encounter.EndTurn();
        encounter.EndTurn();

        Assert.Null(encounter.UseEntry("Bash", second));

        Assert.False(second.HasCondition(ConditionType.Prone));
        Assert.Equal(second.Stats.MaximumHitPoints, second.CurrentHitPoints);
    }

    [Fact]
    public void AnUnexecutableRiderIsSkippedWhileTheDamageStillLands()
    {
        // Deafened is not on ConditionRules.Executable, so the rider is refused as
        // scenery — but the printed damage is fully modelled and lands anyway, exactly
        // as a Phase Spider's bite still bites.
        var roar = new SaveEffect(
            Ability.Wisdom,
            15,
            null,
            [new AttackDamage(DiceExpression.Parse("1d4"), DamageType.Thunder, 2)],
            SaveSuccessOutcome.NoEffect,
            [new AppliedCondition(ConditionType.Deafened)]);

        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1, 1),
            Breather("Roar", roar),
            Hero("a", x: 1));

        var victim = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.Null(encounter.UseEntry("Roar", victim));

        Assert.False(victim.HasCondition(ConditionType.Deafened));
        Assert.True(victim.CurrentHitPoints < victim.Stats.MaximumHitPoints);
        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.Condition);
    }

    [Fact]
    public void AGrappleFromASaveEndsOnlyByEscape()
    {
        // The Water Elemental's shape: a failed save leaves the target Grappled with a
        // printed escape DC and no reach to measure the grapple against — so nothing but
        // the escape check (or the grappler's incapacity) ends it.
        var engulf = new SaveEffect(
            Ability.Strength,
            15,
            null,
            [new AttackDamage(DiceExpression.Parse("1d4"), DamageType.Bludgeoning, 2)],
            SaveSuccessOutcome.HalfDamage,
            [new AppliedCondition(ConditionType.Grappled, EscapeDifficultyClass: 14, MaximumTargetSize: CreatureSize.Large)]);

        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1, 1, 20),
            Breather("Engulf", engulf),
            Hero("a", x: 1));

        var victim = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.Null(encounter.UseEntry("Engulf", victim));

        var grapple = victim.ConditionState(ConditionType.Grappled);
        Assert.NotNull(grapple);
        Assert.Equal(14, grapple.EscapeDifficultyClass);
        Assert.Null(grapple.GrappleRangeFeet);

        encounter.EndTurn();

        // Still held at the start of its own turn; the escape check is what frees it.
        Assert.True(victim.HasCondition(ConditionType.Grappled));
        Assert.Null(encounter.Escape());
        Assert.False(victim.HasCondition(ConditionType.Grappled));
    }

    [Fact]
    public void FailureOrSuccessLandsTheEffectEitherWay()
    {
        // "Failure or Success:" — the save is rolled and narrated, and the whole effect,
        // damage and rider both, lands even on a success.
        var bellow = new SaveEffect(
            Ability.Constitution,
            12,
            null,
            [new AttackDamage(DiceExpression.Parse("1d6"), DamageType.Thunder, 3)],
            SaveSuccessOutcome.SameAsFailure,
            [new AppliedCondition(ConditionType.Prone)]);

        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 20, 1),
            Breather("Bellow", bellow),
            Hero("a", x: 1));

        var victim = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.Null(encounter.UseEntry("Bellow", victim));

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("success.", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("a takes 1 Thunder damage", StringComparison.Ordinal)
                && !step.Narration.Contains("halved", StringComparison.Ordinal));
        Assert.True(victim.HasCondition(ConditionType.Prone));
    }

    [Fact]
    public void ASaveThatKillsAGrapplerFreesItsVictim()
    {
        // A grapple that outlives its grappler is invisible: the victim simply never
        // moves again. The save path has to sweep for broken grapples exactly as the
        // attack path does.
        var bolt = new SaveEffect(
            Ability.Dexterity,
            15,
            null,
            [new AttackDamage(DiceExpression.Parse("1d6"), DamageType.Lightning, 3)],
            SaveSuccessOutcome.HalfDamage,
            []);

        var grappler = CombatTestData.Combatant(
            "grappler",
            sideId: CombatTestData.Heroes,
            stats: CombatTestData.Stats(maximumHitPoints: 4, initiativeBonus: -10),
            x: 1,
            y: 5);
        var held = CombatTestData.Combatant(
            "held",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10),
            x: 2,
            y: 5);

        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1, 1, 6),
            Breather("Bolt", bolt),
            grappler,
            held);

        held.AddCondition(new ActiveCondition(ConditionType.Grappled, "grappler", null, 12, 5));
        Assert.True(held.HasCondition(ConditionType.Grappled));

        Assert.Null(encounter.UseEntry("Bolt", grappler));

        Assert.True(grappler.IsDead);
        Assert.False(held.HasCondition(ConditionType.Grappled));
    }

    /// <summary>
    /// #386: the Mummy's 30-foot Dreadful Glare used to land from anywhere on the
    /// board because nothing in <c>UseSaveEntry</c> ever measured distance. These pin
    /// the three cases the acceptance criteria names: in range executes exactly as
    /// before, out of range refuses named and spends nothing, and a null range — every
    /// entry in the corpus today, since the extraction half has not landed — reaches
    /// any distance exactly as it always has.
    /// </summary>
    [Fact]
    public void ASingleTargetSaveWithinItsPrintedRangeExecutesAsToday()
    {
        var gaze = new SaveEffect(
            Ability.Wisdom,
            11,
            null,
            [],
            SaveSuccessOutcome.NoEffect,
            [new AppliedCondition(ConditionType.Frightened)],
            RangeFeet: 30);

        // 4 squares * 5 ft. = 20 ft., inside the 30 ft. range.
        var encounter = Fight(
            new ScriptedRandomSource(1, 1, 1),
            Breather("Dreadful Glare", gaze),
            Hero("a", x: 4));

        var victim = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.Null(encounter.UseEntry("Dreadful Glare", victim));

        Assert.True(victim.HasCondition(ConditionType.Frightened));
    }

    [Fact]
    public void ASingleTargetSaveBeyondItsPrintedRangeRefusesAndSpendsNothing()
    {
        var gaze = new SaveEffect(
            Ability.Wisdom,
            11,
            null,
            [],
            SaveSuccessOutcome.NoEffect,
            [new AppliedCondition(ConditionType.Frightened)],
            RangeFeet: 30);

        // 10 squares * 5 ft. = 50 ft., beyond the 30 ft. range — nothing rolls, so an
        // unscripted-roll throw would catch a refusal that spent the save anyway.
        var encounter = Fight(
            new ScriptedRandomSource(1, 1),
            Breather("Dreadful Glare", gaze),
            Hero("a", x: 10));

        var actor = encounter.Combatants.Single(combatant => combatant.Id == "breather");
        var victim = encounter.Combatants.Single(combatant => combatant.Id == "a");

        var refusal = encounter.UseEntry("Dreadful Glare", victim);

        Assert.Equal("entry.out_of_range", refusal?.Code);
        Assert.True(actor.Turn.HasAction);
        Assert.False(victim.HasCondition(ConditionType.Frightened));
        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.Entry);
    }

    [Fact]
    public void ASingleTargetSaveWithNoPrintedRangeReachesAnyDistance()
    {
        var gaze = new SaveEffect(
            Ability.Wisdom,
            11,
            null,
            [],
            SaveSuccessOutcome.NoEffect,
            [new AppliedCondition(ConditionType.Frightened)]);

        Assert.Null(gaze.RangeFeet);

        // Far enough that a 30 ft.-range creature could never have reached it — the
        // point of this test is that nothing here is checked at all.
        var encounter = Fight(
            new ScriptedRandomSource(1, 1, 1),
            Breather("Dreadful Glare", gaze),
            Hero("a", x: 11));

        var victim = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.Null(encounter.UseEntry("Dreadful Glare", victim));

        Assert.True(victim.HasCondition(ConditionType.Frightened));
    }

    /// <summary>
    /// The point-aimed half of the same rule: an area entry aimed at bare ground with
    /// no creature reference answers to the identical printed range, mirroring
    /// <c>CastSpell</c>'s own point-aimed check.
    /// </summary>
    [Fact]
    public void APointAimedAreaSaveBeyondItsPrintedRangeRefusesAndSpendsNothing()
    {
        var blast = new SaveEffect(
            Ability.Dexterity,
            13,
            new EffectArea(AreaShape.Sphere, 20),
            [new AttackDamage(DiceExpression.Parse("2d6"), DamageType.Fire, 7)],
            SaveSuccessOutcome.HalfDamage,
            [],
            RangeFeet: 30);

        var encounter = Fight(
            new ScriptedRandomSource(1, 1),
            Breather("Blast", blast),
            Hero("a", x: 10));

        var actor = encounter.Combatants.Single(combatant => combatant.Id == "breather");

        // 10 squares * 5 ft. = 50 ft., beyond the 30 ft. range.
        var refusal = encounter.UseEntry("Blast", new GridPosition(10, 5));

        Assert.Equal("entry.out_of_range", refusal?.Code);
        Assert.True(actor.Turn.HasAction);
        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.Entry);
    }

    [Fact]
    public void ThePolicyBreathesWhenTheConeIsClear()
    {
        // No attack reaches — the breather has none — so the limited-use breath is the
        // action, aimed at the nearest enemy with nobody friendly in the way.
        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1, 1, 1),
            Breather("Fire Breath", ConeSave(), new UsageLimit(UsageLimitKind.Recharge, RechargeMinimum: 5)),
            Hero("a", x: 3));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Entry
                && step.Narration.Contains("uses Fire Breath", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePolicyHoldsItsBreathOverAPackmate()
    {
        // The packmate stands in the cone between the breather and its target, so the
        // policy moves instead of breathing — a wolf roasting its own pack reads as a
        // bug in every transcript it appears in.
        var packmate = CombatTestData.Combatant(
            "packmate",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10, attacks: []),
            x: 2,
            y: 5);

        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 1),
            Breather("Fire Breath", ConeSave(), new UsageLimit(UsageLimitKind.Recharge, RechargeMinimum: 5)),
            packmate,
            Hero("a", x: 3));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("uses Fire Breath", StringComparison.Ordinal));
        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.Move);
    }

    /// <summary>
    /// #386's policy half: picking a target <c>UseEntry</c> would refuse just costs
    /// the turn on a refusal, so the choice has to respect the same range the engine
    /// enforces — the single-target shape a Mummy's Dreadful Glare actually has,
    /// mirroring <see cref="ThePolicyHoldsItsBreathOverAPackmate"/>'s shape for the
    /// area case.
    /// </summary>
    [Fact]
    public void ThePolicyDoesNotUseASingleTargetSaveBeyondItsPrintedRange()
    {
        var gaze = new SaveEffect(
            Ability.Wisdom,
            11,
            null,
            [],
            SaveSuccessOutcome.NoEffect,
            [new AppliedCondition(ConditionType.Frightened)],
            RangeFeet: 30);

        // 10 squares * 5 ft. = 50 ft., beyond the 30 ft. range.
        var encounter = Fight(
            new ScriptedRandomSource(1, 1),
            Breather("Dreadful Glare", gaze, new UsageLimit(UsageLimitKind.Recharge, RechargeMinimum: 5)),
            Hero("a", x: 10));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("uses Dreadful Glare", StringComparison.Ordinal));
        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.Move);
    }

    /// <summary>
    /// #405: <c>SaveReaches</c>'s range guard has no test that fails without it
    /// specifically — the engine's own <c>entry.out_of_range</c> refusal
    /// (<see cref="ASingleTargetSaveWithinItsPrintedRangeExecutesAsToday"/>'s family)
    /// masks the gap, since a refused pick and a never-considered pick both end in
    /// <c>MoveTowards</c>. What the guard alone buys is the <em>pick</em>:
    /// <c>TryUseLimitedEntry</c> orders candidates by average damage and takes the
    /// first, so an out-of-range gaze with the higher average would win that
    /// ordering, get refused by <c>UseEntry</c>, and the whole call reports no entry
    /// used — never falling through to the second-best, genuinely in-range entry.
    /// With the guard, <c>SaveReaches</c> excludes the out-of-range candidate before
    /// the ordering ever runs, so the in-range entry is what gets picked and actually
    /// used — which is exactly what this pins.
    /// </summary>
    [Fact]
    public void ThePolicyPicksTheInRangeEntryOverAHigherDamageOutOfRangeOne()
    {
        var strongButOutOfRange = new SaveEffect(
            Ability.Wisdom,
            11,
            null,
            [new AttackDamage(DiceExpression.Parse("4d6"), DamageType.Psychic, 14)],
            SaveSuccessOutcome.NoEffect,
            [],
            RangeFeet: 30);

        var weakButInRange = new SaveEffect(
            Ability.Wisdom,
            11,
            null,
            [new AttackDamage(DiceExpression.Parse("1d4"), DamageType.Psychic, 2)],
            SaveSuccessOutcome.NoEffect,
            [],
            RangeFeet: 60);

        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
        {
            Entries =
            [
                new MonsterEntry(
                    "Strong Gaze",
                    MonsterEntrySection.Action,
                    "Strong Gaze.",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: strongButOutOfRange,
                    Usage: new UsageLimit(UsageLimitKind.PerDay, UsesPerDay: 1)),
                new MonsterEntry(
                    "Weak Gaze",
                    MonsterEntrySection.Action,
                    "Weak Gaze.",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: weakButInRange,
                    Usage: new UsageLimit(UsageLimitKind.PerDay, UsesPerDay: 1)),
            ],
        };

        var breather = CombatTestData.Combatant("breather", sideId: CombatTestData.Monsters, stats: stats, y: 5);

        // 10 squares * 5 ft. = 50 ft.: beyond Strong Gaze's 30 ft. range, within Weak
        // Gaze's 60 ft. one. Two initiative rolls (Encounter.Start), then a natural 1
        // fails the save, then the 1d4 damage roll.
        var encounter = Fight(new ScriptedRandomSource(1, 1, 1, 2), breather, Hero("a", x: 10));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Entry
                && step.Narration.Contains("uses Weak Gaze", StringComparison.Ordinal));
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("Strong Gaze", StringComparison.Ordinal));
        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.Move);
    }

    private static SaveEffect ConeSave() => new(
        Ability.Dexterity,
        13,
        new EffectArea(AreaShape.Cone, 15),
        [new AttackDamage(DiceExpression.Parse("2d6"), DamageType.Fire, 7)],
        SaveSuccessOutcome.HalfDamage,
        []);

    private static SaveEffect LineSave() => new(
        Ability.Dexterity,
        13,
        new EffectArea(AreaShape.Line, 30, 5),
        [new AttackDamage(DiceExpression.Parse("1d6"), DamageType.Lightning, 3)],
        SaveSuccessOutcome.HalfDamage,
        []);

    /// <summary>
    /// A monster at (0,5) whose only action is the given saving-throw entry. Unlimited
    /// unless a usage limit is given — the policy only reaches for limited entries, so
    /// the policy tests pass one.
    /// </summary>
    private static Combatant Breather(string entryName, SaveEffect save, UsageLimit? usage = null)
    {
        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
        {
            Entries =
            [
                new MonsterEntry(entryName, MonsterEntrySection.Action, $"{entryName}.",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: save,
                    Usage: usage),
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
