using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Characters;

/// <summary>The content a character is built from.</summary>
/// <param name="Species">The chosen species.</param>
/// <param name="Class">The chosen class.</param>
/// <param name="Background">The chosen background.</param>
/// <param name="Weapons">The weapons the draft names, by id.</param>
/// <param name="Armor">The armour the draft names, by id. May be empty.</param>
public sealed record CharacterBuildContent(
    SpeciesDefinition Species,
    ClassDefinition Class,
    BackgroundDefinition Background,
    IReadOnlyDictionary<string, WeaponDefinition> Weapons,
    IReadOnlyDictionary<string, ArmorDefinition> Armor);

/// <summary>
/// Turns a <see cref="CharacterDraft"/> into a <see cref="CharacterSheet"/>.
/// </summary>
/// <remarks>
/// Every number on a sheet is computed here, so a character's AC and their armour can
/// never disagree. Where the SRD offers a choice the engine cannot make — how the
/// background's ability increases are spent, which skills were taken — the draft
/// supplies it; everything else is derived.
/// </remarks>
public static class CharacterResolver
{
    /// <summary>Builds a sheet, or throws naming what is wrong with the draft.</summary>
    public static CharacterSheet Resolve(
        CharacterDraft draft,
        CharacterBuildContent content,
        HitPointMethod hitPointMethod = HitPointMethod.Average,
        IRandomSource? random = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(content);

        if (draft.Level < 1 || draft.Level > AdvancementRules.MaximumSupportedLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(draft),
                draft.Level,
                $"This game supports levels 1-{AdvancementRules.MaximumSupportedLevel}.");
        }

        if (hitPointMethod == HitPointMethod.Rolled && random is null)
        {
            throw new ArgumentNullException(nameof(random), "Rolled hit points need a random source.");
        }

        var scores = ApplyBackgroundIncreases(draft, content.Background);
        var proficiency = AdvancementRules.ProficiencyBonusForLevel(draft.Level);
        var levelRow = content.Class.AtLevel(draft.Level)
            ?? throw new InvalidOperationException($"{content.Class.Name} has no level {draft.Level} row.");

        var features = ResolveFeatures(content.Class, draft.Level);
        var constitution = AbilityRules.ModifierFor(scores[Ability.Constitution]);

        var (armorClass, armorSource) = ResolveArmorClass(draft, content, scores, features);

