using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Content.Tests;

/// <summary>
/// The company a creature keeps: the theme map that stops a random fight fielding an
/// Ape beside a Giant Eagle and a Scout as one squad — the played complaint
/// (2026-08-20) this layer exists for.
/// </summary>
public class EncounterThemeTests
{
    private const decimal TierOneMaximum = 4m;

    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void TheMapCoversTheTraditionalPoolExactly()
    {
        // Both directions: an unmapped pool creature would build unconstrained
        // (nothing may hold unclassified silently), and a mapped name outside the pool
        // is a typo or a stat block the roster no longer fields.
        var pool = MonsterPool.Draw(Content.Monsters, TierOneMaximum)
            .Select(monster => monster.Name)
            .ToHashSet(StringComparer.Ordinal);

        var mapped = EncounterThemes.MappedNames.ToHashSet(StringComparer.Ordinal);

        Assert.Empty(pool.Except(mapped));
        Assert.Empty(mapped.Except(pool));
    }

    [Fact]
    public void EveryBuiltEncounterIsACoherentCompany()
    {
        // The builder's own product, over many seeds, every difficulty, both count
        // regimes: every creature in a fight is a companion of its anchor.
        var pool = MonsterPool.Draw(Content.Monsters, TierOneMaximum);

        for (var seed = 1; seed <= 300; seed++)
        {
            foreach (var difficulty in new[]
                     { EncounterDifficulty.Low, EncounterDifficulty.Moderate, EncounterDifficulty.High })
            {
                var built = EncounterBuilder.ForLevels(
                    pool, [3, 3, 3, 3], difficulty, new SeededRandomSource(seed));

                if (built.Monsters.Count == 0)
                {
                    continue;
                }

                var anchor = built.Monsters[0];

                Assert.All(built.Monsters, monster => Assert.True(
                    EncounterThemes.Companions(anchor, monster),
                    $"Seed {seed} {difficulty}: {monster.Name} is no companion of {anchor.Name} "
                    + $"in [{string.Join(", ", built.Monsters.Select(m => m.Name))}]."));
            }
        }
    }

    [Fact]
    public void AHordeNeverAnchorsOnALoner()
    {
        var pool = MonsterPool.Draw(Content.Monsters, TierOneMaximum);

        for (var seed = 1; seed <= 300; seed++)
        {
            var built = EncounterBuilder.ForLevels(
                pool,
                [5, 5, 5, 5],
                EncounterDifficulty.Low,
                new SeededRandomSource(seed),
                maximumMonsters: 10,
                minimumMonsters: 6);

            if (built.Monsters.Count > 0)
            {
                Assert.True(
                    EncounterThemes.KeepsCompany(built.Monsters[0]),
                    $"Seed {seed}: a horde anchored on the loner {built.Monsters[0].Name}.");
            }
        }
    }

    [Fact]
    public void ALonerFightsAloneOrBesideItsOwnKind()
    {
        var owlbear = Content.Monsters.Single(monster => monster.Name == "Owlbear");
        var basilisk = Content.Monsters.Single(monster => monster.Name == "Basilisk");
        var goblin = Content.Monsters.Single(monster => monster.Name == "Goblin Warrior");

        Assert.True(EncounterThemes.Companions(owlbear, owlbear));
        Assert.False(EncounterThemes.Companions(owlbear, basilisk));
        Assert.False(EncounterThemes.Companions(owlbear, goblin));
        Assert.False(EncounterThemes.KeepsCompany(owlbear));
    }

    [Fact]
    public void ABridgeCreatureBringsEitherOfItsWorlds()
    {
        // The Worg is a goblin mount and a pack hunter; judged against the anchor, its
        // fight may hold goblins or wolves — and a goblin and a dire wolf who share no
        // theme directly still stand together in it.
        var worg = Content.Monsters.Single(monster => monster.Name == "Worg");
        var goblin = Content.Monsters.Single(monster => monster.Name == "Goblin Warrior");
        var direWolf = Content.Monsters.Single(monster => monster.Name == "Dire Wolf");

        Assert.True(EncounterThemes.Companions(worg, goblin));
        Assert.True(EncounterThemes.Companions(worg, direWolf));
        Assert.False(EncounterThemes.Companions(goblin, direWolf));
    }

    [Fact]
    public void AnUnmappedPoolBuildsUnconstrained()
    {
        // The authored escape hatch: a caller's own creatures, absent from the map,
        // never trip the companion filter.
        var lion = Content.Monsters.Single(monster => monster.Name == "Lion");
        var ape = Content.Monsters.Single(monster => monster.Name == "Ape");

        Assert.Null(EncounterThemes.ThemesOf(lion));
        Assert.True(EncounterThemes.Companions(lion, ape));
    }
}
