using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SrdExtract.Pdf;

namespace SrdExtract.Parsing;

/// <summary>The result of parsing the equipment tables.</summary>
public sealed record EquipmentParseResult(
    IReadOnlyList<WeaponDefinition> Weapons,
    IReadOnlyList<ArmorDefinition> Armor,
    IReadOnlyList<ParseDiagnostic> Diagnostics);

/// <summary>
/// Parses the Weapons and Armor tables.
/// </summary>
/// <remarks>
/// These are parsed by grammar rather than by column coordinates, because almost every
/// column is a closed set — eight mastery properties, thirteen damage types, three coin
/// abbreviations — which anchors a row far more reliably than an x boundary would. The
/// one genuinely free-text column, Properties, is what sits between the anchors.
/// Property lists too long for one line wrap onto a second, which is why a line that
/// does not match a full row is treated as a continuation rather than discarded.
/// </remarks>
public static partial class EquipmentParser
{
    public static EquipmentParseResult Parse(
        IReadOnlyList<SourceLine> weaponLines,
        IReadOnlyList<SourceLine> armorLines)
    {
        ArgumentNullException.ThrowIfNull(weaponLines);
        ArgumentNullException.ThrowIfNull(armorLines);

        var diagnostics = new List<ParseDiagnostic>();

        return new EquipmentParseResult(
            ParseWeapons(weaponLines, diagnostics),
            ParseArmor(armorLines, diagnostics),
            diagnostics);
    }

    private static List<WeaponDefinition> ParseWeapons(
        IReadOnlyList<SourceLine> lines,
        List<ParseDiagnostic> diagnostics)
    {
        var weapons = new List<WeaponDefinition>();

        var category = WeaponCategory.Simple;
        var kind = WeaponKind.Melee;
        var sawAnyGroup = false;
        string? pendingName = null;

        foreach (var line in lines)
        {
            var text = line.Text.Trim();

            if (WeaponGroupPattern().Match(text) is { Success: true } group)
            {
                category = Enum.Parse<WeaponCategory>(group.Groups["category"].Value, ignoreCase: true);
                kind = Enum.Parse<WeaponKind>(group.Groups["kind"].Value, ignoreCase: true);
                sawAnyGroup = true;
                pendingName = null;
                continue;
            }

            if (!sawAnyGroup)
            {
                continue;
            }

            var match = WeaponRowPattern().Match(text);
            if (!match.Success)
            {
                // A row's Properties column can spill onto its own line. The spill has
                // no anchors of its own, so it is attributed to the row above it.
                if (pendingName is not null && weapons.Count > 0 && IsPropertyContinuation(text))
                {
                    weapons[^1] = ApplyExtraProperties(weapons[^1], text);
                }

                continue;
            }

            if (TryBuildWeapon(match, category, kind, out var weapon, out var reason))
            {
                weapons.Add(weapon);
                pendingName = weapon.Name;
            }
            else
            {
                diagnostics.Add(new ParseDiagnostic(match.Groups["name"].Value, reason));
                pendingName = null;
            }
        }

        return weapons;
    }

