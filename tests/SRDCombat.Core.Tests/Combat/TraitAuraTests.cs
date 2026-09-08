using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The trait-aura execution seam and its one in-pool case, the Ghast's Stench (#670):
/// a passive per-turn area effect a creature emits, resolved through a save forced at the
/// start of each victim's turn rather than as a spent action.
/// </summary>
/// <remarks>
/// The rule these pin, verbatim from SRD 5.2.1 p. 287: "Stench. Constitution Saving Throw:
/// DC 10, any creature that starts its turn in a 5-foot Emanation originating from the
/// ghast. Failure: The target has the Poisoned condition until the start of its next turn.
/// Success: The target is immune to this ghast's Stench for 24 hours." The reading — who
/// emits, who is spared, the rest-of-fight immunity, the emitter-incapacity semantics —
/// is written on <see cref="AuraEffect"/> and executed in <c>Encounter.FireAuras</c>.
/// <para>
/// Every combatant here is hand-authored, not loaded from content: an engine test should
/// fail when the engine changes, not when the bestiary is re-extracted — even now that
/// #676 has taught the extractor to classify the real Ghast's Stench, which
/// <c>RealMonsterCombatTests</c> exercises against the committed bestiary instead. The
/// scripted die is load-bearing throughout — it throws on any unscripted roll, so a test
/// that passes also proves nobody rolled a save it should not have: an out-of-range
/// victim, a dead emitter, or a creature already immune.
/// </para>
/// </remarks>
public class TraitAuraTests
{
    [Fact]
    public void AVictimThatStartsItsTurnInRangeAndFailsIsPoisoned()
    {
        // Hero acts first (initiative +10 vs the ghast's -10); at its turn start the Stench
        // washes over it, it rolls 1 + 2 = 3 against DC 10 and fails, and gains Poisoned.
        var encounter = Fight(
            new ScriptedRandomSource(1, 1, 1),
            StenchGhast(),
            Victim("a", x: 1));

        var hero = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.True(hero.HasCondition(ConditionType.Poisoned));
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Entry
                && step.Narration.Contains("Stench washes over a", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Condition
                && step.Narration.Contains("a has the Poisoned condition", StringComparison.Ordinal));
    }

