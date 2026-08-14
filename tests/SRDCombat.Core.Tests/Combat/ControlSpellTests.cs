using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The policy prices hard crowd control (#145): a hold is worth the target's threat
/// per round for the rounds it plausibly lasts, discounted by the save — competing
/// against the swing under the same slot margin as every damaging spell, with the
/// cautious healer's reserve still outranking it.
/// </summary>
public class ControlSpellTests
{
    [Fact]
    public void TheBigThreatIsWorthTheSlot()
    {
        // A 21-threat brute: 21 × 2 held rounds × 0.6 fail chance ≈ 25, far past the
        // mace's 9.75 bar. The caster holds it rather than swinging at it.
        var (encounter, caster, enemy) = Stage(enemyDamage: "3d10 + 5", extraRolls: [2, 3]);

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(0, caster.Features.SpellSlotsRemaining.GetValueOrDefault(2));
        Assert.True(enemy.HasCondition(ConditionType.Paralyzed));
    }

    [Fact]
    public void TheWeakOneGetsTheMaceInstead()
    {
        // A 2-threat nuisance prices out at ~2.4: the slot stays in its pocket and
        // the swing lands.
        var (encounter, caster, enemy) = Stage(enemyDamage: "1d4", extraRolls: [15, 4]);

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(1, caster.Features.SpellSlotsRemaining.GetValueOrDefault(2));
        Assert.False(enemy.HasCondition(ConditionType.Paralyzed));
        Assert.True(enemy.CurrentHitPoints < enemy.Stats.MaximumHitPoints);
    }

    [Fact]
    public void APrintedTargetTypeIsNeverArguedWith()
    {
        // The same brute as a Beast: Hold Person names Humanoids, the policy never
        // aims it elsewhere, and the turn is an ordinary swing.
        var (encounter, caster, enemy) = Stage(
            enemyDamage: "3d10 + 5",
            enemyType: CreatureType.Beast,
            extraRolls: [15, 4]);

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(1, caster.Features.SpellSlotsRemaining.GetValueOrDefault(2));
        Assert.False(enemy.HasCondition(ConditionType.Paralyzed));
    }

    [Fact]
    public void TheCautiousHealerStillOutranksTheHold()
    {
        // An ally at 5 of 20 hit points: the slots are spoken for, the hold is a
        // slotted spell, and the measured lesson stands — a slot spent while somebody
        // bleeds is gone when they drop.
        var (encounter, caster, enemy) = Stage(
            enemyDamage: "3d10 + 5",
            withHurtAlly: true,
            extraRolls: [15, 4]);

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(1, caster.Features.SpellSlotsRemaining.GetValueOrDefault(2));
        Assert.False(enemy.HasCondition(ConditionType.Paralyzed));
    }

    // ── The stage ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A caster with a hand-built Hold Person, a heal to make it a healer, and a
    /// mace; one adjacent enemy whose damage line sets its threat. Extra rolls feed
    /// whatever the turn consumes after initiative.
    /// </summary>
    private static (Encounter Encounter, Combatant Caster, Combatant Enemy) Stage(
        string enemyDamage,
        CreatureType enemyType = CreatureType.Humanoid,
        bool withHurtAlly = false,
        int[]? extraRolls = null)
    {
        var hold = new SpellDefinition
        {
            Id = "spell.hold",
            Name = "hold",
            Level = 2,
            School = MagicSchool.Enchantment,
            Classes = ["Cleric"],
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
                Ability.Wisdom,
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

        var mend = new SpellDefinition
        {
            Id = "spell.mend",
            Name = "mend",
            Level = 1,
            School = MagicSchool.Evocation,
            Classes = ["Cleric"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "Action",
            Components = SpellComponents.Verbal,
            DurationText = "Instantaneous",
            Mechanics = EntryMechanics.Healing,
            SourcePage = 1,
            RangeText = "Touch",
            RangeFeet = null,
            Text = "A test heal.",
            Heal = new SpellHeal(DiceExpression.Parse("2d8"), AddsSpellcastingModifier: false),
        };

        var shell = CombatTestData.Character("caster");

        var stats = shell.Stats with
        {
            InitiativeBonus = 10,
            Attacks = [CombatTestData.MeleeAttack("Mace", damage: "1d6 + 2", type: DamageType.Bludgeoning)],
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 3,
                Spells: [hold, mend],
                SpellSlots: new Dictionary<int, int> { [2] = 1 },
                SpellcastingAbility: Ability.Wisdom,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        var caster = new Combatant("caster", "caster", CombatTestData.Heroes, stats, new GridPosition(1, 2));

        var enemy = CombatTestData.Combatant(
            "enemy",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                maximumHitPoints: 30,
                initiativeBonus: -10,
                attacks: [CombatTestData.MeleeAttack("Slam", damage: enemyDamage)]) with
            { Type = enemyType },
            x: 2,
            y: 2);

        var combatants = new List<Combatant> { caster, enemy };
        var rolls = new List<int> { 15, 1 };

        if (withHurtAlly)
        {
            var ally = CombatTestData.Character("ally", x: 0, y: 2);
            combatants.Add(ally);
            rolls.Insert(1, 10);
            DamageRules.Apply(ally, 15, DamageType.Slashing);
        }

        // Whatever the turn consumes beyond initiative: the casting case's failed
        // save and the held enemy's first repeat roll, or a swing's d20 and damage.
        rolls.AddRange(extraRolls ?? []);

        var encounter = Encounter.Start(
            new Battlefield(10, 5),
            combatants,
            new ScriptedRandomSource([.. rolls]));

        return (encounter, caster, enemy);
    }
}
