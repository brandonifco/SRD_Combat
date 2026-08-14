using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The pregenerated party, built from real extracted content.
/// </summary>
/// <remarks>
/// These four exist so that sitting down to a fight exercises the breadth of what has
/// been built rather than four variations on "hit it with a sword". That intent is only
/// worth anything if it is checked, so these tests assert the *coverage* the party is
/// supposed to provide, not just that it resolves.
/// </remarks>
public class PregeneratedPartyTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void ThePartyResolvesFromRealContent()
    {
        var party = PregeneratedParty.Build(Content);

        Assert.Equal(4, party.Count);

        Assert.All(party, member =>
        {
            Assert.True(member.Sheet.MaximumHitPoints > 0);
            Assert.True(member.Sheet.ArmorClass > 0);
            Assert.NotEmpty(member.Sheet.Attacks);
            Assert.Equal(PregeneratedParty.SideId, member.Combatant.SideId);
            Assert.True(member.Combatant.CanAct);
        });
    }

    [Fact]
    public void EveryMemberBringsADifferentMechanicalShape()
    {
        var party = PregeneratedParty.Build(Content);

        var features = party
            .SelectMany(member => member.Sheet.Features.Select(granted => granted.Feature))
            .ToHashSet();

        // One from each class, so a fight touches attacks, a resource, sneak damage and
        // spellcasting rather than four sword swings.
        Assert.Contains(ClassFeature.SecondWind, features);
        Assert.Contains(ClassFeature.Rage, features);
        Assert.Contains(ClassFeature.SneakAttack, features);
        Assert.Contains(ClassFeature.Expertise, features);
        Assert.Contains(ClassFeature.FightingStyle, features);

        Assert.Contains(party, member => member.Combatant.Stats.Character is { CanCast: true });
    }

    [Fact]
    public void TheClericNeverCarriesASpellItHasNoSlotFor()
    {
        // Caught by playing: the Cast menu of a level 1 Cleric offered Hold Person,
        // Revivify and Spirit Guardians — every one of them refusable and nothing
        // else — because the pregens took a second path that read their curated list
        // straight onto the sheet, skipping the printed columns.
        foreach (var level in new[] { 1, 2, 3, 4, 5 })
        {
            var cleric = PregeneratedParty.Build(Content, level)
                .Single(member => member.Draft.Name == "Aldous");

            var slots = cleric.Sheet.SpellSlots;
            var highest = slots.Count > 0 ? slots.Keys.Max() : 0;
            var prepared = cleric.Combatant.Stats.Character!.Spells;

            Assert.All(prepared, spell =>
                Assert.True(
                    spell.IsCantrip || spell.Level <= highest,
                    $"level {level} Cleric prepared {spell.Name} (level {spell.Level}) with no slot for it"));

            // And the printed Prepared Spells column is a ceiling, not a suggestion.
            var allowance = CharacterCreation.SpellAllowances(
                Content.ClassesById["class.cleric"],
                level,
                cleric.Sheet.DivineOrder);

            Assert.True(prepared.Count(spell => !spell.IsCantrip) <= allowance.Prepared);
            Assert.True(prepared.Count(spell => spell.IsCantrip) <= allowance.Cantrips);
        }
    }

    [Fact]
    public void TheClericStillGrowsIntoItsWholePlanByLevelFive()
    {
        // The other half of the same rule: nothing was lost, it arrives when the
        // slots do. Hold Person needs level 2 slots, Revivify and Spirit Guardians
        // level 3.
        var levelOne = PregeneratedParty.Build(Content, 1)
            .Single(member => member.Draft.Name == "Aldous")
            .Combatant.Stats.Character!.Spells.Select(spell => spell.Id).ToArray();

        Assert.DoesNotContain("spell.hold-person", levelOne);
        Assert.DoesNotContain("spell.revivify", levelOne);
        Assert.Contains("spell.sacred-flame", levelOne);
        Assert.Contains("spell.cure-wounds", levelOne);

        var levelFive = PregeneratedParty.Build(Content, 5)
            .Single(member => member.Draft.Name == "Aldous")
            .Combatant.Stats.Character!.Spells.Select(spell => spell.Id).ToArray();

        Assert.Contains("spell.hold-person", levelFive);
        Assert.Contains("spell.revivify", levelFive);
        Assert.Contains("spell.spirit-guardians", levelFive);
    }

    [Fact]
    public void TheClericTakesProtectorAndTheChoiceIsNoLongerReported()
    {
        var aldous = PregeneratedParty.Build(Content)
            .Single(member => member.Draft.Name == "Aldous");

        Assert.Equal(DivineOrder.Protector, aldous.Sheet.DivineOrder);
        Assert.DoesNotContain("Divine Order", aldous.Sheet.UnimplementedFeatures);
    }

    [Fact]
    public void TheClericsChannelDivinityComesOffTheClassTable()
    {
        // The Cleric Features table prints the Channel Divinity column from level 2:
        // two uses through level 5, and none at all at level 1.
        var levelOne = PregeneratedParty.Build(Content, level: 1)
            .Single(member => member.Draft.Name == "Aldous");

        Assert.False(levelOne.Sheet.Has(ClassFeature.ChannelDivinity));
        Assert.Equal(0, levelOne.Combatant.Stats.Character!.ChannelDivinityUses);

        var levelTwo = PregeneratedParty.Build(Content, level: 2)
            .Single(member => member.Draft.Name == "Aldous");

        Assert.True(levelTwo.Sheet.Has(ClassFeature.ChannelDivinity));
        Assert.Equal(2, levelTwo.Combatant.Stats.Character!.ChannelDivinityUses);
        Assert.Equal(2, levelTwo.Combatant.Features.ChannelDivinityRemaining);
    }

    [Fact]
    public void TheRoguesSneakAttackAndTheBarbariansRageReachTheCombatant()
    {
        // The class table's per-level resource columns have to arrive on the combatant,
        // or the features are granted and inert — the quietest possible failure.
        var party = PregeneratedParty.Build(Content);

        var rogue = party.Single(member => member.Sheet.ClassName == "Rogue");
        var barbarian = party.Single(member => member.Sheet.ClassName == "Barbarian");
        var fighter = party.Single(member => member.Sheet.ClassName == "Fighter");

        Assert.NotNull(rogue.Combatant.Stats.Character!.SneakAttackDamage);
        Assert.True(barbarian.Combatant.Features.RagesRemaining > 0);
        Assert.True(fighter.Combatant.Features.SecondWindRemaining > 0);
    }

    [Fact]
    public void TheCasterKnowsOneSpellOfEachShape()
    {
        // All three casting paths, so playing exercises each: an attack roll, a saving
        // throw, and healing. The party had no healing at all until it was modelled, and
        // a run died out within a few fights however easy they were.
        var caster = PregeneratedParty.Build(Content)
            .Single(member => member.Combatant.Stats.Character is { CanCast: true });

        var spells = caster.Combatant.Stats.Character!.Spells;

        Assert.Contains(spells, spell => spell.IsSpellAttack);
        Assert.Contains(spells, spell => spell.Save is not null);
        Assert.Contains(spells, spell => spell.Heal is not null);
        Assert.All(spells, spell => Assert.True(
            spell.IsSpellAttack || spell.Save is not null || spell.Heal is not null
                || spell.Revival is not null,
            $"{spell.Name} would be refused at the point of casting."));
    }

    [Fact]
    public void TheClericCanHealAFallenPartyMemberBackIntoTheFight()
    {
        // The end-to-end proof with real content: real spell, real character, real
        // encounter. A downed character was unrecoverable until healing existed, and a
        // run died out within a few fights however easy they were.
        var party = PregeneratedParty.Build(Content, level: 1);
        var cleric = party.Single(member => member.Combatant.Stats.Character is { CanCast: true });
        var fallen = party.Single(member => member.Draft.Name == "Sable");

        var encounter = Encounter.Start(
            new Battlefield(8, 8),
            [.. party.Select(member => member.Combatant)],
            new SeededRandomSource(1));

        DamageRules.Apply(
            fallen.Combatant,
            fallen.Sheet.MaximumHitPoints,
            DamageType.Bludgeoning);

        Assert.Equal(0, fallen.Combatant.CurrentHitPoints);
        Assert.False(fallen.Combatant.CanAct);

        // Whoever the encounter gave the first turn to, put the Cleric on their feet.
        while (encounter.ActiveCombatant != cleric.Combatant)
        {
            encounter.EndTurn();
        }

        Assert.Null(encounter.CastSpell("spell.cure-wounds", fallen.Combatant));

        Assert.True(fallen.Combatant.CurrentHitPoints > 0);
        Assert.True(fallen.Combatant.CanAct);
    }

    [Fact]
    public void TheBarbarianIsUnarmouredSoUnarmoredDefenseApplies()
    {
        // Putting a Barbarian in Chain Mail would silently switch off its own AC rule.
        var barbarian = PregeneratedParty.Build(Content)
            .Single(member => member.Sheet.ClassName == "Barbarian");

        Assert.Null(barbarian.Draft.ArmorId);
        Assert.Contains("Unarmored Defense", barbarian.Sheet.ArmorClassSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePartyCanFightARealMonsterToAConclusion()
    {
        // The whole stack in one test: extracted content, resolved characters, the
        // curated monster pool, and the engine running a fight to its end.
        var party = PregeneratedParty.Build(Content, level: 1, x: 1);

        var monster = MonsterPool.Draw(Content.Monsters, maximumChallengeRating: 0.5m)
            .First(candidate => candidate.ChallengeRating >= 0.25m);

        var enemy = new Combatant(
            "enemy",
            monster.Name,
            "monsters",
            CombatantStats.FromMonster(monster),
            new GridPosition(6, 1));

        var encounter = Encounter.Start(
            new Battlefield(10, 6),
            [.. party.Select(member => member.Combatant), enemy],
            new SeededRandomSource(20250812));

        SimpleTacticsPolicy.RunToCompletion(encounter);

        Assert.True(encounter.IsComplete);
        Assert.NotNull(encounter.WinningSide);
        Assert.NotEmpty(encounter.Log);
    }

    [Fact]
    public void LevellingIsReResolvingTheDraft()
    {
        // The property the draft/sheet split exists to protect: a level 5 party comes
        // from the same drafts, not from edited sheets.
        var first = PregeneratedParty.Build(Content, level: 1);
        var fifth = PregeneratedParty.Build(Content, level: 5);

        Assert.Equal(
            first.Select(member => member.Draft.Name),
            fifth.Select(member => member.Draft.Name));

        Assert.All(
            first.Zip(fifth),
            pair => Assert.True(
                pair.Second.Sheet.MaximumHitPoints > pair.First.Sheet.MaximumHitPoints,
                $"{pair.First.Draft.Name} gained no hit points between levels 1 and 5."));
    }
}
