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
        Assert.Contains("Brown Bear", names);
        Assert.Contains("Goblin Warrior", names);

        // A weak wild animal is a poor fight, not an absurd one, and stays in.
        Assert.Contains("Rat", names);
        Assert.Contains("Raven", names);
        Assert.Contains("Deer", names);
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
