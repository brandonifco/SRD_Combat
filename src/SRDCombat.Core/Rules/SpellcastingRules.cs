using SRDCombat.Core.Combat;
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

    /// <summary>
    /// Whether casting this spell would do something the engine executes: an attack
    /// roll, healing, or a saving throw with damage or an imposable condition behind it,
    /// in an area the engine can resolve.
    /// </summary>
    /// <remarks>
    /// The same tests <c>Encounter.CastSpell</c> applies before spending anything — its
    /// <c>spell.not_implemented</c>, <c>spell.area_not_modelled</c> and
    /// <c>spell.save_effect_not_modelled</c> refusals are this predicate's three false
    /// branches, kept granular there because a refusal explains itself and collapsed
    /// here because a chooser only needs yes or no. Character creation filters its spell
    /// menu with this, because offering a spell that can never be cast would be an
    /// unimplemented rule wearing a checkbox.
    /// </remarks>
    public static bool HasExecutableEffect(SpellDefinition spell)
    {
        ArgumentNullException.ThrowIfNull(spell);

        return spell.Heal is not null
            || spell.Revival is not null
            || spell.IsSpellAttack
            || (spell.Save is { } save
                && (save.Area is not { } area || AreaTargeting.CanResolve(area.Shape))
                && (spell.Damage.Count > 0
                    || save.FailureDamage.Count > 0
                    || spell.AppliedConditions.Any(ConditionRules.CanBeImposed)
                    || save.AppliedConditions.Any(ConditionRules.CanBeImposed)));
    }
}
