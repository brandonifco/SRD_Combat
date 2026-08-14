using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Squad AI slice 5 (#126): the party knows its own shape. A side with no healer left
/// spends its own remedies earlier, and an AoE-rich caster waits for a clump instead
/// of spending the slot on one goblin.
/// </summary>
public class CompositionDoctrineTests
{
    [Fact]
    public void AHealerIsSomebodyWhoCanStillCastAHeal()
    {
        var healer = Character("healer", healSpell: true, slots: 1);
        var fighter = Character("fighter", x: 0, y: 1);
        var enemy = Enemy(x: 10, y: 2);

        var encounter = Encounter.Start(
            new Battlefield(12, 5),
            [healer, fighter, enemy],
            new ScriptedRandomSource(10, 5, 1));

        Assert.True(PartyDoctrine.HasHealer(encounter, fighter));
    }

    [Fact]
    public void AHealerWithDrySlotsIsNobodysSafetyNet()
    {
        var healer = Character("healer", healSpell: true, slots: 0);
        var fighter = Character("fighter", x: 0, y: 1);
        var enemy = Enemy(x: 10, y: 2);

        var encounter = Encounter.Start(
            new Battlefield(12, 5),
            [healer, fighter, enemy],
            new ScriptedRandomSource(10, 5, 1));

        Assert.False(PartyDoctrine.HasHealer(encounter, fighter));
    }

    [Fact]
    public void WithAHealerStandingAHalfHurtFighterSavesItsSecondWind()
    {
        // 12 of 20 hit points: below the no-healer bar (a third gone) and above the
        // with-healer bar (half gone). The healer's presence is the whole difference.
        var (encounter, fighter) = FighterScenario(withHealer: true);

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(1, fighter.Features.SecondWindRemaining);
    }

    [Fact]
    public void WithNoHealerTheSameFighterSpendsItEarlier()
    {
        var (encounter, fighter) = FighterScenario(withHealer: false);

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(0, fighter.Features.SecondWindRemaining);
    }

    [Fact]
    public void AnAreaSlotIsHeldWhileTheEnemiesAreSpread()
    {
        // Two enemies twenty feet apart and a 10-foot Sphere: whichever is aimed at,
        // the other escapes, and the young fight holds the slot for a better clump.
        var (encounter, caster) = CasterScenario(enemyRows: (0, 4));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(1, caster.Features.SpellSlotsRemaining.GetValueOrDefault(1));
    }

    [Fact]
    public void AnAreaSlotIsSpentTheMomentTheClumpForms()
    {
        // The same fight with the enemies ten feet apart: one aim catches both.
        var (encounter, caster) = CasterScenario(enemyRows: (1, 3));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(0, caster.Features.SpellSlotsRemaining.GetValueOrDefault(1));
    }

    [Fact]
    public void TheLastEnemyStandingIsAsClumpedAsItWillEverGet()
    {
        var caster = Character("caster", areaSpell: true, slots: 1, initiative: 10);
        var enemy = Enemy(x: 10, y: 2);

        var encounter = Encounter.Start(
            new Battlefield(12, 5),
            [caster, enemy],
            new ScriptedRandomSource(15, 1, 10, 3, 3));

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(0, caster.Features.SpellSlotsRemaining.GetValueOrDefault(1));
    }

    [Fact]
    public void PatienceExpiresWithTheClumpThatNeverCame()
    {
        // The spread pair again, but the fight is no longer young: after round three
        // the value rule alone decides, and the slot is spent on the one it catches.
        var (encounter, caster) = CasterScenario(enemyRows: (0, 4));

        while (encounter.Round <= 3)
        {
            encounter.EndTurn();
        }

        while (encounter.ActiveCombatant?.Id != "caster")
        {
            encounter.EndTurn();
        }

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.Equal(0, caster.Features.SpellSlotsRemaining.GetValueOrDefault(1));
    }

    // ── The stages ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A fighter at 12 of 20 hit points with one Second Wind, an enemy far enough
    /// that its turn spends nothing else, and an ally who is or is not a healer.
    /// </summary>
    private static (Encounter Encounter, Combatant Fighter) FighterScenario(bool withHealer)
    {
        var fighter = Character("fighter", secondWind: true, initiative: 10);
        var ally = Character("ally", healSpell: withHealer, slots: withHealer ? 1 : 0, x: 0, y: 1);
        var enemy = Enemy(x: 11, y: 2);

        var encounter = Encounter.Start(
            new Battlefield(12, 5),
            [fighter, ally, enemy],
            // Initiatives, then the no-healer case's Second Wind d10.
            new ScriptedRandomSource(15, 5, 1, 6));

        DamageRules.Apply(fighter, 8, DamageType.Slashing);

        return (encounter, fighter);
    }

    /// <summary>
    /// A caster with one level 1 slot behind a 10-foot-radius area spell, and two
    /// enemies on the far column at the given rows.
    /// </summary>
    private static (Encounter Encounter, Combatant Caster) CasterScenario((int First, int Second) enemyRows)
    {
        var caster = Character("caster", areaSpell: true, slots: 1, initiative: 10);
        var first = Enemy(x: 10, y: enemyRows.First, id: "enemy-a");
        var second = Enemy(x: 10, y: enemyRows.Second, id: "enemy-b");

        var encounter = Encounter.Start(
            new Battlefield(12, 5),
            [caster, first, second],
            // Initiatives; then, when the cast happens, a save d20 and its 2d6 per
            // enemy caught, in that order.
            new ScriptedRandomSource(15, 3, 1, 10, 3, 3, 10, 3, 3));

        return (encounter, caster);
    }

    private static Combatant Enemy(int x, int y, string id = "enemy") =>
        CombatTestData.Combatant(
            id,
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10),
            x: x,
            y: y);

    private static Combatant Character(
        string id,
        bool healSpell = false,
        bool areaSpell = false,
        bool secondWind = false,
        int slots = 0,
        int x = 0,
        int y = 0,
        int initiative = 0)
    {
        var spells = new List<SpellDefinition>();

        if (healSpell)
        {
            spells.Add(Bare($"spell.{id}-mend") with
            {
                Mechanics = EntryMechanics.Healing,
                Heal = new SpellHeal(DiceExpression.Parse("2d8"), AddsSpellcastingModifier: false),
            });
        }

        if (areaSpell)
        {
            spells.Add(Bare($"spell.{id}-burst") with
            {
                Mechanics = EntryMechanics.SavingThrow,
                Save = new SaveEffect(
                    Ability.Dexterity,
                    DifficultyClass: null,
                    Area: new EffectArea(AreaShape.Sphere, 10),
                    FailureDamage: [new AttackDamage(DiceExpression.Parse("2d6"), DamageType.Fire, 7)],
                    SuccessOutcome: SaveSuccessOutcome.HalfDamage,
                    AppliedConditions: []),
            });
        }

        var shell = CombatTestData.Character(id);

        var stats = shell.Stats with
        {
            InitiativeBonus = initiative,
            Attacks = [CombatTestData.MeleeAttack()],
            Character = new CombatantFeatures(
                secondWind ? [ClassFeature.SecondWind] : [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: secondWind ? 1 : 0,
                ActionSurgeUses: 0,
                Level: 1,
                Spells: spells,
                SpellSlots: new Dictionary<int, int> { [1] = slots },
                SpellcastingAbility: spells.Count > 0 ? Ability.Wisdom : null,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        return new Combatant(id, id, CombatTestData.Heroes, stats, new GridPosition(x, y));
    }

    private static SpellDefinition Bare(string id) => new()
    {
        Id = id,
        Name = id,
        Level = 1,
        School = MagicSchool.Evocation,
        Classes = ["Cleric"],
        CastingTime = SpellCastingTime.Action,
        CastingTimeText = "Action",
        Components = SpellComponents.Verbal,
        DurationText = "Instantaneous",
        Mechanics = EntryMechanics.Healing,
        SourcePage = 1,
        RangeText = "60 feet",
        RangeFeet = 60,
        Text = "A test spell.",
    };
}
