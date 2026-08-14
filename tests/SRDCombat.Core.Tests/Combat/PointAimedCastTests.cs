using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Phase 7 polish: an area spell aimed at a bare square. The engine's point overload
/// always existed — the policy simply never used it without a creature in hand — and
/// wiring the clients to it exposed the rule the creature-aimed path enforced and the
/// point path did not: the printed range.
/// </summary>
public class PointAimedCastTests
{
    [Fact]
    public void APointBeyondTheSpellsRangeIsRefusedBeforeTheSlotIsSpent()
    {
        var (encounter, caster) = Stage();

        var refusal = encounter.CastSpell("spell.burst", new GridPosition(19, 0));

        Assert.Equal("spell.out_of_range", refusal?.Code);
        Assert.Equal(1, caster.Features.SpellSlotsRemaining.GetValueOrDefault(1));
    }

    [Fact]
    public void APointInRangeEruptsAndCatchesWhoStandsThere()
    {
        var (encounter, caster) = Stage();
        var enemy = encounter.Combatants.Single(combatant => combatant.Id == "enemy");

        var refusal = encounter.CastSpell("spell.burst", enemy.Position);

        Assert.Null(refusal);
        Assert.Equal(0, caster.Features.SpellSlotsRemaining.GetValueOrDefault(1));
        Assert.True(enemy.CurrentHitPoints < enemy.Stats.MaximumHitPoints);
    }

    [Fact]
    public void ASpellWithNoAreaStillNeedsACreature()
    {
        var (encounter, _) = Stage();

        var refusal = encounter.CastSpell("spell.bolt", new GridPosition(5, 2));

        Assert.Equal("spell.needs_target", refusal?.Code);
    }

    // ── The stage ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A caster at the west edge with one slot, a 60-foot 10-foot-radius burst and a
    /// single-target attack spell; one enemy eight squares out.
    /// </summary>
    private static (Encounter Encounter, Combatant Caster) Stage()
    {
        var burst = Bare("spell.burst") with
        {
            Mechanics = EntryMechanics.SavingThrow,
            Save = new SaveEffect(
                Ability.Dexterity,
                DifficultyClass: null,
                Area: new EffectArea(AreaShape.Sphere, 10),
                FailureDamage: [new AttackDamage(DiceExpression.Parse("2d6"), DamageType.Fire, 7)],
                SuccessOutcome: SaveSuccessOutcome.HalfDamage,
                AppliedConditions: []),
        };

        var bolt = Bare("spell.bolt") with
        {
            Mechanics = EntryMechanics.Attack,
            IsSpellAttack = true,
            Damage = [new AttackDamage(DiceExpression.Parse("1d10"), DamageType.Fire, 5)],
        };

        var shell = CombatTestData.Character("caster");

        var stats = shell.Stats with
        {
            InitiativeBonus = 10,
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 1,
                Spells: [burst, bolt],
                SpellSlots: new Dictionary<int, int> { [1] = 1 },
                SpellcastingAbility: Ability.Wisdom,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        var caster = new Combatant("caster", "caster", CombatTestData.Heroes, stats, new GridPosition(0, 2));

        var enemy = CombatTestData.Combatant(
            "enemy",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10),
            x: 8,
            y: 2);

        var encounter = Encounter.Start(
            new Battlefield(20, 5),
            [caster, enemy],
            // Initiatives, then the in-range case's save d20 and 2d6.
            new ScriptedRandomSource(15, 1, 10, 3, 3));

        return (encounter, caster);
    }

    private static SpellDefinition Bare(string id) => new()
    {
        Id = id,
        Name = id,
        Level = 1,
        School = MagicSchool.Evocation,
        Classes = ["Wizard"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        Components = SpellComponents.Verbal,
        DurationText = "Instantaneous",
        Mechanics = EntryMechanics.SavingThrow,
        SourcePage = 1,
        RangeText = "60 feet",
        RangeFeet = 60,
        Text = "A test spell.",
    };
}
