using System.Text.Json.Nodes;
using SRDCombat.Content;
using SRDCombat.Core.Characters;
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
[Collection("SaveFile filesystem fault injection")]
public class RunSaveTests
{
    // Shared with the scenario-format classes rather than loaded a second time — see
    // TestContent's remarks and #319.
    private static readonly SrdContent Content = TestContent.Srd;

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

    [Theory]
    [InlineData("draft", "Draft")]
    [InlineData("state", "State")]
    public void AParsedSavedMemberMustContainEveryRequiredMember(string property, string expectedName)
    {
        var document = JsonNode.Parse(RunSave.ToJson(RunWithHistory()))!.AsObject();
        document["members"]![0]!.AsObject().Remove(property);

        var failure = Assert.ThrowsAny<Exception>(() => RunSave.FromJson(document.ToJsonString()));

        Assert.Contains(expectedName, failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("currentHitPoints", "CurrentHitPoints")]
    [InlineData("hitDiceRemaining", "HitDiceRemaining")]
    [InlineData("ragesRemaining", "RagesRemaining")]
    [InlineData("secondWindRemaining", "SecondWindRemaining")]
    [InlineData("actionSurgeRemaining", "ActionSurgeRemaining")]
    [InlineData("channelDivinityRemaining", "ChannelDivinityRemaining")]
    [InlineData("spellSlotsRemaining", "SpellSlotsRemaining")]
    [InlineData("experiencePoints", "ExperiencePoints")]
    [InlineData("isDead", "IsDead")]
    public void AParsedCharacterStateMustContainEveryRequiredMember(string property, string expectedName)
    {
        var document = JsonNode.Parse(RunSave.ToJson(RunWithHistory()))!.AsObject();
        document["members"]![0]!["state"]!.AsObject().Remove(property);

        var failure = Assert.ThrowsAny<Exception>(() => RunSave.FromJson(document.ToJsonString()));

        Assert.Contains(expectedName, failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("formatVersion", "FormatVersion")]
    [InlineData("ladder", "Ladder")]
    [InlineData("cleared", "Cleared")]
    [InlineData("members", "Members")]
    public void AParsedSavedRunMustContainEveryRequiredMember(string property, string expectedName)
    {
        var document = JsonNode.Parse(RunSave.ToJson(RunWithHistory()))!.AsObject();
        document.Remove(property);

        var failure = Assert.ThrowsAny<Exception>(() => RunSave.FromJson(document.ToJsonString()));

        Assert.Contains(expectedName, failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("name", "Name")]
    [InlineData("speciesId", "SpeciesId")]
    [InlineData("classId", "ClassId")]
    [InlineData("backgroundId", "BackgroundId")]
    [InlineData("level", "Level")]
    [InlineData("baseAbilityScores", "BaseAbilityScores")]
    public void AParsedCharacterDraftMustContainEveryRequiredMember(string property, string expectedName)
    {
        var document = JsonNode.Parse(RunSave.ToJson(RunWithHistory()))!.AsObject();
        document["members"]![0]!["draft"]!.AsObject().Remove(property);

        var failure = Assert.ThrowsAny<Exception>(() => RunSave.FromJson(document.ToJsonString()));

        Assert.Contains(expectedName, failure.Message, StringComparison.OrdinalIgnoreCase);
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
    /// The acceptance test for #286, its basic form: defeat never touches the save (it
    /// holds the state after the last <em>won</em> fight), so "reload after defeat
    /// retries the same fight" is exactly "loading this save twice and building its
    /// next fight builds the same encounter both times" — which is what a real
    /// <c>--continue</c> does on each fresh process, reseeding from
    /// <c>RunDice.SeedFor(run.Seed, run.Cleared)</c> at the top of the fight rather
    /// than rolling a new one.
    /// </summary>
    [Fact]
    public void ContinuingAfterDefeatBuildsTheSameNextFightEachTime()
    {
        var json = RunSave.ToJson(RunWithHistory());

        Fight ContinueAndBeginNext()
        {
            var run = GauntletRun.Resume(Content, RunSave.FromJson(json));
            var random = new SeededRandomSource(RunDice.SeedFor(run.Seed, run.Cleared));

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

    /// <summary>
    /// The stronger acceptance test #286 actually promises, stated in <c>RunDice</c>'s
    /// own remarks: not merely "the same monsters", but the exact same fight replayed
    /// dice for dice — every rest roll, every attack, every save, byte-for-byte —
    /// because <c>RunDice.SeedFor</c> depends only on the run's seed and how many
    /// fights came before this one, never on how much dice an earlier, differently
    /// played attempt at it happened to spend, nor on how that attempt ended.
    /// </summary>
    [Fact]
    public void ResumingReplaysTheNextFightDiceForDiceRegardlessOfHowEarlierFightsWerePlayed()
    {
        Fight DriveNextFight(GauntletRun run)
        {
            var random = new SeededRandomSource(RunDice.SeedFor(run.Seed, run.Cleared));

            run.PrepareForNext(random);
            var fight = run.BeginNext(random);
            SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
            run.CompleteFight(fight, random);

            return fight;
        }

        const int seed = 20260823;

        var straightThrough = GauntletRun.Start(Content, seed: seed);
        DriveNextFight(straightThrough);
        DriveNextFight(straightThrough);

        // The premise every assertion below rests on: two fights cleared in a row, so
        // there is a third fight to compare and a save worth resuming. If this fires,
        // pick another seed.
        Assert.Equal(RunOutcome.InProgress, straightThrough.Outcome);

        // The save point: after fight 2, before fight 3 — exactly what an autosave
        // holds and exactly what --continue would resume from.
        var savedAfterTwo = RunSave.ToJson(straightThrough);
        var thirdFightPlayedThrough = DriveNextFight(straightThrough);

        // A separate run, resumed from that save, driving only fight 3 — the same
        // fight, reached a different way.
        var resumed = GauntletRun.Resume(Content, RunSave.FromJson(savedAfterTwo));
        var thirdFightAfterResuming = DriveNextFight(resumed);

        Assert.Equal(
            thirdFightPlayedThrough.Built.Monsters.Select(monster => monster.Name),
            thirdFightAfterResuming.Built.Monsters.Select(monster => monster.Name));
        Assert.Equal(
            thirdFightPlayedThrough.Encounter.Combatants.Select(c => (c.Name, c.Position)),
            thirdFightAfterResuming.Encounter.Combatants.Select(c => (c.Name, c.Position)));

        // The transcript, not just the encounter: every roll, save and damage number
        // the fight produced, byte-for-byte the same — the whole segment, the rest
        // that opened it through every blow SimpleTacticsPolicy landed resolving it,
        // came from the same seeded stream both times.
        Assert.Equal(
            thirdFightPlayedThrough.Encounter.Log.Select(step => step.Narration),
            thirdFightAfterResuming.Encounter.Log.Select(step => step.Narration));
    }

    /// <summary>
    /// A save written before #286 carries no seed at all — but unlike a content-version
    /// mismatch, that is not refused. It loads exactly as written; <see cref="GauntletRun.Resume"/>
    /// falls back to 0 the way it does for a missing <c>GoldCopper</c>, and it is the
    /// client's job — not <see cref="RunSave"/>'s — to notice the gap and roll a real
    /// seed once via <see cref="GauntletRun.AdoptSeed"/>, which now writes it to disk
    /// immediately rather than waiting on the next autosave (#361) — see
    /// <c>SaveFileTests</c> for the on-disk round trip, since adoption is no longer a
    /// pure in-memory operation this test class can exercise.
    /// </summary>
    [Fact]
    public void LoadingASeedlessSaveSucceedsAndResumingFallsBackToZero()
    {
        var saved = RunWithHistory().ToSave() with { Seed = null };
        var loaded = RunSave.FromJson(ContentSerializer.Serialize(saved));

        Assert.Null(loaded.Seed);

        var run = GauntletRun.Resume(Content, loaded);

        Assert.Equal(0, run.Seed);
    }

    /// <summary>
    /// The acceptance test for #287's primary gate: a save whose content version does
    /// not match this build's is refused on <see cref="GauntletRun.Resume"/>, before
    /// anything tries to resolve a single id out of it, with a message naming both —
    /// truncated for display, per <see cref="RunSave.FromJson"/>'s sibling in
    /// <c>ContentDrift</c>.
    /// </summary>
    [Fact]
    public void ResumeRefusesAMismatchedContentVersion()
    {
        var saved = RunWithHistory().ToSave() with { ContentVersion = "not-a-real-fingerprint" };

        var failure = Assert.Throws<InvalidDataException>(
            () => GauntletRun.Resume(Content, saved));

        Assert.Contains("not-a-real-f", failure.Message, StringComparison.Ordinal);
        Assert.Contains(Content.ContentFingerprint[..12], failure.Message, StringComparison.Ordinal);
        Assert.Contains(
            "The file is untouched — the build that wrote it can still play it, or start a new run.",
            failure.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A save missing both #286's and #287's fields — the exact shape of a save
    /// written before either landed — loads, resumes, and is fully stamped with real
    /// values for both: the content version by the resolve <see cref="GauntletRun.ToSave"/>
    /// does on any autosave, the seed immediately by <see cref="GauntletRun.AdoptSeed"/>
    /// itself (#361).
    /// </summary>
    [Fact]
    public void ASaveMissingBothSeedAndContentVersionLoadsAndIsFullyStampedAfterOneAutosave()
    {
        var saved = RunWithHistory().ToSave() with { Seed = null, ContentVersion = null };
        var savePath = Path.Combine(Path.GetTempPath(), $"srdcombat-adoptseed-test-{Guid.NewGuid():N}.json");

        try
        {
            SaveFile.BeginNewRun(savePath, ContentSerializer.Serialize(saved));
            var loaded = SaveFile.LoadRun(savePath);

            Assert.NotNull(loaded.Saved);
            Assert.Null(loaded.Saved!.Seed);
            Assert.Null(loaded.Saved.ContentVersion);

            var run = GauntletRun.Resume(Content, loaded.Saved);
            Assert.Equal(RunOutcome.InProgress, run.Outcome);

            run.AdoptSeed(20260823, savePath);

            var reloaded = SaveFile.LoadRun(savePath);
            Assert.NotNull(reloaded.Saved);
            Assert.Equal(20260823, reloaded.Saved!.Seed);
            Assert.Equal(Content.ContentFingerprint, reloaded.Saved.ContentVersion);
        }
        finally
        {
            File.Delete(savePath);
            File.Delete(savePath + ".tmp");
            File.Delete(savePath + ".new");
            File.Delete(savePath + ".bak");
            File.Delete(savePath + ".old");
        }
    }

    /// <summary>
    /// The reversed policy #287 shipped with after review: a save written before it
    /// existed carries no content version, and that is not refused — there is no
    /// coarse comparison to make without one, so <see cref="GauntletRun.Resume"/>
    /// falls through to checking every id it resolves, exactly as it always does for
    /// the same-version edge case.
    /// </summary>
    [Fact]
    public void ResumeAcceptsASaveWithNoContentVersionAndFallsThroughToPerIdChecks()
    {
        var saved = RunWithHistory().ToSave() with { ContentVersion = null };

        var run = GauntletRun.Resume(Content, saved);

        Assert.Equal(RunOutcome.InProgress, run.Outcome);

        // The next autosave stamps the currently loaded content's fingerprint
        // regardless of what the resumed save had, so the gap does not persist.
        Assert.Equal(Content.ContentFingerprint, run.ToSave().ContentVersion);
    }

    /// <summary>
    /// The acceptance test for #287's backstop: even with a save's content version
    /// matching (so <see cref="GauntletRun.Resume"/>'s own coarse gate has nothing to
    /// catch), a draft naming a class this content build does not have refuses cleanly
    /// rather than throwing a bare <see cref="KeyNotFoundException"/> — the crash the
    /// review found, past both clients' exception filters.
    /// </summary>
    [Fact]
    public void ResumingRefusesADraftNamingAClassThisContentDoesNotHave()
    {
        var saved = RunWithHistory().ToSave();
        var tampered = saved with
        {
            Members =
            [
                saved.Members[0] with
                {
                    Draft = saved.Members[0].Draft with { ClassId = "class.nonexistent" },
                },
                .. saved.Members.Skip(1),
            ],
        };

        var failure = Assert.Throws<InvalidDataException>(() => GauntletRun.Resume(Content, tampered));

        Assert.Contains(
            "the save names class 'class.nonexistent' (for", failure.Message, StringComparison.Ordinal);
        Assert.Contains("which the loaded content does not have.", failure.Message, StringComparison.Ordinal);
        Assert.Contains(
            "The file is untouched — the build that wrote it can still play it, or start a new run.",
            failure.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// qc's finding 5: <see cref="CharacterResolver"/>'s own weapon, armor and magic
    /// item checks refuse drift with <see cref="ArgumentException"/>, not
    /// <see cref="InvalidDataException"/> — a Core-level convention distinct from
    /// <c>ContentDrift.Require</c>'s. A drifted armor id behind an equipped armor
    /// magic item used to reach a raw <c>content.Armor[armorId]</c> indexer
    /// (<c>CharacterResolver.cs:253</c>) and throw <see cref="KeyNotFoundException"/>
    /// straight past it; this pins that it refuses instead.
    /// </summary>
    [Fact]
    public void ResumingRefusesADraftWhoseArmorMagicItemPointsAtAMissingArmorId()
    {
        var saved = RunWithHistory().ToSave();
        var wearer = saved.Members.First(member => member.Draft.ArmorId is not null);

        var tampered = saved with
        {
            Members =
            [
                .. saved.Members.Select(member => member == wearer
                    ? member with
                    {
                        Draft = member.Draft with
                        {
                            ArmorId = "armor.nonexistent",
                            MagicItems =
                            [
                                new EquippedMagicItem
                                {
                                    ItemId = "magic-item.armor-plus-1-plus-2-or-plus-3",
                                    Variant = "+1",
                                },
                            ],
                        },
                    }
                    : member),
            ],
        };

        var failure = Assert.Throws<ArgumentException>(() => GauntletRun.Resume(Content, tampered));

        Assert.Contains("armor.nonexistent", failure.Message, StringComparison.Ordinal);
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
