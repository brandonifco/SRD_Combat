using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
using SRDCombat.Content;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Subclasses: the extraction split, the level-3 grant, and the three features that
/// execute — all against the real content.
/// </summary>
/// <remarks>
/// The SRD prints exactly one subclass per class, so a level 3+ character simply has it
/// and no draft choice exists. The split is derived from the printed levels resetting —
/// the class runs to "Level 20", the subclass starts over at "Level 3" — rather than
/// from a curated list that would go stale on re-extraction.
/// </remarks>
public class SubclassTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void EveryClassSplitsIntoClassAndSubclassFeatures()
    {
        foreach (var definition in Content.Classes)
        {
            Assert.True(
                definition.SubclassFeatures.Count >= 4,
                $"{definition.Name} has only {definition.SubclassFeatures.Count} subclass features.");

            // The split rests on every feature carrying its printed level, and on the
            // subclass opening at level 3, where every class grants its subclass.
            Assert.All(definition.Features, feature => Assert.NotNull(feature.GrantedAtLevel));
            Assert.All(definition.SubclassFeatures, feature => Assert.NotNull(feature.GrantedAtLevel));
            Assert.Equal(3, definition.SubclassFeatures.Min(feature => feature.GrantedAtLevel));
        }
    }

    [Fact]
    public void SubclassFeaturesArriveAtLevelThreeAndNotBefore()
    {
        var two = PregeneratedParty.Resolve(Content, PregeneratedParty.Build(Content, 2)[0].Draft, 2);
        var three = PregeneratedParty.Resolve(Content, PregeneratedParty.Build(Content, 3)[0].Draft, 3);

        Assert.False(two.Sheet.Has(ClassFeature.ImprovedCritical));
        Assert.True(three.Sheet.Has(ClassFeature.ImprovedCritical));
    }

    [Fact]
    public void TheThiefsFeaturesAreHonestlyUseless()
    {
        // Fast Hands and Second-Story Work do nothing in a fight this engine can
        // express, so the Rogue gains no executed feature at level 3 — and the gap is
        // reported rather than hidden.
        var rogue = PregeneratedParty.Resolve(Content, PregeneratedParty.Build(Content, 3)[2].Draft, 3);

        Assert.Equal("Rogue", rogue.Sheet.ClassName);
        Assert.Contains("Fast Hands", rogue.Sheet.UnimplementedFeatures);
        Assert.Contains("Second-Story Work", rogue.Sheet.UnimplementedFeatures);
    }

    [Fact]
    public void AChampionCritsOnNineteenAndOnlyWhenItHits()
    {
        var fighter = PregeneratedParty.Resolve(Content, PregeneratedParty.Build(Content, 3)[0].Draft, 3);

        Assert.Equal(19, fighter.Combatant.Stats.CriticalHitThreshold);

        // A 19 against a beatable AC: hit and crit.
        var weak = Dummy("weak", armorClass: 10);

        var crit = AttackRules.Resolve(
            new ScriptedRandomSource(19),
            fighter.Combatant,
            fighter.Combatant.Stats.Attacks[0],
            weak);

        Assert.True(crit.Hit);
        Assert.True(crit.Critical);

        // A 19 against an unbeatable AC: only the natural 20 auto-hits, so no crit —
        // the printed feature widens the crit and says nothing about hitting.
        var fortress = Dummy("fortress", armorClass: 30);

        var miss = AttackRules.Resolve(
            new ScriptedRandomSource(19),
            fighter.Combatant,
            fighter.Combatant.Stats.Attacks[0],
            fortress);

        Assert.False(miss.Hit);
        Assert.False(miss.Critical);
    }

    [Fact]
    public void FrenzyAddsItsDiceOnceOnTheFirstRecklessHit()
    {
        // Level 5, so Extra Attack gives the turn a second swing for the "only once"
        // half of the assertion. The Rage Damage bonus is 2 there, so Frenzy is 2d6.
        var barbarian = PregeneratedParty.Resolve(Content, PregeneratedParty.Build(Content, 5)[1].Draft, 5);

        Assert.True(barbarian.Sheet.Has(ClassFeature.Frenzy));

        var punchbag = Dummy("bag", armorClass: 5, hitPoints: 90);

        // Initiative x2; Rage and Reckless roll nothing; each Reckless attack rolls two
        // d20s (Advantage), then the greataxe's d12, then — first hit only — Frenzy's
        // two d6s (Rage Damage bonus 2).
        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [barbarian.AtPosition(new GridPosition(0, 0)).Combatant, punchbag],
            new ScriptedRandomSource(20, 1, 15, 15, 6, 3, 3, 15, 15, 6));

        var actor = encounter.ActiveCombatant!;

        Assert.Null(encounter.Rage());
        Assert.Null(encounter.RecklessAttack());
        Assert.Null(encounter.Attack(actor.Stats.Attacks[0].Name, punchbag));

        Assert.Contains(encounter.Log, step => step.Narration.Contains("Frenzy adds", StringComparison.Ordinal));

        var frenzies = encounter.Log.Count(step =>
            step.Narration.Contains("Frenzy adds", StringComparison.Ordinal));

        Assert.Null(encounter.Attack(actor.Stats.Attacks[0].Name, punchbag));

        // Still exactly one: "the first target you hit on your turn".
        Assert.Equal(
            frenzies,
            encounter.Log.Count(step => step.Narration.Contains("Frenzy adds", StringComparison.Ordinal)));
    }

    [Fact]
    public void DiscipleOfLifeAddsTwoPlusSlotLevelToEveryLeveledHeal()
    {
        var cleric = PregeneratedParty.Resolve(Content, PregeneratedParty.Build(Content, 3)[3].Draft, 3);

        Assert.True(cleric.Sheet.Has(ClassFeature.DiscipleOfLife));

        var patient = new Combatant(
            "patient",
            "Patient",
            PregeneratedParty.SideId,
            DummyStats(armorClass: 13, hitPoints: 30, initiativeBonus: -5, diesAtZero: false),
            new GridPosition(1, 0),
            new CombatantCarryOver(1));

        var enemy = Dummy("enemy", armorClass: 13, x: 10, initiativeBonus: -10);

        // Initiative x3, then Cure Wounds' 2d8: scripted 4 and 4.
        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [cleric.AtPosition(new GridPosition(0, 0)).Combatant, patient, enemy],
            new ScriptedRandomSource(20, 10, 1, 4, 4));

        Assert.Null(encounter.CastSpell("spell.cure-wounds", patient));

        // 2d8 (8) + Wisdom 3 + Disciple of Life (2 + slot level 1 = 3) = 14.
        Assert.Equal(15, patient.CurrentHitPoints);
    }

    /// <summary>A featureless creature to hit, standing adjacent unless placed.</summary>
    private static Combatant Dummy(
        string id,
        int armorClass,
        int hitPoints = 20,
        int x = 1,
        int initiativeBonus = -10) =>
        new(id, id, "monsters", DummyStats(armorClass, hitPoints, initiativeBonus, diesAtZero: true), new GridPosition(x, 0));

    private static CombatantStats DummyStats(int armorClass, int hitPoints, int initiativeBonus, bool diesAtZero) =>
        new(
            armorClass,
            hitPoints,
            SpeedFeet: 30,
            initiativeBonus,
            Enum.GetValues<Ability>().ToDictionary(a => a, _ => new MonsterAbility(10, 0)),
            ProficiencyBonus: 2,
            CreatureSize.Medium,
            new Dictionary<DamageType, DamageResponse>(),
            [],
            [],
            diesAtZero);
}