    private static bool TryBuildWeapon(
        Match match,
        WeaponCategory category,
        WeaponKind kind,
        out WeaponDefinition weapon,
        out string reason)
    {
        weapon = null!;

        var name = match.Groups["name"].Value.Trim();

        if (!DiceExpression.TryParse(match.Groups["damage"].Value, out var damage))
        {
            reason = $"could not parse damage '{match.Groups["damage"].Value}'.";
            return false;
        }

        var propertyText = match.Groups["properties"].Value.Trim();
        var (properties, versatile, range, ammunition, note) = ParseWeaponProperties(propertyText);

        weapon = new WeaponDefinition
        {
            Id = MakeId("weapon", name),
            Name = name,
            Category = category,
            Kind = kind,
            Damage = damage,
            DamageType = Enum.Parse<DamageType>(match.Groups["damageType"].Value, ignoreCase: true),
            Properties = properties,
            VersatileDamage = versatile,
            Range = range,
            AmmunitionKind = ammunition,
            Mastery = Enum.Parse<WeaponMastery>(match.Groups["mastery"].Value, ignoreCase: true),
            WeightPounds = ParseWeight(match.Groups["weight"].Value),
            CostCopper = ParseCost(match.Groups["cost"].Value),
            PropertyNote = note,
        };

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Reads the Properties column. Three properties carry extra data inline —
    /// <c>Versatile (1d10)</c>, <c>Thrown (Range 20/60)</c> and
    /// <c>Ammunition (Range 100/400; Bolt)</c> — and are unpacked rather than kept as text.
    /// </summary>
    private static (WeaponProperty Properties, DiceExpression? Versatile, WeaponRange? Range, string? Ammunition, string? Note)
        ParseWeaponProperties(string text)
    {
        var properties = WeaponProperty.None;
        DiceExpression? versatile = null;
        WeaponRange? range = null;
        string? ammunition = null;
        string? note = null;

        if (text is "-" or "" or "—")
        {
            return (properties, versatile, range, ammunition, note);
        }

        foreach (var token in SplitProperties(text))
        {
            var bare = ParenthesisPattern().Replace(token, string.Empty).Trim();

            // The Lance's "Two-Handed (unless mounted)" is a real qualifier on an
            // otherwise ordinary property, and is kept as prose beside the flag.
            if (token.Contains("unless", StringComparison.OrdinalIgnoreCase))
            {
                note = token;
            }

            switch (bare.Replace("-", string.Empty).ToLowerInvariant())
            {
                case "ammunition":
                    properties |= WeaponProperty.Ammunition;
                    (range, ammunition) = ParseAmmunition(token);
                    break;
                case "finesse":
                    properties |= WeaponProperty.Finesse;
                    break;
                case "heavy":
                    properties |= WeaponProperty.Heavy;
                    break;
                case "light":
                    properties |= WeaponProperty.Light;
                    break;
                case "loading":
                    properties |= WeaponProperty.Loading;
                    break;
                case "reach":
                    properties |= WeaponProperty.Reach;
                    break;
                case "thrown":
                    properties |= WeaponProperty.Thrown;
                    range ??= ParseRange(token);
                    break;
                case "twohanded":
                    properties |= WeaponProperty.TwoHanded;
                    break;
                case "versatile":
                    properties |= WeaponProperty.Versatile;
                    if (VersatilePattern().Match(token) is { Success: true } dice
                        && DiceExpression.TryParse(dice.Groups["dice"].Value, out var parsed))
                    {
                        versatile = parsed;
                    }

                    break;
                default:
                    break;
            }
        }

        return (properties, versatile, range, ammunition, note);
    }

    /// <summary>
    /// Splits the property list on commas that separate properties, ignoring commas
    /// inside parentheses. <c>Ammunition (Range 100/400; Bolt), Heavy, Loading</c> is
    /// three properties, not four.
    /// </summary>
    private static IEnumerable<string> SplitProperties(string text)
    {
        var depth = 0;
        var token = new StringBuilder();

        foreach (var character in text)
        {
            switch (character)
            {
                case '(':
                    depth++;
                    token.Append(character);
                    break;
                case ')':
                    depth--;
                    token.Append(character);
                    break;
                case ',' when depth == 0:
                    if (token.Length > 0)
                    {
                        yield return token.ToString().Trim();
                        token.Clear();
                    }

                    break;
                default:
                    token.Append(character);
                    break;
            }
        }

        if (token.Length > 0)
        {
            yield return token.ToString().Trim();
        }
    }

    private static (WeaponRange? Range, string? Ammunition) ParseAmmunition(string token)
    {
        var match = AmmunitionPattern().Match(token);

        if (!match.Success)
        {
            return (null, null);
        }

        var range = new WeaponRange(
            int.Parse(match.Groups["normal"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["long"].Value, CultureInfo.InvariantCulture));

        return (range, match.Groups["kind"].Value.Trim());
    }

    private static WeaponRange? ParseRange(string token)
    {
        var match = RangePattern().Match(token);

        return match.Success
            ? new WeaponRange(
                int.Parse(match.Groups["normal"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["long"].Value, CultureInfo.InvariantCulture))
            : null;
    }

    private static WeaponDefinition ApplyExtraProperties(WeaponDefinition weapon, string text)
    {
        var (extra, versatile, range, ammunition, note) = ParseWeaponProperties(text);

        return weapon with
        {
            Properties = weapon.Properties | extra,
            VersatileDamage = weapon.VersatileDamage ?? versatile,
            Range = weapon.Range ?? range,
            AmmunitionKind = weapon.AmmunitionKind ?? ammunition,
            PropertyNote = weapon.PropertyNote ?? note,
        };
    }

    private static bool IsPropertyContinuation(string text) =>
        text.Length > 0 && KnownPropertyWordPattern().IsMatch(text);

    private static List<ArmorDefinition> ParseArmor(
        IReadOnlyList<SourceLine> lines,
        List<ParseDiagnostic> diagnostics)
    {
        var armors = new List<ArmorDefinition>();
        var category = ArmorCategory.Light;
        var sawAnyGroup = false;

        foreach (var line in lines)
        {
            var text = line.Text.Trim();

            if (ArmorGroupPattern().Match(text) is { Success: true } group)
            {
                category = Enum.Parse<ArmorCategory>(group.Groups["category"].Value, ignoreCase: true);
                sawAnyGroup = true;
                continue;
            }

            if (!sawAnyGroup)
            {
                continue;
            }

            var match = ArmorRowPattern().Match(text);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            var armorClassText = match.Groups["ac"].Value.Trim();

            if (!TryParseArmorClass(armorClassText, out var baseArmorClass, out var addsDexterity, out var cap))
            {
                diagnostics.Add(new ParseDiagnostic(name, $"could not parse Armor Class '{armorClassText}'."));
                continue;
            }

            armors.Add(new ArmorDefinition
            {
                Id = MakeId("armor", name),
                Name = name,
                Category = category,
                BaseArmorClass = baseArmorClass,
                AddsDexterityModifier = addsDexterity,
                MaximumDexterityModifier = cap,
                MinimumStrength = match.Groups["strength"].Success
                    ? int.Parse(match.Groups["strength"].Value, CultureInfo.InvariantCulture)
                    : null,
                StealthDisadvantage = match.Groups["stealth"].Value.Contains(
                    "Disadvantage",
                    StringComparison.OrdinalIgnoreCase),
                WeightPounds = ParseWeight(match.Groups["weight"].Value),
                CostCopper = ParseCost(match.Groups["cost"].Value),
            });
        }

        return armors;
    }

    /// <summary>
    /// Parses the Armor Class column: <c>11 + Dex modifier</c>,
    /// <c>14 + Dex modifier (max 2)</c>, a bare <c>16</c>, or a Shield's <c>+2</c>.
    /// </summary>
    private static bool TryParseArmorClass(string text, out int baseValue, out bool addsDexterity, out int? cap)
    {
        baseValue = 0;
        addsDexterity = false;
        cap = null;

        var match = ArmorClassPattern().Match(text);
        if (!match.Success)
        {
            return false;
        }

        baseValue = int.Parse(match.Groups["base"].Value, CultureInfo.InvariantCulture);
        addsDexterity = match.Groups["dex"].Success;

        if (match.Groups["cap"].Success)
        {
            cap = int.Parse(match.Groups["cap"].Value, CultureInfo.InvariantCulture);
        }

        return true;
    }

    /// <summary>Converts a printed weight to pounds. A dash means no meaningful weight.</summary>
    private static decimal ParseWeight(string text)
    {
        var value = text.Replace("lb.", string.Empty, StringComparison.Ordinal).Trim();

        if (value is "-" or "" or "—")
        {
            return 0m;
        }

        var fraction = FractionPattern().Match(value);
        if (fraction.Success)
        {
            return decimal.Parse(fraction.Groups["numerator"].Value, CultureInfo.InvariantCulture)
                   / decimal.Parse(fraction.Groups["denominator"].Value, CultureInfo.InvariantCulture);
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var pounds)
            ? pounds
            : 0m;
    }

    /// <summary>
    /// Converts a printed price to copper pieces, so no precision is lost between
    /// <c>5 CP</c> and <c>1,500 GP</c>.
    /// </summary>
    private static int ParseCost(string text)
    {
        var match = CostPattern().Match(text.Trim());
        if (!match.Success)
        {
            return 0;
        }

        var amount = int.Parse(
            match.Groups["amount"].Value.Replace(",", string.Empty),
            CultureInfo.InvariantCulture);

        return match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "CP" => amount,
            "SP" => amount * 10,
            "EP" => amount * 50,
            "GP" => amount * 100,
            "PP" => amount * 1_000,
            _ => amount,
        };
    }

    private static string MakeId(string prefix, string name)
    {
        var slug = new StringBuilder(prefix).Append('.');
        var lastWasDash = true;

        foreach (var character in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character))
            {
                slug.Append(character);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                slug.Append('-');
                lastWasDash = true;
            }
        }

        return slug.ToString().TrimEnd('-');
    }

