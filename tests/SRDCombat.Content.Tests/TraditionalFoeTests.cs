using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Content.Tests;

/// <summary>
/// The genre cut: the random draw fields traditional D&amp;D monsters and enemies only.
/// </summary>
/// <remarks>
/// Asked for from play on 2026-08-20 — a fight had opened with an Ape beside a Giant
/// Eagle and a Scout. The weight that made animals rarer (three slices earlier) became a
/// cut that removes them, with the giant and swarm variants deliberately surviving it.
/// </remarks>
public class TraditionalFoeTests
{
    private const decimal TierOneMaximum = 4m;

    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void EveryExcludedNameNamesARealStatBlock()
    {
        // The guard that the list cannot outlive a renamed stat block — the same test
        // PlausibleFoes carries, for the same reason.
        var names = Content.Monsters.Select(monster => monster.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(TraditionalFoes.ExcludedNames, excluded => Assert.Contains(excluded, names));
    }

    [Fact]
    public void TheDefaultDrawFieldsNoSafari()
    {
        var names = MonsterPool.Draw(Content.Monsters, TierOneMaximum)
            .Select(monster => monster.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var excluded in TraditionalFoes.ExcludedNames)
        {
            Assert.DoesNotContain(excluded, names);
        }

        // The played complaint, by name.
        Assert.DoesNotContain("Ape", names);
        Assert.DoesNotContain("Giant Eagle", names);
    }

    [Fact]
    public void AGiantRatIsNotARat()
    {
        // Exact names, never substrings — the PlausibleFoes lesson: the fantastic
        // variants of excluded animals survive the cut on purpose.
        //
        // Swarm of Rats and Worg dropped out of this list on 2026-08-24 (span-accounting
        // regeneration, #382) — not because the genre cut changed its mind about
        // either, but because both demoted out of the tier-1 pool entirely: the Swarm
        // of Rats' Bites is #371's own worked example (the "or 2 (1d4) Piercing damage
        // if the swarm is Bloodied" tier, always printed and now honestly residue),
        // and the Worg's Bite has always printed "and the next attack roll made
        // against the target before the start of the worg's next turn has Advantage"
        // — a rider nothing executes, hidden until now behind the old "Hit:"-credits-
        // the-whole-sentence bug. Giant Weasel and Giant Wolf Spider make the same
        // point about the exact-name cut and are still admissible.
        var names = MonsterPool.Draw(Content.Monsters, TierOneMaximum)
            .Select(monster => monster.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Giant Rat", names);
        Assert.Contains("Giant Bat", names);
        Assert.Contains("Giant Weasel", names);
        Assert.Contains("Giant Venomous Snake", names);
        Assert.Contains("Giant Wolf Spider", names);
        Assert.Contains("Dire Wolf", names);
    }

    [Fact]
    public void AnAuthoredFightMayStillFieldALion()
    {
        // The cut governs only the random draw, like every list beside it. The Lion's
        // Multiattack carries a replace-clause the model does not express ("It can
        // replace one attack with a use of Roar.", #290) and so grades Diminished on
        // its own honest accounting, unrelated to the genre cut this test is about —
        // an authored fight accepts that floor explicitly, the same way it opts out of
        // the cut explicitly.
        var names = MonsterPool
            .Draw(Content.Monsters, TierOneMaximum, MonsterCoverage.Diminished, traditionalFoesOnly: false)
            .Select(monster => monster.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Lion", names);
        Assert.Contains("Ape", names);
    }
}
