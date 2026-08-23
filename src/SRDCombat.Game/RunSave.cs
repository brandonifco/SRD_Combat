using System.Text.Json;
using SRDCombat.Content;
using SRDCombat.Core.Characters;

namespace SRDCombat.Game;

/// <summary>One saved party member: the draft that makes them and the state they carry.</summary>
/// <remarks>
/// Paired rather than held in two parallel lists, so a save cannot hold a state without
/// the draft it belongs to.
/// </remarks>
/// <param name="Draft">The choices. Everything derived is re-resolved on load.</param>
/// <param name="State">What the run has done to them: wounds, spent resources, experience, death.</param>
public sealed record SavedMember(CharacterDraft Draft, CharacterState State);

/// <summary>
/// A run on disk: drafts plus progress, never resolved sheets.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing derived is saved.</b> <c>CharacterResolver</c> computes every number on a
/// sheet, so a save that stored sheets would be storing values that could drift from the
/// rules that make them — the exact failure the draft/sheet split exists to prevent.
/// Loading re-resolves each draft at the level the character's <em>experience</em> has
/// earned, so even a hand-edited draft level cannot make a loaded character disagree
/// with their own XP. Levelling uses average hit points precisely so this re-resolution
/// cannot reroll history.
/// </para>
/// <para>
/// The ladder is saved whole rather than regenerated, because a ladder is data — an
/// authored sequence of rungs must survive a reload exactly as a generated one does.
/// </para>
/// </remarks>
public sealed record SavedRun
{
    /// <summary>
    /// Bumped when the save format changes incompatibly. A file with any other version
    /// is refused rather than guessed at — the same rule the content loader follows.
    /// </summary>
    /// <remarks>
    /// <b>The rule for adding a field, stated once here rather than re-litigated per
    /// field:</b> a new field that a save written before it existed can honestly do
    /// without — a default with a real meaning (<see cref="GoldCopper"/>'s empty
    /// purse), a value the client can migrate on load and re-stamp on the next
    /// autosave (<see cref="Seed"/>'s roll, <see cref="ContentVersion"/>'s
    /// piece-by-piece fallback) — is nullable or defaulted, and its absence is never
    /// refused. A field with no honest thing to do about its absence is not a new
    /// field at all; it is a format break, and belongs behind a bump of
    /// <see cref="RunSave.CurrentFormatVersion"/> and this property's own check, the
    /// one gate in <see cref="RunSave.FromJson"/> that refuses on structure alone.
    /// </remarks>
    public required int FormatVersion { get; init; }

    /// <summary>The whole ladder, so authored ladders reload exactly.</summary>
    public required IReadOnlyList<LadderStep> Ladder { get; init; }

    /// <summary>Rungs cleared. The next fight is this rung of the ladder.</summary>
    public required int Cleared { get; init; }

    /// <summary>The party, in seating order.</summary>
    public required IReadOnlyList<SavedMember> Members { get; init; }

    /// <summary>Everyone who has ever fallen, in order — the history, not the state.</summary>
    public IReadOnlyList<string> Casualties { get; init; } = [];

    /// <summary>
    /// The party's purse in copper. Defaults to empty, so a save written before gold
    /// existed loads as a party that has not been paid yet rather than being refused.
    /// </summary>
    public int GoldCopper { get; init; }

    /// <summary>
    /// The run's own seed — fixed once at <see cref="GauntletRun.Start"/> and constant
    /// for the run's whole life. A fight's actual dice are <c>RunDice.SeedFor</c> of
    /// this value and how many fights had been cleared when that fight began, not this
    /// value directly — see <c>RunDice</c>'s own remarks for what that buys and why.
    /// </summary>
    /// <remarks>
    /// Nullable because a save written before #286 carries none — but unlike a
    /// content-version mismatch, that is not refused here. There is an honest thing to
    /// do with a missing seed that there is not with a missing content version: roll
    /// one. The client does that exactly once, the first time it meets a save without
    /// one, says so, and hands it to <see cref="GauntletRun.AdoptSeed"/>, which writes
    /// it to disk immediately rather than waiting for the next cleared fight's autosave
    /// (#361) — see the clients' own "predates run seeds" handling. <see
    /// cref="RunSave.FromJson"/> does not enforce this field at all.
    /// </remarks>
    public int? Seed { get; init; }