    [GeneratedRegex(@"^(?<category>Simple|Martial)\s+(?<kind>Melee|Ranged)\s+Weapons$")]
    private static partial Regex WeaponGroupPattern();

    [GeneratedRegex(@"^(?<name>[A-Za-z' ]+?)\s+(?<damage>\d+(?:d\d+)?)\s+(?<damageType>Acid|Bludgeoning|Cold|Fire|Force|Lightning|Necrotic|Piercing|Poison|Psychic|Radiant|Slashing|Thunder)\s+(?<properties>.*?)\s+(?<mastery>Cleave|Graze|Nick|Push|Sap|Slow|Topple|Vex)\s+(?<weight>(?:[\d./]+\s*lb\.)|-)\s+(?<cost>[\d,]+\s*(?:CP|SP|EP|GP|PP))$")]
    private static partial Regex WeaponRowPattern();

    [GeneratedRegex(@"^(?<category>Light|Medium|Heavy|Shield)\b.*\(.*(?:Don|Doff).*\)$")]
    private static partial Regex ArmorGroupPattern();

    [GeneratedRegex(@"^(?<name>[A-Za-z' ]+?)\s+(?<ac>(?:\+?\d+(?:\s*\+\s*Dex modifier(?:\s*\(max \d+\))?)?))\s+(?:Str\s+(?<strength>\d+)|-)\s+(?<stealth>Disadvantage|-)\s+(?<weight>(?:[\d./]+\s*lb\.)|-)\s+(?<cost>[\d,]+\s*(?:CP|SP|EP|GP|PP))$")]
    private static partial Regex ArmorRowPattern();

