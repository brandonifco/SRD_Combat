namespace SRDCombat.Game;

/// <summary>
/// The shared refusal for save-vs-content drift: a content id a saved draft names
/// that this build's loaded content does not have.
/// </summary>
/// <remarks>
/// <para>
/// <b>The backstop, not the only gate.</b> <see cref="GauntletRun.Resume"/> refuses a
/// <em>present</em> content-version mismatch outright, before resolving anything — the
/// cheap, early check that catches the overwhelming case, a save written against a
/// content build this one is not. <see cref="Require{TValue}"/> is what keeps two
/// rarer cases honest too: a save with no content version at all (written before
/// #287, which is not refused — see <c>SavedRun.ContentVersion</c>'s remarks), and a
/// same-version edge case where the fingerprint agreed by coincidence — a hand-edited
/// save, or a content id renamed without the roster otherwise moving. Without it, a
/// miss on <c>PregeneratedParty</c>, <c>Gauntlet</c>, <c>Loot</c> or <c>Shop</c>'s own
/// dictionary indexers throws a bare <see cref="KeyNotFoundException"/> straight past
/// both clients' exception filters (the review's finding this closes) — this throws
/// <see cref="InvalidDataException"/> instead, the same type a content-version
/// mismatch already refuses with, so a caller that wants "does this resolve" rather
/// than a crash can catch exactly this and only this.
/// </para>
/// </remarks>
internal static class ContentDrift
{
    /// <summary>What a save's refusal calls the file that named a missing id.</summary>
    public const string SaveSubject = "the save";

    /// <summary>What a save's refusal suggests doing about it.</summary>
    public const string SaveRemedy =
        "The file is untouched — the build that wrote it can still play it, or start a new run.";

    /// <summary>
    /// Looks <paramref name="id"/> up in <paramref name="byId"/>, or refuses with a
    /// message naming what was missing and who named it.
    /// </summary>
    /// <param name="byId">The content dictionary the id should be in.</param>
    /// <param name="id">The id a draft (or something built from one) named.</param>
    /// <param name="kind">What kind of content this is, for the message — "species", "class".</param>
    /// <param name="owner">Who named the id, for the message — usually a character's name.</param>
    /// <param name="subject">
    /// What named the id, for the message. Defaults to a save's wording because a save is
    /// what this was built for; a scenario passes its own, so a battle scenario's refusal
    /// does not tell the reader to start a new run.
    /// </param>
    /// <param name="remedy">What to do about it, for the message. See <paramref name="subject"/>.</param>
    public static TValue Require<TValue>(
        IReadOnlyDictionary<string, TValue> byId,
        string id,
        string kind,
        string owner,
        string subject = SaveSubject,
        string remedy = SaveRemedy)
    {
        if (byId.TryGetValue(id, out var value))
        {
            return value;
        }

        throw new InvalidDataException(MissingMessage(id, kind, owner, subject, remedy));
    }

    /// <summary>
    /// The refusal text <see cref="Require{TValue}"/> throws, without throwing it.
    /// </summary>
    /// <remarks>
    /// For a caller that has a list of ids and wants a list of problems rather than the
    /// first one — <see cref="ScenarioFile.CheckAgainst"/>. Single-sourcing the wording
    /// here is the point: two refusals for the same failure, worded differently by where
    /// they were raised, is how a message stops being trusted.
    /// </remarks>
    public static string MissingMessage(string id, string kind, string owner, string subject, string remedy) =>
        $"{subject} names {kind} '{id}' (for {owner}), which the loaded content does not have. {remedy}";

    /// <summary>
    /// A fingerprint shortened to its first 12 hex characters, for a message a player
    /// reads rather than a comparison a computer makes — <see cref="GauntletRun.Resume"/>
    /// always compares the full value; this is display only.
    /// </summary>
    public static string Truncate(string fingerprint) =>
        fingerprint.Length <= 12 ? fingerprint : fingerprint[..12];
}
