using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Channel Divinity's Turn Undead: chosen Undead within 30 feet make a Wisdom save or
/// gain Frightened and Incapacitated for a minute, with three printed early-outs — and
/// Sear Undead's shared Radiant damage roll riding the same use (#369).
/// </summary>
/// <remarks>
/// The test data's Wisdom is 10 unless a test says otherwise, so the fallback DC is
/// 8 + 2 proficiency + 0 = 10 — the same bare arithmetic <see cref="DivineSparkTests"/>
/// uses.
/// </remarks>
public class TurnUndeadTests
{
    [Fact]
    public void ADuplicateTargetIsRefusedBeforeAnythingIsSpent()
    {
        // "Each Undead of your choice" reads as each distinct creature, not each list
        // entry — passing the same target twice must not roll its save twice, spend
        // Sear Undead's shared roll on it twice, or let a later pass's
        // BreakTurnEffectOnDamage see a target already turned by the earlier one.
        var (encounter, cleric, undead) = OneUndeadFight(new ScriptedRandomSource(20, 1));

        var refusal = encounter.TurnUndead([undead, undead]);

        Assert.Equal("feature.turn_undead.duplicate_target", refusal?.Code);
        Assert.Equal(2, cleric.Features.ChannelDivinityRemaining);
        Assert.True(cleric.Turn.HasAction);
        Assert.False(undead.HasCondition(ConditionType.Frightened));
        Assert.False(undead.HasCondition(ConditionType.Incapacitated));
    }

    [Fact]
    public void DamageDoesNotStripAFrightenedThatBelongsToAnotherSource()
    {
        // Finding 2(a): an Undead already Frightened by an unrelated source (X)
        // before the Cleric ever turns it. Turn Undead's own Frightened attempt
        // must not silently take over X's SourceId/Expiry while (via the old merge
        // logic) leaving the "ends on damage" flag stuck on the record — that is
        // exactly the over-removal shape: a later, unrelated source's condition
        // inheriting a flag that was never printed for it, or a flagged condition's
        // ownership being reassigned to whoever last touched the slot.
        var (encounter, _, ally, undead) = ThreeSidedFight(new ScriptedRandomSource(20, 15, 1, 1, 15, 1));

        undead.AddCondition(ConditionType.Frightened, sourceId: "some-other-source");

        Assert.Null(encounter.TurnUndead([undead]));

        // Turn Undead's own Frightened rider was refused outright — the slot X
        // already held is untouched, not merged.
        var frightenedBeforeDamage = undead.ConditionState(ConditionType.Frightened)!;
        Assert.Equal("some-other-source", frightenedBeforeDamage.SourceId);
        Assert.False(frightenedBeforeDamage.EndsEarlyOnDamageOrSourceDown);

        Assert.True(undead.HasCondition(ConditionType.Incapacitated));

        encounter.EndTurn(); // Cleric's turn ends; ally goes next.
        encounter.Attack(ally.Stats.Attacks[0].Name, undead);

        // Only the Cleric's own turning condition (Incapacitated) is removed by the
        // damage — X's Frightened, never flagged, was never at risk and survives
        // with its own ownership intact.
        Assert.True(undead.HasCondition(ConditionType.Frightened));
        Assert.Equal("some-other-source", undead.ConditionState(ConditionType.Frightened)!.SourceId);
        Assert.False(undead.HasCondition(ConditionType.Incapacitated));
    }