    [GeneratedRegex(@"^\+?(?<base>\d+)(?:\s*\+\s*(?<dex>Dex modifier)(?:\s*\(max\s*(?<cap>\d+)\))?)?$")]
    private static partial Regex ArmorClassPattern();

    [GeneratedRegex(@"Range\s+(?<normal>\d+)\s*/\s*(?<long>\d+)\s*;\s*(?<kind>[A-Za-z]+)")]
    private static partial Regex AmmunitionPattern();

    [GeneratedRegex(@"Range\s+(?<normal>\d+)\s*/\s*(?<long>\d+)")]
    private static partial Regex RangePattern();

    [GeneratedRegex(@"\((?<dice>\d+d\d+)\)")]
    private static partial Regex VersatilePattern();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex ParenthesisPattern();

    [GeneratedRegex(@"^(?<numerator>\d+)\s*/\s*(?<denominator>\d+)$")]
    private static partial Regex FractionPattern();

    [GeneratedRegex(@"^(?<amount>[\d,]+)\s*(?<unit>CP|SP|EP|GP|PP)$")]
    private static partial Regex CostPattern();

    [GeneratedRegex(@"^(?:Ammunition|Finesse|Heavy|Light|Loading|Reach|Thrown|Two-Handed|Versatile)\b")]
    private static partial Regex KnownPropertyWordPattern();
}
