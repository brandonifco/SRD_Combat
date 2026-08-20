using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Content.Tests;

/// <summary>
/// The pool the encounter builder draws from, measured against the real bestiary.
/// </summary>
/// <remarks>
/// <para>
/// These run against extracted content on purpose — the pool is <em>derived</em> from
/// the content's own accounting, so the thing worth testing is what the derivation
/// yields over 330 real stat blocks, not over invented ones. The counts are deliberately
/// asserted as floors rather than exact figures: implementing a trait moves them upward,
/// and a test that failed on good news would be retuned rather than read.
/// </para>
/// <para>
/// A shrinking pool is the failure worth catching. That would mean a regeneration made
/// the engine's coverage of real monsters worse, which is exactly the silent regression
/// this project is built to refuse.
/// </para>
/// </remarks>
public class MonsterPoolTests
{
    private const decimal TierOneMaximum = 4m;

    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void TheTierOnePoolIsBigEnoughToBuildAGauntletFrom()
    {
        var pool = MonsterPool.Draw(Content.Monsters, TierOneMaximum);

        Assert.True(
            pool.Count >= 75,
            $"The tier-1 pool has fallen to {pool.Count} monsters; it was 81 when the genre cut " +
            "landed (2026-08-20, TraditionalFoes), 116 before that cut, and 131 before #52 " +
            "dropped the creatures the SRD prices as equipment and #75 dropped the ones with " +
            "nowhere to fight.");
    }

    [Fact]
    public void EveryChallengeRatingInTheBandHasSomethingToDrawFrom()
    {
        // A pool of 81 would still be useless if it were all CR 0. The gauntlet needs a
        // choice at every step of the ladder, so the floor is per band, not overall.
        //
        // CR 0's and CR 4's floors are 3, deliberately below the others': the genre cut
        // removed the mundane animals that padded both bands — CR 0 keeps Awakened
        // Shrub, Giant Fire Beetle and Lemure, CR 4 keeps Ettin, Guard Captain and the
        // Red Dragon Wyrmling — and Brandon accepted the thin bands with expansion
        // fill-ins planned to repopulate them. If a fill-in lands, raise the floor
        // with it.
        var pool = MonsterPool.Draw(Content.Monsters, TierOneMaximum);

        foreach (var (rating, floor) in new[]
                 { (0m, 3), (0.125m, 4), (0.25m, 4), (0.5m, 4), (1m, 4), (2m, 4), (3m, 4), (4m, 3) })
        {
            var atRating = pool.Where(monster => monster.ChallengeRating == rating).ToArray();

            Assert.True(
                atRating.Length >= floor,
                $"CR {rating} has only {atRating.Length} admissible monsters.");
        }
    }

    [Fact]
    public void EveryAdmittedMonstersTurnIsExactlyWhatItsStatBlockPrints()
    {
        // The admission rule, restated as the property it exists to guarantee: nothing
        // in the pool loses part of an action's printed text.
        var pool = MonsterPool.Draw(Content.Monsters, TierOneMaximum);

        Assert.All(pool, monster => Assert.All(
            monster.Entries.Where(entry => entry.Section == MonsterEntrySection.Action),
            entry => Assert.True(
                entry.IsFullyModelled,
                $"{monster.Name}'s {entry.Name} is in the pool with unmodelled text.")));
    }

    [Fact]
    public void EveryAdmittedMonsterCanTakeATurnAndSwing()
    {
        // The pool's whole promise is that these are fair to put in front of a player.
        // Building each one and taking a turn proves the accounting is not describing
        // monsters the engine then refuses.
        foreach (var monster in MonsterPool.Draw(Content.Monsters, TierOneMaximum))
        {
            var stats = CombatantStats.FromMonster(monster);

            Assert.NotEmpty(stats.Entries);
            Assert.True(
                stats.Attacks.Count > 0 || stats.Entries.Any(entry =>
                    entry.Section == MonsterEntrySection.Action && entry.Save is not null),
                $"{monster.Name} is admitted but has nothing to attack or save against.");
        }
    }

    [Fact]
    public void ACreatureThatCannotActIsNeverAdmittedHoweverLowTheFloorIsSet()
    {
        // The Shrieker Fungus has only a Reaction and the Seahorse only a swim action —
        // both faithful readings of the printed blocks rather than coverage gaps. A
        // caller asking for everything still must not be handed a creature that would
        // stand there.
        foreach (var id in new[] { "monster.shrieker-fungus", "monster.seahorse" })
        {
            var monster = Content.MonstersById[id];

            Assert.Equal(MonsterCoverage.Unusable, MonsterPool.CoverageOf(monster));
            Assert.False(MonsterPool.Admits(monster, MonsterCoverage.Unusable));
            Assert.False(MonsterPool.Admits(monster, MonsterCoverage.Diminished));
        }
    }

    [Fact]
    public void ADiminishedMonsterIsExcludedByDefaultAndAvailableOnRequest()
    {
        // The Boar's Gore resolves, and loses "the boar moved 20+ feet straight toward
        // it" — the charge bonus. It fights correctly and hits softer than the book, so
        // it is a legal fight the builder may ask for and never the default.
        var boar = Content.MonstersById["monster.boar"];

        Assert.Equal(MonsterCoverage.Diminished, MonsterPool.CoverageOf(boar));
        Assert.False(MonsterPool.Admits(boar));
        Assert.True(MonsterPool.Admits(boar, MonsterCoverage.Diminished));
    }

    [Fact]
    public void APlayableMonsterIsAdmittedThoughSomethingOutsideItsActionsIsNot()
    {
        // The Ankheg's Bite and Acid Spray are the whole of its turn and both are fully
        // modelled; its Tunneler trait is not. That is the line Playable draws — the
        // turn is right, something outside it is not.
        var ankheg = Content.MonstersById["monster.ankheg"];

        Assert.Equal(MonsterCoverage.Playable, MonsterPool.CoverageOf(ankheg));
        Assert.True(MonsterPool.Admits(ankheg));
        Assert.Contains(ankheg.Entries, entry => !entry.IsFullyModelled);

        // And the near miss that shows the grade is about position, not count: the
        // Specter's Life Drain is its only action and loses "its Hit Point maximum
        // decreases", so one unmodelled clause inside the turn drops it a grade.
        var specter = Content.MonstersById["monster.specter"];

        Assert.Equal(MonsterCoverage.Diminished, MonsterPool.CoverageOf(specter));
        Assert.False(MonsterPool.Admits(specter));
    }

    [Fact]
    public void TheDrawIsOrderedSoAnEncounterBuildIsReproducible()
    {
        var pool = MonsterPool.Draw(Content.Monsters, TierOneMaximum);

        Assert.Equal(
            pool.OrderBy(monster => monster.ChallengeRating)
                .ThenBy(monster => monster.Name, StringComparer.Ordinal)
                .Select(monster => monster.Id),
            pool.Select(monster => monster.Id));
    }

    [Fact]
    public void TheDrawRespectsItsChallengeRatingCeiling()
    {
        var pool = MonsterPool.Draw(Content.Monsters, 1m);

        Assert.NotEmpty(pool);
        Assert.All(pool, monster => Assert.True(monster.ChallengeRating <= 1m));
    }

    [Fact]
    public void EveryMonsterInTheBookGetsExactlyOneGrade()
    {
        // Nothing falls between the four, which is the same rule the extractor runs on:
        // there is no unclassified state to land in.
        Assert.All(
            Content.Monsters,
            monster => Assert.Contains(MonsterPool.CoverageOf(monster), Enum.GetValues<MonsterCoverage>()));
    }
}
