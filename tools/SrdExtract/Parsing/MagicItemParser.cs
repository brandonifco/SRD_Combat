using System.Text;
using System.Text.RegularExpressions;
using SrdExtract.Pdf;
using SRDCombat.Core.Definitions;

namespace SrdExtract.Parsing;

public sealed record MagicItemParseResult(
    IReadOnlyList<MagicItemDefinition> Items,
    IReadOnlyList<ParseDiagnostic> Diagnostics);

/// <summary>
/// Parses Magic Items A–Z (printed pages 209–253).
/// </summary>
/// <remarks>
/// <para>
/// The chapter uses the player-facing typography, not the bestiary's: an item's name is
/// a <c>GillSans-SemiBold</c> heading in the same height band as a spell's, the type
/// line below it ("Wondrous Item, Rare (Requires Attunement)") is <c>Cambria-Italic</c>,
/// and the description is Cambria body text. The type line is the anchor — a heading
/// only opens an item when one follows, which is what keeps table headers and the
/// chapter's own title out.
/// </para>
/// <para>
/// Two wrap shapes are joined before parsing, both learned from the spell chapter's 39
/// silently dropped spells: a long item name wraps onto a second heading line, and a
/// long type line wraps its rarity list or attunement clause onto a second italic line.
/// </para>
/// </remarks>
public static partial class MagicItemParser
{
    private const string HeadingFont = "GillSans-SemiBold";
    private const string TypeLineFont = "Cambria-Italic";
    private const double MinimumHeadingHeight = 7.6;
    private const double MaximumHeadingHeight = 9.2;

    public static MagicItemParseResult Parse(IReadOnlyList<SourceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var items = new List<MagicItemDefinition>();
        var diagnostics = new List<ParseDiagnostic>();

        for (var index = 0; index < lines.Count; index++)
        {
            if (!IsHeading(lines[index]))
            {
                continue;
            }

            // A long name wraps onto a second heading line — "Amulet of Proof against
            // Detection / and Location". Consume the run of heading lines as one name.
            var name = new StringBuilder(lines[index].Text.Trim());
            var next = index + 1;

            while (next < lines.Count && IsHeading(lines[next]))
            {
                name.Append(' ').Append(lines[next].Text.Trim());
                next++;
            }

            // Only a heading followed by a type line opens an item.
            if (next >= lines.Count || !IsTypeLine(lines[next]))
            {
                continue;
            }

            var typeLine = new StringBuilder(lines[next].Text.Trim());
            next++;

            // A wrapped rarity list or attunement clause continues in the same italic
            // font — "Very Rare (Requires" / "Attunement)".
            while (next < lines.Count && lines[next].AllWordsUseFont(TypeLineFont))
            {
                typeLine.Append(' ').Append(lines[next].Text.Trim());
                next++;
            }

            // The description runs to the next item heading.
            var text = new StringBuilder();

            while (next < lines.Count && !(IsHeading(lines[next]) && HeadingOpensItem(lines, next)))
            {
                AppendWrapped(text, lines[next].Text.Trim());
                next++;
            }

            if (TryBuild(name.ToString(), typeLine.ToString(), text.ToString(), out var item, out var reason))
            {
                items.Add(item);
            }
            else
            {
                diagnostics.Add(new ParseDiagnostic(name.ToString(), reason));
            }

            index = next - 1;
        }

        return new MagicItemParseResult(items, diagnostics);
    }

    /// <summary>
    /// Joins a wrapped line, undoing the hyphenation the justified columns introduce —
    /// the same rule <c>MonsterParser</c> uses: a line ending in a hyphen whose
    /// continuation starts lowercase was one word split across two lines.
    /// </summary>
    private static void AppendWrapped(StringBuilder builder, string line)
    {
        if (line.Length == 0)
        {
            return;
        }

        if (builder.Length == 0)
        {
            builder.Append(line);
            return;
        }

        if (builder[^1] == '-' && char.IsLower(line[0]))
        {
            builder.Length -= 1;
            builder.Append(line);
            return;
        }

        builder.Append(' ').Append(line);
    }

    private static bool IsHeading(SourceLine line) =>
        line.Font == HeadingFont
        && line.Height >= MinimumHeadingHeight
        && line.Height <= MaximumHeadingHeight
        && line.Text.Length > 0;

    /// <summary>Whether the heading at <paramref name="index"/> begins an item — i.e. a type line follows its heading run.</summary>
    private static bool HeadingOpensItem(IReadOnlyList<SourceLine> lines, int index)
    {
        while (index < lines.Count && IsHeading(lines[index]))
        {
            index++;
        }

        return index < lines.Count && IsTypeLine(lines[index]);
    }

    private static bool IsTypeLine(SourceLine line) =>
        line.AllWordsUseFont(TypeLineFont) && TypeLinePattern().IsMatch(line.Text.Trim());

