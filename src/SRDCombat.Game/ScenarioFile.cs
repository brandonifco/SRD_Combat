using System.Text.Json;
using SRDCombat.Content;
using SRDCombat.Core.Combat;

namespace SRDCombat.Game;

/// <summary>A scenario read from JSON, or every reason it could not be one.</summary>
/// <remarks>
/// <b>A result record rather than a <c>bool</c> and two <c>out</c>s</b>, following
/// <see cref="RosterParser.Roster"/> — the shape this project already uses for "a value
/// or the reasons it is not one", and the shape #491 asked the battle builder's spec type
/// to settle on. Two consumers make the difference concrete: a builder UI wants to show
/// every problem with a file at once, and a headless batch wants to print them and move
/// to the next scenario. Neither has a command line to throw at.
/// </remarks>
/// <param name="Scenario">The scenario, or null when <paramref name="Errors"/> is not empty.</param>
/// <param name="Errors">Every reason the JSON is not a scenario, in the order found.</param>
public sealed record ScenarioLoad(BattleScenario? Scenario, IReadOnlyList<string> Errors)
{
    /// <summary>Whether there is a scenario to use.</summary>
    public bool IsValid => Errors.Count == 0 && Scenario is not null;
}

/// <summary>
/// Reads and writes <see cref="BattleScenario"/> JSON.
/// </summary>
/// <remarks>
/// <para>
/// <b>Strict machine JSON, decided rather than defaulted.</b> Brandon's answer on
/// 2026-08-26: a scenario file is the authoring surface's artifact and he will not open
/// one in a text editor. So this goes through <see cref="ContentSerializer"/> — the same
/// strictness that guards content and saves, <c>UnmappedMemberHandling.Disallow</c>
/// included, so a typo is refused naming the property rather than skipped — and the
/// format's whole documentation is <c>BattleScenarioShapeTests</c> pinning it. There is
/// deliberately no tolerant parser, no authoring grammar and no hand-authoring guide;
/// were one ever wanted, a lenient front end is purely additive on top of this and
/// nothing here would change.
/// </para>
/// <para>
/// <b>The structure/content split is <see cref="RunSave.FromJson"/>'s, verbatim.</b> This
/// class validates the file's own shape and nothing content-dependent — it has no content
/// to check against, by design. <see cref="ScenarioContent"/> is where a scenario meets
/// content, the way <see cref="GauntletRun.Resume"/> is for a save. The two are separate
/// because the builder UI and the batch runner will want them at different moments, and
/// because a file that is structurally wrong should say so without a 330-monster corpus
/// being loaded first.
/// </para>
/// <para>
/// There is no disk-rotation sibling to <see cref="SaveFile"/> here, deliberately. A save
/// is rewritten under the player's feet after every cleared fight, which is what makes a
/// crash window worth a temp-file rotation (#285, #332, #361, #367); a scenario is
/// written once by an author and read many times.
/// </para>
/// </remarks>
public static class ScenarioFile
{
    /// <summary>The format this build writes and the only one it reads.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// The committed scenario library's directory, relative to the repository root.
    /// </summary>
    /// <remarks>
    /// Committed on purpose (design §5a): versioned, diffable, quotable in an issue,
    /// reviewable in a pull request, and usable by agents and CI. Named here rather than
    /// in each consumer so the runner, the batch tool and the library test cannot
    /// disagree about where scenarios live.
    /// </remarks>
    public const string DirectoryName = "scenarios";

    /// <summary>The extension a scenario file carries.</summary>
    public const string Extension = ".scenario.json";

    /// <summary>Serializes a scenario.</summary>
    public static string ToJson(BattleScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        return ContentSerializer.Serialize(scenario);
    }

    /// <summary>
    /// Deserializes and validates a scenario's own structure. Nothing is repaired,
    /// defaulted or clamped: every problem is reported by name, with the value read and
    /// the range accepted, in <see cref="RosterParser"/>'s and
    /// <see cref="ScenarioArguments"/>' voice.
    /// </summary>
    /// <remarks>
    /// Parse failures — malformed JSON, an unmapped property, a missing
    /// <c>required</c> member — are reported as a single error carrying the
    /// serializer's own message, which names the property. Structural failures past
    /// that point are collected rather than thrown one at a time, because an author
    /// fixing a file wants the whole list.
    /// </remarks>
    public static ScenarioLoad FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        BattleScenario scenario;

        try
        {
            scenario = ContentSerializer.Deserialize<BattleScenario>(json);
        }
        catch (JsonException failure)
        {
            return new ScenarioLoad(null, [failure.Message]);
        }

