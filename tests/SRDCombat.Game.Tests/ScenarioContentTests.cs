using SRDCombat.Content;
using SRDCombat.Core.Characters;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Where a scenario meets the content that gives its ids meaning: everything missing is
/// named, the party resolves through the rules that make it, and a fingerprint that
/// disagrees is said out loud without refusing anything.
/// </summary>
public class ScenarioContentTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    private static CharacterDraft Fighter { get; } = PregeneratedParty.Build(Content)[0].Draft;

    [Fact]
    public void AScenarioNamingOnlyContentThisBuildHasChecksClean()
    {
        var check = ScenarioContent.CheckAgainst(Roster("monster.ogre"), Content);

        Assert.Empty(check.Errors);
        Assert.Empty(check.Notices);
        Assert.True(check.IsValid);
    }

    [Fact]
    public void AMissingMonsterIsRefusedByName()
    {
        var error = Assert.Single(
            ScenarioContent.CheckAgainst(Roster("monster.tarrasque-of-mars"), Content).Errors);

        Assert.Contains("monster 'monster.tarrasque-of-mars'", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scenario's refusal is not a save's. Both go through <c>ContentDrift</c> so the
    /// wording is single-sourced, but a scenario must not tell the reader to start a new
    /// run — there is no run.
    /// </summary>
    [Fact]
    public void AScenarioRefusalNamesTheScenarioAndOffersAScenariosRemedy()
    {
        var error = Assert.Single(ScenarioContent.CheckAgainst(Roster("monster.nothing"), Content).Errors);

        Assert.StartsWith("the scenario names", error, StringComparison.Ordinal);
        Assert.DoesNotContain("start a new run", error, StringComparison.Ordinal);
        Assert.Contains("Re-author the scenario", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every miss, not the first one. A scenario naming three vanished things is told
    /// about three of them — the whole reason the id sweep formats
    /// <c>ContentDrift.MissingMessage</c> rather than calling <c>Require</c> in a loop.
    /// </summary>
    [Fact]
    public void EveryMissingIdIsReportedNotJustTheFirst()
    {
        var scenario = Roster("monster.gone-one", "monster.gone-two") with
        {
            Party = new ScenarioParty
            {
                Members =
                [
                    new ScenarioMember
                    {
                        Level = 3,
                        Draft = Fighter with
                        {
                            SpeciesId = "species.gone",
                            WeaponIds = ["weapon.gone", "weapon.club"],
                            ArmorId = "armor.gone",
                        },
                    },
                ],
            },
        };

        var errors = ScenarioContent.CheckAgainst(scenario, Content).Errors;

        Assert.Contains(errors, error => error.Contains("monster 'monster.gone-one'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("monster 'monster.gone-two'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("species 'species.gone'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("weapon 'weapon.gone'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("armor 'armor.gone'", StringComparison.Ordinal));
    }

    /// <summary>
    /// A draft the resolver refuses for reasons that are not a missing id — here, Expertise
    /// in a skill the character has no proficiency in — is reported rather than thrown, so
    /// a library check names the broken file instead of dying on it.
    /// </summary>
    [Fact]
    public void APartyThatDoesNotResolveIsReportedRatherThanThrown()
    {
        var scenario = Roster("monster.ogre") with
        {
            Party = new ScenarioParty
            {
                Members =
                [
                    new ScenarioMember { Level = 3, Draft = Fighter with { ExpertiseSkills = ["Arcana"] } },
                ],
            },
        };

        Assert.Contains(
            "the party does not resolve",
            Assert.Single(ScenarioContent.CheckAgainst(scenario, Content).Errors),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The stated divergence from <see cref="GauntletRun.Resume"/>, which refuses a
    /// fingerprint mismatch outright: a scenario is a question asked of the current build,
    /// and refusing the whole library after every extractor regeneration would make the
    /// surface useless inside a week. The per-id checks are what actually refuse.
    /// </summary>
    [Fact]
    public void AContentVersionMismatchIsANoticeAndRefusesNothing()
    {
        var check = ScenarioContent.CheckAgainst(
            Roster("monster.ogre") with { ContentVersion = "DEADBEEFDEADBEEF" },
            Content);

        Assert.Empty(check.Errors);
        Assert.True(check.IsValid);
        Assert.Contains("DEADBEEFDEAD", Assert.Single(check.Notices), StringComparison.Ordinal);
    }

    [Fact]
    public void AMatchingContentVersionSaysNothing() =>
        Assert.Empty(
            ScenarioContent.CheckAgainst(
                Roster("monster.ogre") with { ContentVersion = Content.ContentFingerprint },
                Content).Notices);

    /// <summary>
    /// Criterion 7: the preset resolves through <see cref="PregeneratedParty.Build"/> at
    /// the scenario's level, so a change to the pregens applies to every scenario using
    /// it. A library that froze copies of their drafts would drift one file at a time
    /// with nothing failing.
    /// </summary>
    [Fact]
    public void ThePregeneratedPresetResolvesThroughPregeneratedPartyRatherThanAStoredCopy()
    {
        var resolved = ScenarioContent.ResolveParty(
            Roster("monster.ogre") with { Party = new ScenarioParty { PregeneratedLevel = 4 } },
            Content);

        var built = PregeneratedParty.Build(Content, level: 4);

        Assert.Equal(
            built.Select(member => (member.Draft.Name, member.Sheet.Level, member.Sheet.MaximumHitPoints)),
            resolved.Select(member => (member.Draft.Name, member.Sheet.Level, member.Sheet.MaximumHitPoints)));
    }

    /// <summary>
    /// A member is resolved at the level the <em>scenario</em> names, not the one the
    /// draft carries — the same rule <see cref="PregeneratedParty.Resolve"/> follows,
    /// because levelling in this game is re-resolving a draft.
    /// </summary>
    [Fact]
    public void AMemberResolvesAtTheLevelTheScenarioNamesRatherThanTheDraftsOwn()
    {
        var scenario = Roster("monster.ogre") with
        {
            Party = new ScenarioParty
            {
                Members = [new ScenarioMember { Level = 5, Draft = Fighter with { Level = 1 } }],
            },
        };

        Assert.Equal(5, Assert.Single(ScenarioContent.ResolveParty(scenario, Content)).Sheet.Level);
    }

    private static BattleScenario Roster(params string[] monsterIds) => new()
    {
        FormatVersion = ScenarioFile.CurrentFormatVersion,
        Name = "check",
        Notes = "A fixture for the content checks.",
        Party = new ScenarioParty { PregeneratedLevel = 3 },
        Enemies = new ScenarioEnemies
        {
            Roster = [.. monsterIds.Select(id => new ScenarioRosterEntry { MonsterId = id, Count = 1 })],
        },
    };
}