    [Fact]
    public void CharacterizationAlreadyFrightenedByAnotherSource_TurnUndeadsOwnRiderNeverEstablishes()
    {
        // Finding 2(b): the flip side of the test above. Reachability was checked
        // directly against the party's executable surface — every preparable spell,
        // class feature and weapon mastery a level 1-5 party can use — and nothing
        // imposes Frightened or Incapacitated on anything: Hold Person is the only
        // spell in the preparable list that imposes an Incapacitated-bringer
        // (Paralyzed), and it targets only Humanoids, never Undead. So an Undead
        // already Frightened or Incapacitated from a party source before being
        // turned cannot arise from a real party's kit today — the same #614 model
        // gap T10 already named, characterized rather than fixed.
        //
        // The correct reading per print: the Undead is turned (Incapacitated lands,
        // fully flagged) and separately remains Frightened for whatever reason
        // already scared it — two independent reasons for the same condition type,
        // which Dictionary<ConditionType, ActiveCondition>'s one-slot-per-type model
        // cannot track at once. What actually happens instead: Turn Undead's own
        // Frightened rider is refused outright rather than co-existing alongside
        // the pre-existing occupant, so it never lands as its own tracked effect —
        // no early-out flag, no #615 accounting — even though the Undead is still
        // fully turned in every way that matters for CanAct (Incapacitated landed
        // clean, in the slot nothing already occupied).
        var (encounter, cleric, undead) = OneUndeadFight(new ScriptedRandomSource(20, 1, 1));

        undead.AddCondition(ConditionType.Frightened, sourceId: "some-other-source");

        Assert.Null(encounter.TurnUndead([undead]));

        var frightened = undead.ConditionState(ConditionType.Frightened)!;
        Assert.Equal("some-other-source", frightened.SourceId);
        Assert.False(frightened.EndsEarlyOnDamageOrSourceDown);
        Assert.Null(frightened.UnmodelledBehaviour);

        var incapacitated = undead.ConditionState(ConditionType.Incapacitated)!;
        Assert.Equal(cleric.Id, incapacitated.SourceId);
        Assert.True(incapacitated.EndsEarlyOnDamageOrSourceDown);
    }

    [Fact]
    public void TheClericBeingIncapacitatedByADamageComponentFreesTheTurningImmediately()
    {
        // Finding 3: EndTurnEffectsWhoseSourceIsDown must fire at each damage site
        // itself, not only in EndTurn — a Cleric incapacitated mid-round, by
        // someone else's attack on its own turn later in the round, must free
        // anything it turned right then, not merely once whoever incapacitated it
        // gets around to ending their own turn.
        var cleric = ClericCombatant(wisdom: 10, uses: 2, x: 0, initiativeBonus: 30);

        var bigHit = new CombatAttack(
            "Big Hit",
            AttackKind.Melee,
            10,
            5,
            null,
            null,
            [new AttackDamage(new DiceExpression(30, 1, 0), DamageType.Bludgeoning, 30)]);

        var attacker = CombatTestData.Combatant(
            "attacker",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: 20, attacks: [bigHit]),
            x: 1);

        var undead = UndeadCombatant("undead", x: 2, initiativeBonus: -10);

        // Initiative x3 (Cleric 30+20, attacker 20+20, Undead -10+1 — Cleric first,
        // attacker second), Turn Undead's save (fails, 1), the attacker's attack
        // roll on the Cleric (15, an easy hit short of a natural 20), then thirty
        // d1s for exactly 30 damage — the Cleric's whole 30 hit points, downing it
        // without killing it (a character falls Unconscious at 0, rather than
        // dying) in one component.
        var dice = new[] { 20, 20, 1, 1, 15 }.Concat(Enumerable.Repeat(1, 30)).ToArray();

        var encounter = Encounter.Start(
            new Battlefield(10, 8),
            [cleric, attacker, undead],
            new ScriptedRandomSource(dice));

        Assert.Null(encounter.TurnUndead([undead]));
        Assert.True(undead.HasCondition(ConditionType.Incapacitated));

        encounter.EndTurn(); // Cleric's turn ends; the attacker goes next.
        Assert.Same(attacker, encounter.ActiveCombatant);

        encounter.Attack(bigHit.Name, cleric);

        Assert.Equal(0, cleric.CurrentHitPoints);
        Assert.True(cleric.HasCondition(ConditionType.Incapacitated));