    [Fact]
    public void AVictimOutsideTheEmanationIsUntouched()
    {
        // The hero stands 15 ft. away (x: 3), beyond the 5-foot emanation. The scripted die
        // holds only the two initiative rolls, so a save rolled here would overrun it.
        var encounter = Fight(
            new ScriptedRandomSource(1, 1),
            StenchGhast(),
            Victim("a", x: 3));

        var hero = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.False(hero.HasCondition(ConditionType.Poisoned));
        Assert.DoesNotContain(encounter.Log, step => step.Narration.Contains("washes over", StringComparison.Ordinal));
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("Constitution saving throw", StringComparison.Ordinal));
    }

    [Fact]
    public void ASuccessfulSaveGrantsImmunityForTheRestOfTheFightAndIsNotRerolled()
    {
        // The hero rolls 20 + 2 = 22 and saves, gaining immunity to this ghast's Stench.
        // Two turns later — its own next turn — it is still in range but does not roll
        // again: the scripted die carries exactly one save, so a re-roll would overrun it.
        var encounter = Fight(
            new ScriptedRandomSource(1, 1, 20),
            StenchGhast(),
            Victim("a", x: 1));

        var hero = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.False(hero.HasCondition(ConditionType.Poisoned));

        encounter.EndTurn(); // the ghast's turn
        encounter.EndTurn(); // round two: the hero starts its turn in range again

        Assert.False(hero.HasCondition(ConditionType.Poisoned));
        Assert.Equal(
            1,
            encounter.Log.Count(step => step.Narration.Contains("Constitution saving throw", StringComparison.Ordinal)));
        Assert.Equal(
            1,
            encounter.Log.Count(step => step.Narration.Contains("washes over", StringComparison.Ordinal)));
    }

    [Fact]
    public void AFailedSaveIsRolledAgainAtTheStartOfEachTurn()
    {
        // A failer gains no immunity: the Poisoned it took on turn one expires at the start
        // of turn two, and the Stench rolls again — failing again — so it stays Poisoned.
        var encounter = Fight(
            new ScriptedRandomSource(1, 1, 1, 1),
            StenchGhast(),
            Victim("a", x: 1));

        var hero = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.True(hero.HasCondition(ConditionType.Poisoned));

        encounter.EndTurn(); // the ghast's turn
        encounter.EndTurn(); // round two: the earlier Poisoned expires, then the aura re-rolls

        Assert.True(hero.HasCondition(ConditionType.Poisoned));
        Assert.Equal(
            2,
            encounter.Log.Count(step => step.Narration.Contains("Constitution saving throw", StringComparison.Ordinal)));
    }

    [Fact]
    public void ACreatureWithNoAuraEmitsNothing()
    {
        // The no-op the frozen transcript rests on, pinned directly. The monster carries a
        // saving-throw entry — but it is an ordinary action, not an aura (no AuraEffect
        // signal) — so it must not fire at the adjacent hero's turn start. This is the
        // signal, not the presence of a save, that decides an aura: were the hook to fire
        // for any save-bearing entry, the scripted die (two initiative rolls only) would
        // overrun.
        var savingThrowMonster = CombatTestData.Combatant(
            "m",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10, attacks: []) with
            {
                Entries =
                [
                    new MonsterEntry(
                        "Poison Breath",
                        MonsterEntrySection.Action,
                        "Poison Breath.",
                        Mechanics: EntryMechanics.SavingThrow,
                        Save: StenchSave()),
                ],
            },
            x: 0,
            y: 5);

        var encounter = Fight(
            new ScriptedRandomSource(1, 1),
            savingThrowMonster,
            Victim("a", x: 1));

        var hero = encounter.Combatants.Single(combatant => combatant.Id == "a");

        encounter.EndTurn(); // the monster's turn — it, too, emits nothing as a victim

        Assert.False(hero.HasCondition(ConditionType.Poisoned));
        Assert.DoesNotContain(encounter.Log, step => step.Narration.Contains("washes over", StringComparison.Ordinal));
    }

    [Fact]
    public void ADeadEmitterEmitsNothing()
    {
        // The ghast is dead before the fight begins; a living packmate keeps the monster
        // side standing so the fight runs and the hero takes its turn. No emanation
        // originates from a corpse, so nothing washes over the adjacent hero.
        var ghast = StenchGhast("ghast", x: 0);
        DamageRulesHelper.Kill(ghast);

        var packmate = CombatTestData.Combatant(
            "packmate",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10, attacks: []),
            x: 0,
            y: 0);

        var encounter = Fight(
            new ScriptedRandomSource(1, 1, 1),
            ghast,
            packmate,
            Victim("a", x: 1));

        var hero = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.True(ghast.IsDead);
        Assert.False(hero.HasCondition(ConditionType.Poisoned));
        Assert.DoesNotContain(encounter.Log, step => step.Narration.Contains("washes over", StringComparison.Ordinal));
    }

    [Fact]
    public void AnIncapacitatedButLivingEmitterStillStenches()
    {
        // The ghast is Incapacitated but alive. Stench, unlike Aura of Protection, prints
        // no clause turning off while its emitter is Incapacitated (SRD 5.2.1 p. 287 vs
        // p. 55), so the emanation persists and still catches the hero.
        var ghast = StenchGhast("ghast", x: 0);
        ghast.AddCondition(new ActiveCondition(ConditionType.Incapacitated));

        var encounter = Fight(
            new ScriptedRandomSource(1, 1, 1),
            ghast,
            Victim("a", x: 1));

        var hero = encounter.Combatants.Single(combatant => combatant.Id == "a");

        Assert.False(ghast.IsDead);
        Assert.False(ghast.CanAct);
        Assert.True(hero.HasCondition(ConditionType.Poisoned));
    }

    [Fact]
    public void AnIncapacitatedVictimStillRollsAndBanksImmunityThatHoldsOnceItCanAct()
    {
        // "any creature that starts its turn" — a creature has started its turn the moment
        // its clock ticks, Incapacitated or not. So an Incapacitated victim adjacent to the
        // ghast still rolls the save on its (skipped) turn, and a success banks the 24-hour
        // immunity even though the Poisoned rider could never have landed on it while down.
        // When it later becomes able to act, it is already immune and does not re-roll — the
        // scripted die carries exactly one save, so a second would overrun it.
        var hero = Victim("a", x: 1);
        hero.AddCondition(new ActiveCondition(ConditionType.Incapacitated));

        var encounter = Fight(
            new ScriptedRandomSource(1, 1, 20),
            StenchGhast(),
            hero);

        // Turn one: the hero's turn is skipped for Incapacity, but the Stench fired and it
        // saved (20 + 2 = 22), so it never took Poisoned and banked immunity.
        Assert.False(hero.HasCondition(ConditionType.Poisoned));
        Assert.Equal(
            1,
            encounter.Log.Count(step => step.Narration.Contains("Constitution saving throw", StringComparison.Ordinal)));

        // The Incapacity wears off; its next turn it can act and starts in range again.
        hero.RemoveCondition(ConditionType.Incapacitated);
        encounter.EndTurn(); // the ghast's turn, then round two brings the hero's

        Assert.True(hero.CanAct);
        Assert.False(hero.HasCondition(ConditionType.Poisoned));
        Assert.Equal(
            1,
            encounter.Log.Count(step => step.Narration.Contains("Constitution saving throw", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheSameSeedProducesTheSameAuraSaves()
    {
        // Determinism through IRandomSource: an aura save rolls dice (unlike a Parry), so
        // two fights from one seed must narrate byte-identically — the same saves, in the
        // same order, with the same immunity outcomes.
        static Encounter Run(int seed)
        {
            var encounter = Fight(new SeededRandomSource(seed), StenchGhast(), Victim("a", x: 1));

            for (var turn = 0; turn < 6; turn++)
            {
                encounter.EndTurn();
            }

            return encounter;
        }

        var first = Run(4242);
        var second = Run(4242);

        Assert.Equal(
            first.Log.Select(step => step.Narration),
            second.Log.Select(step => step.Narration));

        // The aura actually fired, so the equality above is a fact about aura saves rather
        // than a fight in which nothing happened.
        Assert.Contains(first.Log, step => step.Narration.Contains("washes over", StringComparison.Ordinal));
    }

    /// <summary>
    /// A Stench save: DC 10 Constitution, no damage, a Poisoned rider lasting until the
    /// start of the victim's next turn. The area is the printed 5-foot Emanation — carried
    /// so the fixture mirrors what #676's extractor reclassification now produces from the
    /// real stat block, though <c>FireAuras</c> resolves it per-victim rather than as an
    /// area sweep.
    /// </summary>
    private static SaveEffect StenchSave() => new(
        Ability.Constitution,
        10,
        new EffectArea(AreaShape.Emanation, 5),
        [],
        SaveSuccessOutcome.NoEffect,
        [
            new AppliedCondition(
                ConditionType.Poisoned,
                Duration: new ConditionDuration(ConditionClock.StartOfTurn, ConditionDurationOwner.Bearer, 1)),
        ]);

    private static MonsterEntry StenchEntry() => new(
        "Stench",
        MonsterEntrySection.Trait,
        "Stench.",
        Mechanics: EntryMechanics.SavingThrow,
        Save: StenchSave(),
        Aura: new AuraEffect(5, AuraClock.StartOfVictimTurn));

    /// <summary>A monster whose only trait is the Stench aura, at (x, 5), slow on initiative.</summary>
    private static Combatant StenchGhast(string id = "ghast", int x = 0)
    {
        var stats = CombatTestData.Stats(initiativeBonus: -10, attacks: []) with
        {
            Entries = [StenchEntry()],
        };

        return CombatTestData.Combatant(id, sideId: CombatTestData.Monsters, stats: stats, x: x, y: 5);
    }

    /// <summary>A hero victim at (x, 5), quick on initiative so it acts before the ghast.</summary>
    private static Combatant Victim(string id, int x) =>
        CombatTestData.Combatant(
            id,
            sideId: CombatTestData.Heroes,
            stats: CombatTestData.Stats(maximumHitPoints: 40, initiativeBonus: 10, attacks: []),
            x: x,
            y: 5);

    private static Encounter Fight(IRandomSource random, params Combatant[] combatants) =>
        Encounter.Start(new Battlefield(12, 12), combatants, random);
}
