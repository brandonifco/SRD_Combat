using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Upcasting and cantrip upgrades, against the real content: a higher slot buys the
/// printed extra dice, and a cantrip grows at character level 5.
/// </summary>
public class UpcastingTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void TheScalingSentencesAreStructuredForTheDiceShapedSpells()
    {
        // The two shapes the extractor structures, with the guards that keep them
        // honest: the "above N" must be the spell's own level and the die must match
        // the base effect's. These counts move only when the source or the guards do.
        Assert.Equal(32, Content.Spells.Count(spell => spell.UpcastDicePerSlotLevel is not null));
        Assert.Equal(10, Content.Spells.Count(spell => spell.CantripUpgradeDice is not null));

        Assert.Equal("2d8", Content.SpellsById["spell.cure-wounds"].UpcastDicePerSlotLevel!.ToString());
        Assert.Equal("1d8", Content.SpellsById["spell.sacred-flame"].CantripUpgradeDice!.ToString());
    }

    [Fact]
    public void AHigherSlotBuysThePrintedExtraDiceAndTheNarrationNamesIt()
    {
        // A level 3 Cleric with its level 1 slots already spent: Cure Wounds must burn
        // a level 2 slot and heal 4d8 instead of 2d8.
        var (encounter, cleric, patient) = Fight(drainLevelOneSlots: true);

        // Dice: 4d8 scripted as four 4s = 16, + Wisdom 3 + Disciple of Life (2+1... no:
        // the slot is level 2, so 2 + 2 = 4). Total 23 onto 1 hit point = 24.
        Assert.Null(encounter.CastSpell("spell.cure-wounds", patient));

        Assert.Equal(24, patient.CurrentHitPoints);
        Assert.Contains(encounter.Log, step =>
            step.Narration.Contains("casts Cure Wounds (level 2 slot)", StringComparison.Ordinal));
    }

    [Fact]
    public void DiscipleOfLifeReadsTheSlotActuallySpentNotTheSpellsLevel()
    {
        // The printed feature is "2 plus the spell slot's level" — the slot's, so an
        // upcast heal feeds it too. This is the assertion above, isolated: the level 2
        // cast must add 4, not 3.
        var (encounter, _, patient) = Fight(drainLevelOneSlots: true);

        encounter.CastSpell("spell.cure-wounds", patient);

        // 16 (dice) + 3 (Wisdom) + 4 (Disciple at slot level 2) = 23 healed from 1.
        Assert.Equal(24, patient.CurrentHitPoints);
    }

    [Fact]
    public void ACantripGrowsAtCharacterLevelFive()
    {
        // Sacred Flame: "increases by 1d8 when you reach levels 5 (2d8), ...". At
        // level 3 it rolls one d8; at level 5, two.
        var three = PregeneratedParty.Build(Content, 3)[3];
        var five = PregeneratedParty.Build(Content, 5)[3];

        // One d20 for the save plus the damage dice: 1d8 at level 3, 2d8 at level 5.
        Assert.Equal(2, RolledD8s(three, casterLevel: 3));
        Assert.Equal(3, RolledD8s(five, casterLevel: 5));
    }

    /// <summary>
    /// Casts Sacred Flame with a script that counts how many d8s the fight asks for:
    /// initiative (2), the save (1 d20), then the damage d8s until the script runs dry.
    /// </summary>
    private static int RolledD8s(PartyMember caster, int casterLevel)
    {
        var enemy = new Combatant(
            "enemy",
            "Enemy",
            "monsters",
            CombatantStats.FromMonster(Content.MonstersById["monster.goblin-warrior"]),
            new GridPosition(2, 0));

        var counter = new CountingRandomSource();
        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [caster.AtPosition(new GridPosition(0, 0)).Combatant, enemy],
            counter);

        while (encounter.ActiveCombatant?.Id != caster.Combatant.Id && !encounter.IsComplete)
        {
            encounter.EndTurn();
        }

        counter.CountBySides.Clear();

        Assert.Null(encounter.CastSpell("spell.sacred-flame", enemy));

        // One d20 for the save; the rest of the interesting rolls are the damage d8s.
        return counter.CountBySides.GetValueOrDefault(8) + counter.CountBySides.GetValueOrDefault(20);
    }

    /// <summary>A random source that rolls low and counts what was asked of it.</summary>
    private sealed class CountingRandomSource : IRandomSource
    {
        public Dictionary<int, int> CountBySides { get; } = [];

        public int Roll(int sides)
        {
            CountBySides[sides] = CountBySides.GetValueOrDefault(sides) + 1;
            return 1;
        }
    }

    private static (Encounter Encounter, Combatant Cleric, Combatant Patient) Fight(bool drainLevelOneSlots)
    {
        var cleric = PregeneratedParty
            .Resolve(Content, PregeneratedParty.Build(Content, 3)[3].Draft, 3)
            .AtPosition(new GridPosition(0, 0))
            .Combatant;

        if (drainLevelOneSlots)
        {
            cleric.Features.SpellSlotsRemaining[1] = 0;
        }

        var patient = new Combatant(
            "patient",
            "Patient",
            PregeneratedParty.SideId,
            new CombatantStats(
                13,
                30,
                30,
                -5,
                Enum.GetValues<Ability>().ToDictionary(a => a, _ => new MonsterAbility(10, 0)),
                2,
                CreatureSize.Medium,
                new Dictionary<DamageType, DamageResponse>(),
                [],
                [],
                DiesAtZeroHitPoints: false),
            new GridPosition(1, 0),
            new CombatantCarryOver(1));

        var enemy = new Combatant(
            "enemy",
            "Enemy",
            "monsters",
            CombatantStats.FromMonster(Content.MonstersById["monster.goblin-warrior"]),
            new GridPosition(10, 0));

        // Initiative x3 high-to-low, then Cure Wounds' 4d8 as four 4s.
        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [cleric, patient, enemy],
            new ScriptedRandomSource(20, 10, 1, 4, 4, 4, 4));

        return (encounter, cleric, patient);
    }
}
