using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SrdExtract.Pdf;

namespace SrdExtract.Parsing;

/// <summary>A problem encountered while parsing, reported rather than swallowed.</summary>
/// <param name="Subject">The monster being parsed, or the line's page when unknown.</param>
/// <param name="Message">What went wrong.</param>
public sealed record ParseDiagnostic(string Subject, string Message)
{
    public override string ToString() => $"{Subject}: {Message}";
}

/// <summary>The result of parsing the bestiary.</summary>
public sealed record MonsterParseResult(
    IReadOnlyList<MonsterDefinition> Monsters,
    IReadOnlyList<ParseDiagnostic> Diagnostics);

/// <summary>
/// Turns the Monsters and Animals sections into stat blocks.
/// </summary>
/// <remarks>
/// A state machine over <see cref="SourceLine"/>s, classifying each line by its font
/// (see <see cref="StatBlockFonts"/>) and accumulating into the monster currently being
/// built. A monster's block routinely continues across a column or page break, so
/// nothing is reset at those boundaries — only a new name line ends the current block.
/// </remarks>
public static partial class MonsterParser
{
    public static MonsterParseResult Parse(IReadOnlyList<SourceLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var monsters = new List<MonsterDefinition>();
        var diagnostics = new List<ParseDiagnostic>();

        MonsterBuilder? current = null;

        void Flush()
        {
            if (current is null)
            {
                return;
            }

            if (current.TryBuild(out var monster, out var reason))
            {
                monsters.Add(monster);
            }
            else
            {
                diagnostics.Add(new ParseDiagnostic(current.Name, reason));
            }

            current = null;
        }

        foreach (var line in lines)
        {
            if (line.Words.Count == 0)
            {
                continue;
            }

            // A name in the heading font starts a new stat block — unless it is set at
            // the larger size, which is the A-Z group heading above it ("Bandits" over
            // "Bandit" and "Bandit Captain") rather than a creature.
            if (line.Font == StatBlockFonts.Name)
            {
                if (line.Height <= StatBlockFonts.MaximumNameHeight)
                {
                    Flush();
                    current = new MonsterBuilder(line.Text, line.Page);
                }

                continue;
            }

            current?.Accept(line);
        }

        Flush();

        return new MonsterParseResult(monsters, diagnostics);
    }

    /// <summary>Accumulates one stat block as its lines arrive.</summary>
    private sealed partial class MonsterBuilder(string name, int page)
    {
        private readonly List<(int Score, int PrintedModifier, int Save)> _abilityTriples = [];
        private readonly Dictionary<MovementMode, int> _speeds = [];
        private readonly Dictionary<string, int> _skills = new(StringComparer.Ordinal);
        private readonly Dictionary<DamageType, DamageResponse> _damageResponses = [];
        private readonly List<ConditionType> _conditionImmunities = [];
        private readonly List<MonsterSense> _senses = [];
        private readonly List<string> _languages = [];
        private readonly List<string> _gear = [];
        private readonly List<EntryBuilder> _entries = [];
        private readonly StringBuilder _statBuffer = new();

        private MonsterEntrySection _section = MonsterEntrySection.Trait;
        private bool _inStatHeader = true;
        private bool _metaSeen;
        private bool _canHover;
        private int? _armorClass;
        private int? _initiative;
        private int? _hitPoints;
        private DiceExpression? _hitDice;
        private int? _passivePerception;
        private decimal? _challengeRating;
        private int? _experience;
        private int? _lairExperience;
        private int? _proficiencyBonus;
        private IReadOnlyList<CreatureSize> _sizes = [];
        private CreatureType _type;
        private string? _subtype;
        private string _alignment = "Unaligned";

        public string Name { get; } = name;

        public void Accept(SourceLine line)
        {
            switch (line.Font)
            {
                // "MOD SAVE" sits in the header font at a much smaller size and is a
                // column label, not a section.
                case StatBlockFonts.SectionHeader when line.Height >= StatBlockFonts.MinimumSectionHeaderHeight:
                    FlushStatBuffer();
                    _inStatHeader = false;
                    _section = ParseSection(line.Text) ?? _section;
                    return;

                case StatBlockFonts.SectionHeader:
                    return;

                case StatBlockFonts.AbilityTable:
                    _abilityTriples.AddRange(StatBlockLineGrammar.ParseAbilityRow(line.Text));
                    return;

                case StatBlockFonts.Italic when !_metaSeen && _inStatHeader:
                    ApplyMeta(line.Text);
                    return;

                case StatBlockFonts.Stat:
                    FlushStatBuffer();
                    _statBuffer.Append(line.Text);
                    return;

                case StatBlockFonts.EntryName:
                    FlushStatBuffer();
                    _inStatHeader = false;
                    StartEntry(line);
                    return;

                default:
                    // Body prose: either the wrapped remainder of a stat line, or the
                    // continuation of the entry currently being read.
                    if (_inStatHeader)
                    {
                        AppendWrapped(_statBuffer, line.Text);
                    }
                    else if (_entries.Count > 0)
                    {
                        _entries[^1].Append(line.Text);
                    }

                    return;
            }
        }

