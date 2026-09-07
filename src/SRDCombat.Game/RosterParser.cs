using SRDCombat.Core.Definitions;

namespace SRDCombat.Game;

/// <summary>
/// Parses the clients' <c>--spawn</c> roster — <c>"Ogre, 2 Goblin Warrior, Wolf"</c> —
/// into monster definitions for <see cref="EncounterFactory.BuildChosen"/> (#456).
/// </summary>
/// <remarks>
/// <para>
/// Entries are comma-separated; an entry is an optional leading count and a monster
/// name, matched case-insensitively against the bestiary. Every entry that fails to
/// parse is reported by name in <see cref="Roster.Errors"/> and nothing is silently
/// dropped — a test aid that quietly thinned the cast it was asked for would be the
/// keyword-filter bug (rule 2) rebuilt as a convenience. The count is capped at
/// <see cref="MaximumCount"/> per entry, guarding a typo's order of magnitude rather
/// than the size of the board a roster line can ask for — ten entries of the cap still
/// reach a two-hundred-monster board.
/// </para>
/// <para>
/// <b>The grammar is unambiguous only because the bestiary holds a corpus invariant
/// nothing here asserts</b> (#464, the #412 trip-wire pattern): no monster name
/// contains a comma (or the comma split would sever a name), and none begins with a
/// token <c>int.TryParse</c> accepts (or a leading count would eat the first word of
/// the name — or worse, misparse into a different monster entirely).
/// <c>RosterParserCorpusInvariantTests</c> in the test project pins both, plus
/// case-insensitive uniqueness, which <see cref="Parse"/>'s <c>FirstOrDefault</c>
/// match silently assumes. The first bestiary entry to violate either shape does not
/// fail loudly here — it forces a grammar decision instead.
/// </para>
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
            var nameWords = words;

            if (words.Length > 1 && int.TryParse(words[0], out var parsed))
            {
                count = parsed;
                nameWords = words[1..];
            }

            // Joined from the split words rather than sliced out of `entry` on both
            // branches, so internal whitespace runs collapse identically whether or
            // not a count prefix is present — "2 Goblin  Warrior" and "Goblin  Warrior"
            // both normalise to "Goblin Warrior" (#464; previously only the counted
            // branch normalised, so the same doubled space refused without a count).
            var name = string.Join(' ', nameWords);

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
