using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Characters;

/// <summary>A resolved skill: its ability, its total bonus, and whether it is proficient.</summary>
public sealed record SkillBonus(string Skill, Ability Ability, int Bonus, bool IsProficient);

/// <summary>
/// A fully resolved character, ready to fight.
/// </summary>
/// <remarks>
/// Everything here is derived. Nothing on a sheet is stored independently of the rules
/// that produce it, which is what stops a character's AC and their armour from drifting
/// apart.
/// </remarks>
public sealed record CharacterSheet
{
    public required string Name { get; init; }

    public required string SpeciesName { get; init; }

    public required string ClassName { get; init; }

    public required string BackgroundName { get; init; }

    public required int Level { get; init; }

    public required int ProficiencyBonus { get; init; }

    /// <summary>Final ability scores, after the background's increases.</summary>
    public required IReadOnlyDictionary<Ability, int> AbilityScores { get; init; }

    public required int MaximumHitPoints { get; init; }

    public required int ArmorClass { get; init; }

    /// <summary>How the AC was arrived at, for display and for checking the rules were applied.</summary>
    public required string ArmorClassSource { get; init; }

    public required int SpeedFeet { get; init; }

    public required CreatureSize Size { get; init; }

    /// <summary>Saving throw bonuses for all six abilities.</summary>
    public required IReadOnlyDictionary<Ability, int> SavingThrows { get; init; }

    /// <summary>Every skill, proficient or not.</summary>
    public required IReadOnlyList<SkillBonus> Skills { get; init; }

    /// <summary>Attacks the character can make with the weapons they carry.</summary>
    public required IReadOnlyList<CombatAttack> Attacks { get; init; }

    /// <summary>Implemented class features the character has at this level.</summary>
    public required IReadOnlyList<GrantedFeature> Features { get; init; }

    /// <summary>
    /// The Fighting Style in effect, already folded into <see cref="ArmorClass"/> and
    /// <see cref="Attacks"/>. Carried for display rather than for arithmetic.
    /// </summary>
    public FightingStyle FightingStyle { get; init; } = FightingStyle.Unspecified;

    /// <summary>
    /// The Divine Order in effect. Its halves are already folded in where they land —
    /// Thaumaturge's bonus in <see cref="Skills"/>, Protector's proficiency lines in
    /// what the creation menus and the shop may offer. Carried for display.
    /// </summary>
    public DivineOrder DivineOrder { get; init; } = DivineOrder.Unspecified;

    /// <summary>
    /// Skills taken with Expertise, already doubled in <see cref="Skills"/>. Carried for
    /// display rather than for arithmetic.
    /// </summary>
    public IReadOnlyList<string> ExpertiseSkills { get; init; } = [];

    /// <summary>Spell slots by spell level, from the class table. Empty for a non-caster.</summary>
    public required IReadOnlyDictionary<int, int> SpellSlots { get; init; }

    /// <summary>
    /// Printed feature and species trait names the character has that this engine does
    /// <em>not</em> implement.
    /// </summary>
    /// <remarks>
    /// The same rule as everywhere else in this project: the gap is carried on the object
    /// and countable, rather than being an absence nobody can see. A level 5 Cleric's
    /// sheet says outright that Spellcasting and Sear Undead do nothing yet — and
    /// Divine Order rejoins this list whenever the draft never chose a role, because a
    /// mapped name whose choice is Unspecified executes nothing. Every species trait
    /// stands here too: <see cref="SpeciesTraitRegistry"/> executes none of them yet, so
    /// a Dwarf's Darkvision or a Dragonborn's Breath Weapon reads here exactly like an
    /// unimplemented class feature, rather than a player having to guess from the
    /// silence.
    /// </remarks>
    public required IReadOnlyList<string> UnimplementedFeatures { get; init; }

    /// <summary>
    /// Feat choices the character has earned and this draft did not spend.
    /// </summary>
    /// <remarks>
    /// The printed feature is "the Ability Score Improvement feat <em>or another feat of
    /// your choice for which you qualify</em>", and no other feat is modelled — so a
    /// character who took one of those is legitimate and simply weaker here than at a
    /// table. Counted rather than hidden, for the same reason
    /// <see cref="UnimplementedFeatures"/> exists.
    /// </remarks>
    public int UnspentFeatChoices { get; init; }

    /// <summary>Equipped magic items, for display. Their numbers are already folded in.</summary>
    public IReadOnlyList<string> MagicItemNames { get; init; } = [];

    /// <summary>Item bonus to spell attack rolls — the Wand of the War Mage.</summary>
    public int SpellAttackItemBonus { get; init; }

    /// <summary>"You ignore Half Cover when making a spell attack" — the same wand.</summary>
    public bool IgnoresHalfCoverOnSpellAttacks { get; init; }

    /// <summary>"Any Critical Hit against you becomes a normal hit" — Adamantine Armor.</summary>
    public bool CriticalHitsAgainstBecomeNormal { get; init; }

    /// <summary>The ability modifier for an ability score.</summary>
    public int Modifier(Ability ability) => AbilityRules.ModifierFor(AbilityScores[ability]);

    /// <summary>True when the character has this implemented feature.</summary>
    public bool Has(ClassFeature feature) => Features.Any(granted => granted.Feature == feature);

    /// <summary>Attacks made when taking the Attack action — two with Extra Attack.</summary>
    public int AttacksPerAction => Has(ClassFeature.ExtraAttack) ? 2 : 1;
}

/// <summary>Ability score arithmetic.</summary>
public static class AbilityRules
{
    /// <summary>The modifier for a score: (score - 10) / 2, rounded down.</summary>
    public static int ModifierFor(int score) => (int)Math.Floor((score - 10) / 2.0);

    /// <summary>The standard array, highest first.</summary>
    public static IReadOnlyList<int> StandardArray { get; } = [15, 14, 13, 12, 10, 8];
}
