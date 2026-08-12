using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>
/// Which conditions the engine really executes, and whether a rider printed on an attack
/// may be imposed.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Executable"/> is a curated allowlist, exactly like the extractor's inert
/// list and <c>ClassFeatureRegistry</c>: a condition belongs on it only when the engine
/// actually does what the condition says. Adding a name here without the code behind it
/// would put a condition on a creature that changes nothing — the quietest possible
/// failure, and the one this project is built to avoid.
/// </para>
/// <para>
/// Eleven conditions are on it today.
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Prone</b> — <c>AttackRules</c> gives Advantage within 5 feet and Disadvantage
/// beyond, <c>Encounter.Move</c> refuses to move a Prone creature, and
/// <c>Encounter.StandUp</c> ends it for half the creature's Speed.
/// </item>
/// <item>
/// <b>Grappled</b> — Speed 0, and Disadvantage on attack rolls <em>against any target
/// other than the grappler</em>, which is why the condition remembers who imposed it.
/// <c>Encounter.Escape</c> is the Strength (Athletics) or Dexterity (Acrobatics) check
/// against the printed escape DC, and the grapple also ends on its own when the grappler
/// is Incapacitated or dead, or when the two are further apart than the grapple's range.
/// </item>
/// <item>
/// <b>Restrained</b> — Speed 0, Advantage on attack rolls against it, Disadvantage on its
/// own, and Disadvantage on its Dexterity saving throws. Implemented alongside Grappled
/// because the two share the immobility, and <em>no rider reaches it yet</em>: every
/// printed Restrained rider hangs off "until the grapple ends", a duration shape the model
/// does not express, so its sentence is unmodelled as a whole. It is here ready for
/// saving-throw effects, which is where the rest of them live.
/// </item>
/// <item>
/// <b>Poisoned</b> — Disadvantage on the creature's attack rolls, in
/// <c>AttackRules.ResolveRollMode</c>. The SRD also imposes it on ability checks, and
/// nothing in a fight rolls one: <c>SkillRules</c> is used at character resolution to
/// work out bonuses and never during combat. So this is complete for every roll the
/// engine makes today, and is the one entry here to revisit the moment an in-combat
/// ability check exists.
/// </item>
/// <item>
/// <b>Incapacitated</b> — <c>Combatant.CanAct</c> is false, so the creature takes no
/// actions, and a Dodge it had running stops helping.
/// </item>
/// <item>
/// <b>Unconscious</b> — brings Incapacitated and Prone with it, auto-fails Strength and
/// Dexterity saving throws, and any hit from within 5 feet is a Critical Hit.
/// </item>
/// <item>
/// <b>Blinded</b> — Advantage on attack rolls against it, Disadvantage on its own, in
/// <c>AttackRules</c>. Its "automatically fail any ability check that requires sight" is
/// complete by vacancy: the only check the engine rolls in a fight is the grapple
/// Escape, which does not require sight. Revisit alongside Poisoned's note above the
/// moment a sight-based check exists.
/// </item>
/// <item>
/// <b>Charmed</b> — cannot attack the charmer or target it with a damaging effect. The
/// printed clause heading is "Can't Harm the Charmer", so "damaging" is read as
/// qualifying both "abilities" and "magical effects": a non-damaging effect aimed at the
/// charmer is allowed. Attacks are refused outright, Opportunity Attacks included; the
/// charmer's "Advantage on any ability check to interact with you socially" has nothing
/// to apply to in a fight. Enforced in <c>Encounter</c>'s attack, entry and casting
/// paths, off the condition's <c>SourceId</c>.
/// </item>
/// <item>
/// <b>Frightened</b> — Disadvantage on attack rolls and ability checks "while the source
/// of fear is within line of sight", and no willing movement closer to the source. The
/// engine has no model of sight, so the source is read as always within line of sight
/// while it is on the field, dead or alive — sight does not require the source to be
/// breathing, and the hampering direction is the safe one while sight is unmodelled.
/// "Closer" is judged at the destination: <c>Encounter.Move</c> refuses a destination
/// nearer the source than the square the creature stands in, and does not judge the
/// path between them.
/// </item>
/// <item>
/// <b>Paralyzed</b> — brings Incapacitated, Speed 0, auto-fails Strength and Dexterity
/// saving throws, Advantage on attack rolls against it, and any hit from within 5 feet
/// is a Critical Hit — the same clause Unconscious prints.
/// </item>
/// <item>
/// <b>Stunned</b> — brings Incapacitated, auto-fails Strength and Dexterity saving
/// throws, and Advantage on attack rolls against it. Note what it does not print: no
/// Speed 0 and no automatic Critical Hits — memory adds both, the glossary has neither.
/// </item>
/// </list>
/// <para>
/// Everything else is deliberately absent, and the absences are the point. Deafened,
/// Invisible and Petrified each need a model (hearing, sight, objects) that does not
/// exist. Until one does the rider is reported as not modelled rather than imposed as
/// scenery.
/// </para>
/// </remarks>
public static class ConditionRules
{
    private static readonly HashSet<ConditionType> Executable =
    [
        ConditionType.Blinded,
        ConditionType.Charmed,
        ConditionType.Frightened,
        ConditionType.Grappled,
        ConditionType.Incapacitated,
        ConditionType.Paralyzed,
        ConditionType.Poisoned,
        ConditionType.Prone,
        ConditionType.Restrained,
        ConditionType.Stunned,
        ConditionType.Unconscious,
    ];