        return new CharacterSheet
        {
            Name = draft.Name,
            SpeciesName = content.Species.Name,
            ClassName = content.Class.Name,
            BackgroundName = content.Background.Name,
            Level = draft.Level,
            ProficiencyBonus = proficiency,
            AbilityScores = scores,
            MaximumHitPoints = ResolveHitPoints(content.Class, draft.Level, constitution, hitPointMethod, random),
            ArmorClass = armorClass,
            ArmorClassSource = armorSource,
            SpeedFeet = ResolveSpeed(draft, content, features),
            Size = content.Species.Sizes.Count > 0 ? content.Species.Sizes[0] : CreatureSize.Medium,
            SavingThrows = ResolveSavingThrows(content.Class, scores, proficiency),
            Skills = ResolveSkills(draft, content.Background, scores, proficiency),
            Attacks = ResolveAttacks(draft, content, scores, proficiency),
            Features = features,
            SpellSlots = levelRow.SpellSlots,
            UnimplementedFeatures = ResolveUnimplementedFeatures(content.Class, draft.Level),
        };
    }

    /// <summary>
    /// Applies the background's ability score increases.
    /// </summary>
    /// <remarks>
    /// A 2024 change worth being explicit about: increases come from the
    /// <em>background</em>, not the species. A species grants no ability scores at all.
    /// </remarks>
    private static Dictionary<Ability, int> ApplyBackgroundIncreases(
        CharacterDraft draft,
        BackgroundDefinition background)
    {
        var scores = Enum.GetValues<Ability>()
            .ToDictionary(ability => ability, ability => draft.BaseAbilityScores.GetValueOrDefault(ability, 10));

        if (draft.IncreaseChoice == AbilityIncreaseChoice.OneEach)
        {
            foreach (var ability in background.AbilityScores)
            {
                scores[ability] += 1;
            }
        }
        else
        {
            var primary = draft.PrimaryIncrease
                ?? throw new ArgumentException("A +2/+1 background increase needs a primary ability.", nameof(draft));
            var secondary = draft.SecondaryIncrease
                ?? throw new ArgumentException("A +2/+1 background increase needs a secondary ability.", nameof(draft));

            if (primary == secondary)
            {
                throw new ArgumentException("The +2 and +1 increases must go to different abilities.", nameof(draft));
            }

            foreach (var ability in new[] { primary, secondary })
            {
                if (!background.AbilityScores.Contains(ability))
                {
                    throw new ArgumentException(
                        $"{background.Name} does not offer an increase to {ability}.",
                        nameof(draft));
                }
            }

            scores[primary] += 2;
            scores[secondary] += 1;
        }

        // "None of these increases can raise a score above 20."
        foreach (var ability in scores.Keys.ToList())
        {
            scores[ability] = Math.Min(20, scores[ability]);
        }

        return scores;
    }

    /// <summary>
    /// Hit points: the hit die's maximum at level 1, then its fixed average per level,
    /// plus the Constitution modifier at every level.
    /// </summary>
    private static int ResolveHitPoints(
        ClassDefinition definition,
        int level,
        int constitutionModifier,
        HitPointMethod method,
        IRandomSource? random)
    {
        var die = definition.HitDieSides;

        // Level 1 is always the die's maximum, never rolled.
        var total = die + constitutionModifier;

        // The SRD's fixed value is the die's average rounded up: 6 for a d10.
        var fixedValue = (die / 2) + 1;

        for (var current = 2; current <= level; current++)
        {
            var gained = method == HitPointMethod.Rolled ? random!.Roll(die) : fixedValue;

            // A character always gains at least 1 hit point per level, however punishing
            // their Constitution.
            total += Math.Max(1, gained + constitutionModifier);
        }

        return total;
    }

    /// <summary>
    /// Works out Armor Class from what the character is wearing, or from a feature that
    /// replaces armour.
    /// </summary>
    /// <summary>
    /// Fast Movement: Speed +10 feet "while you aren't wearing Heavy armor". The armour
    /// gate is the printed one; nothing else this engine models changes a character's
    /// Speed, so the species number plus this bonus is the whole derivation.
    /// </summary>
    private static int ResolveSpeed(
        CharacterDraft draft,
        CharacterBuildContent content,
        IReadOnlyList<GrantedFeature> features)
    {
        var speed = content.Species.SpeedFeet;

        var wearsHeavyArmor = draft.ArmorId is { } armorId
            && content.Armor.TryGetValue(armorId, out var armor)
            && armor.Category == ArmorCategory.Heavy;

        if (features.Any(granted => granted.Feature == ClassFeature.FastMovement) && !wearsHeavyArmor)
        {
            speed += 10;
        }

        return speed;
    }

    private static (int ArmorClass, string Source) ResolveArmorClass(
        CharacterDraft draft,
        CharacterBuildContent content,
        IReadOnlyDictionary<Ability, int> scores,
        IReadOnlyList<GrantedFeature> features)
    {
        var dexterity = AbilityRules.ModifierFor(scores[Ability.Dexterity]);
        var shield = draft.HasShield ? ShieldBonus(content) : 0;
        var shieldNote = shield > 0 ? $" + {shield} (Shield)" : string.Empty;

        if (draft.ArmorId is { } armorId)
        {
            if (!content.Armor.TryGetValue(armorId, out var armor))
            {
                throw new ArgumentException($"Unknown armour '{armorId}'.", nameof(draft));
            }

            var dexterityPart = armor.AddsDexterityModifier
                ? armor.MaximumDexterityModifier is { } cap ? Math.Min(cap, dexterity) : dexterity
                : 0;

            return (
                armor.BaseArmorClass + dexterityPart + shield,
                $"{armor.Name} {armor.BaseArmorClass}" +
                (armor.AddsDexterityModifier ? $" + {dexterityPart} (Dex)" : string.Empty) +
                shieldNote);
        }

        // Barbarian Unarmored Defense: 10 + Dexterity + Constitution, and a Shield still
        // applies. Only used when no armour is worn, which is the condition the SRD sets.
        if (features.Any(granted => granted.Feature == ClassFeature.UnarmoredDefenseBarbarian))
        {
            var constitution = AbilityRules.ModifierFor(scores[Ability.Constitution]);

            return (
                10 + dexterity + constitution + shield,
                $"Unarmored Defense 10 + {dexterity} (Dex) + {constitution} (Con){shieldNote}");
        }

        return (10 + dexterity + shield, $"Unarmoured 10 + {dexterity} (Dex){shieldNote}");
    }

    private static int ShieldBonus(CharacterBuildContent content) =>
        content.Armor.Values
            .Where(armor => armor.Category == ArmorCategory.Shield)
            .Select(armor => armor.BaseArmorClass)
            .DefaultIfEmpty(2)
            .Max();

    private static Dictionary<Ability, int> ResolveSavingThrows(
        ClassDefinition definition,
        IReadOnlyDictionary<Ability, int> scores,
        int proficiency) =>
        Enum.GetValues<Ability>().ToDictionary(
            ability => ability,
            ability => AbilityRules.ModifierFor(scores[ability])
                + (definition.SavingThrowProficiencies.Contains(ability) ? proficiency : 0));

    private static IReadOnlyList<SkillBonus> ResolveSkills(
        CharacterDraft draft,
        BackgroundDefinition background,
        IReadOnlyDictionary<Ability, int> scores,
        int proficiency)
    {
        // Proficiency comes from two places and does not stack with itself.
        var proficient = new HashSet<string>(draft.ChosenSkills, StringComparer.OrdinalIgnoreCase);
        proficient.UnionWith(background.SkillProficiencies);

        return SkillRules.AllSkills
            .Select(skill =>
            {
                var ability = SkillRules.AbilityFor(skill);
                var isProficient = proficient.Contains(skill);

                return new SkillBonus(
                    skill,
                    ability,
                    AbilityRules.ModifierFor(scores[ability]) + (isProficient ? proficiency : 0),
                    isProficient);
            })
            .ToArray();
    }

    /// <summary>
    /// Turns carried weapons into attacks.
    /// </summary>
    /// <remarks>
    /// Proficiency is assumed: every class in this game is proficient with the weapons
    /// its own starting equipment gives it, and modelling the exceptions would need the
    /// weapon-proficiency line parsed into categories rather than kept as printed text.
    /// Recorded here rather than left implicit.
    /// </remarks>
    private static IReadOnlyList<CombatAttack> ResolveAttacks(
        CharacterDraft draft,
        CharacterBuildContent content,
        IReadOnlyDictionary<Ability, int> scores,
        int proficiency)
    {
        var strength = AbilityRules.ModifierFor(scores[Ability.Strength]);
        var dexterity = AbilityRules.ModifierFor(scores[Ability.Dexterity]);

        var attacks = new List<CombatAttack>();

        foreach (var weaponId in draft.WeaponIds)
        {
            if (!content.Weapons.TryGetValue(weaponId, out var weapon))
            {
                throw new ArgumentException($"Unknown weapon '{weaponId}'.", nameof(draft));
            }

            // Finesse lets the wielder choose; a ranged weapon uses Dexterity; everything
            // else uses Strength.
            var ability = weapon.Properties.HasFlag(WeaponProperty.Finesse)
                ? Math.Max(strength, dexterity)
                : weapon.Kind == WeaponKind.Ranged
                    ? dexterity
                    : strength;

            var damage = new AttackDamage(
                weapon.Damage with { Modifier = weapon.Damage.Modifier + ability },
                weapon.DamageType,
                (weapon.Damage with { Modifier = weapon.Damage.Modifier + ability }).Average);

            attacks.Add(new CombatAttack(
                weapon.Name,
                weapon.Kind == WeaponKind.Ranged ? AttackKind.Ranged : AttackKind.Melee,
                ability + proficiency,
                weapon.Kind == WeaponKind.Melee
                    ? weapon.Properties.HasFlag(WeaponProperty.Reach) ? 10 : 5
                    : null,
                weapon.Range?.NormalFeet,
                weapon.Range?.LongFeet,
                [damage]));
        }

        return attacks;
    }

    /// <summary>Every implemented feature the class grants at or below this level.</summary>
    private static IReadOnlyList<GrantedFeature> ResolveFeatures(ClassDefinition definition, int level) =>
        definition.Levels
            .Where(row => row.Level <= level)
            .SelectMany(row => row.FeatureNames.Select(name => (row.Level, Feature: ClassFeatureRegistry.Resolve(name))))
            .Where(pair => pair.Feature is not null)
            .GroupBy(pair => pair.Feature!.Value)
            .Select(group => new GrantedFeature(group.Key, group.Min(pair => pair.Level)))
            .OrderBy(granted => granted.Level)
            .ThenBy(granted => granted.Feature)
            .ToArray();

    /// <summary>
    /// Printed features the class grants that this engine does not implement. The gap,
    /// stated on the sheet rather than left invisible.
    /// </summary>
    private static IReadOnlyList<string> ResolveUnimplementedFeatures(ClassDefinition definition, int level) =>
        definition.Levels
            .Where(row => row.Level <= level)
            .SelectMany(row => row.FeatureNames)
            .Where(name => ClassFeatureRegistry.Resolve(name) is null)
            // Subclass placeholders are not features in their own right.
            .Where(name => !name.Contains("Subclass", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