        public bool TryBuild(out MonsterDefinition monster, out string reason)
        {
            monster = null!;
            FlushStatBuffer();

            if (_armorClass is null || _hitPoints is null || _hitDice is null)
            {
                reason = "no AC or hit points were found — probably not a stat block.";
                return false;
            }

            if (_abilityTriples.Count < 6)
            {
                reason = $"only {_abilityTriples.Count} of 6 ability scores were found.";
                return false;
            }

            if (_challengeRating is null || _experience is null || _proficiencyBonus is null)
            {
                reason = "no CR line was found.";
                return false;
            }

            if (!_speeds.ContainsKey(MovementMode.Walk))
            {
                // Every stat block prints a Speed line; a few creatures have 0 ft.
                _speeds[MovementMode.Walk] = 0;
            }

            var abilities = Enum.GetValues<Ability>()
                .Select((ability, index) => (ability, triple: _abilityTriples[index]))
                .ToDictionary(pair => pair.ability, pair => new MonsterAbility(pair.triple.Score, pair.triple.Save));

            monster = new MonsterDefinition
            {
                Id = MakeId(Name),
                Name = Name,
                Sizes = _sizes.Count > 0 ? _sizes : [CreatureSize.Medium],
                Type = _type,
                Subtype = _subtype,
                Alignment = _alignment,
                ArmorClass = _armorClass.Value,
                InitiativeBonus = _initiative ?? abilities[Ability.Dexterity].Modifier,
                HitPoints = _hitPoints.Value,
                HitDice = _hitDice,
                Speeds = _speeds,
                CanHover = _canHover,
                Abilities = abilities,
                Skills = _skills,
                DamageResponses = _damageResponses,
                ConditionImmunities = _conditionImmunities,
                Senses = _senses,
                PassivePerception = _passivePerception ?? 10 + abilities[Ability.Wisdom].Modifier,
                Languages = _languages,
                Gear = _gear,
                ChallengeRating = _challengeRating.Value,
                ExperiencePoints = _experience.Value,
                LairExperiencePoints = _lairExperience,
                ProficiencyBonus = _proficiencyBonus.Value,
                Entries = _entries.Select(entry => entry.Build()).ToArray(),
                SourcePage = page,
            };

            reason = string.Empty;
            return true;
        }

        private void StartEntry(SourceLine line)
        {
            var heading = line.LeadingRunInFont("BoldItalic").TrimEnd('.', ' ');
            var body = line.Text;

            // The entry's name and the start of its prose share one visual line; the
            // font boundary is the only thing separating them.
            if (heading.Length > 0 && body.StartsWith(heading, StringComparison.Ordinal))
            {
                body = body[heading.Length..].TrimStart('.', ' ');
            }

            _entries.Add(new EntryBuilder(
                heading.Length > 0 ? heading : line.Text.TrimEnd('.'),
                _section,
                body));
        }

        private void ApplyMeta(string text)
        {
            _metaSeen = true;

            if (StatBlockLineGrammar.ParseMeta(text) is not { } meta)
            {
                return;
            }

            _sizes = meta.Sizes;
            _type = meta.Type;
            _subtype = meta.Subtype;
            _alignment = meta.Alignment;
        }

        /// <summary>
        /// Parses the buffered stat line once it is known to be complete. Buffering is
        /// what makes wrapped lines work: <c>Senses</c> routinely spills its Passive
        /// Perception onto a second line set in the body font.
        /// </summary>
        private void FlushStatBuffer()
        {
            if (_statBuffer.Length == 0)
            {
                return;
            }

            var text = _statBuffer.ToString().Trim();
            _statBuffer.Clear();

            ApplyStatLine(text);
        }

