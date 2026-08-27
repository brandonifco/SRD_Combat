using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Game;

/// <summary>
/// What a scenario looks like against a particular build's content: what refuses, and
/// what is merely worth saying.
/// </summary>
/// <remarks>
/// The two lists are different in kind and that is the point. An <b>error</b> is a
/// scenario this build cannot run — a monster id the bestiary does not have, a draft the
/// resolver refuses. A <b>notice</b> is something a human should read and nothing should
/// act on: today the only one is a content-fingerprint mismatch, which is provenance
/// rather than identity (see <see cref="BattleScenario.ContentVersion"/>).
/// </remarks>
/// <param name="Errors">Reasons this build cannot run the scenario.</param>
/// <param name="Notices">Things worth saying that refuse nothing.</param>
public sealed record ScenarioCheck(IReadOnlyList<string> Errors, IReadOnlyList<string> Notices)
{
    /// <summary>Whether this build can run the scenario.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Where a scenario meets the content that gives its ids meaning — the
/// <see cref="GauntletRun.Resume"/> half of the split <see cref="ScenarioFile"/>'s
/// remarks describe.
/// </summary>
/// <remarks>
/// This resolves what a scenario's ids <em>name</em> — the party and the explicit cast —
/// and nothing about a fight. Rolling a board, spending a budget and assembling a
/// <c>Fight</c> is <see cref="ScenarioRunner"/>'s (S2, #474); what lives here is the part
/// that is about the <em>scenario</em>: turning authored ids into resolved members and
/// monster definitions, and answering whether this build can run the thing at all.
/// </remarks>
public static class ScenarioContent
{
    /// <summary>What a scenario's refusals call the thing that named a missing id.</summary>
    private const string Subject = "the scenario";

    /// <summary>
    /// What a scenario's refusals suggest doing about it. Not a save's remedy: a save is
    /// a run in progress that the writing build can still play, while a scenario is a
    /// question, and the answer to a question about content this build does not have is
    /// to ask it of the build that does — or to re-author it.
    /// </summary>
    private const string Remedy =
        "Re-author the scenario against this build's content, or run it on the build it was written for.";

    /// <summary>
    /// Checks a scenario against the content that gives its ids meaning: every id it
    /// names resolves, the party it describes resolves, and a fingerprint that disagrees
    /// is said out loud without refusing anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every id, not merely the ones a code path happens to reach.</b> A draft's
    /// species, class and background go through <see cref="ContentDrift.Require"/> inside
    /// <see cref="PregeneratedParty.Resolve"/>, and its weapons, armour and magic items
    /// are refused by <c>CharacterResolver</c> — but only along the branches that
    /// particular draft takes. This walks the ids first and reports every miss, so a
    /// scenario naming three vanished weapons is told about three of them, and only then
    /// attempts the resolution that catches everything else.
    /// </para>
    /// <para>
    /// The id sweep formats <see cref="ContentDrift.MissingMessage"/> rather than calling
    /// <see cref="ContentDrift.Require"/> in a loop and catching: same text,
    /// single-sourced, without using exceptions to walk a list. The resolution attempt
    /// below it does catch, because refusing is <c>CharacterResolver</c>'s published
    /// voice and the point here is to report it rather than die on the first bad file in
    /// a directory.
    /// </para>
    /// </remarks>
    public static ScenarioCheck CheckAgainst(BattleScenario scenario, SrdContent content)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(content);

        var errors = new List<string>();
        var notices = new List<string>();

        if (scenario.ContentVersion is { } version
            && !string.Equals(version, content.ContentFingerprint, StringComparison.Ordinal))
        {
            notices.Add(
                $"authored against different content (scenario {ContentDrift.Truncate(version)}, " +
                $"loaded {ContentDrift.Truncate(content.ContentFingerprint)}). " +
                "A scenario is a question asked of the current build, so this is provenance rather than a " +
                "refusal — every id it names is checked individually.");
        }

        foreach (var entry in scenario.Enemies.Roster ?? [])
        {
            RequireId(content.MonstersById, entry.MonsterId, "monster", scenario.Name, errors);
        }

        foreach (var member in scenario.Party.Members ?? [])
        {
            CheckMemberIds(member.Draft, content, errors);
        }

        try
        {
            _ = ResolveParty(scenario, content);
        }
        catch (Exception failure) when (
            failure is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            // The three types the resolution path refuses with: InvalidDataException from
            // ContentDrift.Require, ArgumentException from CharacterResolver's own
            // validation, InvalidOperationException from a class table with no row at the
            // level asked for.
            errors.Add($"the party does not resolve: {failure.Message}");
        }

