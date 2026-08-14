using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Squad AI slice 3 (#124): roles derived from what a character carries, and the lane
/// and screen computations the engagement phases (#125) will stand on.
/// </summary>
/// <remarks>
/// Deliberately infrastructure-only. The behavioral half of #124 — front liners
/// standing in lanes, the back rank holding behind its screen — was built, measured
/// over the recorded seeds, and reverted: every fragment of positional discipline cost
/// pacing in a policy that otherwise always attacks (the full ladder is in CLAUDE.md,
/// from 8 down to 3.5 and back as each term was removed). Holding a position needs a
/// reason to hold, which is what engagement phases are; these computations wait for
/// that trigger.
/// </remarks>
public class RoleAndScreeningTests
{
    [Fact]
    public void RolesComeFromTheKit_WithHealingOutrankingEverything()
    {
        Assert.Equal(PartyRole.FrontLine, PartyDoctrine.RoleOf(Character("sword", melee: true)));
        Assert.Equal(PartyRole.Skirmisher, PartyDoctrine.RoleOf(Character("bow", melee: false)));
        Assert.Equal(PartyRole.Artillery, PartyDoctrine.RoleOf(Character("boom", melee: false, areaSpell: true)));

        // Healing outranks the area spell: the party's one healer is its Support
        // whatever else it carries.
        Assert.Equal(
            PartyRole.Support,
            PartyDoctrine.RoleOf(Character("medic", melee: false, areaSpell: true, healSpell: true)));
    }

    [Fact]
    public void AMonsterHasNoRoleToPlay()
    {
        var monster = CombatTestData.Combatant("beast", sideId: CombatTestData.Monsters);

        Assert.Equal(PartyRole.FrontLine, PartyDoctrine.RoleOf(monster));
        Assert.Null(monster.Stats.Character);
    }

    [Fact]
    public void TheEnemyLaneRunsThroughTheOnlyDoorway()
    {
        // A sealed wall with one gap at (4,2): the enemy's computed road to the archer
        // must pass through it, which is what would make the gap worth standing in.
        var (encounter, fighter) = Stage();

        var lanes = PartyDoctrine.EnemyLanes(encounter, fighter);

        Assert.Contains(new GridPosition(4, 2), lanes);
        Assert.NotEmpty(lanes);
    }

    [Fact]
    public void WithNoBackRankThereAreNoLanes()
    {
        // Two front liners and an enemy: nothing to screen, nothing to compute.
        var fighter = Character("fighter", melee: true, x: 1, y: 2, initiative: 10);
        var second = Character("second", melee: true, x: 0, y: 2, initiative: -5);

        var enemy = CombatTestData.Combatant(
            "enemy",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10),
            x: 8,
            y: 2);

        var encounter = Encounter.Start(
            new Battlefield(10, 5),
            [fighter, second, enemy],
            new ScriptedRandomSource(20, 5, 1));

        Assert.Empty(PartyDoctrine.EnemyLanes(encounter, fighter));
    }

    [Fact]
    public void TheScreenIsTheClosestFrontLinerToAnEnemy()
    {
        var (encounter, fighter) = Stage();

        // Asked by the archer: the screen's depth is the fighter's distance to the
        // enemy. Asked by the fighter itself — the only front liner — there is no
        // screen to be behind.
        var archer = encounter.Combatants.Single(combatant => combatant.Id == "archer");

        Assert.Equal(
            fighter.Position.DistanceFeetTo(
                encounter.Combatants.Single(combatant => combatant.Id == "enemy").Position),
            PartyDoctrine.ScreenDistanceFeet(encounter, archer));

        Assert.Null(PartyDoctrine.ScreenDistanceFeet(encounter, fighter));
    }

    // ── The stage ───────────────────────────────────────────────────────────────

    /// <summary>Fighter and archer west of a sealed wall with one gap; the enemy east.</summary>
    private static (Encounter Encounter, Combatant Fighter) Stage()
    {
        var fighter = Character("fighter", melee: true, x: 1, y: 2, initiative: 10);
        var archer = Character("archer", melee: false, x: 0, y: 2, initiative: -5);

        var enemy = CombatTestData.Combatant(
            "enemy",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(maximumHitPoints: 30, initiativeBonus: -10),
            x: 9,
            y: 2);

        var encounter = Encounter.Start(
            new Battlefield(10, 5, blocked:
            [
                new GridPosition(4, 0),
                new GridPosition(4, 1),
                new GridPosition(4, 3),
                new GridPosition(4, 4),
            ]),
            [fighter, archer, enemy],
            new ScriptedRandomSource(20, 5, 1));

        return (encounter, fighter);
    }

    private static Combatant Character(
        string id,
        bool melee,
        bool areaSpell = false,
        bool healSpell = false,
        int x = 0,
        int y = 0,
        int initiative = 0)
    {
        var spells = new List<SpellDefinition>();

        if (areaSpell)
        {
            spells.Add(Bare($"spell.{id}-boom") with
            {
                Mechanics = EntryMechanics.SavingThrow,
                Save = new SaveEffect(
                    Ability.Dexterity,
                    DifficultyClass: null,
                    Area: new EffectArea(AreaShape.Sphere, 20),
                    FailureDamage: [new AttackDamage(DiceExpression.Parse("8d6"), DamageType.Fire, 28)],
                    SuccessOutcome: SaveSuccessOutcome.HalfDamage,
                    AppliedConditions: []),
            });
        }

        if (healSpell)
        {
            spells.Add(Bare($"spell.{id}-mend") with
            {
                Mechanics = EntryMechanics.Healing,
                Heal = new SpellHeal(DiceExpression.Parse("2d8"), AddsSpellcastingModifier: false),
            });
        }

        var shell = CombatTestData.Character(id);

        var stats = shell.Stats with
        {
            InitiativeBonus = initiative,
            Attacks = melee
                ? [CombatTestData.MeleeAttack(bonus: 4)]
                : [CombatTestData.RangedAttack(bonus: 4)],
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 1,
                Spells: spells,
                SpellSlots: new Dictionary<int, int> { [1] = 1, [3] = 1 },
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
