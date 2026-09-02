using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Game;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The long-haul loot guard, one seed per class so xUnit runs the ten runs across
/// cores instead of one after another.
/// </summary>
/// <remarks>
/// <para>
/// This was a single <c>[Fact]</c> in <see cref="LootTests"/> that looped seeds 1..10,
/// each playing the whole default thirty-rung ladder to completion under the tactics
/// policy. That one method was the suite's single longest serial block (~208s measured):
/// in xUnit v2 the tests inside a class run <em>serially</em>, and parallelism is only
/// across collections, so ten full gauntlets in a row could never overlap. Splitting one
/// seed per class puts each run in its own collection, and the ten spread across the
/// available cores.
/// </para>
/// <para>
/// <b>Nothing about what is tested changed.</b> The seeds are the same 1..10, the
/// assertion is the same — thirty rungs of drops, upgrades and re-resolves must never
/// produce a draft the resolver refuses mid-run, i.e. the run always reaches a decided
/// outcome — and each run is byte-identical to before because it is fully isolated: a
/// fresh (immutable, shared) content read, a fresh <see cref="GauntletRun"/>, and a fresh
/// <see cref="SeededRandomSource"/> seeded from this class's seed. The only observable
/// difference is that a failure now names the exact seed by its class.
/// </para>
/// </remarks>
public abstract class SeededLootRunTests
{
    /// <summary>The seed this class plays; one concrete class per value 1..10.</summary>
    protected abstract int Seed { get; }

    [Fact]
    public void ASeededRunWithLootPlaysToItsEndWithoutRefusals()
    {
        var run = GauntletRun.Start(TestContent.Srd);
        var random = new SeededRandomSource(Seed);

        while (run.Next is not null)
        {
            run.PrepareForNext(random);
            var fight = run.BeginNext(random);
            SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
            run.CompleteFight(fight, random);
        }

        Assert.NotEqual(RunOutcome.InProgress, run.Outcome);
    }
}

public sealed class SeededLootRunSeed1Tests : SeededLootRunTests { protected override int Seed => 1; }

public sealed class SeededLootRunSeed2Tests : SeededLootRunTests { protected override int Seed => 2; }

public sealed class SeededLootRunSeed3Tests : SeededLootRunTests { protected override int Seed => 3; }

public sealed class SeededLootRunSeed4Tests : SeededLootRunTests { protected override int Seed => 4; }

public sealed class SeededLootRunSeed5Tests : SeededLootRunTests { protected override int Seed => 5; }

public sealed class SeededLootRunSeed6Tests : SeededLootRunTests { protected override int Seed => 6; }

public sealed class SeededLootRunSeed7Tests : SeededLootRunTests { protected override int Seed => 7; }

public sealed class SeededLootRunSeed8Tests : SeededLootRunTests { protected override int Seed => 8; }

public sealed class SeededLootRunSeed9Tests : SeededLootRunTests { protected override int Seed => 9; }

public sealed class SeededLootRunSeed10Tests : SeededLootRunTests { protected override int Seed => 10; }