    /// <summary>
    /// Conditions that set a creature's Speed to 0. Paralyzed and Unconscious print the
    /// same clause, and are listed for fidelity even though their Incapacitated already
    /// stops the creature acting before Speed is consulted.
    /// </summary>
    private static readonly ConditionType[] SpeedZero =
    [
        ConditionType.Grappled,
        ConditionType.Restrained,
        ConditionType.Paralyzed,
        ConditionType.Unconscious,
    ];

    /// <summary>
    /// Conditions printing "You automatically fail Strength and Dexterity saving throws"
    /// — Paralyzed, Stunned and Unconscious carry the clause word for word.
    /// </summary>
    private static readonly ConditionType[] AutoFailsStrengthAndDexteritySaves =
    [
        ConditionType.Paralyzed,
        ConditionType.Stunned,
        ConditionType.Unconscious,
    ];

    /// <summary>True when the engine gives this condition its rules effects.</summary>
    public static bool IsExecutable(ConditionType condition) => Executable.Contains(condition);

    /// <summary>
    /// True when the creature's Speed is 0 and cannot increase.
    /// </summary>
    /// <remarks>
    /// Checked at the point of moving rather than baked into the turn's movement
    /// allowance, because a creature can be grappled part-way through its own turn and
    /// the SRD's "your Speed is 0 and can't increase" takes effect at once.
    /// </remarks>
    public static bool IsImmobile(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        return SpeedZero.Any(combatant.HasCondition);
    }

    /// <summary>The condition holding this creature still, for narrating a refusal.</summary>
    public static ConditionType? ImmobilisedBy(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        return SpeedZero.Cast<ConditionType?>().FirstOrDefault(condition => combatant.HasCondition(condition!.Value));
    }

    /// <summary>
    /// The condition making this creature automatically fail a saving throw with this
    /// ability, or null when the save is rolled normally. No die is consumed by an
    /// automatic failure — the printed clause replaces the roll rather than penalising it.
    /// </summary>
    public static ConditionType? AutoFailingSaveCondition(Combatant combatant, Ability ability)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        if (ability is not (Ability.Strength or Ability.Dexterity))
        {
            return null;
        }

        return AutoFailsStrengthAndDexteritySaves
            .Cast<ConditionType?>()
            .FirstOrDefault(condition => combatant.HasCondition(condition!.Value));
    }

    /// <summary>
    /// Whether a rider could ever be imposed, ignoring who it is aimed at: the model
    /// expresses everything printed with it, and the engine executes the condition.
    /// </summary>
    public static bool CanBeImposed(AppliedCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        return condition.IsFullyModelled && IsExecutable(condition.Condition);
    }

    /// <summary>
    /// Turns a printed duration into a concrete expiry against the right creature's turn
    /// counter.
    /// </summary>
    /// <remarks>
    /// The <c>+ 1</c> is the whole of "next". A rider applied during the devil's own turn
    /// and one applied during somebody else's — on an Opportunity Attack — both read
    /// "until the start of the devil's next turn" and mean different moments; counting
    /// from the owner's turn count at the moment of application gets both right without
    /// either case being special.
    /// </remarks>
    public static ConditionExpiry? ExpiryFor(ConditionDuration? duration, Combatant source, Combatant bearer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bearer);

        if (duration is null)
        {
            return null;
        }

        var owner = duration.Owner == ConditionDurationOwner.Bearer ? bearer : source;

        return new ConditionExpiry(owner.Id, duration.Clock, owner.TurnsBegun + 1);
    }

    /// <summary>
    /// Whether this rider may be imposed on this target — the same check plus the printed
    /// size gate.
    /// </summary>
    /// <remarks>
    /// Immunity is not tested here. <see cref="Combatant.AddCondition"/> owns that, and
    /// checking it twice would let the two answers drift apart.
    /// </remarks>
    public static bool CanImpose(AppliedCondition condition, Combatant target)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(target);

        return CanBeImposed(condition) && condition.AllowsTargetSize(target.Stats.Size);
    }
}
