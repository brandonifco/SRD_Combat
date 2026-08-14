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
/// <param name="MagicItems">Magic item definitions, by id. Null when none are in play.</param>
public sealed record CharacterBuildContent(
    SpeciesDefinition Species,
    ClassDefinition Class,
    BackgroundDefinition Background,
    IReadOnlyDictionary<string, WeaponDefinition> Weapons,
    IReadOnlyDictionary<string, ArmorDefinition> Armor,
    IReadOnlyDictionary<string, MagicItemDefinition>? MagicItems = null);

/// <summary>An equipped item resolved against content and registry: what it is and what it does.</summary>
internal sealed record ResolvedMagicItem(
    EquippedMagicItem Equipped,
    MagicItemDefinition Definition,
    MagicItemPowers Powers);

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

        var magicItems = ResolveMagicItems(draft, content);
        var scores = ApplyBackgroundIncreases(draft, content.Background);

        // Order matters and is the printed one: the background's increases make the
        // starting character, the feat improves that character, and worn gear is worn
        // last — an item that sets a score to 19 is a floor under whatever the rules
        // already produced, not a replacement for them.
        var improvementsTaken = ApplyAbilityScoreImprovements(draft, content.Class, scores);

        ApplyAbilitySettingItems(scores, magicItems);

        var proficiency = AdvancementRules.ProficiencyBonusForLevel(draft.Level);
        var levelRow = content.Class.AtLevel(draft.Level)
            ?? throw new InvalidOperationException($"{content.Class.Name} has no level {draft.Level} row.");

        var features = ResolveFeatures(content.Class, draft.Level);
        var constitution = AbilityRules.ModifierFor(scores[Ability.Constitution]);

        var expertise = ResolveExpertise(draft, content.Background, features);
        var fightingStyle = ResolveFightingStyle(draft, features);
        var divineOrder = ResolveDivineOrder(draft, features);

        var (armorClass, armorSource) = ResolveArmorClass(draft, content, scores, features, fightingStyle, magicItems);

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
            SavingThrows = ResolveSavingThrows(content.Class, scores, proficiency, magicItems),
            Skills = ResolveSkills(draft, content.Background, scores, proficiency, expertise, divineOrder),
            Attacks = ResolveAttacks(
                draft,
                content,
                scores,
                proficiency,
                fightingStyle,
                magicItems,
                ResolveWeaponMasteries(draft, content, features)),
            Features = features,
            FightingStyle = fightingStyle,
            DivineOrder = divineOrder,
            ExpertiseSkills = expertise,
            SpellSlots = levelRow.SpellSlots,
            UnimplementedFeatures = ResolveUnimplementedFeatures(content.Class, draft.Level, divineOrder),
            UnspentFeatChoices = GrantsOf(content.Class, draft.Level, ClassFeature.AbilityScoreImprovement)
                - improvementsTaken,
            MagicItemNames = magicItems.Select(item => ItemDisplayName(item)).ToArray(),
            SpellAttackItemBonus = magicItems.Sum(item => item.Powers.SpellAttackBonus),
            IgnoresHalfCoverOnSpellAttacks = magicItems.Any(item => item.Powers.IgnoresHalfCoverOnSpellAttacks),
            CriticalHitsAgainstBecomeNormal = magicItems.Any(item => item.Powers.CriticalHitsAgainstBecomeNormal),
        };
    }

    /// <summary>"Ring of Protection", or "Weapon, +1, +2, or +3 (+2, Longsword)".</summary>
    private static string ItemDisplayName(ResolvedMagicItem item)
    {
        var qualifiers = new[] { item.Equipped.Variant, item.Equipped.BoundWeaponId }
            .Where(part => part is not null)
            .ToArray();

        return qualifiers.Length == 0
            ? item.Definition.Name
            : $"{item.Definition.Name} ({string.Join(", ", qualifiers)})";
    }

    /// <summary>
    /// Validates the draft's equipped magic items against the content, the registry and
    /// the printed attunement rules, refusing anything the engine would silently fail to
    /// honour.
    /// </summary>
    /// <remarks>
    /// The same stance the resolver takes everywhere: an item the registry does not
    /// execute is refused by name rather than equipped as decoration, because a worn
    /// item doing nothing is an unimplemented rule holding silently. Attunement is
    /// checked against print — no more than three attuned items, no more than one copy
    /// of the same item — and attuning itself is read as happening at the rest between
    /// fights, which every rung of the gauntlet provides.
    /// </remarks>
    private static IReadOnlyList<ResolvedMagicItem> ResolveMagicItems(
        CharacterDraft draft,
        CharacterBuildContent content)
    {
        if (draft.MagicItems.Count == 0)
        {
            return [];
        }

        if (content.MagicItems is null)
        {
            throw new ArgumentException(
                "The draft equips magic items but the build content carries none.",
                nameof(draft));
        }

        var resolved = new List<ResolvedMagicItem>();

        foreach (var equipped in draft.MagicItems)
        {
            if (!content.MagicItems.TryGetValue(equipped.ItemId, out var definition))
            {
                throw new ArgumentException($"Unknown magic item '{equipped.ItemId}'.", nameof(draft));
            }

            if (equipped.Variant is { } variant
                && !definition.Variants.Any(candidate => candidate.Suffix == variant))
            {
                throw new ArgumentException(
                    $"{definition.Name} has no '{variant}' variant.",
                    nameof(draft));
            }

            if (definition.Variants.Count > 0 && equipped.Variant is null)
            {
                throw new ArgumentException(
                    $"{definition.Name} needs a variant — which tier was found?",
                    nameof(draft));
            }

            var powers = MagicItemRegistry.PowersFor(definition.Name, equipped.Variant)
                ?? throw new ArgumentException(
                    $"The engine does not execute '{definition.Name}'; equipping it would be decoration.",
                    nameof(draft));

            ValidatePlacement(draft, content, definition, powers, equipped);

            resolved.Add(new ResolvedMagicItem(equipped, definition, powers));
        }

        if (resolved.GroupBy(item => item.Definition.Id).Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "\"You can't attune to more than one copy of an item\" — and duplicates of an unattuned item stack nothing here.",
                nameof(draft));
        }

        var attuned = resolved.Count(item => item.Definition.RequiresAttunement);

        if (attuned > MagicItemRegistry.AttunementLimit)
        {
            throw new ArgumentException(
                $"\"You can be attuned to no more than three magic items at a time\" — this draft attunes {attuned}.",
                nameof(draft));
        }

        return resolved;
    }

    private static void ValidatePlacement(
        CharacterDraft draft,
        CharacterBuildContent content,
        MagicItemDefinition definition,
        MagicItemPowers powers,
        EquippedMagicItem equipped)
    {
        if (powers.AppliesToWeapon)
        {
            if (equipped.BoundWeaponId is null)
            {
                throw new ArgumentException(
                    $"{definition.Name} enchants a weapon and must be bound to one.",
                    nameof(draft));
            }

            if (!draft.WeaponIds.Contains(equipped.BoundWeaponId, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"{definition.Name} is bound to '{equipped.BoundWeaponId}', which this character does not carry.",
                    nameof(draft));
            }
        }

        if (powers.AppliesToArmor)
        {
            if (draft.ArmorId is not { } armorId)
            {
                throw new ArgumentException(
                    $"{definition.Name} is armour, and this character wears none.",
                    nameof(draft));
            }

            var armor = content.Armor[armorId];

            if (powers.AllowedArmorCategories.Count > 0
                && !powers.AllowedArmorCategories.Contains(armor.Category))
            {
                throw new ArgumentException(
                    $"{definition.Name} is printed as \"({definition.AppliesTo})\", not {armor.Name}.",
                    nameof(draft));
            }

            if (powers.AllowedArmorIds.Count > 0
                && !powers.AllowedArmorIds.Contains(armorId, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"{definition.Name} is printed as \"({definition.AppliesTo})\", not {armor.Name}.",
                    nameof(draft));
            }

            if (powers.ExcludedArmorIds.Contains(armorId, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"{definition.Name} is printed as \"({definition.AppliesTo})\", not {armor.Name}.",
                    nameof(draft));
            }
        }

        if (powers.AppliesToShield && !draft.HasShield)
        {
            throw new ArgumentException(
                $"{definition.Name} is a Shield, and this character carries none.",
                nameof(draft));
        }

        if (powers.RequiresSpellcaster && SpellcastingRules.AbilityFor(draft.ClassId) is null)
        {
            throw new ArgumentException(
                $"{definition.Name} requires attunement by a Spellcaster, and a {content.Class.Name} is not one.",
                nameof(draft));
        }
    }

    /// <summary>
    /// Applies the Ability Score Improvements this level has earned, and reports how
    /// many were applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Increase one ability score of your choice by 2, or increase two ability scores
    /// of your choice by 1. This feat can't increase an ability score above 20."
    /// </para>
    /// <para>
    /// <b>How many the character is entitled to is counted from the class table</b>, not
    /// from the resolved feature list: <see cref="ResolveFeatures"/> collapses repeats of
    /// the same feature to one entry, and this is the feature the SRD grants most often —
    /// four times for most classes, six for a Fighter. Counting the printed rows is the
    /// only reading that stays right above level 5.
    /// </para>
    /// <para>
    /// A draft carrying more choices than the level has earned takes the first N in
    /// order rather than being refused: the same draft describes the character at every
    /// level, which is what makes levelling a re-resolve. Only a malformed choice — the
    /// same ability named twice, which would be a +2 wearing the +1/+1 shape — is
    /// refused.
    /// </para>
    /// </remarks>
    private static int ApplyAbilityScoreImprovements(
        CharacterDraft draft,
        ClassDefinition definition,
        Dictionary<Ability, int> scores)
    {
        if (draft.AbilityScoreImprovements.Count == 0)
        {
            return 0;
        }

        var allowance = GrantsOf(definition, draft.Level, ClassFeature.AbilityScoreImprovement);
        var taken = 0;

        foreach (var improvement in draft.AbilityScoreImprovements.Take(allowance))
        {
            if (improvement.Second == improvement.First)
            {
                throw new ArgumentException(
                    "An Ability Score Improvement raising two scores must name two different abilities.",
                    nameof(draft));
            }

            foreach (var (ability, increase) in improvement.Second is { } second
                         ? new[] { (improvement.First, 1), (second, 1) }
                         : [(improvement.First, 2)])
            {
                scores[ability] = Math.Min(20, scores[ability] + increase);
            }

            taken++;
        }

        return taken;
    }

    /// <summary>
    /// The kinds of weapon whose mastery this character has unlocked, refusing anything
    /// they were not granted or the engine does not execute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>How many is the class table's number, not the feature prose's.</b> Fighter,
    /// Barbarian and Rogue print a Weapon Mastery column that grows with level — a
    /// Fighter has three at level 1 and four at level 4 — so the column is read when it
    /// exists and the printed prose count is the fallback for classes without one.
    /// </para>
    /// <para>
    /// A weapon whose mastery property the engine does not execute is <b>refused by
    /// name</b>. Unlocking Cleave today would be a feature that silently does nothing,
    /// which is the one outcome this project refuses everywhere else.
    /// </para>
    /// </remarks>
    private static IReadOnlyDictionary<string, WeaponMastery> ResolveWeaponMasteries(
        CharacterDraft draft,
        CharacterBuildContent content,
        IReadOnlyList<GrantedFeature> features)
    {
        if (draft.WeaponMasteryIds.Count == 0)
        {
            return new Dictionary<string, WeaponMastery>(StringComparer.Ordinal);
        }

        if (!features.Any(granted => granted.Feature == ClassFeature.WeaponMastery))
        {
            throw new ArgumentException(
                "This character has no feature granting Weapon Mastery.",
                nameof(draft));
        }

        var allowance = WeaponMasteryAllowance(content.Class, draft.Level);

        if (draft.WeaponMasteryIds.Count > allowance)
        {
            throw new ArgumentException(
                $"This character may master {allowance} kinds of weapon, not {draft.WeaponMasteryIds.Count}.",
                nameof(draft));
        }

        if (draft.WeaponMasteryIds.Distinct(StringComparer.Ordinal).Count() != draft.WeaponMasteryIds.Count)
        {
            throw new ArgumentException("The same kind of weapon cannot be mastered twice.", nameof(draft));
        }

        var mastered = new Dictionary<string, WeaponMastery>(StringComparer.Ordinal);

        foreach (var weaponId in draft.WeaponMasteryIds)
        {
            if (!content.Weapons.TryGetValue(weaponId, out var weapon))
            {
                throw new ArgumentException($"Unknown weapon '{weaponId}'.", nameof(draft));
            }

            var mastery = weapon.Mastery;

            if (!WeaponMasteryRules.Executes(mastery))
            {
                throw new ArgumentException(
                    $"The engine does not execute {weapon.Name}'s {mastery} mastery; " +
                    "unlocking it would be a feature that does nothing.",
                    nameof(draft));
            }

            mastered[weaponId] = mastery;
        }

        return mastered;
    }

    /// <summary>
    /// How many kinds of weapon this class has mastery of at this level — the level
    /// table's column where one is printed, and the feature's own count otherwise.
    /// </summary>
    private static int WeaponMasteryAllowance(ClassDefinition definition, int level)
    {
        var row = definition.AtLevel(level);

        if (row?.ResourceCount("Weapon Mastery") is { } printed)
        {
            return printed;
        }

        // The Rogue, Ranger and Paladin print the count in the feature's prose rather
        // than in a column, and all three say two.
        return 2;
    }

    /// <summary>
    /// How many times the class table grants a feature at or below a level, counting
    /// every printed row rather than the collapsed feature list.
    /// </summary>
    private static int GrantsOf(ClassDefinition definition, int level, ClassFeature feature) =>
        definition.Levels
            .Where(row => row.Level <= level)
            .SelectMany(row => row.FeatureNames)
            .Count(name => ClassFeatureRegistry.Resolve(name) == feature);

    /// <summary>
    /// "Your Strength is 19 while you wear these gauntlets. They have no effect on you
    /// if your Strength is 19 or higher without them." — a floor, not an increase, and
    /// applied after the background's increases because the printed sentence describes
    /// the worn state of a finished character.
    /// </summary>
    private static void ApplyAbilitySettingItems(
        Dictionary<Ability, int> scores,
        IReadOnlyList<ResolvedMagicItem> magicItems)
    {
        foreach (var item in magicItems)
        {
            if (item.Powers.SetsAbility is { } ability)
            {
                scores[ability] = Math.Max(scores[ability], item.Powers.SetsAbilityTo);
            }
        }
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
        IReadOnlyList<GrantedFeature> features,
        FightingStyle fightingStyle,
        IReadOnlyList<ResolvedMagicItem> magicItems)
    {
        var dexterity = AbilityRules.ModifierFor(scores[Ability.Dexterity]);
        var shield = draft.HasShield ? ShieldBonus(content) : 0;
        var shieldNote = shield > 0 ? $" + {shield} (Shield)" : string.Empty;

        // Always-on item bonuses: +N armour and Shields, the Ring and Cloak of
        // Protection. The Bracers of Defense are the conditional one — "wearing no
        // armor and using no Shield", both read off the draft.
        var itemBonus = magicItems.Sum(item => item.Powers.ArmorClassBonus);

        if (draft.ArmorId is null && !draft.HasShield)
        {
            itemBonus += magicItems.Sum(item => item.Powers.UnarmoredOnlyArmorClassBonus);
        }

        var itemNote = itemBonus > 0 ? $" + {itemBonus} (magic items)" : string.Empty;

        if (draft.ArmorId is { } armorId)
        {
            if (!content.Armor.TryGetValue(armorId, out var armor))
            {
                throw new ArgumentException($"Unknown armour '{armorId}'.", nameof(draft));
            }

            var dexterityPart = armor.AddsDexterityModifier
                ? armor.MaximumDexterityModifier is { } cap ? Math.Min(cap, dexterity) : dexterity
                : 0;

            // Defense: "While you're wearing Light, Medium, or Heavy armor, you gain a
            // +1 bonus to Armor Class." A Shield is none of those three, so a shield
            // alone does not turn it on — which is why the category is tested rather
            // than "is anything worn".
            var defense = fightingStyle == FightingStyle.Defense
                && armor.Category is ArmorCategory.Light or ArmorCategory.Medium or ArmorCategory.Heavy
                    ? 1
                    : 0;

            return (
                armor.BaseArmorClass + dexterityPart + shield + defense + itemBonus,
                $"{armor.Name} {armor.BaseArmorClass}" +
                (armor.AddsDexterityModifier ? $" + {dexterityPart} (Dex)" : string.Empty) +
                shieldNote +
                (defense > 0 ? " + 1 (Defense)" : string.Empty) +
                itemNote);
        }

        // Barbarian Unarmored Defense: 10 + Dexterity + Constitution, and a Shield still
        // applies. Only used when no armour is worn, which is the condition the SRD sets.
        if (features.Any(granted => granted.Feature == ClassFeature.UnarmoredDefenseBarbarian))
        {
            var constitution = AbilityRules.ModifierFor(scores[Ability.Constitution]);

            return (
                10 + dexterity + constitution + shield + itemBonus,
                $"Unarmored Defense 10 + {dexterity} (Dex) + {constitution} (Con){shieldNote}{itemNote}");
        }

        return (10 + dexterity + shield + itemBonus, $"Unarmoured 10 + {dexterity} (Dex){shieldNote}{itemNote}");
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
        int proficiency,
        IReadOnlyList<ResolvedMagicItem> magicItems)
    {
        // "+1 bonus to Armor Class and saving throws" — the Ring and Cloak of
        // Protection print no ability list, so the bonus is on all six.
        var itemBonus = magicItems.Sum(item => item.Powers.SavingThrowBonus);

        return Enum.GetValues<Ability>().ToDictionary(
            ability => ability,
            ability => AbilityRules.ModifierFor(scores[ability])
                + (definition.SavingThrowProficiencies.Contains(ability) ? proficiency : 0)
                + itemBonus);
    }

    private static IReadOnlyList<SkillBonus> ResolveSkills(
        CharacterDraft draft,
        BackgroundDefinition background,
        IReadOnlyDictionary<Ability, int> scores,
        int proficiency,
        IReadOnlyList<string> expertise,
        DivineOrder divineOrder)
    {
        var proficient = ProficientSkills(draft, background);
        var expert = new HashSet<string>(expertise, StringComparer.OrdinalIgnoreCase);

        // Thaumaturge: "a bonus to your Intelligence (Arcana or Religion) checks. The
        // bonus equals your Wisdom modifier (minimum of +1)." Read as a bonus both
        // skills carry whenever their check is rolled — the parenthesis names the two
        // checks the bonus rides, not a pick between them.
        var thaumaturgy = divineOrder == DivineOrder.Thaumaturge
            ? Math.Max(1, AbilityRules.ModifierFor(scores[Ability.Wisdom]))
            : 0;

        return SkillRules.AllSkills
            .Select(skill =>
            {
                var ability = SkillRules.AbilityFor(skill);
                var isProficient = proficient.Contains(skill);

                // Expertise doubles the proficiency bonus rather than adding a second
                // one, and it only ever applies where proficiency already does — which
                // ResolveExpertise has already refused a draft for getting wrong.
                var multiplier = expert.Contains(skill) ? 2 : 1;

                var divine = skill is "Arcana" or "Religion" ? thaumaturgy : 0;

                return new SkillBonus(
                    skill,
                    ability,
                    AbilityRules.ModifierFor(scores[ability]) + (isProficient ? proficiency * multiplier : 0) + divine,
                    isProficient);
            })
            .ToArray();
    }

    /// <summary>Every skill the character is proficient in, from either source.</summary>
    /// <remarks>Proficiency comes from two places and does not stack with itself.</remarks>
    private static HashSet<string> ProficientSkills(CharacterDraft draft, BackgroundDefinition background)
    {
        var proficient = new HashSet<string>(draft.ChosenSkills, StringComparer.OrdinalIgnoreCase);
        proficient.UnionWith(background.SkillProficiencies);

        return proficient;
    }

    /// <summary>
    /// Validates the draft's Expertise picks against what the class actually grants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The allowance is counted from the granted features rather than from the class
    /// name, so the Rogue's Expertise and the Ranger's Deft Explorer — the same rule
    /// under two printed names — need no special case. Both map to
    /// <see cref="ClassFeature.Expertise"/>, and each grant is worth what the SRD prints
    /// for it: the Rogue's level 1 Expertise gives two skills, Deft Explorer gives one.
    /// </para>
    /// <para>
    /// Level 6's second pair of Rogue picks is deliberately not modelled: this game stops
    /// at level 5, and an allowance for a level no character reaches would be untested
    /// rules. Refusing what the character has not earned is the point — a draft asking
    /// for Expertise it was never granted is a mistake, not a preference.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> ResolveExpertise(
        CharacterDraft draft,
        BackgroundDefinition background,
        IReadOnlyList<GrantedFeature> features)
    {
        if (draft.ExpertiseSkills.Count == 0)
        {
            return [];
        }

        var allowance = features
            .Where(granted => granted.Feature == ClassFeature.Expertise)
            .Sum(granted => granted.Level == 1 ? 2 : 1);

        if (allowance == 0)
        {
            throw new ArgumentException(
                "This character has no feature granting Expertise.",
                nameof(draft));
        }

        if (draft.ExpertiseSkills.Count > allowance)
        {
            throw new ArgumentException(
                $"This character may take Expertise in {allowance} skill(s), not {draft.ExpertiseSkills.Count}.",
                nameof(draft));
        }

        if (draft.ExpertiseSkills.Distinct(StringComparer.OrdinalIgnoreCase).Count() != draft.ExpertiseSkills.Count)
        {
            throw new ArgumentException("Expertise cannot be taken twice in the same skill.", nameof(draft));
        }

        // "You gain Expertise in two of your skill proficiencies" — it doubles a
        // proficiency the character has, so it cannot be spent on one they lack.
        var proficient = ProficientSkills(draft, background);

        foreach (var skill in draft.ExpertiseSkills)
        {
            if (!SkillRules.AllSkills.Contains(skill, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unknown skill '{skill}'.", nameof(draft));
            }

            if (!proficient.Contains(skill))
            {
                throw new ArgumentException(
                    $"Expertise needs proficiency in {skill} first.",
                    nameof(draft));
            }
        }

        return draft.ExpertiseSkills;
    }

    /// <summary>
    /// The Fighting Style the character actually has, refusing one they were never
    /// granted.
    /// </summary>
    private static FightingStyle ResolveFightingStyle(
        CharacterDraft draft,
        IReadOnlyList<GrantedFeature> features)
    {
        if (draft.FightingStyle == FightingStyle.Unspecified)
        {
            return FightingStyle.Unspecified;
        }

        if (!features.Any(granted => granted.Feature == ClassFeature.FightingStyle))
        {
            throw new ArgumentException(
                "This character has no feature granting a Fighting Style.",
                nameof(draft));
        }

        return draft.FightingStyle;
    }

    /// <summary>
    /// The Divine Order the character actually has, refusing one they were never
    /// granted — the Fighting Style rule with a different feature behind it.
    /// </summary>
    private static DivineOrder ResolveDivineOrder(
        CharacterDraft draft,
        IReadOnlyList<GrantedFeature> features)
    {
        if (draft.DivineOrder == DivineOrder.Unspecified)
        {
            return DivineOrder.Unspecified;
        }

        if (!features.Any(granted => granted.Feature == ClassFeature.DivineOrder))
        {
            throw new ArgumentException(
                "This character has no feature granting a Divine Order.",
                nameof(draft));
        }

        return draft.DivineOrder;
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
        int proficiency,
        FightingStyle fightingStyle,
        IReadOnlyList<ResolvedMagicItem> magicItems,
        IReadOnlyDictionary<string, WeaponMastery> masteries)
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

            // "Two-Handed: This weapon requires two hands when you attack with it,"
            // and a donned shield is strapped to one of them. The model tracks no
            // hands, so the rule lives here as a draft refusal: a build that could
            // never legally swing its own weapon must not resolve. The Lance's
            // printed "(unless mounted)" qualifier changes nothing in a game with no
            // mounts. This gap held silently until the shop sold Brenna a Maul to
            // carry beside her shield — nothing before the merchant ever handed a
            // shield-bearer a two-hander.
            if (draft.HasShield && weapon.Properties.HasFlag(WeaponProperty.TwoHanded))
            {
                throw new ArgumentException(
                    $"{weapon.Name} is Two-Handed and the draft carries a shield; " +
                    "there are not enough hands to attack with it.",
                    nameof(draft));
            }

            // Finesse lets the wielder choose; a ranged weapon uses Dexterity; everything
            // else uses Strength.
            var ability = weapon.Properties.HasFlag(WeaponProperty.Finesse)
                ? Math.Max(strength, dexterity)
                : weapon.Kind == WeaponKind.Ranged
                    ? dexterity
                    : strength;

            // The enchantments bound to this weapon: "+N to attack rolls and damage
            // rolls made with this magic weapon", and the Vicious Weapon's extra dice
            // "of the same type as the weapon's normal damage".
            var bound = magicItems.Where(item => item.Equipped.BoundWeaponId == weaponId).ToArray();
            var attackBonus = bound.Sum(item => item.Powers.AttackRollBonus);
            var damageBonus = bound.Sum(item => item.Powers.DamageRollBonus);

            var rolled = weapon.Damage with { Modifier = weapon.Damage.Modifier + ability + damageBonus };
            var damageComponents = new List<AttackDamage> { new(rolled, weapon.DamageType, rolled.Average) };

            foreach (var extra in bound.Select(item => item.Powers.ExtraWeaponDamageDice).OfType<DiceExpression>())
            {
                damageComponents.Add(new AttackDamage(extra, weapon.DamageType, extra.Average));
            }

            // Archery: "+2 bonus to attack rolls you make with Ranged weapons." The
            // weapon's kind decides it, not the attack's range band — a thrown Dagger is
            // a Melee weapon with a range, and the style does not touch it.
            var archery = fightingStyle == FightingStyle.Archery && weapon.Kind == WeaponKind.Ranged ? 2 : 0;

            attacks.Add(new CombatAttack(
                weapon.Name,
                weapon.Kind == WeaponKind.Ranged ? AttackKind.Ranged : AttackKind.Melee,
                ability + proficiency + archery + attackBonus,
                weapon.Kind == WeaponKind.Melee
                    ? weapon.Properties.HasFlag(WeaponProperty.Reach) ? 10 : 5
                    : null,
                weapon.Range?.NormalFeet,
                weapon.Range?.LongFeet,
                damageComponents)
            {
                // The property is "usable only by a character who has a feature ... that
                // unlocks the property", so an unmastered weapon carries None and the
                // engine never sees it.
                Mastery = masteries.TryGetValue(weaponId, out var mastered) ? mastered : null,
                AbilityModifier = ability,
            });
        }

        return attacks;
    }

    /// <summary>Every implemented feature the class grants at or below this level.</summary>
    /// <summary>Every implemented feature this character has at this level, subclass included.</summary>
    /// <remarks>
    /// <b>The subclass needs no choice on the draft</b>: the SRD prints exactly one per
    /// class — the Champion for the Fighter, the Berserker for the Barbarian — so a
    /// character of level 3 or higher simply has it, and its features arrive at the
    /// printed level each one's own heading carries. If a source with a second subclass
    /// ever exists, this is where the choice would go.
    /// </remarks>
    private static IReadOnlyList<GrantedFeature> ResolveFeatures(ClassDefinition definition, int level)
    {
        var fromTable = definition.Levels
            .Where(row => row.Level <= level)
            .SelectMany(row => row.FeatureNames.Select(name => (row.Level, Feature: ClassFeatureRegistry.Resolve(name))));

        var fromSubclass = definition.SubclassFeatures
            .Where(feature => feature.GrantedAtLevel is { } granted && granted <= level)
            .Select(feature => (Level: feature.GrantedAtLevel!.Value, Feature: ClassFeatureRegistry.Resolve(feature.Name)));

        return fromTable
            .Concat(fromSubclass)
            .Where(pair => pair.Feature is not null)
            .GroupBy(pair => pair.Feature!.Value)
            .Select(group => new GrantedFeature(group.Key, group.Min(pair => pair.Level)))
            .OrderBy(granted => granted.Level)
            .ThenBy(granted => granted.Feature)
            .ToArray();
    }

    /// <summary>
    /// Printed features the class grants that this engine does not implement. The gap,
    /// stated on the sheet rather than left invisible.
    /// </summary>
    private static IReadOnlyList<string> ResolveUnimplementedFeatures(
        ClassDefinition definition,
        int level,
        DivineOrder divineOrder) =>
        definition.Levels
            .Where(row => row.Level <= level)
            .SelectMany(row => row.FeatureNames)
            .Concat(definition.SubclassFeatures
                .Where(feature => feature.GrantedAtLevel is { } granted && granted <= level)
                .Select(feature => feature.Name))
            .Where(name => ClassFeatureRegistry.Resolve(name) is null
                // A registered name whose choice was never made executes nothing, and a
                // gap nothing executes must stay visible: Divine Order rejoins the
                // report while the draft holds Unspecified.
                || (ClassFeatureRegistry.Resolve(name) == ClassFeature.DivineOrder
                    && divineOrder == DivineOrder.Unspecified))
            // Subclass placeholders are not features in their own right.
            .Where(name => !name.Contains("Subclass", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
