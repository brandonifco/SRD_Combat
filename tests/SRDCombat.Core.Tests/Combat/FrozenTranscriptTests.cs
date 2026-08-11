using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Phase 1's acceptance test: two authored sides fight to a conclusion, deterministically.
/// </summary>
/// <remarks>
/// <para>
/// The frozen transcript pins the exact narrated sequence of a whole fight. It is the
/// single most valuable test in the suite for the same reason it was in the neighbouring
/// 5eGoldBox project: it proves a refactor was behaviour-preserving in a way no
/// collection of unit tests can, because it covers the interaction between initiative,
/// movement, Opportunity Attacks, damage and death all at once.
/// </para>
/// <para>
/// When it fails, read the diff before touching the fixture. A change to the transcript
/// is a change to how the game plays. Regenerate it only once the new behaviour is
/// understood and intended, using <see cref="TranscriptWriter"/>.
/// </para>
/// </remarks>
public class FrozenTranscriptTests
{
    internal const string FixtureName = "skirmish-transcript.txt";

    [Fact]
    public void AWholeFight_MatchesTheFrozenTranscript()
    {
        var path = Path.Combine(RepositoryPaths.FixtureDirectory, FixtureName);

        Assert.True(File.Exists(path), $"Missing fixture '{path}'. Regenerate it with TranscriptWriter.");

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd('\n');
        var actual = Render(RunSkirmish()).ReplaceLineEndings("\n").TrimEnd('\n');

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TheSameSeed_AlwaysProducesTheSameFight()
    {
        // Belt and braces against the transcript passing for the wrong reason: if
        // anything in the engine reached for ambient randomness, two runs from the same
        // seed would diverge even though the committed fixture happened to match one.
        Assert.Equal(Render(RunSkirmish()), Render(RunSkirmish()));
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentFights()
    {
        var first = Render(RunSkirmish());

        var other = SkirmishScenario.Create(new SeededRandomSource(SkirmishScenario.Seed + 1));
        SimpleTacticsPolicy.RunToCompletion(other);

        Assert.NotEqual(first, Render(other));
    }

    [Fact]
    public void TheFightActuallyResolves()
    {
        var encounter = RunSkirmish();

        Assert.True(encounter.IsComplete);
        Assert.NotNull(encounter.WinningSide);

        // Every combatant on the losing side is out of the fight, and at least one is
        // genuinely dead rather than merely unconscious.
        var losers = encounter.Combatants.Where(c => c.SideId != encounter.WinningSide).ToList();
        Assert.All(losers, combatant => Assert.False(combatant.IsActive));
        Assert.Contains(losers, combatant => combatant.IsDead);
    }

    [Fact]
    public void TheFightExercisesTheHardParts()
    {
        // The transcript's value depends on what it happens to cover. The scenario's
        // seed was chosen so this one fight reaches the interactions most likely to
        // break, and this guards against a future change quietly producing a duller
        // fight that still passes the byte-for-byte comparison.
        var kinds = RunSkirmish().Log.Select(step => step.Kind).ToHashSet();

        Assert.Contains(CombatStepKind.Move, kinds);
        Assert.Contains(CombatStepKind.OpportunityAttack, kinds);
        Assert.Contains(CombatStepKind.Downed, kinds);
        Assert.Contains(CombatStepKind.DeathSave, kinds);
        Assert.Contains(CombatStepKind.Died, kinds);
        Assert.Contains(CombatStepKind.EncounterEnded, kinds);
    }

    [Fact]
    public void TheTranscriptNarratesEveryRoll()
    {
        var encounter = RunSkirmish();

        // The Gold Box-style log this project committed to showing needs the numbers, not
        // a summary. Every attack line carries its d20 result and the AC it was against.
        var attacks = encounter.Log.Where(step => step.Kind == CombatStepKind.Attack).ToList();

        Assert.NotEmpty(attacks);
        Assert.All(attacks, step =>
        {
            Assert.Contains("d20 ", step.Narration, StringComparison.Ordinal);
            Assert.Contains("vs AC ", step.Narration, StringComparison.Ordinal);
        });

        var damage = encounter.Log.Where(step => step.Kind == CombatStepKind.Damage).ToList();
        Assert.NotEmpty(damage);
        Assert.All(damage, step => Assert.Contains("damage", step.Narration, StringComparison.Ordinal));
    }

    internal static Encounter RunSkirmish()
    {
        var encounter = SkirmishScenario.Create();
        SimpleTacticsPolicy.RunToCompletion(encounter);
        return encounter;
    }

    internal static string Render(Encounter encounter) =>
        string.Join('\n', encounter.Log.Select(step => step.Narration)) + '\n';
}

/// <summary>
/// Regenerates the frozen transcript.
/// </summary>
/// <remarks>
/// Skipped by default and run by hand: remove the Skip, run it, put the Skip back, and
/// review the resulting diff as carefully as any code change. The same un-skip/run/
/// re-skip convention the neighbouring project uses for its own fixture writers.
/// </remarks>
public class TranscriptWriter
{
    [Fact(Skip = "Writes the committed fixture. Un-skip, run, re-skip, and review the diff.")]
    public void WriteSkirmishTranscript()
    {
        Directory.CreateDirectory(RepositoryPaths.FixtureDirectory);

        File.WriteAllText(
            Path.Combine(RepositoryPaths.FixtureDirectory, FrozenTranscriptTests.FixtureName),
            FrozenTranscriptTests.Render(FrozenTranscriptTests.RunSkirmish()));
    }
}
