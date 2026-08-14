using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Revivify (#119): "You touch a creature that has died within the last minute. That
/// creature revives with 1 Hit Point." — the window, the transition, and the refusals
/// that fire before the slot is spent.
/// </summary>
public class RevivalTests
{
    [Fact]
    public void TheDeadComeBackWithOneHitPoint()
    {
        var (encounter, caster, ally, _) = Fight();

        KillAllyOnEnemyTurn(encounter);
        Assert.True(ally.IsDead);
        Assert.Equal(1, ally.DiedInRound);

        // The caster's turn: the touch lands on the adjacent corpse.
        Assert.Null(encounter.CastSpell("spell.test-revivify", ally));

        Assert.False(ally.IsDead);
        Assert.Equal(1, ally.CurrentHitPoints);
        Assert.Null(ally.DiedInRound);
        Assert.False(ally.HasCondition(ConditionType.Unconscious));
        Assert.Equal(0, caster.Features.SpellSlotsRemaining[3]);
        Assert.Contains(encounter.Log, step => step.Narration.Contains("back from the dead"));
    }

    [Fact]
    public void TheLivingAreRefused()
    {
        var (encounter, _, ally, _) = Fight();

        // Nobody dies; the enemy's and ally's turns pass to reach the caster's.
        encounter.EndTurn();
        encounter.EndTurn();
        var refusal = encounter.CastSpell("spell.test-revivify", ally);

        Assert.Equal("spell.target_not_dead", refusal?.Code);
    }

    [Fact]
    public void TheWindowClosesAfterTenRounds()
    {
        var (encounter, caster, ally, _) = Fight();

        KillAllyOnEnemyTurn(encounter);

        // Spin the fight forward until more than ten rounds separate the death from
        // the cast, then on to the caster's own turn. Nothing else acts: turns pass.
        while (encounter.Round <= 11 || encounter.ActiveCombatant?.Id != "caster")
        {
            encounter.EndTurn();
        }

        var refusal = encounter.CastSpell("spell.test-revivify", ally);

        Assert.Equal("spell.dead_too_long", refusal?.Code);
        Assert.Equal(1, caster.Features.SpellSlotsRemaining[3]);
    }

    [Fact]
    public void ADeathTheFightNeverSawIsTooLongAgo()
    {
        // Killed outside the encounter's own paths, the corpse carries no round stamp,
        // and null reads as "too long ago" — refusing a revival the rules might have
        // allowed is recoverable, reviving one they forbid is not.
        var (encounter, _, _, _) = Fight();

        var stranger = CombatTestData.Combatant("stranger", stats: CombatTestData.Stats(maximumHitPoints: 5));
        SRDCombat.Core.Rules.DamageRules.Apply(stranger, 5, DamageType.Slashing);
        Assert.True(stranger.IsDead);
        Assert.Null(stranger.DiedInRound);

        KillAllyOnEnemyTurn(encounter);
        var refusal = encounter.CastSpell("spell.test-revivify", stranger);

        Assert.Equal("spell.dead_too_long", refusal?.Code);
    }

    [Fact]
    public void ACorpseSomeoneStandsOnIsRefused()
    {
        var (encounter, _, ally, enemy) = Fight();

        KillAllyOnEnemyTurn(encounter, thenStepOntoCorpse: true);
        Assert.Equal(enemy.Position, ally.Position);

        var refusal = encounter.CastSpell("spell.test-revivify", ally);

        Assert.Equal("spell.no_room_to_stand", refusal?.Code);
    }

    [Fact]
    public void ThePolicyWalksToTheBodyAndRevivesIt()
    {
        // The caster starts across the room; its policy turn walks to the corpse and
        // spends the Action on the touch.
        var (encounter, _, ally, _) = Fight(casterX: 6);

        KillAllyOnEnemyTurn(encounter);

        SimpleTacticsPolicy.TakeTurn(encounter);

        Assert.False(ally.IsDead);
        Assert.Equal(1, ally.CurrentHitPoints);
    }

    // ── The stage ───────────────────────────────────────────────────────────────

    /// <summary>
    /// An enemy (initiative 30) beside a 1-hit-point ally monster, a revival caster,
    /// and a big dumb brute so the fight survives the ally's death. Dice: three or four
    /// initiative rolls, then the enemy's killing blow (18 to hit, 3 damage).
    /// </summary>
    private static (Encounter Encounter, Combatant Caster, Combatant Ally, Combatant Enemy) Fight(
        int casterX = 1)
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

        var shell = CombatTestData.Character("caster");

        var stats = shell.Stats with
        {
            InitiativeBonus = 0,
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

        var caster = new Combatant("caster", "Caster", CombatTestData.Heroes, stats, new GridPosition(casterX, 1));

        // A monster-statted ally dies the instant it hits 0, which keeps the death out
        // of the death-save machinery this test is not about.
        var ally = CombatTestData.Combatant(
            "ally",
            sideId: CombatTestData.Heroes,
            stats: CombatTestData.Stats(maximumHitPoints: 1, armorClass: 5, initiativeBonus: 5, attacks: []),
            x: 2,
            y: 1);

        var enemy = CombatTestData.Combatant(
            "enemy",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: 30, attacks: [CombatTestData.MeleeAttack(bonus: 10)]),
            x: 3,
            y: 1);

        var brute = CombatTestData.Combatant(
            "brute",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -30, maximumHitPoints: 60, attacks: []),
            x: 8,
            y: 2);

        var encounter = Encounter.Start(
            new Battlefield(10, 4),
            [caster, ally, enemy, brute],
            new ScriptedRandomSource(10, 10, 10, 10, 18, 3));

        return (encounter, caster, ally, enemy);
    }

    /// <summary>The enemy's opening turn: kill the ally, optionally stand on the body, end.</summary>
    private static void KillAllyOnEnemyTurn(Encounter encounter, bool thenStepOntoCorpse = false)
    {
        var ally = encounter.Combatants.Single(combatant => combatant.Id == "ally");

        Assert.Null(encounter.Attack("Sword", ally));
        Assert.True(ally.IsDead);

        if (thenStepOntoCorpse)
        {
            Assert.Null(encounter.Move(ally.Position));
        }

        encounter.EndTurn();
    }
}
