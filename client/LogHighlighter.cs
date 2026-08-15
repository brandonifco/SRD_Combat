using System.Text.RegularExpressions;
using Godot;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Viewer;

/// <summary>
/// Colours the combat log: who acted, what they used, what it cost.
/// </summary>
/// <remarks>
/// <para>
/// <b>The terms come from the fight, not from reading the sentences.</b> Every name this
/// highlights — the combatants', their attacks', their spells', their stat-block entries'
/// — is asked of the encounter itself, so a log line is coloured by matching text the
/// engine already told the screen about rather than by parsing the engine's phrasing. A
/// grammar would go quietly wrong the first time a narration was reworded; a name that
/// stops appearing simply stops being coloured, which is a missing highlight rather than
/// a wrong one.
/// </para>
/// <para>
/// Damage and misses are the exception and are matched as text, because neither is a
/// name — they are the outcome the sentence ends on. Both patterns are anchored on words
/// the narration builds its sentences from ("takes 7 Slashing damage", "— miss"), and
/// both fail safe: no match simply leaves the line in its base colour.
/// </para>
/// <para>
/// This is display only. Nothing here decides anything — the client still holds no rules,
/// and a line whose terms are all unknown reads exactly as it did before.
/// </para>
/// </remarks>
public sealed partial class LogHighlighter
{
    /// <summary>A run of log text and the colour to draw it in.</summary>
    public readonly record struct Span(string Text, Color Colour);

    public static readonly Color PartyName = new("7dc4f0");
    public static readonly Color MonsterName = new("e8917c");
    public static readonly Color ActionName = new("c3a6ea");
    public static readonly Color Damage = new("ff5f4d");
    public static readonly Color Miss = new("e8c84a");

    /// <summary>"takes 7 Slashing damage", and the flat "1 Piercing damage" a stat block prints.</summary>
    [GeneratedRegex(@"\d+ [A-Z]\w+ damage", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex DamagePattern();

    /// <summary>The outcome word itself, never the "miss" inside another word.</summary>
    [GeneratedRegex(@"\bmiss(es)?\b", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex MissPattern();

    /// <summary>
    /// Every term to look for, longest first — so "Giant Wolf Spider" is found whole
    /// rather than being broken up by a shorter name that sits inside it.
    /// </summary>
    private readonly (string Term, Color Colour)[] _terms;

    private LogHighlighter((string, Color)[] terms) => _terms = terms;

    /// <summary>A highlighter that colours nothing — before a fight exists.</summary>
    public static LogHighlighter None { get; } = new([]);

    /// <summary>Collects the fight's own names.</summary>
    public static LogHighlighter For(Encounter encounter, string partySideId)
    {
        ArgumentNullException.ThrowIfNull(encounter);

        var terms = new Dictionary<string, Color>(StringComparer.Ordinal);

        // A name is claimed by whoever holds it first, and combatants come first, so a
        // monster called after its own weapon stays a monster in the log.
        foreach (var combatant in encounter.Combatants)
        {
            terms.TryAdd(
                combatant.Name,
                combatant.SideId == partySideId ? PartyName : MonsterName);
        }

        foreach (var combatant in encounter.Combatants)
        {
            foreach (var attack in combatant.Stats.Attacks)
            {
                terms.TryAdd(attack.Name, ActionName);
            }

            foreach (var entry in combatant.Stats.Entries)
            {
                terms.TryAdd(entry.Name, ActionName);
            }

            if (combatant.Stats.Character is not { } character)
            {
                continue;
            }

            foreach (var spell in character.Spells)
            {
                terms.TryAdd(spell.Name, ActionName);
            }

            // Read off the enum rather than listed by hand: the engine narrates a
            // feature by its printed name, and these names are that name in PascalCase,
            // so splitting the case recovers it. A feature whose narration says
            // something else just goes uncoloured.
            foreach (var feature in character.Features)
            {
                terms.TryAdd(Spaced(feature.ToString()), ActionName);
            }
        }

        foreach (var mastery in Enum.GetValues<WeaponMastery>())
        {
            terms.TryAdd(mastery.ToString(), ActionName);
        }

        return new LogHighlighter([.. terms
            .Where(term => term.Key.Length > 2)
            .OrderByDescending(term => term.Key.Length)
            .Select(term => (term.Key, term.Value))]);
    }

    /// <summary>Breaks one line into runs, each with the colour it should be drawn in.</summary>
    public IReadOnlyList<Span> Spans(string line, Color baseColour)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.Length == 0)
        {
            return [];
        }

        // One colour per character, then runs of like colour are joined back up. It
        // costs a scan of the line and saves the interval bookkeeping that overlapping
        // matches would otherwise need — a monster whose name contains a weapon's, a
        // damage phrase inside a longer clause.
        var paint = new Color?[line.Length];

        foreach (var (term, colour) in _terms)
        {
            for (var at = line.IndexOf(term, StringComparison.Ordinal); at >= 0;
                 at = line.IndexOf(term, at + term.Length, StringComparison.Ordinal))
            {
                if (!IsWholeWord(line, at, term.Length))
                {
                    continue;
                }

                Paint(paint, at, term.Length, colour, overwrite: false);
            }
        }

        // Damage and the miss outrank a name that happens to sit inside them, because
        // the outcome is what a reader scans the log for.
        foreach (Match match in DamagePattern().Matches(line))
        {
            Paint(paint, match.Index, match.Length, Damage, overwrite: true);
        }

        foreach (Match match in MissPattern().Matches(line))
        {
            Paint(paint, match.Index, match.Length, Miss, overwrite: true);
        }

        var spans = new List<Span>();
        var start = 0;

        for (var index = 1; index <= line.Length; index++)
        {
            if (index < line.Length && paint[index] == paint[start])
            {
                continue;
            }

            spans.Add(new Span(line[start..index], paint[start] ?? baseColour));
            start = index;
        }

        return spans;
    }

    private static void Paint(Color?[] paint, int from, int length, Color colour, bool overwrite)
    {
        for (var index = from; index < from + length; index++)
        {
            if (overwrite || paint[index] is null)
            {
                paint[index] = colour;
            }
        }
    }

    /// <summary>
    /// Whether a match stands on its own. Without this, "Sable" would light up inside
    /// a longer word and the Rogue's name would appear where she is not.
    /// </summary>
    private static bool IsWholeWord(string line, int at, int length)
    {
        var before = at == 0 || !char.IsLetterOrDigit(line[at - 1]);
        var after = at + length >= line.Length || !char.IsLetterOrDigit(line[at + length]);

        return before && after;
    }

    /// <summary>"SecondWind" to "Second Wind" — the printed name the narration uses.</summary>
    private static string Spaced(string pascalCase)
    {
        var spaced = new System.Text.StringBuilder(pascalCase.Length + 4);

        for (var index = 0; index < pascalCase.Length; index++)
        {
            if (index > 0 && char.IsUpper(pascalCase[index]) && !char.IsUpper(pascalCase[index - 1]))
            {
                spaced.Append(' ');
            }

            spaced.Append(pascalCase[index]);
        }

        return spaced.ToString();
    }
}
