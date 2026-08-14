using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The two printed-sentence wires from the spellbook widening: an attack spell's rider
/// rides its hit the way a bite's does, and Shatter's Construct clause reads the
/// target's type.
/// </summary>
public class SpellRiderTests
{
    [Fact]
    public void AnAttackSpellsRiderLandsOnTheHit()
    {
        // Ray of Sickness's shape: on a hit, damage and Poisoned until the end of the
        // caster's next turn. Scripted: initiative 20/1, attack d20 15 (+6 = 21, hit),
        // 2d8 damage (3, 3). The rider consumes no die.
        var (encounter, caster, target) = SpellFight(
            RiderSpell(),
            new ScriptedRandomSource(20, 1, 15, 3, 3));

        Assert.Null(encounter.CastSpell("spell.test-ray", target));
        Assert.True(target.HasCondition(ConditionType.Poisoned));

        // "Until the end of your next turn": the caster's next end-of-turn, not the
        // target's. End the caster's current turn, play the target's whole turn, and
        // the poison must still hold; the end of the caster's next turn lifts it.
        encounter.EndTurn();
        Assert.True(target.HasCondition(ConditionType.Poisoned));

        encounter.EndTurn();
        Assert.True(target.HasCondition(ConditionType.Poisoned));

        encounter.EndTurn();
        Assert.False(target.HasCondition(ConditionType.Poisoned));

        Assert.NotNull(caster);
    }

    [Fact]
    public void AMissImposesNothing()
    {
        var (encounter, _, target) = SpellFight(
            RiderSpell(),
            new ScriptedRandomSource(20, 1, 2));

        Assert.Null(encounter.CastSpell("spell.test-ray", target));
        Assert.False(target.HasCondition(ConditionType.Poisoned));
    }

    [Fact]
    public void AConstructSavesAtDisadvantage()
    {
        // Shatter's sentence. Disadvantage consumes two d20s and keeps the lower:
        // 18 then 4 → 4 + 2 = 6 against DC 14 fails; without the clause the 18 would
        // have passed. The two damage dice follow.
        var (encounter, _, construct) = SpellFight(
            ConstructSaveSpell(),
            new ScriptedRandomSource(20, 1, 18, 4, 3, 3),
            targetType: CreatureType.Construct);

        Assert.Null(encounter.CastSpell("spell.test-shatter", construct));

        var save = encounter.Log.Last(step => step.Narration.Contains("saving throw"));
        Assert.Contains("failure", save.Narration);
    }

    [Fact]
    public void AnyoneElseSavesNormally()
    {
        // The same spell against a Humanoid: one d20, and the 18 passes.
        var (encounter, _, target) = SpellFight(
            ConstructSaveSpell(),
            new ScriptedRandomSource(20, 1, 18, 3, 3));

        Assert.Null(encounter.CastSpell("spell.test-shatter", target));

        var save = encounter.Log.Last(step => step.Narration.Contains("saving throw"));
        Assert.Contains("success", save.Narration);
    }

    // ── Builders ────────────────────────────────────────────────────────────────

    private static SpellDefinition RiderSpell() => Bare("spell.test-ray", "Test Ray") with
    {
        Mechanics = EntryMechanics.Attack,
        IsSpellAttack = true,
        Damage = [new AttackDamage(DiceExpression.Parse("2d8"), DamageType.Poison, 9)],
        AppliedConditions =
        [
            new AppliedCondition(
                ConditionType.Poisoned,
                Duration: new ConditionDuration(ConditionClock.EndOfTurn, ConditionDurationOwner.Source)),
        ],
    };

    private static SpellDefinition ConstructSaveSpell() => Bare("spell.test-shatter", "Test Shatter") with
    {
        Mechanics = EntryMechanics.SavingThrow,
        Save = new SaveEffect(
            Ability.Constitution,
            DifficultyClass: null,
            Area: null,
            FailureDamage: [new AttackDamage(DiceExpression.Parse("2d8"), DamageType.Thunder, 9)],
            SuccessOutcome: SaveSuccessOutcome.HalfDamage,
            AppliedConditions: [],
            ConstructsSaveAtDisadvantage: true),
    };

    private static SpellDefinition Bare(string id, string name) => new()
    {
        Id = id,
        Name = name,
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
    };

    private static (Encounter Encounter, Combatant Caster, Combatant Target) SpellFight(
        SpellDefinition spell,
        ScriptedRandomSource dice,
        CreatureType targetType = CreatureType.Humanoid)
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
                SpellcastingAbility: Ability.Intelligence,
                SpellSaveDifficultyClass: 14,
                SpellAttackBonus: 6),
        };

        var caster = new Combatant("caster", "Caster", CombatTestData.Heroes, stats, new GridPosition(0, 1));

        var target = CombatTestData.Combatant(
            "target",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10, maximumHitPoints: 40) with { Type = targetType },
            x: 4,
            y: 1);

        return (Encounter.Start(new Battlefield(9, 3), [caster, target], dice), caster, target);
    }
}