        private void ApplyStatLine(string text)
        {
            if (ArmorClassPattern().Match(text) is { Success: true } armorClass)
            {
                _armorClass = int.Parse(armorClass.Groups["ac"].Value, CultureInfo.InvariantCulture);

                if (armorClass.Groups["init"].Success)
                {
                    _initiative = int.Parse(
                        armorClass.Groups["init"].Value.Replace(" ", string.Empty),
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture);
                }

                return;
            }

            if (HitPointsPattern().Match(text) is { Success: true } hitPoints)
            {
                _hitPoints = int.Parse(hitPoints.Groups["hp"].Value, CultureInfo.InvariantCulture);

                if (DiceExpression.TryParse(hitPoints.Groups["dice"].Value, out var dice))
                {
                    _hitDice = dice;
                }

                return;
            }

            if (TryTakeLabel(text, "Speed", out var speedText))
            {
                var (speeds, canHover) = StatBlockLineGrammar.ParseSpeeds(speedText);
                foreach (var (mode, feet) in speeds)
                {
                    _speeds[mode] = feet;
                }

                _canHover |= canHover;
                return;
            }

            if (TryTakeLabel(text, "Skills", out var skillText))
            {
                foreach (Match match in SkillPattern().Matches(skillText))
                {
                    _skills[match.Groups["name"].Value.Trim()] = int.Parse(
                        match.Groups["bonus"].Value.Replace(" ", string.Empty),
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture);
                }

                return;
            }

            if (TryTakeLabel(text, "Resistances", out var resistances))
            {
                ApplyDamageResponses(resistances, DamageResponse.Resistance);
                return;
            }

            if (TryTakeLabel(text, "Vulnerabilities", out var vulnerabilities))
            {
                ApplyDamageResponses(vulnerabilities, DamageResponse.Vulnerability);
                return;
            }

            if (TryTakeLabel(text, "Immunities", out var immunities))
            {
                ApplyDamageResponses(immunities, DamageResponse.Immunity);
                return;
            }

            if (TryTakeLabel(text, "Senses", out var senseText))
            {
                var (senses, passive) = StatBlockLineGrammar.ParseSenses(senseText);
                _senses.AddRange(senses);
                _passivePerception ??= passive;
                return;
            }

            if (TryTakeLabel(text, "Languages", out var languageText))
            {
                _languages.AddRange(SplitList(languageText).Where(value => !IsNone(value)));
                return;
            }

            if (TryTakeLabel(text, "Gear", out var gearText))
            {
                _gear.AddRange(SplitList(gearText).Where(value => !IsNone(value)));
                return;
            }

            if (StatBlockLineGrammar.ParseChallenge(text) is { } challenge)
            {
                _challengeRating = challenge.Rating;
                _experience = challenge.Experience;
                _lairExperience = challenge.LairExperience;
                _proficiencyBonus = challenge.ProficiencyBonus;
            }
        }

        /// <summary>
        /// Applies one of the three damage-response lines. The Immunities line mixes
        /// damage types and conditions, separated by a semicolon
        /// (<c>Immunities Fire, Poison; Poisoned</c>), so each token is tried as both.
        /// </summary>
        private void ApplyDamageResponses(string text, DamageResponse response)
        {
            foreach (var token in SplitList(text))
            {
                if (Enum.TryParse<DamageType>(token, ignoreCase: true, out var damageType))
                {
                    _damageResponses[damageType] = response;
                }
                else if (Enum.TryParse<ConditionType>(token, ignoreCase: true, out var condition)
                         && !_conditionImmunities.Contains(condition))
                {
                    _conditionImmunities.Add(condition);
                }
            }
        }

        private static bool TryTakeLabel(string text, string label, out string remainder)
        {
            if (text.StartsWith(label + " ", StringComparison.Ordinal))
            {
                remainder = text[(label.Length + 1)..].Trim();
                return true;
            }

            remainder = string.Empty;
            return false;
        }

        private static IEnumerable<string> SplitList(string text) => text
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value.Length > 0);

        private static bool IsNone(string value) =>
            value is "-" or "None" || value.Equals("none", StringComparison.OrdinalIgnoreCase);

        private static MonsterEntrySection? ParseSection(string text) => text.Trim() switch
        {
            "Traits" => MonsterEntrySection.Trait,
            "Actions" => MonsterEntrySection.Action,
            "Bonus Actions" => MonsterEntrySection.BonusAction,
            "Reactions" => MonsterEntrySection.Reaction,
            "Legendary Actions" => MonsterEntrySection.LegendaryAction,
            _ => null,
        };

        [GeneratedRegex(@"^AC\s+(?<ac>\d+)(?:.*?Initiative\s+(?<init>[+-]\s?\d+))?")]
        private static partial Regex ArmorClassPattern();

        [GeneratedRegex(@"^HP\s+(?<hp>\d+)\s*\((?<dice>[^)]+)\)")]
        private static partial Regex HitPointsPattern();

        [GeneratedRegex(@"(?<name>[A-Z][A-Za-z' ]*?)\s*(?<bonus>[+-]\s?\d+)")]
        private static partial Regex SkillPattern();
    }

    /// <summary>Accumulates one trait or action as its wrapped lines arrive.</summary>
    private sealed class EntryBuilder(string name, MonsterEntrySection section, string firstLine)
    {
        private readonly StringBuilder _text = new(firstLine);

        public void Append(string line) => AppendWrapped(_text, line);

        public MonsterEntry Build()
        {
            var text = _text.ToString().Trim();

            // Every entry goes through classification, so none can pass as plain prose.
            return EntryMechanicsParser.Classify(name, section, text);
        }
    }

    /// <summary>
    /// Joins a wrapped line onto what came before, undoing the hyphenation the SRD's
    /// justified columns introduce: a line ending in a hyphen whose continuation starts
    /// lowercase was one word split across two lines.
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

    /// <summary>Turns a printed name into a stable slug — <c>monster.bandit-captain</c>.</summary>
    private static string MakeId(string name)
    {
        var slug = new StringBuilder("monster.");
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
}
