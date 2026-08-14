using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Squad AI slice 4 (#125): the engagement phase and the standoff arithmetic it rests
/// on. Infrastructure, like #124's lanes — the movement policy does not consult it,
/// because every wiring measured cost pacing or never fired; the measured ladder is on
/// <see cref="PartyDoctrine.Phase"/>'s remarks.
/// </summary>
public class EngagementPhaseTests
{
    [Fact]
    public void AMonsterAlwaysCommits()
    {
        var monster = CombatTestData.Combatant("beast", sideId: CombatTestData.Monsters, x: 12, y: 2);
        var hero = Character("hero", ranged: false, x: 0, y: 2, initiative: 10);

        var encounter = Encounter.Start(
            new Battlefield(20, 5),
            [hero, monster],
            new ScriptedRandomSource(20, 1));

        Assert.Equal(EngagementPhase.Commit, PartyDoctrine.Phase(encounter, monster));
    }

    [Fact]
    public void ARangedHeavyPartyHoldsWhileTheEnemyIsFarAndOutRanged()
    {
        // One front liner (6.5 idled) and two archers (11 ranged margin) against a
        // melee-only enemy twelve squares out: the margin covers the idled output and
        // nothing can make contact next turn, so holding wins the exchange.
        var encounter = Stage(archers: 2, fighters: 1, enemyRanged: false, enemyX: 12);

        Assert.Equal(
            EngagementPhase.Hold,
            PartyDoctrine.Phase(encounter, encounter.Combatants.First(c => c.Id == "archer0")));
    }

    [Fact]
    public void AMeleeHeavyPartyCommitsBecauseHoldingWastesItsOutput()
    {
        // Two front liners (13 idled) and one archer (5.5 margin): the margin can
        // never cover what the front line is not swinging, whatever the enemy does.
        var encounter = Stage(archers: 1, fighters: 2, enemyRanged: false, enemyX: 12);

        Assert.Equal(
            EngagementPhase.Commit,
            PartyDoctrine.Phase(encounter, encounter.Combatants.First(c => c.Id == "archer0")));
    }

    [Fact]
    public void AnEnemyRangedAnswerErasesTheMargin()
    {
        // The same ranged-heavy party, but the enemy shoots back for 5.5: the margin
        // drops to 5.5 against 6.5 idled, and the standoff no longer pays.
        var encounter = Stage(archers: 2, fighters: 1, enemyRanged: true, enemyX: 12);

        Assert.Equal(
            EngagementPhase.Commit,
            PartyDoctrine.Phase(encounter, encounter.Combatants.First(c => c.Id == "archer0")));
    }

    [Fact]
    public void ContactOneMoveAwayCommits()
    {
        // The same ranged-heavy party, but the enemy stands seven squares from the
        // fighter: one move and a reach from contact, so there is no free round left
        // to hold through.
        var encounter = Stage(archers: 2, fighters: 1, enemyRanged: false, enemyX: 7);

        Assert.Equal(
            EngagementPhase.Commit,
            PartyDoctrine.Phase(encounter, encounter.Combatants.First(c => c.Id == "archer0")));
    }

    [Fact]
    public void RangedThreatCountsTheBowNotTheSword()
    {
        Assert.Equal(0, PartyDoctrine.RangedThreatPerRound(Character("blade", ranged: false)));

        // 1d6 + 2 at the SRD's own printed average: 3 + 2.
        Assert.Equal(5, PartyDoctrine.RangedThreatPerRound(Character("bow", ranged: true)));
    }

    [Fact]
    public void RangedThreatTakesTheCantripWhenItOutshootsTheWeapon()
    {
        // Sacred Flame's shape: no weapon worth firing, a d8 cantrip at 60 feet.
        var caster = Character("caster", ranged: false, cantripDice: "2d8");

        Assert.Equal(9, PartyDoctrine.RangedThreatPerRound(caster));
    }

    // ── The stage ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A party column at the west edge — fighters then archers — and one enemy east
    /// of it, melee-only or shooting back, at the given column.
    /// </summary>
    private static Encounter Stage(int archers, int fighters, bool enemyRanged, int enemyX)
    {
        var combatants = new List<Combatant>();
        var row = 0;

        for (var i = 0; i < fighters; i++)
        {
            combatants.Add(Character($"fighter{i}", ranged: false, x: 1, y: row++, initiative: 10));
        }

        for (var i = 0; i < archers; i++)
        {
            combatants.Add(Character($"archer{i}", ranged: true, x: 0, y: row++, initiative: 5));
        }

        combatants.Add(CombatTestData.Combatant(
            "enemy",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(
                initiativeBonus: -10,
                attacks: enemyRanged
                    ? [CombatTestData.MeleeAttack(), CombatTestData.RangedAttack()]
                    : [CombatTestData.MeleeAttack()]),
            x: enemyX,
            y: 2));

        return Encounter.Start(
            new Battlefield(20, 5),
            combatants,
            new ScriptedRandomSource(20, 15, 10, 5, 1));
    }

    private static Combatant Character(
        string id,
        bool ranged,
        string? cantripDice = null,
        int x = 0,
        int y = 0,
        int initiative = 0)
    {
        var spells = new List<SpellDefinition>();

        if (cantripDice is not null)
        {
            var dice = DiceExpression.Parse(cantripDice);

            spells.Add(new SpellDefinition
            {
                Id = $"spell.{id}-flame",
                Name = $"{id} flame",
                Level = 0,
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
                Text = "A test cantrip.",
                Save = new SaveEffect(
                    Ability.Dexterity,
                    DifficultyClass: null,
                    Area: null,
                    FailureDamage: [new AttackDamage(dice, DamageType.Radiant, (int)dice.Average)],
                    SuccessOutcome: SaveSuccessOutcome.NoEffect,
                    AppliedConditions: []),
            });
        }

        var shell = CombatTestData.Character(id);

        var stats = shell.Stats with
        {
            InitiativeBonus = initiative,
            Attacks = ranged
                ? [CombatTestData.RangedAttack()]
                : [CombatTestData.MeleeAttack()],
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
                SpellSlots: new Dictionary<int, int>(),
                SpellcastingAbility: spells.Count > 0 ? Ability.Wisdom : null,
                SpellSaveDifficultyClass: 13,
                SpellAttackBonus: 5),
        };

        return new Combatant(id, id, CombatTestData.Heroes, stats, new GridPosition(x, y));
    }
}
