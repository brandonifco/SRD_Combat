using SRDCombat.Core.Definitions;

namespace SRDCombat.Game;

/// <summary>
/// Parses the clients' <c>--spawn</c> roster — <c>"Ogre, 2 Goblin Warrior, Wolf"</c> —
/// into monster definitions for <see cref="EncounterFactory.BuildChosen"/> (#456).
/// </summary>
/// <remarks>
/// Entries are comma-separated; an entry is an optional leading count and a monster
/// name, matched case-insensitively against the bestiary. Every entry that fails to
/// parse is reported by name in <see cref="Roster.Errors"/> and nothing is silently
/// dropped — a test aid that quietly thinned the cast it was asked for would be the
/// keyword-filter bug (rule 2) rebuilt as a convenience. The count is capped at
/// <see cref="MaximumCount"/> per entry so a typo cannot ask the engine for a
/// two-hundred-monster board.
/// </remarks>
public static class RosterParser
{
    /// <summary>
    /// Per-entry ceiling — twice <see cref="EncounterFactory.HordeMaximum"/>, room to
    /// overfill a horde on purpose without admitting a typo's order of magnitude.
    /// </summary>
    public const int MaximumCount = 20;

    /// <summary>The parsed cast, or the reasons it could not be one.</summary>
    public sealed record Roster(
        IReadOnlyList<MonsterDefinition> Monsters,
        IReadOnlyList<string> Errors);

    public static Roster Parse(string text, IReadOnlyList<MonsterDefinition> bestiary)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(bestiary);

        var monsters = new List<MonsterDefinition>();
        var errors = new List<string>();

        foreach (var entry in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var words = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var count = 1;
            var name = entry;

            if (words.Length > 1 && int.TryParse(words[0], out var parsed))
            {
                count = parsed;
                name = string.Join(' ', words[1..]);
            }

            if (count < 1 || count > MaximumCount)
            {
                errors.Add($"\"{entry}\": count must be 1–{MaximumCount}");
                continue;
            }

            var match = bestiary.FirstOrDefault(monster =>
                string.Equals(monster.Name, name, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                errors.Add($"\"{name}\": no such monster");
                continue;
            }

            monsters.AddRange(Enumerable.Repeat(match, count));
        }

        if (monsters.Count == 0 && errors.Count == 0)
        {
            errors.Add("the roster is empty");
        }

        return new Roster(monsters, errors);
    }
}