    private static bool TryBuild(
        string name,
        string typeLine,
        string text,
        out MagicItemDefinition item,
        out string reason)
    {
        item = null!;

        var match = TypeLinePattern().Match(typeLine);

        if (!match.Success)
        {
            reason = $"Unparseable type line '{typeLine}'.";
            return false;
        }

        var category = match.Groups["category"].Value switch
        {
            "Armor" => MagicItemCategory.Armor,
            "Potion" => MagicItemCategory.Potion,
            "Ring" => MagicItemCategory.Ring,
            "Rod" => MagicItemCategory.Rod,
            "Scroll" => MagicItemCategory.Scroll,
            "Staff" => MagicItemCategory.Staff,
            "Wand" => MagicItemCategory.Wand,
            "Weapon" => MagicItemCategory.Weapon,
            _ => MagicItemCategory.WondrousItem,
        };

        var appliesTo = match.Groups["applies"].Success ? match.Groups["applies"].Value.Trim() : null;
        var remainder = typeLine[match.Length..].Trim().TrimStart(',').Trim();

        // The attunement clause is the trailing parenthetical opening with "Requires
        // Attunement"; everything before it is the rarity section.
        string? attunementRequirement = null;
        var requiresAttunement = false;

        var attunement = AttunementPattern().Match(remainder);

        if (attunement.Success)
        {
            requiresAttunement = true;
            attunementRequirement = attunement.Groups["qualifier"].Success
                ? attunement.Groups["qualifier"].Value.Trim()
                : null;
            remainder = remainder[..attunement.Index].Trim();
        }

        if (!TryParseRarities(remainder, out var rarity, out var variants, out var rarityProblem))
        {
            reason = $"Unparseable rarity '{remainder}' ({rarityProblem}).";
            return false;
        }

        if (text.Length == 0)
        {
            reason = "The item has no description text.";
            return false;
        }

        item = new MagicItemDefinition
        {
            Id = "magic-item." + Slug(name),
            Name = name,
            Category = category,
            AppliesTo = appliesTo,
            Rarity = rarity,
            Variants = variants,
            RequiresAttunement = requiresAttunement,
            AttunementRequirement = attunementRequirement,
            Text = text,
        };

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// The rarity section: either one plain rarity, "Rarity Varies", or a list whose
    /// entries carry their tier in parentheses — "Uncommon (+1), Rare (+2), or Very
    /// Rare (+3)", and the Horn of Valhalla's "Rare (Silver or Brass), Very Rare
    /// (Bronze), or Legendary (Iron)".
    /// </summary>
    private static bool TryParseRarities(
        string section,
        out MagicItemRarity rarity,
        out IReadOnlyList<MagicItemVariant> variants,
        out string problem)
    {
        rarity = MagicItemRarity.Varies;
        variants = [];
        problem = string.Empty;

        if (section.Equals("Rarity Varies", StringComparison.Ordinal))
        {
            return true;
        }

        var parsed = new List<MagicItemVariant>();

        foreach (Match entry in RarityEntryPattern().Matches(section))
        {
            if (!TryRarity(entry.Groups["rarity"].Value, out var tier))
            {
                problem = $"unknown rarity '{entry.Groups["rarity"].Value}'";
                return false;
            }

            parsed.Add(new MagicItemVariant(
                entry.Groups["suffix"].Success ? entry.Groups["suffix"].Value.Trim() : string.Empty,
                tier));
        }

        if (parsed.Count == 0)
        {
            problem = "no rarity found";
            return false;
        }

        if (parsed.Count == 1 && parsed[0].Suffix.Length == 0)
        {
            rarity = parsed[0].Rarity;
            return true;
        }

        if (parsed.Any(variant => variant.Suffix.Length == 0))
        {
            problem = "a multi-rarity list with an unmarked entry";
            return false;
        }

        variants = parsed;
        return true;
    }

    private static bool TryRarity(string text, out MagicItemRarity rarity)
    {
        rarity = text switch
        {
            "Common" => MagicItemRarity.Common,
            "Uncommon" => MagicItemRarity.Uncommon,
            "Rare" => MagicItemRarity.Rare,
            "Very Rare" => MagicItemRarity.VeryRare,
            "Legendary" => MagicItemRarity.Legendary,
            "Artifact" => MagicItemRarity.Artifact,
            _ => (MagicItemRarity)(-1),
        };

        return rarity >= 0;
    }

    private static string Slug(string name)
    {
        var slug = name.ToLowerInvariant().Replace("+", "plus-", StringComparison.Ordinal);
        slug = NonSlugCharacters().Replace(slug, "-");

        return slug.Trim('-');
    }

    /// <summary>
    /// The type line's opening: a category, an optional parenthetical saying what it
    /// applies to. "Wondrous Item" must be listed before "Wand" would never match it —
    /// alternation order is load-bearing, the same trap as "Melee or Ranged".
    /// </summary>
    [GeneratedRegex(@"^(?<category>Wondrous Item|Armor|Potion|Ring|Rod|Scroll|Staff|Wand|Weapon)\b\s*(\((?<applies>[^)]*)\))?",
        RegexOptions.Compiled)]
    private static partial Regex TypeLinePattern();

    /// <summary>One rarity entry, with the tier marker the variant lists print.</summary>
    [GeneratedRegex(@"(?<rarity>Very Rare|Common|Uncommon|Rare|Legendary|Artifact)\s*(\((?<suffix>[^)]*)\))?",
        RegexOptions.Compiled)]
    private static partial Regex RarityEntryPattern();

    /// <summary>The attunement clause, with its optional qualifier.</summary>
    [GeneratedRegex(@"\(Requires Attunement(?<qualifier>[^)]+)?\)", RegexOptions.Compiled)]
    private static partial Regex AttunementPattern();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex NonSlugCharacters();
}
