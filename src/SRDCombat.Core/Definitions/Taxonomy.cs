namespace SRDCombat.Core.Definitions;

/// <summary>The six abilities, in the order the SRD prints them.</summary>
public enum Ability
{
    Strength,
    Dexterity,
    Constitution,
    Intelligence,
    Wisdom,
    Charisma,
}

/// <summary>
/// Creature size. Governs the squares a creature occupies on the grid, so it is a
/// live mechanic here rather than flavour.
/// </summary>
public enum CreatureSize
{
    Tiny,
    Small,
    Medium,
    Large,
    Huge,
    Gargantuan,
}

/// <summary>The SRD's fourteen creature types.</summary>
public enum CreatureType
{
    Aberration,
    Beast,
    Celestial,
    Construct,
    Dragon,
    Elemental,
    Fey,
    Fiend,
    Giant,
    Humanoid,
    Monstrosity,
    Ooze,
    Plant,
    Undead,
}

/// <summary>The SRD's thirteen damage types.</summary>
public enum DamageType
{
    Acid,
    Bludgeoning,
    Cold,
    Fire,
    Force,
    Lightning,
    Necrotic,
    Piercing,
    Poison,
    Psychic,
    Radiant,
    Slashing,
    Thunder,
}

/// <summary>How a creature moves. Absent modes mean the creature cannot move that way.</summary>
public enum MovementMode
{
    Walk,
    Burrow,
    Climb,
    Fly,
    Swim,
}

/// <summary>Special senses. Ordinary sight is not modelled — every creature has it.</summary>
public enum SenseType
{
    Blindsight,
    Darkvision,
    Tremorsense,
    Truesight,
}

/// <summary>
/// The SRD 5.2.1 conditions. Exhaustion is deliberately absent: it is a numeric
/// track rather than an on/off state, and is modelled separately.
/// </summary>
public enum ConditionType
{
    Blinded,
    Charmed,
    Deafened,
    Frightened,
    Grappled,
    Incapacitated,
    Invisible,
    Paralyzed,
    Petrified,
    Poisoned,
    Prone,
    Restrained,
    Stunned,
    Unconscious,
}

/// <summary>How a creature responds to a damage type.</summary>
public enum DamageResponse
{
    Resistance,
    Immunity,
    Vulnerability,
}
