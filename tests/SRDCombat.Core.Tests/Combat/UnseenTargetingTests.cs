using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Concealed's mechanism (<c>target.unseen</c>, <see cref="Rules.VisionRules"/>, #673),
/// generalized by #691 beyond Divine Spark (<see cref="DivineSparkTests"/>) to every
/// spell and stat-block entry whose printed "you can see" / "the X can see" targeting
/// clause extraction now structures onto <see cref="SpellDefinition.TargetRequiresSight"/>
/// and <see cref="SaveEffect.TargetRequiresSight"/>.
/// </summary>
public class UnseenTargetingTests
{
    // ── CastSpell ────────────────────────────────────────────────────────────────

    [Fact]
    public void AnAttackSpellThatRequiresSightRefusesAnInvisibleTarget()
    {
        var (encounter, target) = SpellFight(AttackSpell(requiresSight: true), scripted: [20, 1]);
        target.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: target.Id, FindDifficultyClass: 15));

        var refusal = encounter.CastSpell("spell.test-ray", target);

        Assert.Equal("target.unseen", refusal?.Code);
    }

    [Fact]
    public void AnAttackSpellThatDoesNotRequireSightIgnoresInvisibility()
    {
        // Regression control: most spells print no "can see" clause at all, and an
        // Invisible target still grants only the printed Attacks Affected
        // Advantage/Disadvantage (#673) rather than an outright refusal. Rolls: two
        // for initiative, two for the attack roll (Disadvantage against an unseen
        // Invisible target), one for damage.
        var (encounter, target) = SpellFight(AttackSpell(requiresSight: false), scripted: [20, 1, 15, 10, 5]);
        target.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: target.Id, FindDifficultyClass: 15));

        Assert.Null(encounter.CastSpell("spell.test-ray", target));
    }

    [Fact]
    public void ASingleTargetSaveSpellThatRequiresSightRefusesAnInvisibleTarget()
    {
        // Sacred Flame's own shape: a save, not an attack — proving the check reaches
        // both branches CastSpell's shared "targets a creature directly" condition
        // covers.
        var (encounter, target) = SpellFight(DexSaveSpell(requiresSight: true), scripted: [20, 1]);
        target.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: target.Id, FindDifficultyClass: 15));

        var refusal = encounter.CastSpell("spell.test-flame", target);

        Assert.Equal("target.unseen", refusal?.Code);
    }

    // ── UseSaveEntry ─────────────────────────────────────────────────────────────

    [Fact]
    public void ASingleTargetSaveEntryThatRequiresSightRefusesAnInvisibleTarget()
    {
        var (encounter, _, target) = EntryFight(sightGatedSaveArea: null, requiresSight: true);
        target.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: target.Id, FindDifficultyClass: 15));

        var refusal = encounter.UseEntry("Dreadful Glare", target);

        Assert.Equal("target.unseen", refusal?.Code);
    }

    [Fact]
    public void ASingleTargetSaveEntryThatDoesNotRequireSightIgnoresInvisibility()
    {
        // Two rolls for initiative, one for the target's saving throw.
        var (encounter, _, target) = EntryFight(
            sightGatedSaveArea: null, requiresSight: false, scripted: [20, 1, 10]);
        target.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: target.Id, FindDifficultyClass: 15));

        Assert.Null(encounter.UseEntry("Dreadful Glare", target));
    }

    [Fact]
    public void AnAreaEntryMerelyAimedViaATargetReferenceIsNeverGatedOnSight()
    {
        // Structural knockout: the check only ever runs from inside the
        // "save.Area is null && target is not null" branch, so an area save aimed via
        // a target reference — Combatant, not just a bare point — is untouched
        // regardless of TargetRequiresSight. Extraction never actually sets this
        // combination (a point-aimed Sphere's own "the dragon can see" never claims,
        // see EntryMechanicsCharacterizationTests), but the engine itself must not
        // depend on that for correctness: pass both the point and the target combatant
        // so the guard the P2 review caught — passing only a point trivially bypasses
        // "target is not null" regardless of whether the Area gate is even checked —
        // cannot silently pass this test again. Two rolls for initiative, then a
        // saving throw for every creature the 20-foot-radius Sphere catches — the
        // actor sits right at its edge, so both actor and target may roll.
        var (encounter, _, target) = EntryFight(
            sightGatedSaveArea: new EffectArea(AreaShape.Sphere, 20),
            requiresSight: true,
            scripted: [20, 1, 10, 10]);
        target.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: target.Id, FindDifficultyClass: 15));

        var refusal = encounter.UseEntry("Dreadful Glare", target.Position, target);

        Assert.Null(refusal);
    }

    // ── The named stall risk (#673's designer reading, restated by #691) ────────────

    [Fact]
    public void ABlindedMonsterWithNoReachingAttackFallsThroughToMovingInsteadOfStalling()
    {
        // The monster has no attacks at all (TryAttack always fails to reach) and its
        // only stat-block entry is the sight-gated save — Blinded closes its own eyes
        // (VisionRules.HasOpenEyes), so target.unseen refuses it every time regardless
        // of the target's own condition. The turn must still end: TryUseLimitedEntry
        // treats the refusal as "did not use it" and falls through to closing the
        // distance, rather than the policy looping or throwing.
        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
        {
            Entries =
            [
                new MonsterEntry("Dreadful Glare", MonsterEntrySection.Action, "...",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: new SaveEffect(
                        Ability.Wisdom,
                        11,
                        null,
                        [],
                        SaveSuccessOutcome.NoEffect,
                        [],
                        RangeFeet: 60,
                        TargetRequiresSight: true)),
            ],
        };

        var monster = CombatTestData.Combatant(
            "monster", sideId: CombatTestData.Monsters, stats: stats, x: 0, y: 0);
        monster.AddCondition(new ActiveCondition(ConditionType.Blinded, SourceId: monster.Id));

        var victim = CombatTestData.Combatant("victim", sideId: CombatTestData.Heroes, x: 5, y: 0);

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [monster, victim],
            new ScriptedRandomSource(20, 1));

        var exception = Record.Exception(() => SimpleTacticsPolicy.TakeTurn(encounter));

        Assert.Null(exception);
        Assert.DoesNotContain(
            encounter.Log,
            step => step.Kind == CombatStepKind.Entry
                && step.Narration.Contains("uses Dreadful Glare", StringComparison.Ordinal));

        // The turn actually ended rather than hanging — the encounter moved on to the
        // victim's turn instead of leaving the Blinded monster stuck retrying forever.
        Assert.Equal(victim.Id, encounter.ActiveCombatant?.Id);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────

    private static SpellDefinition AttackSpell(bool requiresSight) => new()
    {
        Id = "spell.test-ray",
        Name = "Test Ray",
        Level = 1,
        School = MagicSchool.Evocation,
        Classes = ["Wizard"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        Components = SpellComponents.Verbal,
        DurationText = "Instantaneous",
        Mechanics = EntryMechanics.Attack,
        SourcePage = 1,
        RangeText = "60 feet",
        RangeFeet = 60,
        Text = "A test spell.",
        IsSpellAttack = true,
        TargetRequiresSight = requiresSight,
        Damage = [new AttackDamage(DiceExpression.Parse("1d10"), DamageType.Fire, 5)],
    };

    private static SpellDefinition DexSaveSpell(bool requiresSight) => new()
    {
        Id = "spell.test-flame",
        Name = "Test Flame",
        Level = 1,
        School = MagicSchool.Evocation,
        Classes = ["Cleric"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        Components = SpellComponents.Verbal,
        DurationText = "Instantaneous",
        Mechanics = EntryMechanics.SavingThrow,
        SourcePage = 1,
        RangeText = "60 feet",
        RangeFeet = 60,
        Text = "A test spell.",
        TargetRequiresSight = requiresSight,
        Save = new SaveEffect(
            Ability.Dexterity,
            DifficultyClass: 13,
            Area: null,
            FailureDamage: [new AttackDamage(DiceExpression.Parse("2d6"), DamageType.Radiant, 7)],
            SuccessOutcome: SaveSuccessOutcome.HalfDamage,
            AppliedConditions: []),
    };

    /// <summary>A caster at (0,1) and a target at (4,1), clear line, well within range.</summary>
    private static (Encounter Encounter, Combatant Target) SpellFight(
        SpellDefinition spell, IReadOnlyList<int> scripted)
    {
        var shell = CombatTestData.Character("caster");

        var stats = shell.Stats with
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
                Spells: [spell],
                SpellSlots: new Dictionary<int, int> { [1] = 2 },
                SpellcastingAbility: Ability.Wisdom,
                SpellSaveDifficultyClass: 14,
                SpellAttackBonus: 6),
        };

        var caster = new Combatant("caster", "Caster", CombatTestData.Heroes, stats, new GridPosition(0, 1));
        var target = CombatTestData.Combatant(
            "target", sideId: CombatTestData.Monsters, stats: CombatTestData.Stats(initiativeBonus: -10), x: 4, y: 1);

        return (Encounter.Start(new Battlefield(9, 3), [caster, target], new ScriptedRandomSource([.. scripted])), target);
    }

    /// <summary>
    /// An actor at (0,1) with a single-target or point-aimed sight-gated save entry
    /// (Dreadful Glare's own shape, 60 ft. range) and a target at (4,1), clear line.
    /// </summary>
    private static (Encounter Encounter, Combatant Actor, Combatant Target) EntryFight(
        EffectArea? sightGatedSaveArea, bool requiresSight, IReadOnlyList<int>? scripted = null)
    {
        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
        {
            Entries =
            [
                new MonsterEntry("Dreadful Glare", MonsterEntrySection.Action, "...",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: new SaveEffect(
                        Ability.Wisdom,
                        11,
                        sightGatedSaveArea,
                        [],
                        SaveSuccessOutcome.NoEffect,
                        [],
                        RangeFeet: sightGatedSaveArea is null ? 60 : null,
                        TargetRequiresSight: requiresSight)),
            ],
        };

        var actor = CombatTestData.Combatant("actor", sideId: CombatTestData.Monsters, stats: stats, x: 0, y: 1);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Heroes, x: 4, y: 1);

        var encounter = Encounter.Start(
            new Battlefield(9, 3), [actor, target], new ScriptedRandomSource([.. scripted ?? [20, 1]]));

        return (encounter, actor, target);
    }
}
