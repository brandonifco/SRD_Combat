using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Rules;

/// <summary>
/// What an executed magic item does to the character wearing or wielding it.
/// </summary>
/// <remarks>
/// Every field is something <c>CharacterResolver</c> genuinely applies; there is no
/// field for an effect the engine does not execute, on purpose — an effect that cannot
/// be expressed here cannot be smuggled into the registry half-done.
/// </remarks>
public sealed record MagicItemPowers
{
    /// <summary>Bonus to attack rolls with the bound weapon.</summary>
    public int AttackRollBonus { get; init; }

    /// <summary>Bonus to damage rolls with the bound weapon.</summary>
    public int DamageRollBonus { get; init; }

    /// <summary>
    /// Extra damage dice on every hit with the bound weapon, "of the same type as the
    /// weapon's normal damage" — the Vicious Weapon's shape.
    /// </summary>
    public DiceExpression? ExtraWeaponDamageDice { get; init; }

    /// <summary>Always-on bonus to Armor Class while worn or held.</summary>
    public int ArmorClassBonus { get; init; }

    /// <summary>
    /// Bonus to Armor Class only while "wearing no armor and using no Shield" — the
    /// Bracers of Defense's printed condition, tested against the draft.
    /// </summary>
    public int UnarmoredOnlyArmorClassBonus { get; init; }

    /// <summary>Bonus to every saving throw — the Ring and Cloak of Protection.</summary>
    public int SavingThrowBonus { get; init; }

    /// <summary>Bonus to spell attack rolls — the Wand of the War Mage.</summary>
    public int SpellAttackBonus { get; init; }

    /// <summary>The ability a "Your Strength is 19" item sets, if any.</summary>
    public Ability? SetsAbility { get; init; }

    /// <summary>What the ability becomes. "No effect if it is this or higher without it."</summary>
    public int SetsAbilityTo { get; init; }

    /// <summary>"Any Critical Hit against you becomes a normal hit" — Adamantine Armor.</summary>
    public bool CriticalHitsAgainstBecomeNormal { get; init; }

    /// <summary>This item enchants one carried weapon and must be bound to it.</summary>
    public bool AppliesToWeapon { get; init; }

    /// <summary>This item is the worn armour itself and needs the draft to wear armour.</summary>
    public bool AppliesToArmor { get; init; }

    /// <summary>This item is the Shield itself and needs the draft to carry one.</summary>
    public bool AppliesToShield { get; init; }

    /// <summary>
    /// Armour ids this item may be, when the type line restricts it — Elven Chain is
    /// "(Chain Mail or Chain Shirt)". Empty means any worn armour.
    /// </summary>
    public IReadOnlyList<string> AllowedArmorIds { get; init; } = [];

    /// <summary>
    /// Armour categories this item may be — Adamantine is "(Any Medium or Heavy...)".
    /// Empty means any.
    /// </summary>
    public IReadOnlyList<ArmorCategory> AllowedArmorCategories { get; init; } = [];

    /// <summary>
    /// Armour ids the type line excludes — Adamantine's "Except Hide Armor".
    /// </summary>
    public IReadOnlyList<string> ExcludedArmorIds { get; init; } = [];

    /// <summary>Attunement is only valid on a character who can cast — the Wand.</summary>
    public bool RequiresSpellcaster { get; init; }
}

/// <summary>
/// The curated allowlist mapping a printed magic item name to the effect the engine
/// executes — the same bargain as <c>ClassFeatureRegistry</c> and
/// <c>MonsterTraitRegistry</c>: <b>add a name here only alongside the code that does the
/// thing.</b> An item absent from this list cannot be equipped; the resolver refuses it
/// by name rather than letting it ride along doing nothing.
/// </summary>
/// <remarks>
/// <para>
/// Readings written down, per the project rule:
/// </para>
/// <para>
/// <b>Wand of the War Mage's "you ignore Half Cover"</b> — the engine models no cover of
/// any kind, so no spell attack ever suffers it and the clause is satisfied vacuously.
/// If cover is ever modelled, this entry must learn to ignore it in the same change.
/// </para>
/// <para>
/// <b>Elven Chain's "You are considered trained with this armor"</b> — the resolver does
/// not model armour-training penalties at all (see the remark on
/// <c>CharacterResolver.ResolveAttacks</c> for the same stance on weapons), so the
/// override is satisfied by construction. What the entry does execute is the printed
/// +1 bonus and the Chain Mail / Chain Shirt restriction.
/// </para>
/// <para>
/// <b>Ability-setting items say "Your Strength is 19", not "+"</b> — applied as a floor
/// after the background's increases, which is exactly "no effect on you if your score is
/// 19 or higher without it".
/// </para>
/// </remarks>
public static class MagicItemRegistry
{
    private static MagicItemPowers PlusWeapon(int bonus) => new()
    {
        AttackRollBonus = bonus,
        DamageRollBonus = bonus,
        AppliesToWeapon = true,
    };

