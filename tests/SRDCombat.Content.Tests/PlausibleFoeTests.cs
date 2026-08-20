using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Content.Tests;

/// <summary>
/// The third axis: which creatures may be drawn as an enemy at all.
/// </summary>
/// <remarks>
/// The bug these exist for was reported from a real build — "Warrior Infantry, Giant
/// Wasp, Violet Fungus, <b>Camel</b>" at 200 XP, with the budget arithmetic exactly
/// right. Coverage was right too; the Camel was simply never an enemy.
/// </remarks>
public class PlausibleFoeTests
{
    private const decimal TierOneMaximum = 4m;

    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void ThePoolNoLongerFieldsEquipment()
    {
        var pool = MonsterPool.Draw(Content.Monsters, TierOneMaximum);
        var names = pool.Select(monster => monster.Name).ToHashSet(StringComparer.Ordinal);

        // The two the issue actually reported, plus the rest of the printed table.
        Assert.DoesNotContain("Camel", names);
        Assert.DoesNotContain("Mule", names);

        foreach (var excluded in PlausibleFoes.ExcludedNames)
        {
            Assert.DoesNotContain(excluded, names);
        }
    }

    [Fact]
    public void TheCreaturesWorthFightingAreStillThere()
    {
        var names = MonsterPool.Draw(Content.Monsters, TierOneMaximum)
            .Select(monster => monster.Name)
            .ToHashSet(StringComparer.Ordinal);

        // A Wolf and a Camel are both Unaligned Beasts, which is exactly why no
        // derivation from type or alignment could have done this.
        Assert.Contains("Wolf", names);
        Assert.Contains("Goblin Warrior", names);

        // This axis still admits the wild animals — "a weak wild animal is a poor
        // fight, not an absurd one" remains its reading — and it is the genre cut one
        // axis up (TraditionalFoes, 2026-08-20) that now keeps them out of the default
        // draw. Asserted with the genre cut lifted, so the two axes stay separate.
        var withoutGenreCut = MonsterPool
            .Draw(Content.Monsters, TierOneMaximum, traditionalFoesOnly: false)
            .Select(monster => monster.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Brown Bear", withoutGenreCut);
        Assert.Contains("Rat", withoutGenreCut);
        Assert.Contains("Raven", withoutGenreCut);
        Assert.Contains("Deer", withoutGenreCut);
    }

    [Fact]
    public void AGiantGoatIsNotAGoat()
    {
        // Exact names, never substrings: the Giant Goat is a wild mountain creature with
        // a charging Ram, and a substring test would take it out with the farm animal.
        //
        // Asserted against PlausibleFoes rather than against the pool on purpose — the
        // Giant Goat is out of the tier-1 pool today for an unrelated reason (its charge
        // rider leaves it Diminished), so a pool-level assertion would pass without
        // testing this rule at all, and would start failing the day coverage improved.
        Assert.False(PlausibleFoes.Admits(Content.MonstersById["monster.goat"]));
        Assert.True(PlausibleFoes.Admits(Content.MonstersById["monster.giant-goat"]));
    }

    [Fact]
    public void EveryExcludedNameIsARealStatBlock()
    {
        // A list matched by name can outlive the thing it names — a renamed creature
        // would silently stop being excluded and start turning up as an enemy. This is
        // the guard, and it lives here rather than in MonsterValidator because that
        // validates whatever list it is handed, including single stat blocks.
        var names = Content.Monsters.Select(monster => monster.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(PlausibleFoes.ExcludedNames, excluded => Assert.Contains(excluded, names));
    }

    [Fact]
    public void TheAquaticRuleCatchesExactlyTheCreaturesWithNowhereToFight()
    {
        // Verified against all 330 stat blocks before the rule was trusted, which is
        // what the issue asked for: nine, and nothing else in the book.
        var aquatic = Content.Monsters.Where(PlausibleFoes.IsAquatic).Select(m => m.Name).ToArray();

        Assert.Equal(
            [
                "Giant Seahorse", "Giant Shark", "Hunter Shark", "Killer Whale", "Octopus",
                "Piranha", "Reef Shark", "Seahorse", "Swarm of Piranhas",
            ],
            aquatic.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void ATokenLandSpeedAloneIsNotEnough()
    {
        // The clause that stops the obvious version being wrong. Every one of these
        // walks 5 feet and is a perfectly good land encounter, several of them staples;
        // a bare "walks 5 or less" rule would have taken the lot.
        foreach (var id in new[]
                 {
                     "monster.animated-flying-sword", "monster.swarm-of-bats", "monster.will-o-wisp",
                     "monster.ghost", "monster.violet-fungus", "monster.bat", "monster.owl",
                 })
        {
            var monster = Content.MonstersById[id];

            Assert.Equal(PlausibleFoes.TokenLandSpeedFeet, monster.Speeds[MovementMode.Walk]);
            Assert.False(PlausibleFoes.IsAquatic(monster), $"{monster.Name} is not aquatic.");
        }
    }

    [Fact]
    public void TheAmphibiousCreaturesTheBookMeansToBeMetAshoreAreKept()
    {
        // The boundary is the SRD's own: these walk 10 feet or more. The Giant Octopus
        // is the closest call in the book, and the SRD gave it twice the Octopus's land
        // speed on purpose.
        foreach (var id in new[]
                 {
                     "monster.giant-octopus", "monster.merfolk-skirmisher", "monster.merrow",
                     "monster.aboleth", "monster.archelon", "monster.crab",
                 })
        {
            Assert.False(PlausibleFoes.IsAquatic(Content.MonstersById[id]));
        }
    }

    [Fact]
    public void TheExclusionIsOnlyForRandomDraws()
    {
        // An authored fight may still want a stampeding elephant, so the filter is a
        // parameter rather than a fact about the creature.
        var everything = MonsterPool.Draw(Content.Monsters, TierOneMaximum, plausibleFoesOnly: false)
            .Select(monster => monster.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Camel", everything);
    }

    [Fact]
    public void ExclusionIsSeparateFromCoverage()
    {
        // The Camel is mechanically Complete and still not a foe. Keeping these apart is
        // the point: "how much of this creature does the engine execute" keeps one answer.
        var camel = Content.MonstersById["monster.camel"];

        Assert.Equal(MonsterCoverage.Complete, MonsterPool.CoverageOf(camel));
        Assert.True(MonsterPool.Admits(camel));
        Assert.False(PlausibleFoes.Admits(camel));
    }
}