    /// <summary>
    /// The <see cref="SrdContent.ContentFingerprint"/> of the content this run was
    /// last saved against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Checked in <see cref="GauntletRun.Resume"/>, not here — <see cref="RunSave.FromJson"/>
    /// validates the file's own structure and nothing content-dependent, the same
    /// division <see cref="Seed"/>'s remarks describe. A <em>present</em> value that
    /// disagrees with the loaded content's fingerprint is refused there: two builds'
    /// content can differ in ways a single id lookup would never catch (an id
    /// survives, a number behind it changed), so a whole-roster mismatch is refused
    /// outright rather than guessed at.
    /// </para>
    /// <para>
    /// A <em>missing</em> value — a save written before #287 — is not refused. There
    /// is no coarse comparison to make without one, so <c>Resume</c> falls through to
    /// resolving every character normally; <c>ContentDrift.Require</c>'s per-id
    /// checks are what actually catch drift for a save in this state, the same
    /// backstop that also covers the rarer same-version edge case. Every
    /// <see cref="GauntletRun.ToSave"/> call stamps the <em>currently loaded</em>
    /// content's fingerprint regardless of what a resumed save had, so a run in this
    /// state carries a real value again after its very next autosave.
    /// </para>
    /// </remarks>
    public string? ContentVersion { get; init; }
}

/// <summary>
/// Reads and writes <see cref="SavedRun"/> JSON. <see cref="SaveFile"/> owns getting it
/// to and from disk atomically; this owns the format.
/// </summary>
/// <remarks>
/// Serialization goes through <see cref="ContentSerializer"/> deliberately: the same
/// strictness that guards content guards a save — an unknown property is an error rather
/// than skipped, and the on-disk shape is pinned by <c>SavedRunShapeTests</c>.
/// </remarks>
public static class RunSave
{
    /// <summary>The format this build writes and the only one it reads.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Serializes a run's saveable snapshot.</summary>
    public static string ToJson(GauntletRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return ContentSerializer.Serialize(run.ToSave());
    }

    /// <summary>
    /// Deserializes and validates a save's own structure. Anything malformed is
    /// refused with a reason, never repaired silently.
    /// </summary>
    /// <remarks>
    /// Content-dependent checks — <see cref="SavedRun.ContentVersion"/> against the
    /// loaded content's fingerprint, every id a draft names — are not this method's:
    /// this has no content to check against, by design, the same way it trusts
    /// <see cref="SavedRun.Seed"/> without rolling one. <see cref="GauntletRun.Resume"/>
    /// is where a save meets content, and it is where both those checks live.
    /// </remarks>
    public static SavedRun FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var saved = ContentSerializer.Deserialize<SavedRun>(json);

        if (saved.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Save format {saved.FormatVersion} is not this build's {CurrentFormatVersion}; refusing to guess.");
        }

        if (saved.Ladder.Count == 0)
        {
            throw new InvalidDataException("A saved run needs at least one rung.");
        }

        if (saved.Members.Count == 0)
        {
            throw new InvalidDataException("A saved run needs at least one character.");
        }

        if (saved.Cleared < 0 || saved.Cleared > saved.Ladder.Count)
        {
            throw new InvalidDataException(
                $"Cleared {saved.Cleared} of a {saved.Ladder.Count}-rung ladder is not a position on it.");
        }

        // Only reachable by hand-editing: a save is written after a *won* fight, and a
        // won fight has a living character. Refused because resuming it would ask the
        // encounter factory to build a fight for nobody.
        if (saved.Cleared < saved.Ladder.Count && saved.Members.All(member => member.State.IsDead))
        {
            throw new InvalidDataException("Every saved character is dead; there is no run to resume.");
        }

        return saved;
    }
}
