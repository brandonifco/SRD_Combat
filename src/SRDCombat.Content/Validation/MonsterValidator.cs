using System.Globalization;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Content.Validation;

/// <summary>
/// Checks extracted monsters against invariants the SRD's own printed numbers imply.
/// </summary>
/// <remarks>
/// Most of these exist because the content is machine-extracted from a PDF, and the
/// characteristic extraction failure is a plausible-looking number in the wrong field.
/// The strongest check by far is hit points against hit dice: the SRD prints
/// <c>HP 195 (17d12 + 85)</c>, and 195 is exactly the average of that expression, so
/// any disagreement means one of the two was misread.
/// </remarks>
public static class MonsterValidator
{
    public static ValidationResult Validate(IReadOnlyList<MonsterDefinition> monsters)
    {
        ArgumentNullException.ThrowIfNull(monsters);

        var issues = new List<ValidationIssue>();

        foreach (var duplicate in monsters
                     .GroupBy(monster => monster.Id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "monster.id.duplicate",
                duplicate.Key,
                $"{duplicate.Count()} monsters share this id."));
        }

        foreach (var monster in monsters)
        {
            ValidateOne(monster, issues);
        }

        return new ValidationResult(issues);
    }

    private static void ValidateOne(MonsterDefinition monster, List<ValidationIssue> issues)
    {
        void Add(ValidationSeverity severity, string code, string message) =>
            issues.Add(new ValidationIssue(severity, code, monster.Id, message));

        if (string.IsNullOrWhiteSpace(monster.Id) || !IsSlug(monster.Id))
        {
            Add(ValidationSeverity.Error, "monster.id.malformed", $"'{monster.Id}' is not a lowercase dotted slug.");
        }

        if (string.IsNullOrWhiteSpace(monster.Name))
        {
            Add(ValidationSeverity.Error, "monster.name.missing", "Name is blank.");
        }

        if (monster.Sizes.Count == 0)
        {
            Add(ValidationSeverity.Error, "monster.size.missing", "No size was extracted.");
        }

        // The load-bearing extraction check. See the remarks on this class.
        if (monster.HitPoints != monster.HitDice.Average)
        {
            Add(
                ValidationSeverity.Error,
                "monster.hit_points.disagree_with_dice",
                $"Printed HP {monster.HitPoints} but {monster.HitDice} averages {monster.HitDice.Average}.");
        }

        if (monster.HitPoints <= 0)
        {
            Add(ValidationSeverity.Error, "monster.hit_points.not_positive", $"HP is {monster.HitPoints}.");
        }

        if (monster.ArmorClass is < 1 or > 30)
        {
            Add(ValidationSeverity.Error, "monster.armor_class.out_of_range", $"AC is {monster.ArmorClass}.");
        }

        ValidateAbilities(monster, Add);
        ValidateSpeeds(monster, Add);
        ValidateChallenge(monster, Add);
        ValidateEntries(monster, Add);
    }

    private static void ValidateAbilities(MonsterDefinition monster, Action<ValidationSeverity, string, string> add)
    {
        foreach (var ability in Enum.GetValues<Ability>())
        {
            if (!monster.Abilities.TryGetValue(ability, out var value))
            {
                add(ValidationSeverity.Error, "monster.ability.missing", $"No {ability} score was extracted.");
                continue;
            }

            if (value.Score is < 1 or > 30)
            {
                add(
                    ValidationSeverity.Error,
                    "monster.ability.out_of_range",
                    $"{ability} score is {value.Score}.");
            }

            // A save is the ability modifier, optionally plus the proficiency bonus.
            // Anything else means a column was misread — the two columns sit side by
            // side in the stat block and are easy to transpose.
            var plain = value.Modifier;
            var proficient = value.Modifier + monster.ProficiencyBonus;

            if (value.SaveBonus != plain && value.SaveBonus != proficient)
            {
                add(
                    ValidationSeverity.Warning,
                    "monster.ability.save_unexplained",
                    $"{ability} save is {value.SaveBonus:+0;-0;+0}, but the modifier is " +
                    $"{plain:+0;-0;+0} and proficiency would give {proficient:+0;-0;+0}.");
            }
        }
    }

    private static void ValidateSpeeds(MonsterDefinition monster, Action<ValidationSeverity, string, string> add)
    {
        if (!monster.Speeds.ContainsKey(MovementMode.Walk))
        {
            add(ValidationSeverity.Error, "monster.speed.no_walk", "No walking speed was extracted.");
        }

        foreach (var (mode, feet) in monster.Speeds)
        {
            if (feet < 0 || feet % 5 != 0)
            {
                add(
                    ValidationSeverity.Error,
                    "monster.speed.not_a_multiple_of_five",
                    $"{mode} speed is {feet} ft.");
            }
        }

        if (monster.CanHover && !monster.Speeds.ContainsKey(MovementMode.Fly))
        {
            add(ValidationSeverity.Error, "monster.speed.hover_without_fly", "Marked as hovering with no fly speed.");
        }
    }

    private static void ValidateChallenge(MonsterDefinition monster, Action<ValidationSeverity, string, string> add)
    {
        if (!ChallengeRatingRules.IsDefined(monster.ChallengeRating))
        {
            add(
                ValidationSeverity.Error,
                "monster.challenge_rating.undefined",
                $"CR {monster.ChallengeRating.ToString(CultureInfo.InvariantCulture)} is not a rating the SRD defines.");
            return;
        }

        var expectedExperience = ChallengeRatingRules.GetExperience(monster.ChallengeRating);

        // A CR 0 creature may legitimately be worth 0 rather than 10.
        var experienceIsAcceptable = monster.ExperiencePoints == expectedExperience
            || (monster.ChallengeRating == 0m && monster.ExperiencePoints == 0);

        if (!experienceIsAcceptable)
        {
            // A warning rather than an error, because the SRD itself is not always
            // internally consistent here: the Archmage prints CR 12 with XP 8,000 where
            // its own Challenge Rating table says 8,400. The printed value is kept —
            // silently "correcting" the source would be worse than reporting it.
            add(
                ValidationSeverity.Warning,
                "monster.experience.disagrees_with_challenge_rating",
                $"CR {monster.ChallengeRating.ToString(CultureInfo.InvariantCulture)} should be worth " +
                $"{expectedExperience} XP but the block prints {monster.ExperiencePoints}.");
        }

        var expectedProficiency = ChallengeRatingRules.GetProficiencyBonus(monster.ChallengeRating);
        if (monster.ProficiencyBonus != expectedProficiency)
        {
            add(
                ValidationSeverity.Error,
                "monster.proficiency.disagrees_with_challenge_rating",
                $"CR {monster.ChallengeRating.ToString(CultureInfo.InvariantCulture)} implies PB " +
                $"+{expectedProficiency} but the block prints +{monster.ProficiencyBonus}.");
        }

        if (monster.LairExperiencePoints is { } lair && lair <= monster.ExperiencePoints)
        {
            add(
                ValidationSeverity.Warning,
                "monster.experience.lair_not_greater",
                $"Lair XP {lair} is not greater than base XP {monster.ExperiencePoints}.");
        }
    }

    private static void ValidateEntries(MonsterDefinition monster, Action<ValidationSeverity, string, string> add)
    {
        foreach (var entry in monster.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                add(ValidationSeverity.Error, "monster.entry.name_missing", "An entry has no name.");
            }

            if (entry.Attack is not { } attack)
            {
                continue;
            }

            if (attack.ReachFeet is null && attack.NormalRangeFeet is null)
            {
                add(
                    ValidationSeverity.Error,
                    "monster.attack.no_reach_or_range",
                    $"'{entry.Name}' has neither reach nor range.");
            }

            if (attack.LongRangeFeet is { } longRange
                && attack.NormalRangeFeet is { } normalRange
                && longRange < normalRange)
            {
                add(
                    ValidationSeverity.Error,
                    "monster.attack.long_range_shorter_than_normal",
                    $"'{entry.Name}' has long range {longRange} ft. under normal range {normalRange} ft.");
            }

            foreach (var damage in attack.Damage)
            {
                // The same check as hit points, applied to every damage expression:
                // the SRD prints "10 (2d6 + 3)" and 10 must be that expression's average.
                if (damage.PrintedAverage != damage.Amount.Average)
                {
                    add(
                        ValidationSeverity.Error,
                        "monster.attack.damage_disagrees_with_average",
                        $"'{entry.Name}' prints {damage.PrintedAverage} for {damage.Amount}, " +
                        $"which averages {damage.Amount.Average}.");
                }
            }
        }
    }

    private static bool IsSlug(string value) =>
        value.All(character => char.IsAsciiLetterLower(character)
            || char.IsAsciiDigit(character)
            || character is '.' or '-');
}
