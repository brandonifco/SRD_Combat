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

    private static readonly SrdContent Content = TestContent.Srd;

    [Fact]
    public void TheTierOnePoolIsBigEnoughToBuildAGauntletFrom()
    {
        // The floor dropped 75 -> 68 on 2026-08-24 (span-accounting regeneration,
        // #382). The census the switchover ran is what moved this, not a change of
        // heart about the bar: entries that always carried a rider the engine never
        // executed — hidden behind the old sentence-credit accounting — now show that
        // rider as honest residue and demote out of the pool. This floor is
        // TRANSITIONAL, not a tuning target: it is expected to RISE as #370-#373's
        // semantic fixes restore the demoted creatures and #267's fill-ins land. Do
        // not read a future increase as "the bar moved" — it is the pool recovering
        // what the old accounting was silently claiming for it.
        //
        // The floor rose 68 -> 73 on 2026-08-25 (#371's PR #408): Blood Hawk, Swarm
        // of Bats, Swarm of Crawling Claws, Swarm of Insects and Swarm of Rats all
        // re-entered on the Bloodied alternative-damage tier now structuring and
        // executing — the ratchet #390's own acceptance criteria requires, so a
        // floor that never rises again as more of #390's ledger lands has stopped
        // doing its job. Two of #390's seven shape-1 names stay out: Swarm of
        // Piranhas (blocked separately by an attack-header Advantage parenthetical)
        // and Swarm of Venomous Snakes (an em-dash "or…plus" combination #371 leaves
        // as residue rather than mis-structuring — #409).
        var pool = MonsterPool.Draw(Content.Monsters, TierOneMaximum);

        Assert.True(
            pool.Count >= 73,
            $"The tier-1 pool has fallen to {pool.Count} monsters; it was 75 before the 2026-08-24 " +
            "span-accounting regeneration (#382), 68 before #371's alternative-damage restorations " +
            "(2026-08-25, PR #408), 81 when the genre cut landed (2026-08-20, TraditionalFoes), 116 " +
            "before that cut, and 131 before #52 dropped the creatures the SRD prices as equipment " +
            "and #75 dropped the ones with nowhere to fight.");
    }

    [Fact]
    public void EveryChallengeRatingInTheBandHasSomethingToDrawFrom()
    {
        // A pool of 81 would still be useless if it were all CR 0. The gauntlet needs a
        // choice at every step of the ladder, so the floor is per band, not overall.
        //
        // CR 0's and CR 4's floors were 3, deliberately below the others': the genre
        // cut removed the mundane animals that padded both bands — CR 0 keeps
        // Awakened Shrub, Giant Fire Beetle and Lemure, CR 4 kept Ettin, Guard Captain
        // and the Red Dragon Wyrmling — and Brandon accepted the thin bands with
        // expansion fill-ins planned to repopulate them.
        //
        // CR 4's floor dropped 3 -> 2 on 2026-08-24 (span-accounting regeneration,
        // #382): the Ettin's Morningstar has always printed "and the target has
        // Disadvantage on the next attack roll it makes before the end of its next
        // turn", a rider nothing executes, hidden until now behind the old
        // "Hit:"-credits-the-whole-sentence bug. The census is telling the truth
        // about a gap that was always there. TRANSITIONAL, not a tuning target — see
        // TheTierOnePoolIsBigEnoughToBuildAGauntletFrom's comment; raise it back to 3
        // when the Ettin (or another CR 4 creature) is restored.
        var pool = MonsterPool.Draw(Content.Monsters, TierOneMaximum);

        foreach (var (rating, floor) in new[]
                 { (0m, 3), (0.125m, 4), (0.25m, 4), (0.5m, 4), (1m, 4), (2m, 4), (3m, 4), (4m, 2) })
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
        // The Ghast's Bite and Claw are the whole of its turn and both are fully
        // modelled; its Stench trait is not (a Trait-section SavingThrow entry
        // UseEntry never reaches at all — design §2.5's own named example — so its
        // rider claims nothing and the Emanation's own qualifiers are residue too).
        // That is the line Playable draws — the turn is right, something outside it
        // is not.
        //
        // This used to be the Ankheg's Bite and Acid Spray, but the 2026-08-24
        // span-accounting regeneration (#382) gave the Ankheg's Bite honest residue of
        // its own — the Advantage parenthetical in its attack header, design §2.3's
        // own worked example ("(with Advantage if the target is Grappled by the
        // ankheg)") — which drops the Ankheg to Diminished and makes it the wrong
        // example for this test now.
        var ghast = Content.MonstersById["monster.ghast"];

        Assert.Equal(MonsterCoverage.Playable, MonsterPool.CoverageOf(ghast));
        Assert.True(MonsterPool.Admits(ghast));
        Assert.Contains(ghast.Entries, entry => !entry.IsFullyModelled);

        // And the near miss that shows the grade is about position, not count: the
        // Specter's Life Drain is its only action and loses "its Hit Point maximum
        // decreases", so one unmodelled clause inside the turn drops it a grade.
        var specter = Content.MonstersById["monster.specter"];

        Assert.Equal(MonsterCoverage.Diminished, MonsterPool.CoverageOf(specter));
        Assert.False(MonsterPool.Admits(specter));
    }

    [Fact]
    public void AMultiattackReplaceClauseDropsTheGrade()
    {
        // The canary from #290: before the replace-clause accounting fix, the Lion's
        // Multiattack ("It can replace one attack with a use of Roar.") read as fully
        // modelled and the Lion graded Playable; the Pirate's did too and it graded
        // Complete. Neither creature's replacement option ever fires, so both must now
        // grade below Playable and drop out of the default pool.
        var lion = Content.MonstersById["monster.lion"];

        Assert.Equal(MonsterCoverage.Diminished, MonsterPool.CoverageOf(lion));
        Assert.False(MonsterPool.Admits(lion));

        var pirate = Content.MonstersById["monster.pirate"];

        Assert.Equal(MonsterCoverage.Diminished, MonsterPool.CoverageOf(pirate));
        Assert.False(MonsterPool.Admits(pirate));
    }

    [Fact]
    public void ABundledUseInsideTheCompositionSentenceDropsTheGrade()
    {
        // The canary from #341: a Multiattack can fold an unexecuted use inside its own
        // composition sentence — "The marilith makes six Pact Blade attacks and uses
        // Constrict." — where DescribesTheComposition matches the composition it
        // recognises and says nothing about what rides beside it. Before this fix the
        // Marilith graded Playable on the strength of a Constrict that never fires.
        var marilith = Content.MonstersById["monster.marilith"];

        Assert.Equal(MonsterCoverage.Diminished, MonsterPool.CoverageOf(marilith));
        Assert.False(MonsterPool.Admits(marilith));

        // The Clay Golem's own composition sentence carried the same shape ("or it
        // makes three Slam attacks if it used Hasten this turn") and graded Playable
        // the same way; #342's alternative-composition accounting fixed it first, so
        // by the time this fix landed it was already Diminished. Pinned here too since
        // the issue that reported both (#341) named it as one of the two.
        var clayGolem = Content.MonstersById["monster.clay-golem"];

        Assert.Equal(MonsterCoverage.Diminished, MonsterPool.CoverageOf(clayGolem));
        Assert.False(MonsterPool.Admits(clayGolem));
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
