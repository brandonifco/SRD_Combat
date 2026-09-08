using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The death-burst execution seam (#679): an on-death area save a creature triggers
/// automatically when it dies, resolved through its own <see cref="MonsterEntry.Save"/>
/// exactly as any other stat-block saving-throw entry, but fired once — at
/// <c>Encounter.MarkDied</c> — rather than spent as an action.
/// </summary>
/// <remarks>
/// The rule these pin, verbatim from SRD 5.2.1 (Magma Mephit): "Death Burst. The mephit
/// explodes when it dies. Dexterity Saving Throw: DC 11, each creature in a 5-foot
/// Emanation originating from the mephit. Failure: 7 (2d6) Fire damage. Success: Half
/// damage." The reading — why the hook sits at <c>MarkDied</c>, why the carrier excludes
/// itself for free, and the cascade (does a burst-killed carrier's own burst fire? yes)
/// — is written on <see cref="DeathBurstEffect"/> and executed in
/// <c>Encounter.FireDeathBurst</c>.
/// <para>
/// Every combatant here is hand-authored, not loaded from content, for the same reason
/// <c>TraitAuraTests</c> gives: an engine test should fail when the engine changes, not
/// when the bestiary is re-extracted. The scripted die is load-bearing throughout — it
/// throws on any unscripted roll, so a passing test also proves nobody rolled a save (or
/// a burst) it should not have.
/// </para>
/// <para>
/// The killing blow in every test is a ranged attack from far outside any burst's
/// 5-foot Emanation, deliberately: a melee attacker would stand within reach of its own
/// kill's blast and become an unplanned victim, muddying "who the burst catches" with
/// "who swung the killing blow".
/// </para>
/// </remarks>
public class DeathBurstTests
{
    [Fact]
    public void AVictimInRangeThatFailsTakesFullFailureDamage()
    {
        // Bow kills the burster outright (1 HP, AC 5); the victim standing 5 ft. away
        // rolls its Dex save (roll 5 + 2 = 7) against DC 10, fails, and takes the full
        // 1d4 (rolled 3) Fire damage.
        var encounter = Fight(
            new ScriptedRandomSource(10, 10, 10, /* inits */ 10, 1, /* attack, damage */ 5, 3 /* save, failure damage */),
            Hero(),
            Burster("burster", x: 0),
            Bystander("victim", x: 1));

        var burster = encounter.Combatants.Single(combatant => combatant.Id == "burster");
        var victim = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        encounter.Attack("Bow", burster);

        Assert.True(burster.IsDead);
        Assert.Equal(37, victim.CurrentHitPoints);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("bursts outward", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Damage
                && step.Narration.Contains("victim takes 3 Fire damage", StringComparison.Ordinal));
    }

    [Fact]
    public void AVictimInRangeThatSucceedsTakesHalfDamage()
    {
        // The victim's save (roll 8 + 2 = 10) meets DC 10 and succeeds; the burst still
        // rolls its 1d4 (rolled 4) — "Success: Half damage" is not "Success: No effect"
        // — and halves it to 2.
        var encounter = Fight(
            new ScriptedRandomSource(10, 10, 10, 10, 1, 8, 4),
            Hero(),
            Burster("burster", x: 0),
            Bystander("victim", x: 1));

        var burster = encounter.Combatants.Single(combatant => combatant.Id == "burster");
        var victim = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        encounter.Attack("Bow", burster);

        Assert.True(burster.IsDead);
        Assert.Equal(38, victim.CurrentHitPoints);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("halved by a successful save", StringComparison.Ordinal));
    }

    [Fact]
    public void AVictimOutsideTheEmanationIsUntouched()
    {
        // The victim stands 15 ft. away (x: 3), beyond the 5-foot emanation. The
        // scripted die carries no save or failure-damage roll at all, so a roll against
        // this victim would overrun it.
        var encounter = Fight(
            new ScriptedRandomSource(10, 10, 10, 10, 1),
            Hero(),
            Burster("burster", x: 0),
            Bystander("victim", x: 3));

        var burster = encounter.Combatants.Single(combatant => combatant.Id == "burster");
        var victim = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        encounter.Attack("Bow", burster);

        Assert.True(burster.IsDead);
        Assert.Equal(40, victim.CurrentHitPoints);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("bursts outward", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("catching 0 creature(s)", StringComparison.Ordinal));
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Narration.Contains("Dexterity saving throw", StringComparison.Ordinal));
    }

    [Fact]
    public void ACreatureWithNoDeathBurstBurstsNothing()
    {
        // The no-op the frozen transcript rests on, pinned directly. The monster carries
        // a saving-throw entry — but it is an ordinary Trait, not a death burst (no
        // DeathBurstEffect signal) — so dying must not fire it. A bystander stands
        // exactly where a burst would have caught it; the scripted die carries no save
        // or failure-damage roll, so a roll against it would overrun the script.
        var ordinary = CombatTestData.Combatant(
            "ordinary",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(armorClass: 5, maximumHitPoints: 1, initiativeBonus: -10, attacks: [])
                with
            {
                Entries =
                [
                    new MonsterEntry(
                        "Noxious Fumes",
                        MonsterEntrySection.Trait,
                        "Noxious Fumes.",
                        Mechanics: EntryMechanics.SavingThrow,
                        Save: BurstSave()),
                ],
            },
            x: 0,
            y: 5);

        var encounter = Fight(
            new ScriptedRandomSource(10, 10, 10, 10, 1),
            Hero(),
            ordinary,
            Bystander("victim", x: 1));

        var victim = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        encounter.Attack("Bow", ordinary);

        Assert.True(ordinary.IsDead);
        Assert.Equal(40, victim.CurrentHitPoints);
        Assert.DoesNotContain(encounter.Log, step => step.Narration.Contains("bursts outward", StringComparison.Ordinal));
    }

    [Fact]
    public void ABurstThatKillsAnotherBursterCascadesExactlyOnceEach()
    {
        // Ordering (#679): does a burst-killed carrier's own burst fire? Yes — the
        // print carries no exception for what killed it. Bow kills burster "a" (1 HP);
        // its burst catches "b" 5 ft. away, who fails, takes 1 damage and (also at 1
        // HP) dies from the burst itself — and "b"'s own Death Burst then fires,
        // catching "c" 5 ft. beyond it (10 ft. from "a", outside "a"'s own emanation).
        // Each burst is logged exactly once: the cascade terminates rather than
        // looping, and neither creature's burst double-fires.
        var encounter = Fight(
            new ScriptedRandomSource(
                10, 10, 10, 10, // four inits: hero, a, b, c
                10, 1, // hero's attack roll, damage roll — kills "a"
                5, 1, // "b"'s save (fails) and "a"'s burst failure damage — kills "b"
                5, 2), // "c"'s save (fails) and "b"'s burst failure damage
            Hero(),
            Burster("a", x: 0),
            Burster("b", x: 1, maximumHitPoints: 1),
            Bystander("c", x: 2));

        var a = encounter.Combatants.Single(combatant => combatant.Id == "a");
        var b = encounter.Combatants.Single(combatant => combatant.Id == "b");
        var c = encounter.Combatants.Single(combatant => combatant.Id == "c");

        encounter.Attack("Bow", a);

        Assert.True(a.IsDead);
        Assert.True(b.IsDead);
        Assert.False(c.IsDead);
        Assert.Equal(38, c.CurrentHitPoints);
        Assert.Equal(
            2,
            encounter.Log.Count(step => step.Narration.Contains("bursts outward", StringComparison.Ordinal)));
    }

    [Fact]
    public void AVictimKilledByANestedBurstIsNotReprocessedByTheOuterBurst()
    {
        // The overlap the previous test's geometry deliberately avoided: "a"'s own
        // 5-foot Emanation catches BOTH "b" and "c" directly (all three mutually
        // within 5 ft. of one another), not just "b". Bow kills "a"; "a"'s burst
        // resolves against its snapshot of victims in order — "b" first, then "c" —
        // and "b" dies from that very failure damage, so "b"'s own Death Burst fires
        // *before* "a"'s outer loop ever reaches "c", killing "c" too. When "a"'s
        // loop then reaches its own "c" entry, "c" is already dead: it must not roll
        // a second save, take a second helping of damage, or narrate a second death.
        // The scripted die carries exactly one save and one failure-damage roll for
        // "c" (both consumed by "b"'s burst) — a second roll against "c" from "a"'s
        // own loop would overrun it.
        var encounter = Fight(
            new ScriptedRandomSource(
                10, 10, 10, 10, // four inits: hero, a, b, c
                10, 1, // hero's attack roll, damage roll — kills "a"
                5, 1, // "b"'s save (fails) and "a"'s burst failure damage — kills "b"
                5, 1), // "c"'s save (fails) and "b"'s burst failure damage — kills "c"
            Hero(),
            Burster("a", x: 0),
            Burster("b", x: 1, maximumHitPoints: 1),
            Bystander("c", x: 1, y: 4, maximumHitPoints: 1));

        var a = encounter.Combatants.Single(combatant => combatant.Id == "a");
        var b = encounter.Combatants.Single(combatant => combatant.Id == "b");
        var c = encounter.Combatants.Single(combatant => combatant.Id == "c");

        encounter.Attack("Bow", a);

        Assert.True(a.IsDead);
        Assert.True(b.IsDead);
        Assert.True(c.IsDead);

        // "c" died exactly once: one Died step, one Damage step, one save roll spent
        // (the scripted die proves the roll count; this proves the log agrees).
        Assert.Equal(1, encounter.Log.Count(step => step.Kind == CombatStepKind.Died && step.ActorId == "c"));
        Assert.Equal(
            1,
            encounter.Log.Count(step => step.Kind == CombatStepKind.Damage && step.TargetId == "c"));
    }

    [Fact]
    public void ARevivedCarrierBurstsAgainOnItsSecondDeath()
    {
        // #679's fire-once guard is per-life, not permanent (Codex review): "the
        // mephit explodes when it dies" carries no "only once, ever" clause, so a
        // carrier that revives and dies a second time must burst a second time.
        // Round 1: the bow kills "burster" and its burst catches "victim". Round 2:
        // the caster (its own attack spent reviving instead) casts a test Revivify on
        // it, clearing the guard. Round 3: the bow kills it again, and its burst must
        // catch "victim" a second time — the scripted die's final save-and-damage
        // pair would overrun if it did not.
        var random = new ScriptedRandomSource(
            10, 10, 10, // three inits: hero, burster, victim
            10, 1, // hero's attack roll, damage roll — kills "burster" the first time
            5, 3, // "victim"'s first save (fails) and first failure damage
            // Revival itself rolls no dice — Test Revivify hands over a fixed 1 hit
            // point (SpellRevival(1)) — but it does not stand the corpse back up:
            // Unconscious brings Prone with it (Combatant's own reading), and only
            // Unconscious itself lifts on revival, so the revived "burster" is still
            // Prone when the bow fires again — Disadvantage at range, two d20s here.
            10, 10, 1, // hero's second attack (Disadvantage), damage roll — kills again
            5, 3); // "victim"'s second save (fails) and second failure damage

        var encounter = Fight(random, HeroWithRevival(x: 2), Burster("burster", x: 6), Bystander("victim", x: 7));

        var burster = encounter.Combatants.Single(combatant => combatant.Id == "burster");
        var victim = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        encounter.Attack("Bow", burster);
        Assert.True(burster.IsDead);

        AdvanceToNextTurnOf(encounter, "hero");

        // Test Revivify is Touch range: close to 5 ft., cast, then retreat back out
        // of the burst's own reach before ending the turn — 15 ft. each way, exactly
        // the hero's 30 ft. Speed — so the hero is not itself caught in the second
        // burst and is not within 5 ft. of any hostile for the second kill (the
        // Prone-at-range Disadvantage below is the only source left, not a second
        // one stacking on top of it).
        Assert.Null(encounter.Move(new GridPosition(5, 5)));
        Assert.Null(encounter.CastSpell("spell.test-revivify", burster));
        Assert.False(burster.IsDead);
        Assert.Null(encounter.Move(new GridPosition(2, 5)));

        AdvanceToNextTurnOf(encounter, "hero");
        encounter.Attack("Bow", burster);
        Assert.True(burster.IsDead);

        Assert.Equal(
            2,
            encounter.Log.Count(step => step.Narration.Contains("bursts outward", StringComparison.Ordinal)));
        Assert.Equal(34, victim.CurrentHitPoints);
    }

    [Fact]
    public void TheSameSeedProducesTheSameDeathBurstSaves()
    {
        // Determinism through IRandomSource: a death burst rolls dice (the trigger
        // itself is not random, but its save and damage are), so two fights from one
        // seed must narrate byte-identically.
        static Encounter Run(int seed)
        {
            var encounter = Fight(new SeededRandomSource(seed), Hero(), Burster("burster", x: 0), Bystander("victim", x: 1));

            // The hero has only one legal action worth taking; a bounded loop attacks
            // whenever it is the hero's turn and otherwise just advances, until the
            // burster is dead or the loop gives up (a natural 1 auto-misses one turn in
            // twenty, so a handful of rounds is generous headroom).
            for (var round = 0; round < 8 && !encounter.Combatants.Single(c => c.Id == "burster").IsDead; round++)
            {
                if (encounter.ActiveCombatant?.Id == "hero")
                {
                    encounter.Attack("Bow", encounter.Combatants.Single(c => c.Id == "burster"));
                }
                else
                {
                    encounter.EndTurn();
                }
            }

            return encounter;
        }

        var first = Run(4242);
        var second = Run(4242);

        Assert.Equal(
            first.Log.Select(step => step.Narration),
            second.Log.Select(step => step.Narration));

        // The burst actually fired in both runs, so the equality above is a fact about
        // death-burst saves rather than a fight in which nothing happened.
        Assert.Contains(first.Log, step => step.Narration.Contains("bursts outward", StringComparison.Ordinal));
    }

    /// <summary>
    /// A Death Burst save: DC 10 Dexterity, 1d4 damage of the given type, halved on a
    /// success (never negated — "Success: Half damage", not "No effect"). The area is
    /// a 5-foot Emanation, the printed shape for every carrier (#679).
    /// </summary>
    private static SaveEffect BurstSave(DamageType type = DamageType.Fire)
    {
        var dice = DiceExpression.Parse("1d4");

        return new(
            Ability.Dexterity,
            10,
            new EffectArea(AreaShape.Emanation, 5),
            [new AttackDamage(dice, type, dice.Average)],
            SaveSuccessOutcome.HalfDamage,
            []);
    }

    private static MonsterEntry DeathBurstEntry() => new(
        "Death Burst",
        MonsterEntrySection.Trait,
        "Death Burst.",
        Mechanics: EntryMechanics.SavingThrow,
        Save: BurstSave(),
        DeathBurst: new DeathBurstEffect());

    /// <summary>A death-burst carrier: 1 hit point, AC 5 — any non-miss kills it outright.</summary>
    private static Combatant Burster(string id, int x, int y = 5, int maximumHitPoints = 1) =>
        CombatTestData.Combatant(
            id,
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                    armorClass: 5,
                    maximumHitPoints: maximumHitPoints,
                    initiativeBonus: -10,
                    attacks: [])
                with
            {
                Entries = [DeathBurstEntry()],
            },
            x: x,
            y: y);

    /// <summary>A bystander within or beyond a burst's reach, on the monster side by default —
    /// Death Burst prints no "enemies only" restriction, so its own side is not spared.</summary>
    private static Combatant Bystander(string id, int x, int y = 5, string sideId = CombatTestData.Monsters, int maximumHitPoints = 40) =>
        CombatTestData.Combatant(
            id,
            sideId: sideId,
            stats: CombatTestData.Stats(maximumHitPoints: maximumHitPoints, initiativeBonus: -10, attacks: []),
            x: x,
            y: y);

    /// <summary>
    /// A hero far outside any 5-foot emanation (20 ft. from the burster's column at
    /// x: 0), armed with a bow so the killing blow never puts the attacker inside its
    /// own target's blast.
    /// </summary>
    private static Combatant Hero(string id = "hero") =>
        CombatTestData.Combatant(
            id,
            sideId: CombatTestData.Heroes,
            stats: CombatTestData.Stats(initiativeBonus: 10, attacks: [CombatTestData.RangedAttack("Bow", bonus: 10)]),
            x: -4,
            y: 5);

    /// <summary>
    /// A hero exactly like <see cref="Hero"/>, plus a test Revivify carried the way
    /// <c>RevivalTests</c>' own caster carries one — for
    /// <see cref="ARevivedCarrierBurstsAgainOnItsSecondDeath"/>, which needs the same
    /// combatant to both strike the killing blow and later revive its target.
    /// </summary>
    private static Combatant HeroWithRevival(string id = "hero", int x = 2)
    {
        var revival = new SpellDefinition
        {
            Id = "spell.test-revivify",
            Name = "Test Revivify",
            Level = 3,
            School = MagicSchool.Necromancy,
            Classes = ["Cleric"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            Components = SpellComponents.Verbal,
            DurationText = "Instantaneous",
            Mechanics = EntryMechanics.Healing,
            SourcePage = 1,
            RangeText = "Touch",
            Text = "A test spell.",
            Revival = new SpellRevival(1),
        };

        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: [CombatTestData.RangedAttack("Bow", bonus: 10)])
            with
        {
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 5,
                Spells: [revival],
                SpellSlots: new Dictionary<int, int> { [3] = 1 },
                SpellcastingAbility: Ability.Wisdom,
                SpellSaveDifficultyClass: 14,
                SpellAttackBonus: 6),
        };

        return CombatTestData.Combatant(id, sideId: CombatTestData.Heroes, stats: stats, x: x, y: 5);
    }

    /// <summary>
    /// Ends turns until <paramref name="id"/> is the acting combatant — for a test that
    /// needs its own actor to come back around across a round where nothing else in the
    /// fight takes an action worth scripting.
    /// </summary>
    private static void AdvanceToNextTurnOf(Encounter encounter, string id)
    {
        do
        {
            encounter.EndTurn();
        }
        while (encounter.ActiveCombatant?.Id != id && !encounter.IsComplete);
    }

    private static Encounter Fight(IRandomSource random, params Combatant[] combatants) =>
        Encounter.Start(new Battlefield(12, 12), combatants, random);
}
