using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>What happened when damage was applied.</summary>
/// <param name="Raw">The damage rolled, before resistance and temporary hit points.</param>
/// <param name="Effective">The damage after resistance, immunity and vulnerability.</param>
/// <param name="AbsorbedByTemporaryHitPoints">How much the temporary pool soaked.</param>
/// <param name="Response">How the creature responds to this damage type, if at all.</param>
/// <param name="Downed">The creature dropped to 0 hit points on this damage.</param>
/// <param name="Died">The creature died outright.</param>
/// <param name="DeathSaveFailures">
/// Death Saving Throw failures inflicted by taking damage while already at 0 hit points.
/// </param>
public sealed record DamageResult(
    int Raw,
    int Effective,
    int AbsorbedByTemporaryHitPoints,
    DamageResponse? Response,
    bool Downed,
    bool Died,
    int DeathSaveFailures);

/// <summary>Applying damage and healing.</summary>
public static class DamageRules
{
    /// <summary>
    /// Applies resistance, immunity or vulnerability to a damage amount.
    /// </summary>
    /// <remarks>
    /// Resistance halves and rounds down; vulnerability doubles; immunity zeroes.
    /// The rounding matters: 7 damage resisted is 3, not 3.5 or 4.
    /// </remarks>
    public static int ApplyResponse(int amount, DamageResponse? response) => response switch
    {
        DamageResponse.Immunity => 0,
        DamageResponse.Resistance => amount / 2,
        DamageResponse.Vulnerability => amount * 2,
        _ => amount,
    };

    /// <summary>Applies one damage component to a creature.</summary>
    /// <param name="additionalHalvings">
    /// Halvings from sources outside the creature's own damage responses — a Barbarian's
    /// Rage, or a Rogue's Uncanny Dodge. Applied in turn, each rounding down, because
    /// two separate effects that each halve damage do just that.
    /// </param>
    public static DamageResult Apply(
        Combatant target,
        int amount,
        DamageType type,
        bool fromCriticalHit = false,
        int additionalHalvings = 0)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        target.Stats.DamageResponses.TryGetValue(type, out var response);
        var hasResponse = target.Stats.DamageResponses.ContainsKey(type);

        var effective = ApplyResponse(amount, hasResponse ? response : null);

        for (var halving = 0; halving < additionalHalvings; halving++)
        {
            effective /= 2;
        }

        // Damage taken while already at 0 hit points does not reduce hit points further;
        // it inflicts Death Saving Throw failures instead — two from a Critical Hit.
        if (target.CurrentHitPoints == 0 && !target.IsDead)
        {
            return ApplyToDyingCreature(target, amount, effective, hasResponse ? response : null, fromCriticalHit);
        }

        var absorbed = Math.Min(target.TemporaryHitPoints, effective);
        if (absorbed > 0)
        {
            target.SetTemporaryHitPoints(target.TemporaryHitPoints - absorbed);
        }

        var toHitPoints = effective - absorbed;
        var hitPointsBefore = target.CurrentHitPoints;
        target.ReduceHitPoints(toHitPoints);

        if (target.CurrentHitPoints > 0)
        {
            return new DamageResult(amount, effective, absorbed, hasResponse ? response : null, false, false, 0);
        }

        // Massive damage: a creature dies outright if the damage left over after
        // reaching 0 equals or exceeds its hit point maximum.
        var overflow = toHitPoints - hitPointsBefore;
        var massive = overflow >= target.Stats.MaximumHitPoints;

        // A monster dies the instant it drops to 0; a character falls Unconscious.
        var dies = massive || target.Stats.DiesAtZeroHitPoints;

        if (dies)
        {
            target.MarkDead();
            return new DamageResult(amount, effective, absorbed, hasResponse ? response : null, true, true, 0);
        }

        target.SetTemporaryHitPoints(0);
        target.AddCondition(ConditionType.Unconscious);
        target.ResetDeathSaves();

        return new DamageResult(amount, effective, absorbed, hasResponse ? response : null, true, false, 0);
    }

    private static DamageResult ApplyToDyingCreature(
        Combatant target,
        int raw,
        int effective,
        DamageResponse? response,
        bool fromCriticalHit)
    {
        // Damage equal to or greater than the hit point maximum kills outright, even at
        // 0 hit points.
        if (effective >= target.Stats.MaximumHitPoints)
        {
            target.MarkDead();
            return new DamageResult(raw, effective, 0, response, false, true, 0);
        }

        var failures = fromCriticalHit ? 2 : 1;
        target.AddDeathSaveFailure(failures);

        // Taking damage at 0 hit points ends stability — the creature goes back to
        // making Death Saving Throws rather than lying there safely.
        target.ClearStable();
        target.AddCondition(ConditionType.Unconscious);

        if (target.DeathSaveFailures >= 3)
        {
            target.MarkDead();
            return new DamageResult(raw, effective, 0, response, false, true, failures);
        }

        return new DamageResult(raw, effective, 0, response, false, false, failures);
    }

    /// <summary>Restores hit points, waking an Unconscious creature that was merely dying.</summary>
    public static int Heal(Combatant target, int amount)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        if (target.IsDead)
        {
            return 0;
        }

        var before = target.CurrentHitPoints;
        target.RegainHitPoints(amount);

        return target.CurrentHitPoints - before;
    }

    /// <summary>
    /// Grants temporary hit points. These never stack — a new pool replaces a smaller
    /// one and is declined in favour of a larger one.
    /// </summary>
    public static void GrantTemporaryHitPoints(Combatant target, int amount)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        if (amount > target.TemporaryHitPoints)
        {
            target.SetTemporaryHitPoints(amount);
        }
    }
}
