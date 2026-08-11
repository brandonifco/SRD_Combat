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
/// Four conditions are on it today.
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Prone</b> — <c>AttackRules</c> gives Advantage within 5 feet and Disadvantage
/// beyond, <c>Encounter.Move</c> refuses to move a Prone creature, and
/// <c>Encounter.StandUp</c> ends it for half the creature's Speed.
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
/// <b>Unconscious</b> — brings Incapacitated and Prone with it, and any hit from within
/// 5 feet is a Critical Hit.
/// </item>
/// </list>
/// <para>
/// Everything else is deliberately absent, and the absences are the point. Grappled and
/// Restrained need a speed of 0, an Escape action against the printed escape DC, and the
/// grapple to end when the grappler does. Frightened needs line of sight to the source,
/// which this engine has no concept of. Charmed, Blinded, Paralyzed and Stunned are each
/// a small piece of work that nobody has done. Until it is done the rider is reported as
/// not modelled rather than imposed as scenery.
/// </para>
/// </remarks>
public static class ConditionRules
{
    private static readonly HashSet<ConditionType> Executable =
    [
        ConditionType.Prone,
        ConditionType.Poisoned,
        ConditionType.Incapacitated,
        ConditionType.Unconscious,
    ];

    /// <summary>True when the engine gives this condition its rules effects.</summary>
    public static bool IsExecutable(ConditionType condition) => Executable.Contains(condition);

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