        // Freed immediately, inside the very Attack() call that incapacitated the
        // Cleric — not merely by the time anyone's EndTurn() next runs.
        Assert.False(undead.HasCondition(ConditionType.Incapacitated));
    }

    [Fact]
    public void ImposedConditionsRecordTheUnmodelledFleeBehaviour()
    {
        // Finding #4 (designer ruling): the printed flee is a rule this engine
        // cannot yet grant a turned creature (#615), and it is accounted on the
        // condition itself — the same shape AppliedCondition.UnmodelledRequirement
        // gives a stat-block rider — rather than left in a doc comment alone.
        var (encounter, _, undead) = OneUndeadFight(new ScriptedRandomSource(20, 1, 1));

        Assert.Null(encounter.TurnUndead([undead]));

        var frightened = undead.ConditionState(ConditionType.Frightened)!;
        var incapacitated = undead.ConditionState(ConditionType.Incapacitated)!;

        Assert.False(string.IsNullOrWhiteSpace(frightened.UnmodelledBehaviour));
        Assert.Contains("#615", frightened.UnmodelledBehaviour, StringComparison.Ordinal);
        Assert.Contains("flee", frightened.UnmodelledBehaviour, StringComparison.Ordinal);

        Assert.False(string.IsNullOrWhiteSpace(incapacitated.UnmodelledBehaviour));
        Assert.Contains("#615", incapacitated.UnmodelledBehaviour, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedSaveGainsBothConditionsFlaggedAndTimedAPassGainsNeither()
    {
        // Initiative 20 / 1 / 1 (Cleric first), then a low Wisdom save for the first
        // Undead (fails) and a high one for the second (passes).
        var (encounter, cleric, undead1, undead2) = TwoUndeadFight(new ScriptedRandomSource(20, 1, 1, 1, 20));

        Assert.Null(encounter.TurnUndead([undead1, undead2]));

        Assert.True(undead1.HasCondition(ConditionType.Frightened));
        Assert.True(undead1.HasCondition(ConditionType.Incapacitated));

        var frightened = undead1.ConditionState(ConditionType.Frightened)!;
        var incapacitated = undead1.ConditionState(ConditionType.Incapacitated)!;

        Assert.Equal(cleric.Id, frightened.SourceId);
        Assert.Equal(cleric.Id, incapacitated.SourceId);
        Assert.True(frightened.EndsEarlyOnDamageOrSourceDown);
        Assert.True(incapacitated.EndsEarlyOnDamageOrSourceDown);

        // "for 1 minute" — ten of the bearer's own turns out, ending at end of turn.
        // undead1 has begun no turn of its own yet, so its clock reads 0 + 10.
        Assert.Equal(new ConditionExpiry(undead1.Id, ConditionClock.EndOfTurn, 10), frightened.Expiry);
        Assert.Equal(new ConditionExpiry(undead1.Id, ConditionClock.EndOfTurn, 10), incapacitated.Expiry);

        Assert.False(undead2.HasCondition(ConditionType.Frightened));
        Assert.False(undead2.HasCondition(ConditionType.Incapacitated));

        Assert.Equal(1, cleric.Features.ChannelDivinityRemaining);
        Assert.False(cleric.Turn.HasAction);

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("resists", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("fails", StringComparison.Ordinal));
    }

    [Fact]
    public void DamageFromAnAllyEndsTheTurning()
    {
        // Initiative: Cleric 20, ally 15, Undead 1. Turn Undead's own save (fails, 1),
        // then the ally's attack roll (15, an easy hit that stops short of a natural
        // 20 — a Critical Hit would double the 1d1 and need a second scripted die)
        // and its damage (1).
        var (encounter, _, ally, undead) = ThreeSidedFight(new ScriptedRandomSource(20, 15, 1, 1, 15, 1));

        Assert.Null(encounter.TurnUndead([undead]));
        Assert.True(undead.HasCondition(ConditionType.Frightened));
        Assert.True(undead.HasCondition(ConditionType.Incapacitated));

        encounter.EndTurn(); // Cleric's turn ends; ally goes next.
        Assert.Same(ally, encounter.ActiveCombatant);

        encounter.Attack(ally.Stats.Attacks[0].Name, undead);

        Assert.False(undead.HasCondition(ConditionType.Frightened));
        Assert.False(undead.HasCondition(ConditionType.Incapacitated));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("the turning breaks on the blow", StringComparison.Ordinal));
    }

    [Fact]
    public void ZeroEffectiveDamageDoesNotEndTheTurning()
    {
        // The same fight, except the Undead is immune to the attack's damage type, so
        // the blow lands for 0 effective damage — "takes any damage" is not satisfied.
        var (encounter, _, ally, undead) = ThreeSidedFight(
            new ScriptedRandomSource(20, 15, 1, 1, 15, 1),
            undeadImmuneToSlashing: true);

        Assert.Null(encounter.TurnUndead([undead]));
        encounter.EndTurn();

        encounter.Attack(ally.Stats.Attacks[0].Name, undead);

        Assert.True(undead.HasCondition(ConditionType.Frightened));
        Assert.True(undead.HasCondition(ConditionType.Incapacitated));
    }

    [Fact]
    public void TheClericBeingIncapacitatedFreesEveryoneItTurned()
    {
        var (encounter, cleric, undead) = OneUndeadFight(new ScriptedRandomSource(20, 1, 1));

        Assert.Null(encounter.TurnUndead([undead]));
        Assert.True(undead.HasCondition(ConditionType.Incapacitated));

        // Hand-applied Stunned brings Incapacitated with it — the same shape the
        // grapple sweep already reads off a grappler's own incapacity.
        cleric.AddCondition(ConditionType.Stunned);

        encounter.EndTurn();

        Assert.False(undead.HasCondition(ConditionType.Frightened));
        Assert.False(undead.HasCondition(ConditionType.Incapacitated));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("can no longer hold the turning", StringComparison.Ordinal));
    }

    [Fact]
    public void TheClericDyingFreesEveryoneItTurned()
    {
        var (encounter, cleric, undead) = OneUndeadFight(new ScriptedRandomSource(20, 1, 1));

        Assert.Null(encounter.TurnUndead([undead]));
        Assert.True(undead.HasCondition(ConditionType.Incapacitated));

        DamageRulesHelper.Kill(cleric);
        Assert.True(cleric.IsDead);

        encounter.EndTurn();

        Assert.False(undead.HasCondition(ConditionType.Frightened));
        Assert.False(undead.HasCondition(ConditionType.Incapacitated));
    }

    [Fact]
    public void GrazeAlsoBreaksTheTurning()
    {
        // Weapon Mastery's Graze deals its ability-modifier damage on a miss, outside
        // the ordinary hit path — the exact Graze/Cleave census #382 and the
        // Concentration bug already learned must be exhaustive.
        var attack = CombatTestData.MeleeAttack(bonus: 5, damage: "1d1") with
        {
            Mastery = WeaponMastery.Graze,
            AbilityModifier = 3,
        };

        var cleric = ClericCombatant(wisdom: 10, uses: 2, x: 0);
        var attacker = CombatTestData.Combatant(
            "attacker",
            sideId: CombatTestData.Heroes,
            stats: CombatTestData.Stats(initiativeBonus: 15, attacks: [attack]),
            x: 1);
        var undead = UndeadCombatant("undead", x: 2, initiativeBonus: -10);

        var encounter = Encounter.Start(
            new Battlefield(10, 8),
            [cleric, attacker, undead],
            // Initiative x3, Turn Undead's save (fails), then the attack roll (a miss).
            new ScriptedRandomSource(20, 15, 1, 1, 2));

        Assert.Null(encounter.TurnUndead([undead]));
        encounter.EndTurn(); // attacker goes next
        Assert.Same(attacker, encounter.ActiveCombatant);

        encounter.Attack(attack.Name, undead);

        Assert.False(undead.HasCondition(ConditionType.Frightened));
        Assert.False(undead.HasCondition(ConditionType.Incapacitated));
    }

    // Cleave is deliberately not exercised here the way Graze and the save-effect
    // loop are. TryCleave's own second-target search filters on Combatant.IsActive,
    // and a turned creature is Incapacitated — so a turned Undead can never be
    // Cleave's auto-selected second target in the first place. The
    // BreakTurnEffectOnDamage call at that site (Encounter.cs, beside
    // CheckConcentration(second, ...)) is still wired for four-site census
    // consistency with Graze, the attack/Multiattack loop and the save-effect loop —
    // and stands ready the moment Cleave's own filter ever changes — but it is not
    // independently reachable or knockout-verifiable against a turned creature today.
    // Stated here rather than faked with a test that cannot exercise what it claims to.

    [Fact]
    public void ASaveEffectAlsoBreaksTheTurning()
    {
        // The fourth site: a save-effect's own damage loop. Divine Spark's Harm reuses
        // the same shared Channel Divinity uses, so a second use on the same Cleric
        // reaches it without inventing a new spell — one round later, since both
        // effects spend the Action.
        var (encounter, cleric, undead) = OneUndeadFight(new ScriptedRandomSource(20, 1, 1, 1, 5), uses: 2);

        Assert.Null(encounter.TurnUndead([undead]));
        Assert.True(undead.HasCondition(ConditionType.Incapacitated));

        encounter.EndTurn(); // Cleric's turn ends.
        encounter.EndTurn(); // Undead's (do-nothing) turn ends.
        Assert.Same(cleric, encounter.ActiveCombatant);

        // Divine Spark's own Constitution save (fails, DC 10) and its 1d8 roll.
        Assert.Null(encounter.DivineSpark(undead, DivineSparkUse.Harm));

        Assert.False(undead.HasCondition(ConditionType.Frightened));
        Assert.False(undead.HasCondition(ConditionType.Incapacitated));
    }

    [Fact]
    public void TheEffectExpiresOnTheUndeadsTenthTurnIfNothingElseEndsItFirst()
    {
        var (encounter, cleric, undead) = OneUndeadFight(new ScriptedRandomSource(20, 1, 1), attacksForCleric: false);

        Assert.Null(encounter.TurnUndead([undead]));

        for (var i = 0; i < 10; i++)
        {
            Assert.False(undead.IsDead);
            encounter.EndTurn(); // Cleric's turn ends.
            encounter.EndTurn(); // Undead's turn ends.
        }

        Assert.False(undead.HasCondition(ConditionType.Frightened));
        Assert.False(undead.HasCondition(ConditionType.Incapacitated));
        Assert.Contains(
            encounter.Log,
            step => step.Narration == $"{undead.Name} is no longer Frightened.");
    }

    [Theory]
    [InlineData("not_undead")]
    [InlineData("out_of_range")]
    [InlineData("total_cover")]
    [InlineData("inactive")]
    [InlineData("no_targets")]
    [InlineData("exhausted")]
    [InlineData("action_spent")]
    public void EveryRefusalFiresBeforeAnythingIsSpent(string shape)
    {
        var random = new ScriptedRandomSource(20, 1);

        var cleric = ClericCombatant(wisdom: 10, uses: shape == "exhausted" ? 0 : 2, x: 0);

        var undeadX = shape == "out_of_range" ? 7 : 2;
        var undead = UndeadCombatant("undead", x: undeadX, initiativeBonus: -10);

        var battlefield = shape == "total_cover"
            ? new Battlefield(10, 8, blocked: [new GridPosition(1, 0)])
            : new Battlefield(10, 8);

        var target = shape switch
        {
            "not_undead" => CombatTestData.Combatant("living", sideId: CombatTestData.Monsters, x: 2),
            _ => undead,
        };

        if (shape == "inactive")
        {
            DamageRulesHelper.Kill(target);
        }

        var encounter = Encounter.Start(battlefield, [cleric, target], random);

        if (shape == "action_spent")
        {
            Assert.Null(encounter.Dodge());
        }

        IReadOnlyList<Combatant> targets = shape == "no_targets" ? Array.Empty<Combatant>() : [target];

        var refusal = encounter.TurnUndead(targets);

        var expectedCode = shape switch
        {
            "not_undead" => "feature.turn_undead.not_undead",
            "out_of_range" => "feature.turn_undead.out_of_range",
            "total_cover" => "feature.total_cover",
            "inactive" => "feature.turn_undead.inactive",
            "no_targets" => "feature.turn_undead.no_targets",
            "exhausted" => "feature.channel_divinity.exhausted",
            "action_spent" => "action.spent",
            _ => throw new InvalidOperationException(shape),
        };

        Assert.Equal(expectedCode, refusal?.Code);
        Assert.Equal(shape == "exhausted" ? 0 : 2, cleric.Features.ChannelDivinityRemaining);

        if (shape != "action_spent")
        {
            Assert.True(cleric.Turn.HasAction);
        }
    }

    [Fact]
    public void ImmunityToOneConditionStillLandsTheOther()
    {
        var cleric = ClericCombatant(wisdom: 10, uses: 2, x: 0);
        var undead = UndeadCombatant(
            "undead",
            x: 1,
            initiativeBonus: -10,
            conditionImmunities: [ConditionType.Frightened]);

        var encounter = Encounter.Start(
            new Battlefield(10, 8),
            [cleric, undead],
            new ScriptedRandomSource(20, 1, 1));

        Assert.Null(encounter.TurnUndead([undead]));

        Assert.False(undead.HasCondition(ConditionType.Frightened));
        Assert.True(undead.HasCondition(ConditionType.Incapacitated));

        // The sweep still frees whichever landed.
        DamageRulesHelper.Kill(cleric);
        encounter.EndTurn();

        Assert.False(undead.HasCondition(ConditionType.Incapacitated));
    }

    // No flee-behaviour test here (AC-11's "tries to move as far from you as it
    // can"). Encounter.BeginTurn's pre-existing "if (!combatant.CanAct)" skip — the
    // same gate every other Incapacitating condition relies on — advances past a
    // turned creature's turn entirely before SimpleTacticsPolicy.TakeTurn is ever
    // invoked with it active, so a flee branch inside TakeTurn is unreachable dead
    // code under the current turn loop. Turn Undead's mechanical effects (the save,
    // both conditions, all three early-outs, Sear Undead) are fully implemented and
    // pinned above; the turned creature is fully neutralised (Frightened,
    // Incapacitated, its turn skipped) but does not yet actively retreat. This is
    // strictly *less* than the book, never more — the creature never gets an
    // advantage the print does not grant it — so nothing here is unsafe to ship
    // without the retreat. The retreat itself needs a new "Incapacitated-but-mobile"
    // turn seam and is filed as a follow-on for architect, not decided here.

    [Fact]
    public void SearUndeadDealsSharedRadiantDamageWithoutEndingTheTurnEffect()
    {
        // Wisdom 14 (+2): Sear Undead rolls 2d8. Initiative x3, Turn Undead's two saves
        // (both fail), the shared 2d8 (5 and 3 = 8), each rolled once.
        var cleric = ClericCombatant(wisdom: 14, uses: 2, x: 0, searUndead: true);
        var undead1 = UndeadCombatant("undead1", x: 1, initiativeBonus: -10);
        var undead2 = UndeadCombatant("undead2", x: 2, initiativeBonus: -11);

        var encounter = Encounter.Start(
            new Battlefield(10, 8),
            [cleric, undead1, undead2],
            new ScriptedRandomSource(20, 1, 1, 1, 1, 5, 3));

        Assert.Null(encounter.TurnUndead([undead1, undead2]));

        // Both took the same 8-point Radiant total.
        Assert.Equal(12, undead1.CurrentHitPoints);
        Assert.Equal(12, undead2.CurrentHitPoints);

        // The damage did not free either target — the whole point of "this damage
        // doesn't end the turn effect": it landed before the turn effect existed to
        // break.
        Assert.True(undead1.HasCondition(ConditionType.Frightened));
        Assert.True(undead1.HasCondition(ConditionType.Incapacitated));
        Assert.True(undead2.HasCondition(ConditionType.Frightened));
        Assert.True(undead2.HasCondition(ConditionType.Incapacitated));

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("Sear Undead", StringComparison.Ordinal)
                && step.Narration.Contains("Radiant", StringComparison.Ordinal));
    }

    [Fact]
    public void SearUndeadIsNotRolledWithoutTheFeature()
    {
        // A level-3 Cleric (no Sear Undead) turns an Undead that fails: no Radiant
        // damage rolled at all, so the script does not need a d8 to spend.
        var (encounter, _, undead) = OneUndeadFight(new ScriptedRandomSource(20, 1, 1));

        Assert.Null(encounter.TurnUndead([undead]));

        Assert.Equal(20, undead.CurrentHitPoints);
    }

    [Fact]
    public void CharacterizationTurnedThenSeparatelyStunned_CurrentlyLosesIncapacitatedWhenTheTurningBreaks()
    {
        // T10's named risk (spec, #369): conditions are keyed one-per-type
        // (Dictionary<ConditionType, ActiveCondition>). Turn Undead imposes a
        // *standalone* Incapacitated; a second, independent Incapacitated-bringer
        // (Stunned) landing on the same bearer cannot get its own slot — AddCondition's
        // TryAdd no-ops because Turn Undead's entry already occupies it — so the
        // dictionary has no record that Stunned wants Incapacitated held too. This is
        // CONFIRMED BROKEN by running this test (not a hypothetical): when the turning
        // ends on damage, BreakTurnEffectOnDamage removes the shared Incapacitated
        // entry, and the creature reads as no longer Incapacitated even though it is
        // still Stunned.
        //
        // Unreachable in current play: nothing in the pregen roster or the extant
        // monster pool Stuns (or Paralyzes/Petrifies) an Undead — the collision needs
        // two independent Incapacitated-bringers on the same creature, and Turn Undead
        // is the only standalone source that exists today. So this is characterized
        // rather than fixed here: the correct behaviour (the bearer stays Incapacitated
        // as long as *any* source holds it) is written down below, and the actual
        // (wrong) behaviour is what the assertions pin, so a future stun-capable party
        // or monster surfaces this loudly — a test that goes red the moment it becomes
        // reachable — rather than silently mistracking Incapacitated. Per #369's spec,
        // this is NOT patched with ad hoc reference counting; the real fix (a
        // multi-source Incapacitated model) is filed and routed to architect: #614.
        //
        // Stunned grants Advantage on attack rolls against the bearer, so the ally's
        // attack rolls two d20s (taking the higher) once it lands.
        var (encounter, _, ally, undead) = ThreeSidedFight(new ScriptedRandomSource(20, 15, 1, 1, 15, 14, 1));

        Assert.Null(encounter.TurnUndead([undead]));
        Assert.True(undead.HasCondition(ConditionType.Incapacitated));

        // A second, independent source of Incapacitated (Stunned), landing on top of
        // Turn Undead's own standalone Incapacitated.
        undead.AddCondition(ConditionType.Stunned, sourceId: "someone-else");
        Assert.True(undead.HasCondition(ConditionType.Stunned));
        Assert.True(undead.HasCondition(ConditionType.Incapacitated));

        encounter.EndTurn(); // Cleric's turn ends; ally goes next.
        encounter.Attack(ally.Stats.Attacks[0].Name, undead); // Breaks the turning.

        Assert.False(undead.HasCondition(ConditionType.Frightened));
        Assert.True(undead.HasCondition(ConditionType.Stunned));

        // The CURRENT (wrong) behaviour: Stunned alone should keep this creature
        // Incapacitated per print, but the shared dictionary slot went with Turn
        // Undead's own entry. Correct behaviour is `Assert.True` here; #614 tracks
        // fixing that. Pinned as `False` so this test passes today and turns red the
        // moment #614 lands (or the moment this collision becomes reachable in a real
        // fight and mistracks Incapacitated silently, whichever comes first).
        Assert.False(undead.HasCondition(ConditionType.Incapacitated));
    }

    // --- Test data -----------------------------------------------------------------

    private static Dictionary<Ability, MonsterAbility> ClericAbilities(int wisdom) => new()
    {
        [Ability.Strength] = new(10, 0),
        [Ability.Dexterity] = new(10, 0),
        [Ability.Constitution] = new(14, 2),
        [Ability.Intelligence] = new(10, 0),
        [Ability.Wisdom] = new(wisdom, (wisdom - 10) / 2),
        [Ability.Charisma] = new(10, 0),
    };

    private static Combatant ClericCombatant(
        int wisdom,
        int uses,
        int x,
        int y = 0,
        int initiativeBonus = 20,
        bool searUndead = false,
        bool hasAttack = true)
    {
        var features = new List<ClassFeature> { ClassFeature.ChannelDivinity };

        if (searUndead)
        {
            features.Add(ClassFeature.SearUndead);
        }

        var stats = CombatTestData.Stats(
            maximumHitPoints: 30,
            initiativeBonus: initiativeBonus,
            diesAtZeroHitPoints: false,
            attacks: hasAttack ? null : []) with
        {
            Abilities = ClericAbilities(wisdom),
            Character = new CombatantFeatures(
                features,
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: searUndead ? 5 : 3,
                ChannelDivinityUses: uses),
        };

        return new Combatant("cleric", "Cleric", CombatTestData.Heroes, stats, new GridPosition(x, y));
    }

    private static Combatant UndeadCombatant(
        string id,
        int x,
        int y = 0,
        int initiativeBonus = -10,
        int wisdomSaveBonus = 0,
        int maximumHitPoints = 20,
        IReadOnlyList<ConditionType>? conditionImmunities = null,
        IReadOnlyDictionary<DamageType, DamageResponse>? damageResponses = null)
    {
        var abilities = new Dictionary<Ability, MonsterAbility>
        {
            [Ability.Strength] = new(10, 0),
            [Ability.Dexterity] = new(10, 0),
            [Ability.Constitution] = new(10, 0),
            [Ability.Intelligence] = new(10, 0),
            [Ability.Wisdom] = new(10, wisdomSaveBonus),
            [Ability.Charisma] = new(10, 0),
        };

        var stats = CombatTestData.Stats(
            maximumHitPoints: maximumHitPoints,
            initiativeBonus: initiativeBonus,
            conditionImmunities: conditionImmunities,
            damageResponses: damageResponses) with
        {
            Abilities = abilities,
            Type = CreatureType.Undead,
        };

        return CombatTestData.Combatant(id, sideId: CombatTestData.Monsters, stats: stats, x: x, y: y);
    }

    /// <summary>A Cleric and one Undead within easy reach, Cleric acting first.</summary>
    private static (Encounter Encounter, Combatant Cleric, Combatant Undead) OneUndeadFight(
        IRandomSource random,
        int uses = 2,
        bool attacksForCleric = true)
    {
        var cleric = ClericCombatant(wisdom: 10, uses: uses, x: 0, hasAttack: attacksForCleric);
        var undead = UndeadCombatant("undead", x: 1, initiativeBonus: -10);

        var encounter = Encounter.Start(new Battlefield(10, 8), [cleric, undead], random);

        return (encounter, cleric, undead);
    }

    /// <summary>A Cleric and two Undead, Cleric acting first.</summary>
    private static (Encounter Encounter, Combatant Cleric, Combatant Undead1, Combatant Undead2) TwoUndeadFight(
        IRandomSource random)
    {
        var cleric = ClericCombatant(wisdom: 10, uses: 2, x: 0);
        var undead1 = UndeadCombatant("undead1", x: 1, initiativeBonus: -10);
        var undead2 = UndeadCombatant("undead2", x: 2, initiativeBonus: -11);

        var encounter = Encounter.Start(new Battlefield(10, 8), [cleric, undead1, undead2], random);

        return (encounter, cleric, undead1, undead2);
    }

    /// <summary>A Cleric, an allied attacker, and one Undead, Cleric acting first.</summary>
    private static (Encounter Encounter, Combatant Cleric, Combatant Ally, Combatant Undead) ThreeSidedFight(
        IRandomSource random,
        bool undeadImmuneToSlashing = false)
    {
        var cleric = ClericCombatant(wisdom: 10, uses: 2, x: 0, initiativeBonus: 20);

        var ally = CombatTestData.Combatant(
            "ally",
            sideId: CombatTestData.Heroes,
            stats: CombatTestData.Stats(
                initiativeBonus: 15,
                attacks: [CombatTestData.MeleeAttack(bonus: 5, damage: "1d1")]),
            x: 1,
            y: 1);

        var undead = UndeadCombatant(
            "undead",
            x: 1,
            initiativeBonus: -10,
            damageResponses: undeadImmuneToSlashing
                ? new Dictionary<DamageType, DamageResponse> { [DamageType.Slashing] = DamageResponse.Immunity }
                : null);

        var encounter = Encounter.Start(new Battlefield(10, 8), [cleric, ally, undead], random);

        return (encounter, cleric, ally, undead);
    }
}
