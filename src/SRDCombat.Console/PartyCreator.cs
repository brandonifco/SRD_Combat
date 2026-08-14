using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

namespace SRDCombat.Console;

/// <summary>
/// Interactive party creation: four drafts built at the keyboard, every option shown
/// with its printed description.
/// </summary>
/// <remarks>
/// <para>
/// The Phase 5 charter, from the design doc: GoldBox's creation wizard was rejected
/// because every option was a bare name, so <b>browsing an option and committing to it
/// are different actions here</b> — picking a number shows the SRD's own text and then
/// asks, and nothing advances until the answer is yes. The other half of the charter
/// is that this class holds no rules: every menu comes from
/// <c>CharacterCreation</c>, the scores from <c>AbilityScoreRules</c>, and the final
/// word on the draft is <c>CharacterResolver</c>'s, whose refusal is shown with its
/// reason and returns to the start of that character rather than being swallowed.
/// </para>
/// </remarks>
internal static class PartyCreator
{
    /// <summary>Builds four drafts interactively. Null when the player quits.</summary>
    public static IReadOnlyList<CharacterDraft>? CreateParty(SrdContent content)
    {
        System.Console.WriteLine();
        System.Console.WriteLine("Party creation — four characters. Type a number to read about an option,");
        System.Console.WriteLine("then confirm to take it; 'q' at any prompt quits creation.");

        var drafts = new List<CharacterDraft>();

        while (drafts.Count < 4)
        {
            var draft = CreateCharacter(content, drafts.Count + 1);

            if (draft is null)
            {
                return null;
            }

            drafts.Add(draft);
        }

        return drafts;
    }

    private static CharacterDraft? CreateCharacter(SrdContent content, int number)
    {
        while (true)
        {
            System.Console.WriteLine();
            System.Console.WriteLine($"— Character {number} of 4 —");

            var name = Ask($"Name for character {number}: ");

            if (name is null)
            {
                return null;
            }

            var @class = Choose("Class", CharacterCreation.Classes(content), DescribeClass);

            if (@class is null)
            {
                return null;
            }

            var species = Choose("Species", CharacterCreation.Species(content), DescribeSpecies);

            if (species is null)
            {
                return null;
            }

            var background = Choose("Background", CharacterCreation.Backgrounds(content), DescribeBackground);

            if (background is null)
            {
                return null;
            }

            var scores = ChooseScores();

            if (scores is null)
            {
                return null;
            }

            var draft = ApplyRemainingChoices(
                content,
                new CharacterDraft
                {
                    Name = string.IsNullOrWhiteSpace(name) ? $"Hero {number}" : name.Trim(),
                    ClassId = @class.Id,
                    SpeciesId = species.Id,
                    BackgroundId = background.Id,
                    Level = 1,
                    BaseAbilityScores = scores,
                },
                @class,
                background);

            if (draft is null)
            {
                return null;
            }

            // The resolver has the final word; its refusal is a reason, not a crash.
            try
            {
                var member = PregeneratedParty.Resolve(content, draft, level: 1);
                DescribeSheet(member);
            }
            catch (Exception refusal) when (refusal is ArgumentException or InvalidOperationException)
            {
                System.Console.WriteLine($"  The rules refuse this character: {refusal.Message}");
                System.Console.WriteLine("  Starting this character over.");
                continue;
            }

            var keep = Ask("Keep this character? (y/n): ");

            if (keep is null)
            {
                return null;
            }

            if (keep.Trim().StartsWith('y'))
            {
                return draft;
            }
        }
    }

