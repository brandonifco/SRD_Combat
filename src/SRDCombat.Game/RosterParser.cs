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

    /// <summary>
    /// Turns a parsed cast into the roster entries a <see cref="BattleScenario"/> stores,
    /// so a typed <c>--spawn</c> line becomes a scenario value the runner can build from
    /// (#474).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Runs, not groups, because order is the fight.</b> The cast's order decides which
    /// monster gets which spawn square and which index its combatant id carries, so
    /// <c>"Goblin Warrior, Ogre, Goblin Warrior"</c> must not come back as two Goblins and
    /// an Ogre. Only adjacent equal ids are folded, which makes the conversion exactly
    /// reversible: expanding the entries in order reproduces the cast it was given, every
    /// time.
    /// </para>
    /// <para>
    /// A run longer than <see cref="MaximumCount"/> is split rather than clamped or
    /// refused. <c>"20 Wolf, 20 Wolf"</c> is a legal thing to type and its forty wolves
    /// are a legal cast; the ceiling is a per-entry guard against a typo's order of
    /// magnitude, so honouring it here means emitting two entries of twenty, not losing
    /// twenty wolves.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ScenarioRosterEntry> ToRoster(IReadOnlyList<MonsterDefinition> cast)
    {
        ArgumentNullException.ThrowIfNull(cast);

        var entries = new List<ScenarioRosterEntry>();

        foreach (var monster in cast)
        {
            if (entries.Count > 0
                && string.Equals(entries[^1].MonsterId, monster.Id, StringComparison.Ordinal)
                && entries[^1].Count < MaximumCount)
            {
                entries[^1] = entries[^1] with { Count = entries[^1].Count + 1 };
                continue;
            }

            entries.Add(new ScenarioRosterEntry { MonsterId = monster.Id, Count = 1 });
        }

        return entries;
    }
}