        return new ScenarioCheck(errors, notices);
    }

    /// <summary>
    /// Turns a scenario's authored party into resolved members, seated in a column the
    /// way <see cref="PregeneratedParty.Build"/> seats its four.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The preset resolves through <see cref="PregeneratedParty.Build"/>, never
    /// through a stored copy of its drafts</b>, which is the whole reason the preset
    /// exists: a scenario that froze a copy of the pregenerated four would silently stop
    /// tracking a change to them, and the library would drift one file at a time with
    /// nothing failing. A scenario using the preset stores no drafts at all — read the
    /// JSON and there is a level and nothing else.
    /// </para>
    /// <para>
    /// Refuses rather than returns problems, in <c>CharacterResolver</c>'s and
    /// <see cref="ContentDrift"/>'s voice. <see cref="CheckAgainst"/> is the method for a
    /// caller that wants a list; this is the method for a caller that has already
    /// checked.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PartyMember> ResolveParty(BattleScenario scenario, SrdContent content)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(content);

        if (scenario.Party.PregeneratedLevel is { } level)
        {
            return PregeneratedParty.Build(content, level);
        }

        var members = scenario.Party.Members
            ?? throw new InvalidDataException(
                "the scenario names neither a pregenerated level nor members; "
                + "ScenarioFile.FromJson refuses this, so it was built in memory rather than loaded.");

        return
        [
            .. members.Select((member, index) =>
                PregeneratedParty.Resolve(content, member.Draft, member.Level, x: 0, y: index)),
        ];
    }

    /// <summary>
    /// Turns a scenario's explicit cast into monster definitions, in the order it named
    /// them and one per head.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Order is preserved and counts are expanded, because both are the fight.</b> The
    /// cast's order decides which creature takes which spawn square and which index its
    /// combatant id carries, so the entries are walked in sequence rather than grouped;
    /// <see cref="RosterParser.ToRoster"/> is the other half of that promise, folding only
    /// adjacent equal ids so the two are exactly reversible.
    /// </para>
    /// <para>
    /// The sibling of <see cref="ResolveParty"/>, and refusing in the same voice for the
    /// same reason: <see cref="CheckAgainst"/> is the method for a caller that wants the
    /// list of problems, and this is the method for a caller that has already checked.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<MonsterDefinition> ResolveRoster(BattleScenario scenario, SrdContent content)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(content);

        var roster = scenario.Enemies.Roster
            ?? throw new InvalidDataException(
                "the scenario names no roster; ScenarioFile.FromJson refuses a scenario that names "
                + "neither a roster nor a budget, so this one was built in memory rather than loaded.");

        return
        [
            .. roster.SelectMany(entry => Enumerable.Repeat(
                ContentDrift.Require(
                    content.MonstersById, entry.MonsterId, "monster", scenario.Name, Subject, Remedy),
                entry.Count)),
        ];
    }

    private static void CheckMemberIds(CharacterDraft draft, SrdContent content, List<string> errors)
    {
        var owner = draft.Name;

        RequireId(content.SpeciesById, draft.SpeciesId, "species", owner, errors);
        RequireId(content.ClassesById, draft.ClassId, "class", owner, errors);
        RequireId(content.BackgroundsById, draft.BackgroundId, "background", owner, errors);

        foreach (var weaponId in draft.WeaponIds)
        {
            RequireId(content.WeaponsById, weaponId, "weapon", owner, errors);
        }

        foreach (var masteryId in draft.WeaponMasteryIds)
        {
            RequireId(content.WeaponsById, masteryId, "mastered weapon", owner, errors);
        }

        if (draft.ArmorId is { } armorId)
        {
            RequireId(content.ArmorById, armorId, "armor", owner, errors);
        }

        foreach (var item in draft.MagicItems)
        {
            RequireId(content.MagicItemsById, item.ItemId, "magic item", owner, errors);
        }

        foreach (var spellId in draft.ChosenSpellIds)
        {
            RequireId(content.SpellsById, spellId, "spell", owner, errors);
        }
    }

    private static void RequireId<TValue>(
        IReadOnlyDictionary<string, TValue> byId,
        string id,
        string kind,
        string owner,
        List<string> errors)
    {
        if (!byId.ContainsKey(id))
        {
            errors.Add(ContentDrift.MissingMessage(id, kind, owner, Subject, Remedy));
        }
    }
}