    private static CharacterDraft? ApplyRemainingChoices(
        SrdContent content,
        CharacterDraft draft,
        ClassDefinition @class,
        BackgroundDefinition background)
    {
        var increases = ChooseBackgroundIncreases(background);

        if (increases is null)
        {
            return null;
        }

        draft = increases.Value.Choice == AbilityIncreaseChoice.TwoAndOne
            ? draft with
            {
                IncreaseChoice = AbilityIncreaseChoice.TwoAndOne,
                PrimaryIncrease = increases.Value.Primary,
                SecondaryIncrease = increases.Value.Secondary,
            }
            : draft with { IncreaseChoice = AbilityIncreaseChoice.OneEach };

        var skills = ChooseSkills(@class);

        if (skills is null)
        {
            return null;
        }

        draft = draft with { ChosenSkills = skills };

        if (CharacterCreation.GrantsFightingStyle(@class, level: 1))
        {
            var style = ChooseFightingStyle();

            if (style is null)
            {
                return null;
            }

            draft = draft with { FightingStyle = style.Value };
        }

        var gear = ChooseGear(content, @class);

        if (gear is null)
        {
            return null;
        }

        draft = draft with
        {
            WeaponIds = gear.Value.WeaponIds,
            ArmorId = gear.Value.ArmorId,
            HasShield = gear.Value.Shield,
        };

        var masteries = ChooseMasteries(content, @class);

        if (masteries is null)
        {
            return null;
        }

        draft = draft with { WeaponMasteryIds = masteries };

        var spells = ChooseSpells(content, @class);

        if (spells is null)
        {
            return null;
        }

        return draft with { ChosenSpellIds = spells };
    }

    // ── The choices ─────────────────────────────────────────────────────────────

