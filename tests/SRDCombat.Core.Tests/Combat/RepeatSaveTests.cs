using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The Hold Person shape: a save spell whose failure imposes a condition "for the
/// duration", escapable by repeating the save at the end of each of the bearer's turns,
/// swept away the moment the caster's Concentration ends, and gated on the printed
/// target type. Every test builds the spell by hand, so the engine's half stands
/// whatever the extractor does.
/// </summary>
public class RepeatSaveTests
{
    [Fact]
    public void AFailedSaveHoldsTheTarget()
    {
        var (encounter, _, victim) = Stage(victimSaveRoll: 2);

        Assert.Null(encounter.CastSpell("spell.hold", victim));
        Assert.True(victim.HasCondition(ConditionType.Paralyzed));
        Assert.True(victim.HasCondition(ConditionType.Incapacitated));
    }

    [Fact]
    public void TheBearerRepeatsTheSaveAtItsTurnEndAndShakesFree()
    {
        // Cast fails the save (2); the victim's own turn ends and the repeat rolls a
        // 19 — free. The repeated save is Wisdom, which Paralyzed does not auto-fail.
        var (encounter, _, victim) = Stage(victimSaveRoll: 2, repeatRolls: [19]);

        Assert.Null(encounter.CastSpell("spell.hold", victim));

        // One EndTurn is a whole round here: the held victim's turn is a skip, and
        // the skip is exactly where the repeat save rolls.
        encounter.EndTurn();

        Assert.False(victim.HasCondition(ConditionType.Paralyzed));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("repeats the Wisdom saving throw", StringComparison.Ordinal)
                && step.Narration.Contains("success", StringComparison.Ordinal));
    }

    [Fact]
    public void AFailedRepeatKeepsHolding()
    {
        var (encounter, _, victim) = Stage(victimSaveRoll: 2, repeatRolls: [3]);

        Assert.Null(encounter.CastSpell("spell.hold", victim));

        // One EndTurn carries the round through the held victim's skipped turn, where
        // the repeat save rolls its scripted 3 and fails.
        encounter.EndTurn();

        Assert.True(victim.HasCondition(ConditionType.Paralyzed));
    }

    [Fact]
    public void BrokenConcentrationReleasesTheHold()
    {
        // A bruiser stands beside the caster: after the hold lands, its sword forces
        // the Constitution save, scripted to fail — the Concentration goes, and the
        // Paralyzed goes with it, no expiry waited for.
        var (encounter, _, victim) = Stage(
            victimSaveRoll: 2,
            withBruiser: true,
            repeatRolls: [15, 5, 1]);

        Assert.Null(encounter.CastSpell("spell.hold", victim));
        Assert.True(victim.HasCondition(ConditionType.Paralyzed));

        encounter.EndTurn(); // The caster's turn ends; the bruiser's begins.
        Assert.Null(encounter.Attack("Sword", encounter.Combatants.Single(c => c.Id == "caster")));

        Assert.False(victim.HasCondition(ConditionType.Paralyzed));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("the spell holding it has ended", StringComparison.Ordinal));
    }

    [Fact]
    public void ARepeatSaveTheBearerAutoFailsConsumesNoDie()
    {
        // A hand-built variant whose repeat is a Strength save: Paralyzed prints "you
        // automatically fail Strength and Dexterity saving throws", so the way out
        // never opens and no die is rolled — the script would throw if one were.
        var (encounter, _, victim) = Stage(victimSaveRoll: 2, spellId: "spell.clutch");

        Assert.Null(encounter.CastSpell("spell.clutch", victim));
        encounter.EndTurn();

        Assert.True(victim.HasCondition(ConditionType.Paralyzed));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("automatically fails the Strength saving throw", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePrintedTargetTypeIsAGate()
    {
        var (encounter, caster, _) = Stage(victimSaveRoll: 2, victimType: CreatureType.Beast);
        var beast = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        var refusal = encounter.CastSpell("spell.hold", beast);

        Assert.Equal("spell.wrong_target_type", refusal?.Code);
        Assert.Equal(1, caster.Features.SpellSlotsRemaining.GetValueOrDefault(2));
        Assert.True(caster.Turn.HasAction);
    }

    // ── The stage ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A caster with a hand-built Hold Person (and a Strength-save variant), and one
    /// enemy of the given type. The caster wins initiative; dice beyond initiative are
    /// the victim's first save, then whatever the test scripts next.
    /// </summary>
    private static (Encounter Encounter, Combatant Caster, Combatant Victim) Stage(
        int victimSaveRoll,
        int[]? repeatRolls = null,
        bool withBruiser = false,
        CreatureType victimType = CreatureType.Humanoid,
        string spellId = "spell.hold")
    {
        var hold = Spell("spell.hold", Ability.Wisdom);
        var clutch = Spell("spell.clutch", Ability.Strength);

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
                Level: 3,
                Spells: [hold, clutch],
                SpellSlots: new Dictionary<int, int> { [2] = 1 },
                SpellcastingAbility: Ability.Wisdom,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        var caster = new Combatant("caster", "caster", CombatTestData.Heroes, stats, new GridPosition(0, 2));

        var victim = CombatTestData.Combatant(
            "victim",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10) with { Type = victimType },
            x: 4,
            y: 2);

        var combatants = new List<Combatant> { caster, victim };
        var rolls = new List<int> { 15, 1 };

        if (withBruiser)
        {
            combatants.Add(CombatTestData.Combatant(
                "bruiser",
                sideId: CombatTestData.Monsters,
                stats: CombatTestData.Stats(initiativeBonus: 2),
                x: 1,
                y: 2));
            rolls.Add(10);
        }

        rolls.Add(victimSaveRoll);
        rolls.AddRange(repeatRolls ?? []);

        var encounter = Encounter.Start(
            new Battlefield(10, 5),
            combatants,
            new ScriptedRandomSource([.. rolls]));

        return (encounter, caster, victim);
    }

    /// <summary>The Hold Person shape with the repeat save on the given ability.</summary>
    private static SpellDefinition Spell(string id, Ability save) => new()
    {
        Id = id,
        Name = id,
        Level = 2,
        School = MagicSchool.Enchantment,
        Classes = ["Cleric", "Wizard"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        Components = SpellComponents.Verbal,
        DurationText = "Concentration, up to 1 minute",
        RequiresConcentration = true,
        Mechanics = EntryMechanics.SavingThrow,
        SourcePage = 1,
        RangeText = "60 feet",
        RangeFeet = 60,
        Text = "A test hold.",
        TargetCreatureType = CreatureType.Humanoid,
        Save = new SaveEffect(
            save,
            DifficultyClass: null,
            Area: null,
            FailureDamage: [],
            SuccessOutcome: SaveSuccessOutcome.NoEffect,
            AppliedConditions:
            [
                new AppliedCondition(
                    ConditionType.Paralyzed,
                    Duration: ConditionDuration.ConcentrationUpToOneMinuteWithRepeatSave),
            ]),
    };
}
