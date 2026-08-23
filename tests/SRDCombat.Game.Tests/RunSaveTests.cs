using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Game;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The save format: drafts plus progress, never resolved sheets, refused rather than
/// repaired when anything is wrong with it.
/// </summary>
/// <remarks>
/// These pin the on-disk shape the way <c>ContentSerializerTests</c> pins content — a
/// change to what a save holds should fail here loudly, not surface as an unreadable
/// file after the format has drifted.
/// </remarks>
public class RunSaveTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    /// <summary>A run with one fight behind it, so the save holds real progress.</summary>
    private static GauntletRun RunWithHistory(int seed = 11)
    {
        var run = GauntletRun.Start(Content);
        var random = new SeededRandomSource(seed);

        run.PrepareForNext(random);
        var fight = run.BeginNext(random);
        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
        run.CompleteFight(fight);

        // The premise every test here rests on: the seed clears the first fight, so the
        // save holds a run worth resuming. If this fires, pick another seed.
        Assert.Equal(RunOutcome.InProgress, run.Outcome);

        return run;
    }

    [Fact]
    public void ASaveHoldsChoicesAndProgressAndNothingDerived()
    {
        var json = RunSave.ToJson(RunWithHistory());

        // The choices and the progress are there...
        Assert.Contains("\"formatVersion\"", json, StringComparison.Ordinal);
        Assert.Contains("\"cleared\"", json, StringComparison.Ordinal);
        Assert.Contains("\"classId\"", json, StringComparison.Ordinal);
        Assert.Contains("\"experiencePoints\"", json, StringComparison.Ordinal);
        Assert.Contains("\"currentHitPoints\"", json, StringComparison.Ordinal);

        // ...and nothing the resolver derives is, so a save cannot drift from the rules
        // that make the party. These are sheet fields, and the sheet is not saved.
        Assert.DoesNotContain("maximumHitPoints", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("armorClass", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("proficiencyBonus", json, StringComparison.OrdinalIgnoreCase);

        // Level is derived from experience, so it must not be persisted on the state.
        Assert.DoesNotContain("\"level\": ", json.Split("\"draft\"")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ASaveRoundTripsExactly()
    {
        var original = RunWithHistory();

        var reloaded = GauntletRun.Resume(Content, RunSave.FromJson(RunSave.ToJson(original)));

        // Canonical-JSON equality: if everything a save holds survives the trip, the
        // two serialize to the same bytes.
        Assert.Equal(RunSave.ToJson(original), RunSave.ToJson(reloaded));
        Assert.Equal(original.Cleared, reloaded.Cleared);
        Assert.Equal(original.Casualties, reloaded.Casualties);
    }

    [Fact]
    public void AResumedRunPlaysOnToItsEnd()
    {
        var saved = RunSave.FromJson(RunSave.ToJson(RunWithHistory()));
        var run = GauntletRun.Resume(Content, saved);
        var random = new SeededRandomSource(99);

        while (run.Next is not null)
        {
            run.PrepareForNext(random);
            var fight = run.BeginNext(random);
            SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
            run.CompleteFight(fight);
        }

        Assert.NotEqual(RunOutcome.InProgress, run.Outcome);
        Assert.True(run.Cleared >= saved.Cleared, "Resuming must not lose cleared fights.");
    }

    [Fact]
    public void AResumedCharacterIsAsStrongAsTheirExperienceSaysNotTheFile()
    {
        // The draft's level is overridden by what experience has earned, so a
        // hand-edited save cannot smuggle in a level the run never awarded.
        var saved = RunWithHistory().ToSave();
        var inflated = saved with
        {
            Members = [.. saved.Members.Select(member =>
                member with { Draft = member.Draft with { Level = 5 } })],
        };

        var run = GauntletRun.Resume(Content, inflated);

        foreach (var (member, state) in run.Party.Zip(run.States))
        {
            Assert.Equal(state.Level, member.Sheet.Level);
        }
    }

    /// <summary>
    /// The acceptance test for #286: defeat never touches the save (it holds the state
    /// after the last <em>won</em> fight), so "reload after defeat retries the same
    /// fight" is exactly "loading this save twice and building its next fight builds
    /// the same encounter both times" — which is what a real <c>--continue</c> does on
    /// each fresh process, seeding its dice from <see cref="GauntletRun.Seed"/> rather
    /// than rolling a new one.
    /// </summary>
    [Fact]
    public void ContinuingAfterDefeatBuildsTheSameNextFightEachTime()
    {
        var json = RunSave.ToJson(RunWithHistory());

        Fight ContinueAndBeginNext()
        {
            var run = GauntletRun.Resume(Content, RunSave.FromJson(json));
            var random = new SeededRandomSource(run.Seed);

            run.PrepareForNext(random);
            return run.BeginNext(random);
        }

        var first = ContinueAndBeginNext();
        var second = ContinueAndBeginNext();

        Assert.Equal(
            first.Built.Monsters.Select(monster => monster.Name),
            second.Built.Monsters.Select(monster => monster.Name));
        Assert.Equal(
            first.Encounter.Combatants.Select(combatant => (combatant.Name, combatant.Position)),
            second.Encounter.Combatants.Select(combatant => (combatant.Name, combatant.Position)));
    }

    [Fact]
    public void LoadRefusesASaveWithoutASeed()
    {
        // The exact shape of a save written before #286: everything else is fine, but
        // there is no seed to rebuild the next fight from, and inventing one would make
        // "the same fight" a lie the moment this file is continued.
        var saved = RunWithHistory().ToSave() with { Seed = null };

        var failure = Assert.Throws<InvalidDataException>(
            () => RunSave.FromJson(ContentSerializer.Serialize(saved)));

        Assert.Contains("seed", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadRejectsAnUnknownProperty()
    {
        // The same rule content follows: a typo in a save is a bug, not something to
        // skip past silently.
        var json = RunSave.ToJson(RunWithHistory())
            .Replace("\"cleared\"", "\"clearedFights\"", StringComparison.Ordinal);

        Assert.ThrowsAny<Exception>(() => RunSave.FromJson(json));
    }

    [Fact]
    public void LoadRefusesAnotherFormatVersion()
    {
        var saved = RunWithHistory().ToSave() with { FormatVersion = 99 };

        var failure = Assert.Throws<InvalidDataException>(
            () => RunSave.FromJson(ContentSerializer.Serialize(saved)));

        Assert.Contains("99", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadRefusesAPositionOffTheLadder()
    {
        var saved = RunWithHistory().ToSave() with { Cleared = 31 };

        Assert.Throws<InvalidDataException>(() => RunSave.FromJson(ContentSerializer.Serialize(saved)));
    }

    [Fact]
    public void LoadRefusesARunWithNobodyLeftToResume()
    {
        // Unreachable by playing — a save is written after a won fight — so refusing it
        // is strictly about hand-edited files.
        var saved = RunWithHistory().ToSave();
        var wiped = saved with
        {
            Members = [.. saved.Members.Select(member =>
                member with { State = member.State with { IsDead = true } })],
        };

        Assert.Throws<InvalidDataException>(() => RunSave.FromJson(ContentSerializer.Serialize(wiped)));
    }

    [Fact]
    public void AFinishedRunResumesAsFinished()
    {
        var saved = RunWithHistory().ToSave();
        var finished = saved with { Cleared = saved.Ladder.Count };

        var run = GauntletRun.Resume(Content, RunSave.FromJson(ContentSerializer.Serialize(finished)));

        Assert.Equal(RunOutcome.Survived, run.Outcome);
        Assert.Null(run.Next);
    }

    [Fact]
    public void ADeadCharacterStaysDeadThroughASave()
    {
        var saved = RunWithHistory().ToSave();
        var withACasualty = saved with
        {
            Members =
            [
                saved.Members[0] with
                {
                    State = saved.Members[0].State with { IsDead = true, CurrentHitPoints = 0 },
                },
                .. saved.Members.Skip(1),
            ],
            Casualties = [saved.Members[0].Draft.Name],
        };

        var run = GauntletRun.Resume(Content, RunSave.FromJson(ContentSerializer.Serialize(withACasualty)));

        Assert.Contains(saved.Members[0].Draft.Name, run.Fallen);
        Assert.Contains(saved.Members[0].Draft.Name, run.Casualties);
    }
}
