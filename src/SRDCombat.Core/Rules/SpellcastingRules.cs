using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>
/// The arithmetic of casting: which ability a class casts with, and the two numbers
/// derived from it.
/// </summary>
/// <remarks>
/// The class→ability map is curated rather than extracted. The SRD states it in each
/// class's Spellcasting feature prose ("Wisdom is your spellcasting ability"), and the
/// Core Traits table's Primary Ability only coincides with it for full casters — a
/// Paladin's primary abilities are Strength <em>and</em> Charisma, and it casts on
/// Charisma. Reading it from Primary Ability would be right for six classes and quietly
/// wrong for two.
/// </remarks>
public static class SpellcastingRules
{
    private static readonly IReadOnlyDictionary<string, Ability> AbilityByClassId =
        new Dictionary<string, Ability>(StringComparer.OrdinalIgnoreCase)
        {
            ["class.bard"] = Ability.Charisma,
            ["class.cleric"] = Ability.Wisdom,
            ["class.druid"] = Ability.Wisdom,
            ["class.paladin"] = Ability.Charisma,
            ["class.ranger"] = Ability.Wisdom,
            ["class.sorcerer"] = Ability.Charisma,
            ["class.warlock"] = Ability.Charisma,
            ["class.wizard"] = Ability.Intelligence,
        };

    /// <summary>The ability a class casts with, or null when the class does not cast.</summary>
    public static Ability? AbilityFor(string classId)
    {
        ArgumentNullException.ThrowIfNull(classId);

        return AbilityByClassId.TryGetValue(classId, out var ability) ? ability : null;
    }

    /// <summary>The DC a target must beat to resist a spell: 8 + proficiency + ability modifier.</summary>
    public static int SaveDifficultyClass(int proficiencyBonus, int abilityModifier) =>
        8 + proficiencyBonus + abilityModifier;

    /// <summary>The bonus added to a spell attack roll: proficiency + ability modifier.</summary>
    public static int AttackBonus(int proficiencyBonus, int abilityModifier) =>
        proficiencyBonus + abilityModifier;

    /// <summary>
    /// The DC to maintain Concentration after taking damage: 10, or half the damage
    /// taken, whichever is higher.
    /// </summary>
    public static int ConcentrationDifficultyClass(int damageTaken) => Math.Max(10, damageTaken / 2);
}