        var errors = new List<string>();

        if (scenario.FormatVersion != CurrentFormatVersion)
        {
            errors.Add(
                $"formatVersion {scenario.FormatVersion} is not this build's {CurrentFormatVersion}; refusing to guess.");
        }

        if (string.IsNullOrWhiteSpace(scenario.Name))
        {
            errors.Add("name: a scenario needs a label to be reported and found by.");
        }

        CheckParty(scenario.Party, errors);
        CheckEnemies(scenario.Enemies, errors);
        CheckObjective(scenario.Objective, errors);

        return errors.Count == 0 ? new ScenarioLoad(scenario, []) : new ScenarioLoad(null, errors);
    }

    private static void CheckParty(ScenarioParty party, List<string> errors)
    {
        switch (party)
        {
            case { PregeneratedLevel: null, Members: null }:
                errors.Add(
                    "party: names neither a pregeneratedLevel nor members; a scenario needs one or the other.");
                return;

            case { PregeneratedLevel: not null, Members: not null }:
                errors.Add(
                    "party: names both a pregeneratedLevel and members; a scenario takes one or the other, not both.");
                return;

            case { PregeneratedLevel: { } level }:
                CheckLevel(level, "party.pregeneratedLevel", errors);
                return;
        }

        var members = party.Members!;

        if (members.Count == 0)
        {
            errors.Add("party.members: an explicit party needs at least one member.");
        }
        else if (members.Count > ScenarioParty.MaximumMembers)
        {
            errors.Add(
                $"party.members: {members.Count} members, which is more than the {ScenarioParty.MaximumMembers} " +
                "a scenario may field.");
        }

        for (var index = 0; index < members.Count; index++)
        {
            CheckLevel(members[index].Level, $"party.members[{index}].level", errors);

            if (string.IsNullOrWhiteSpace(members[index].Draft.Name))
            {
                errors.Add($"party.members[{index}].draft.name: a character needs a name to be narrated by.");
            }
        }
    }

    private static void CheckEnemies(ScenarioEnemies enemies, List<string> errors)
    {
        switch (enemies)
        {
            case { Roster: null, Budget: null }:
                errors.Add(
                    "enemies: names neither a roster nor a budget; a scenario asks either "
                    + "\"this fight\" or \"this kind of fight\".");
                return;

            case { Roster: not null, Budget: not null }:
                errors.Add(
                    "enemies: names both a roster and a budget; a scenario asks one of those two questions, "
                    + "not both.");
                return;

            case { Budget: { } budget }:
                CheckLevel(budget.Level, "enemies.budget.level", errors);

                if (budget.MaximumChallengeRating < 0m)
                {
                    errors.Add(
                        $"enemies.budget.maximumChallengeRating={budget.MaximumChallengeRating}: "
                        + "a challenge rating ceiling cannot be negative.");
                }

                return;
        }

        var roster = enemies.Roster!;

        if (roster.Count == 0)
        {
            errors.Add("enemies.roster: an explicit cast needs at least one entry.");
        }

        for (var index = 0; index < roster.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(roster[index].MonsterId))
            {
                errors.Add($"enemies.roster[{index}].monsterId: no monster named.");
            }

            if (roster[index].Count is < 1 or > RosterParser.MaximumCount)
            {
                errors.Add(
                    $"enemies.roster[{index}].count={roster[index].Count}: "
                    + $"out of range (1-{RosterParser.MaximumCount})");
            }
        }
    }

    private static void CheckObjective(ObjectiveSpec? objective, List<string> errors)
    {
        if (objective is null)
        {
            return;
        }

        // Rounds belongs to SurviveRounds and to nothing else. A non-zero count on a
        // KillLeader objective is not harmless-and-ignored: it is an author believing
        // they asked for something, which is the shape of every refusal in this file.
        if (objective.Kind == ObjectiveKind.SurviveRounds)
        {
            if (objective.Rounds < 1)
            {
                errors.Add(
                    $"objective.rounds={objective.Rounds}: a SurviveRounds objective needs at least one round.");
            }
        }
        else if (objective.Rounds != 0)
        {
            errors.Add(
                $"objective.rounds={objective.Rounds}: only a SurviveRounds objective counts rounds, "
                + $"and this one is {objective.Kind}.");
        }
    }

    private static void CheckLevel(int level, string field, List<string> errors)
    {
        if (level < BattleScenario.MinimumLevel || level > BattleScenario.MaximumLevel)
        {
            errors.Add(
                $"{field}={level}: out of range "
                + $"({BattleScenario.MinimumLevel}-{BattleScenario.MaximumLevel})");
        }
    }
}
