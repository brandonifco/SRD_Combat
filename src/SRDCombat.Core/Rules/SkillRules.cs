using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>
/// The SRD's eighteen skills and the ability each uses.
/// </summary>
/// <remarks>
/// Held here rather than extracted, for the same reason the Challenge Rating and
/// Character Advancement tables are: it is a small, closed, stable rules table that
/// other content is validated <em>against</em>. Class skill lists name these, so having
/// them as code means an extracted class naming a skill that does not exist is a
/// detectable error rather than a silently accepted string.
/// </remarks>
public static class SkillRules
{
    private static readonly IReadOnlyDictionary<string, Ability> AbilityBySkill =
        new Dictionary<string, Ability>(StringComparer.OrdinalIgnoreCase)
        {
            ["Athletics"] = Ability.Strength,

            ["Acrobatics"] = Ability.Dexterity,
            ["Sleight of Hand"] = Ability.Dexterity,
            ["Stealth"] = Ability.Dexterity,

            ["Arcana"] = Ability.Intelligence,
            ["History"] = Ability.Intelligence,
            ["Investigation"] = Ability.Intelligence,
            ["Nature"] = Ability.Intelligence,
            ["Religion"] = Ability.Intelligence,

            ["Animal Handling"] = Ability.Wisdom,
            ["Insight"] = Ability.Wisdom,
            ["Medicine"] = Ability.Wisdom,
            ["Perception"] = Ability.Wisdom,
            ["Survival"] = Ability.Wisdom,

            ["Deception"] = Ability.Charisma,
            ["Intimidation"] = Ability.Charisma,
            ["Performance"] = Ability.Charisma,
            ["Persuasion"] = Ability.Charisma,
        };

    /// <summary>Every skill name, alphabetically.</summary>
    public static IReadOnlyList<string> AllSkills { get; } =
        AbilityBySkill.Keys.Order(StringComparer.Ordinal).ToArray();

    /// <summary>True when the name is one of the SRD's skills.</summary>
    public static bool IsSkill(string name) => AbilityBySkill.ContainsKey(name);

    /// <summary>
    /// A creature's bonus on a check with this skill.
    /// </summary>
    /// <remarks>
    /// Falls back to the bare ability modifier when the creature has no listed bonus,
    /// which is exactly what an unproficient check is. A monster's stat block prints only
    /// the skills it is good at, so an absence is a real answer rather than missing data.
    /// </remarks>
    public static int BonusFor(Combatant combatant, string skill)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        return combatant.Stats.SkillBonuses.TryGetValue(skill, out var bonus)
            ? bonus
            : combatant.Stats.ModifierFor(AbilityFor(skill));
    }

    /// <summary>The ability a skill check uses.</summary>
    public static Ability AbilityFor(string skill) =>
        AbilityBySkill.TryGetValue(skill, out var ability)
            ? ability
            : throw new ArgumentOutOfRangeException(nameof(skill), skill, "Not an SRD skill.");
}
