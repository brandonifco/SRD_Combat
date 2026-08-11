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
/// Three conditions are on it today.
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Prone</b> — <c>AttackRules</c> gives Advantage within 5 feet and Disadvantage
/// beyond, <c>Encounter.Move</c> refuses to move a Prone creature, and
/// <c>Encounter.StandUp</c> ends it for half the creature's Speed.
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
/// Restrained would need a speed of 0, an Escape action against the printed escape DC,
/// and the grapple to end when the grappler does; Poisoned, Frightened and Charmed all
/// print a duration this engine has no clock for. Each is a separate piece of work, and
/// until it exists the rider is reported as not modelled rather than imposed as scenery.
/// </para>
/// </remarks>
public static class ConditionRules
{
    private static readonly HashSet<ConditionType> Executable =
    [
        ConditionType.Prone,
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