    private static MagicItemPowers PlusArmor(int bonus) => new()
    {
        ArmorClassBonus = bonus,
        AppliesToArmor = true,
        AllowedArmorCategories = [ArmorCategory.Light, ArmorCategory.Medium, ArmorCategory.Heavy],
    };

    private static MagicItemPowers PlusShield(int bonus) => new()
    {
        ArmorClassBonus = bonus,
        AppliesToShield = true,
    };

    private static MagicItemPowers Protection() => new()
    {
        ArmorClassBonus = 1,
        SavingThrowBonus = 1,
    };

    private static MagicItemPowers SetsAbility(Ability ability) => new()
    {
        SetsAbility = ability,
        SetsAbilityTo = 19,
    };

    /// <summary>
    /// Powers for a printed item name and, for a variant item, the tier's suffix.
    /// Null when the engine does not execute the item.
    /// </summary>
    public static MagicItemPowers? PowersFor(string name, string? variant = null) => (name, variant) switch
    {
        ("Weapon, +1, +2, or +3", "+1") => PlusWeapon(1),
        ("Weapon, +1, +2, or +3", "+2") => PlusWeapon(2),
        ("Weapon, +1, +2, or +3", "+3") => PlusWeapon(3),

        ("Armor, +1, +2, or +3", "+1") => PlusArmor(1),
        ("Armor, +1, +2, or +3", "+2") => PlusArmor(2),
        ("Armor, +1, +2, or +3", "+3") => PlusArmor(3),

        ("Shield, +1, +2, or +3", "+1") => PlusShield(1),
        ("Shield, +1, +2, or +3", "+2") => PlusShield(2),
        ("Shield, +1, +2, or +3", "+3") => PlusShield(3),

        ("Wand of the War Mage, +1, +2, or +3", "+1") => new MagicItemPowers { SpellAttackBonus = 1, RequiresSpellcaster = true },
        ("Wand of the War Mage, +1, +2, or +3", "+2") => new MagicItemPowers { SpellAttackBonus = 2, RequiresSpellcaster = true },
        ("Wand of the War Mage, +1, +2, or +3", "+3") => new MagicItemPowers { SpellAttackBonus = 3, RequiresSpellcaster = true },

        ("Ring of Protection", null) => Protection(),
        ("Cloak of Protection", null) => Protection(),

        ("Bracers of Defense", null) => new MagicItemPowers { UnarmoredOnlyArmorClassBonus = 2 },

        ("Gauntlets of Ogre Power", null) => SetsAbility(Ability.Strength),
        ("Amulet of Health", null) => SetsAbility(Ability.Constitution),
        ("Headband of Intellect", null) => SetsAbility(Ability.Intelligence),

        ("Adamantine Armor", null) => new MagicItemPowers
        {
            CriticalHitsAgainstBecomeNormal = true,
            AppliesToArmor = true,
            AllowedArmorCategories = [ArmorCategory.Medium, ArmorCategory.Heavy],
            ExcludedArmorIds = ["armor.hide-armor"],
        },

        ("Vicious Weapon", null) => new MagicItemPowers
        {
            ExtraWeaponDamageDice = DiceExpression.Parse("2d6"),
            AppliesToWeapon = true,
        },

        ("Elven Chain", null) => new MagicItemPowers
        {
            ArmorClassBonus = 1,
            AppliesToArmor = true,
            AllowedArmorIds = ["armor.chain-mail", "armor.chain-shirt"],
        },

        _ => null,
    };

    /// <summary>Whether any tier of this printed name executes.</summary>
    public static bool Executes(MagicItemDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.Variants.Count > 0
            ? definition.Variants.Any(variant => PowersFor(definition.Name, variant.Suffix) is not null)
            : PowersFor(definition.Name) is not null;
    }

    /// <summary>
    /// "You can be attuned to no more than three magic items at a time." Printed in
    /// Equipment's Magic Items section.
    /// </summary>
    public const int AttunementLimit = 3;

    /// <summary>
    /// Every printed name this registry executes at least one tier of. The content
    /// validator checks each exists in the extracted chapter, so the allowlist cannot
    /// quietly outlive a renamed or lost item.
    /// </summary>
    public static IReadOnlyList<string> ExecutedNames { get; } =
    [
        "Weapon, +1, +2, or +3",
        "Armor, +1, +2, or +3",
        "Shield, +1, +2, or +3",
        "Wand of the War Mage, +1, +2, or +3",
        "Ring of Protection",
        "Cloak of Protection",
        "Bracers of Defense",
        "Gauntlets of Ogre Power",
        "Amulet of Health",
        "Headband of Intellect",
        "Adamantine Armor",
        "Vicious Weapon",
        "Elven Chain",
    ];
}
