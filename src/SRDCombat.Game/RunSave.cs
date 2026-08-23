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
    /// The seed every draw this run has made — rests, encounter draws, combat rolls —
    /// traces back to. Persisted so a reload can hand <see cref="GauntletRun.PrepareForNext"/>
    /// and <see cref="GauntletRun.BeginNext"/> a fresh <c>SeededRandomSource</c> built
    /// from the same number, rather than a freshly rolled one, so the fight that ended
    /// the run comes back exactly instead of being re-rolled.
    /// </summary>
    /// <remarks>
    /// Nullable, deliberately, rather than <c>required</c>: a save written before this
    /// field existed carries none, and <see cref="RunSave.FromJson"/> reads that absence
    /// itself and refuses the file with a named reason — unlike <see cref="GoldCopper"/>,
    /// there is no honest default seed to invent, because inventing one would make "the
    /// same fight" a lie the very first time this exact save is continued.
    /// </remarks>
    public int? Seed { get; init; }
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
    /// Deserializes and validates a save. Anything malformed is refused with a reason,
    /// never repaired silently.
    /// </summary>
    public static SavedRun FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var saved = ContentSerializer.Deserialize<SavedRun>(json);

        if (saved.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Save format {saved.FormatVersion} is not this build's {CurrentFormatVersion}; refusing to guess.");
        }

        if (saved.Seed is null)
        {
            throw new InvalidDataException(
                "This save predates seed tracking, so --continue cannot reconstruct the same next " +
                "fight from it; start a new run.");
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