    private static Dictionary<Ability, int>? ChooseScores()
    {
        while (true)
        {
            System.Console.WriteLine();
            System.Console.WriteLine("Ability scores — the printed methods:");
            System.Console.WriteLine("  1. Standard Array: assign 15, 14, 13, 12, 10, 8");
            System.Console.WriteLine($"  2. Point Buy: {AbilityScoreRules.PointBudget} points, scores 8-15" +
                " (8:0 9:1 10:2 11:3 12:4 13:5 14:7 15:9)");

            var answer = Ask("Method (1/2): ");

            switch (answer?.Trim())
            {
                case null:
                    return null;
                case "1":
                    return AssignStandardArray();
                case "2":
                    var bought = PointBuy();

                    if (bought is not null)
                    {
                        return bought;
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static Dictionary<Ability, int>? AssignStandardArray()
    {
        var remaining = AbilityScoreRules.StandardArray.ToList();
        var scores = new Dictionary<Ability, int>();

        foreach (var ability in Enum.GetValues<Ability>())
        {
            while (true)
            {
                var answer = Ask($"  {ability} (remaining: {string.Join(", ", remaining)}): ");

                if (answer is null)
                {
                    return null;
                }

                if (int.TryParse(answer.Trim(), out var value) && remaining.Remove(value))
                {
                    scores[ability] = value;
                    break;
                }

                System.Console.WriteLine("  Pick one of the remaining numbers.");
            }
        }

        return scores;
    }

    private static Dictionary<Ability, int>? PointBuy()
    {
        var scores = Enum.GetValues<Ability>().ToDictionary(ability => ability, _ => 8);

        while (true)
        {
            var spent = AbilityScoreRules.PointCost(scores)!.Value;
            System.Console.WriteLine(
                $"  {string.Join("  ", scores.Select(pair => $"{Abbreviation(pair.Key)} {pair.Value}"))}" +
                $"  |  {AbilityScoreRules.PointBudget - spent} points left");

            var answer = Ask("  '+str' raises, '-str' lowers, 'done' finishes: ");

            if (answer is null)
            {
                return null;
            }

            var trimmed = answer.Trim().ToLowerInvariant();

            if (trimmed == "done")
            {
                if (AbilityScoreRules.IsLegalPointBuy(scores))
                {
                    return scores;
                }

                System.Console.WriteLine("  That spends more than the budget.");
                continue;
            }

            if (trimmed.Length >= 4 && (trimmed[0] == '+' || trimmed[0] == '-')
                && ParseAbility(trimmed[1..]) is { } ability)
            {
                var target = scores[ability] + (trimmed[0] == '+' ? 1 : -1);

                if (target is >= 8 and <= 15
                    && AbilityScoreRules.PointCost(
                        new Dictionary<Ability, int>(scores) { [ability] = target })
                        is { } cost
                    && cost <= AbilityScoreRules.PointBudget)
                {
                    scores[ability] = target;
                    continue;
                }
            }

            System.Console.WriteLine("  Scores run 8-15 within the budget — '+wis', '-cha', 'done'.");
        }
    }

    private static (AbilityIncreaseChoice Choice, Ability Primary, Ability Secondary)? ChooseBackgroundIncreases(
        BackgroundDefinition background)
    {
        var offered = background.AbilityScores;

        while (true)
        {
            System.Console.WriteLine();
            System.Console.WriteLine(
                $"{background.Name} raises {string.Join(", ", offered)}: +2 to one and +1 to another, or +1 to all three.");

            var answer = Ask("  '2' for +2/+1, '3' for +1 to each: ");

            switch (answer?.Trim())
            {
                case null:
                    return null;
                case "3":
                    return (AbilityIncreaseChoice.OneEach, default, default);
                case "2":
                    var primary = PickAbility($"  +2 to which ({string.Join("/", offered)})? ", offered);

                    if (primary is null)
                    {
                        return null;
                    }

                    var rest = offered.Where(ability => ability != primary).ToArray();
                    var secondary = PickAbility($"  +1 to which ({string.Join("/", rest)})? ", rest);

                    if (secondary is null)
                    {
                        return null;
                    }

                    return (AbilityIncreaseChoice.TwoAndOne, primary.Value, secondary.Value);
                default:
                    break;
            }
        }
    }

    private static IReadOnlyList<string>? ChooseSkills(ClassDefinition @class)
    {
        var (count, options) = CharacterCreation.SkillChoices(@class);
        var chosen = new List<string>();

        System.Console.WriteLine();
        System.Console.WriteLine($"Skills — choose {count}:");

        while (chosen.Count < count)
        {
            for (var index = 0; index < options.Count; index++)
            {
                var marker = chosen.Contains(options[index]) ? "*" : " ";
                System.Console.WriteLine(
                    $"  {marker}{index + 1}. {options[index]} ({SkillRules.AbilityFor(options[index])})");
            }

            var answer = Ask($"  Skill {chosen.Count + 1} of {count}: ");

            if (answer is null)
            {
                return null;
            }

            if (int.TryParse(answer.Trim(), out var pick)
                && pick >= 1 && pick <= options.Count
                && !chosen.Contains(options[pick - 1]))
            {
                chosen.Add(options[pick - 1]);
            }
        }

        return chosen;
    }

    private static FightingStyle? ChooseFightingStyle()
    {
        var styles = CharacterCreation.ExecutedFightingStyles;

        while (true)
        {
            System.Console.WriteLine();
            System.Console.WriteLine("Fighting Style — the styles the engine executes; others exist in print");
            System.Console.WriteLine("and would sit on the sheet unimplemented:");
            System.Console.WriteLine("  1. Archery — +2 to attack rolls with Ranged weapons");
            System.Console.WriteLine("  2. Defense — +1 AC while wearing armor");
            System.Console.WriteLine("  0. Skip — take an unexecuted printed style later");

            var answer = Ask("  Style: ");

            switch (answer?.Trim())
            {
                case null:
                    return null;
                case "0":
                    return FightingStyle.Unspecified;
                case "1":
                    return styles[0];
                case "2":
                    return styles[1];
                default:
                    break;
            }
        }
    }

    private static (IReadOnlyList<string> WeaponIds, string? ArmorId, bool Shield)? ChooseGear(
        SrdContent content,
        ClassDefinition @class)
    {
        var weapon = Choose("Weapon", CharacterCreation.WeaponOptions(content, @class), DescribeWeapon);

        if (weapon is null)
        {
            return null;
        }

        string? armorId = null;
        var armorOptions = CharacterCreation.ArmorOptions(content, @class);

        if (armorOptions.Count > 0)
        {
            var wants = Ask("Wear armor? (y/n): ");

            if (wants is null)
            {
                return null;
            }

            if (wants.Trim().StartsWith('y'))
            {
                var armor = Choose("Armor", armorOptions, DescribeArmor);

                if (armor is null)
                {
                    return null;
                }

                armorId = armor.Id;
            }
        }

        var shield = false;

        if (CharacterCreation.MayCarryShield(@class))
        {
            var answer = Ask("Carry a Shield? (+2 AC) (y/n): ");

            if (answer is null)
            {
                return null;
            }

            shield = answer.Trim().StartsWith('y');
        }

        return ([weapon.Id], armorId, shield);
    }

    private static IReadOnlyList<string>? ChooseMasteries(SrdContent content, ClassDefinition @class)
    {
        var allowance = CharacterCreation.MasteryAllowance(@class, level: 1);

        if (allowance == 0)
        {
            return [];
        }

        var options = CharacterCreation.MasteryOptions(content, @class);
        var chosen = new List<string>();

        System.Console.WriteLine();
        System.Console.WriteLine($"Weapon Mastery — up to {allowance} kinds whose property the engine executes:");

        while (chosen.Count < allowance)
        {
            for (var index = 0; index < options.Count; index++)
            {
                var marker = chosen.Contains(options[index].Id) ? "*" : " ";
                System.Console.WriteLine($"  {marker}{index + 1}. {options[index].Name} — {options[index].Mastery}");
            }

            var answer = Ask($"  Mastery {chosen.Count + 1} of up to {allowance} ('done' to stop): ");

            if (answer is null)
            {
                return null;
            }

            if (answer.Trim().Equals("done", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (int.TryParse(answer.Trim(), out var pick)
                && pick >= 1 && pick <= options.Count
                && !chosen.Contains(options[pick - 1].Id))
            {
                chosen.Add(options[pick - 1].Id);
            }
        }

        return chosen;
    }

    private static IReadOnlyList<string>? ChooseSpells(SrdContent content, ClassDefinition @class)
    {
        var options = CharacterCreation.SpellOptions(content, @class);

        if (options.Count == 0)
        {
            return [];
        }

        var (cantrips, prepared) = CharacterCreation.SpellAllowances(@class, level: 1);
        var chosen = new List<string>();

        System.Console.WriteLine();
        System.Console.WriteLine(
            $"Spells — the verified menu. Level 1 prepares {cantrips} cantrip(s) and {prepared} spell(s);");
        System.Console.WriteLine("choices beyond that are the plan for later levels, in this order.");

        while (true)
        {
            for (var index = 0; index < options.Count; index++)
            {
                var marker = chosen.Contains(options[index].Id) ? "*" : " ";
                var kind = options[index].IsCantrip ? "cantrip" : $"level {options[index].Level}";
                System.Console.WriteLine($"  {marker}{index + 1}. {options[index].Name} ({kind})");
            }

            var answer = Ask("  Number to read and take, 'done' to finish: ");

            if (answer is null)
            {
                return null;
            }

            if (answer.Trim().Equals("done", StringComparison.OrdinalIgnoreCase))
            {
                return chosen;
            }

            if (int.TryParse(answer.Trim(), out var pick) && pick >= 1 && pick <= options.Count)
            {
                var spell = options[pick - 1];

                if (chosen.Contains(spell.Id))
                {
                    chosen.Remove(spell.Id);
                    continue;
                }

                System.Console.WriteLine();
                System.Console.WriteLine($"  {spell.Name} — {spell.CastingTimeText}, {spell.RangeText}");
                System.Console.WriteLine(Wrap(spell.Text, indent: "  "));

                var take = Ask($"  Take {spell.Name}? (y/n): ");

                if (take is null)
                {
                    return null;
                }

                if (take.Trim().StartsWith('y'))
                {
                    chosen.Add(spell.Id);
                }
            }
        }
    }

    // ── Browsing: pick a number, read the print, then commit ────────────────────

    private static TOption? Choose<TOption>(
        string what,
        IReadOnlyList<TOption> options,
        Action<TOption> describe)
        where TOption : class
    {
        while (true)
        {
            System.Console.WriteLine();
            System.Console.WriteLine($"{what}:");

            for (var index = 0; index < options.Count; index++)
            {
                System.Console.WriteLine($"  {index + 1}. {NameOf(options[index])}");
            }

            var answer = Ask($"  {what} number: ");

            if (answer is null)
            {
                return null;
            }

            if (int.TryParse(answer.Trim(), out var pick) && pick >= 1 && pick <= options.Count)
            {
                var option = options[pick - 1];

                System.Console.WriteLine();
                describe(option);

                var take = Ask($"  Take {NameOf(option)}? (y/n): ");

                if (take is null)
                {
                    return null;
                }

                if (take.Trim().StartsWith('y'))
                {
                    return option;
                }
            }
        }
    }

    private static string NameOf(object option) => option switch
    {
        ClassDefinition definition => definition.Name,
        SpeciesDefinition definition => definition.Name,
        BackgroundDefinition definition => definition.Name,
        WeaponDefinition definition => definition.Name,
        ArmorDefinition definition => definition.Name,
        _ => option.ToString() ?? "?",
    };

    // ── The printed descriptions ────────────────────────────────────────────────

    private static void DescribeClass(ClassDefinition @class)
    {
        System.Console.WriteLine(
            $"  {@class.Name} — d{@class.HitDieSides} hit die, primary " +
            $"{string.Join(" or ", @class.PrimaryAbilities)}, saves " +
            $"{string.Join(" and ", @class.SavingThrowProficiencies)}.");
        System.Console.WriteLine($"  Weapons: {@class.WeaponProficiencies}. Armor: " +
            (string.IsNullOrEmpty(@class.ArmorTraining) ? "none" : @class.ArmorTraining) + ".");

        foreach (var feature in @class.Features.Where(feature => feature.GrantedAtLevel == 1))
        {
            System.Console.WriteLine($"  Level 1 — {feature.Name}:");
            System.Console.WriteLine(Wrap(feature.Text, indent: "    "));
        }
    }

    private static void DescribeSpecies(SpeciesDefinition species)
    {
        System.Console.WriteLine(
            $"  {species.Name} — {species.Sizes[0]}, Speed {species.SpeedFeet} ft.");

        foreach (var trait in species.Traits)
        {
            System.Console.WriteLine($"  {trait.Name}:");
            System.Console.WriteLine(Wrap(trait.Text, indent: "    "));
        }
    }

    private static void DescribeBackground(BackgroundDefinition background)
    {
        System.Console.WriteLine($"  {background.Name} — raises {string.Join(", ", background.AbilityScores)}.");
        System.Console.WriteLine($"  Feat: {background.FeatName}. Skills: {string.Join(", ", background.SkillProficiencies)}.");
        System.Console.WriteLine(Wrap($"Equipment: {background.Equipment}", indent: "  "));
    }

    private static void DescribeWeapon(WeaponDefinition weapon)
    {
        var properties = weapon.Properties == WeaponProperty.None ? "" : $" [{weapon.Properties}]";
        System.Console.WriteLine(
            $"  {weapon.Name} — {weapon.Category}, {weapon.Damage} {weapon.DamageType}{properties}, " +
            $"Mastery: {weapon.Mastery}.");
    }

    private static void DescribeArmor(ArmorDefinition armor) =>
        System.Console.WriteLine($"  {armor.Name} — {armor.Category}.");

    private static void DescribeSheet(PartyMember member)
    {
        var sheet = member.Sheet;

        System.Console.WriteLine();
        System.Console.WriteLine(
            $"  {sheet.Name}: AC {sheet.ArmorClass}, {sheet.MaximumHitPoints} hit points, " +
            $"Speed {sheet.SpeedFeet} ft.");

        if (member.Combatant.Stats.Character is { Spells.Count: > 0 } caster)
        {
            System.Console.WriteLine(
                $"  Prepares: {string.Join(", ", caster.Spells.Select(spell => spell.Name))}.");
        }

        if (sheet.UnimplementedFeatures.Count > 0)
        {
            System.Console.WriteLine(Wrap(
                $"Printed but not yet implemented: {string.Join(", ", sheet.UnimplementedFeatures)}.",
                indent: "  "));
        }
    }

    // ── Small helpers ───────────────────────────────────────────────────────────

    /// <summary>Reads a line; null means quit ('q' or end of input).</summary>
    private static string? Ask(string prompt)
    {
        System.Console.Write(prompt);
        var answer = System.Console.ReadLine();

        return answer is null || answer.Trim().Equals("q", StringComparison.OrdinalIgnoreCase)
            ? null
            : answer;
    }

    private static Ability? PickAbility(string prompt, IReadOnlyList<Ability> offered)
    {
        while (true)
        {
            var answer = Ask(prompt);

            if (answer is null)
            {
                return null;
            }

            if (ParseAbility(answer.Trim()) is { } ability && offered.Contains(ability))
            {
                return ability;
            }
        }
    }

    private static Ability? ParseAbility(string text) =>
        Enum.GetValues<Ability>()
            .Cast<Ability?>()
            .FirstOrDefault(ability => ability!.Value.ToString()
                .StartsWith(text, StringComparison.OrdinalIgnoreCase));

    private static string Abbreviation(Ability ability) => ability.ToString()[..3].ToUpperInvariant();

    private static string Wrap(string text, string indent)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var line = indent;

        foreach (var word in words)
        {
            if (line.Length + word.Length + 1 > 78 && line.Length > indent.Length)
            {
                lines.Add(line);
                line = indent;
            }

            line += (line.Length > indent.Length ? " " : "") + word;
        }

        if (line.Length > indent.Length)
        {
            lines.Add(line);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
