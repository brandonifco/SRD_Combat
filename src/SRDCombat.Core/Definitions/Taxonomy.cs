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

/// <summary>
/// Special senses. Ordinary sight is <c>SRDCombat.Core.Rules.VisionRules</c>'s
/// line-of-sight predicate (#671/#672), which every creature has and none of these
/// names govern; these are the senses that see <em>past</em> ordinary sight's limits
/// (through Blinded, in Darkness, past Invisible). Two of the four are carried into
/// combat as of #673 — <see cref="Combat.CombatantStats.BlindsightFeet"/> and
/// <see cref="Combat.CombatantStats.TruesightFeet"/>, read by <c>VisionRules</c> to let
/// a Blindsighted viewer see past Blinded and to let either sense see past Invisible —
/// because Invisible is the first thing in this engine either sense has something to
/// matter for. Darkvision and Tremorsense stay uncarried: darkness is not modelled at
/// all (every battlefield is Bright Light), and nothing yet keys off "can't be seen"
/// for a reason Tremorsense would answer.
/// </summary>
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
