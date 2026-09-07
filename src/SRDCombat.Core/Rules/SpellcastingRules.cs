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
    /// here because a chooser only needs yes or no. <b>Character creation does not filter
    /// its menu with this</b> — this is shape only, and shape says yes to Bestow Curse,
    /// whose extracted save-plus-damage is a sliver of what the spell prints. The menu's
    /// actual authority is <c>SRDCombat.Game.PreparableSpells</c>, hand-verified against
    /// print rather than derived (#292); it asserts itself a subset of what this
    /// predicate allows and never an override of it, so this remains the floor every
    /// curated spell must clear, not the gate deciding what is offered.
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

    /// <summary>
    /// The spell's damage, averaged, counted once — following whichever field the
    /// resolver would actually roll.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SpellDefinition.Damage"/>'s own doc comment states the trap: "a save
    /// spell's damage is also on <c>Save</c>" — the extractor carries a save spell's
    /// dice on both fields because <c>Encounter.Casting</c>'s scaling has to grow both
    /// (see its <c>Grown</c> remarks), but only one of them is ever rolled. Summing
    /// both, as two independent valuation call sites once did (#376), double-counts the
    /// same dice and roughly doubles a save spell's priced value against a weapon or an
    /// attack-roll cantrip.
    /// </para>
    /// <para>
    /// <b>Which field is rolled depends on the branch <c>Encounter.CastSpell</c>
    /// takes, not on whether <c>Save</c> is merely present.</b> That branch checks
    /// <c>IsSpellAttack</c> <em>first</em>: when true, <c>ResolveSpellAttack</c> runs
    /// and rolls <c>Damage</c>, even if a <c>Save</c> is also attached (Ice Knife's
    /// ranged spell attack for 1d10 plus an unconditional 2d6 Cold save-or-half, Arcane
    /// Hand's Clenched Fist reading a melee spell attack alongside the option's shared
    /// <c>Save</c> shape). Only when <c>IsSpellAttack</c> is false does
    /// <c>ResolveSpellSave</c> run, and only then is <c>Save.FailureDamage</c> what
    /// gets rolled. So the precedence here mirrors the resolver's own: attack first,
    /// then save, then plain damage — never "any non-null <c>Save</c> wins", which
    /// prices an attack-plus-save spell from the field the resolver never touches for
    /// it. (It went unnoticed against today's corpus only because Ice Knife's and
    /// Arcane Hand's extraction duplicates every damage component into both fields
    /// identically; a corrected extraction that split the attack dice from the save
    /// dice would make the old precedence price from the wrong number.)
    /// </para>
    /// <para>
    /// A non-attack save spell whose <c>Save.FailureDamage</c> comes back empty falls
    /// through to <c>Damage</c> rather than pricing at zero. No spell in the corpus
    /// takes this shape today (a save spell's <c>Damage</c> and <c>FailureDamage</c>
    /// are always either both populated or both empty), but the fallthrough keeps the
    /// estimate from silently zeroing a spell that has real damage recorded somewhere.
    /// </para>
    /// </remarks>
    public static double AverageDamage(SpellDefinition spell)
    {
        ArgumentNullException.ThrowIfNull(spell);

        if (spell.IsSpellAttack)
        {
            return spell.Damage.Sum(component => component.Amount.Average);
        }

        var failureDamage = spell.Save?.FailureDamage.Sum(component => component.Amount.Average) ?? 0;

        return failureDamage > 0 ? failureDamage : spell.Damage.Sum(component => component.Amount.Average);
    }
}
