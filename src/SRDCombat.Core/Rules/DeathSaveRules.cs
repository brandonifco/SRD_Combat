using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Rules;

/// <summary>What a Death Saving Throw did.</summary>
/// <param name="Roll">The d20 rolled. Death Saves are not tied to an ability, so there is no modifier.</param>
/// <param name="Succeeded">Whether the roll met the DC of 10.</param>
/// <param name="Failures">Failures added — two on a natural 1.</param>
/// <param name="Successes">Successes added.</param>
/// <param name="RegainedConsciousness">A natural 20 restored 1 hit point.</param>
/// <param name="BecameStable">A third success stabilised the creature.</param>
/// <param name="Died">A third failure killed the creature.</param>
public sealed record DeathSaveResult(
    D20Roll Roll,
    bool Succeeded,
    int Failures,
    int Successes,
    bool RegainedConsciousness,
    bool BecameStable,
    bool Died);

/// <summary>Death Saving Throws.</summary>
public static class DeathSaveRules
{
    /// <summary>The fixed DC. A Death Save is a flat d20 against 10, with no modifier.</summary>
    public const int DifficultyClass = 10;

    /// <summary>
    /// True when this creature must make a Death Saving Throw at the start of its turn:
    /// at 0 hit points, not dead, not yet stable.
    /// </summary>
    public static bool MustRoll(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        return combatant.IsDying;
    }

    /// <summary>Rolls one Death Saving Throw and applies its result.</summary>
    public static DeathSaveResult Roll(IRandomSource random, Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(combatant);

        var roll = D20Test.Roll(random, modifier: 0);

        // A natural 20 does not merely succeed — the creature regains 1 hit point and is
        // back on its feet, which is a different outcome from a third success.
        if (roll.IsNatural20)
        {
            DamageRules.Heal(combatant, 1);
            return new DeathSaveResult(roll, true, 0, 0, RegainedConsciousness: true, false, false);
        }

        if (roll.IsNatural1)
        {
            combatant.AddDeathSaveFailure(2);
            var diedOnOne = combatant.DeathSaveFailures >= 3;

            if (diedOnOne)
            {
                combatant.MarkDead();
            }

            return new DeathSaveResult(roll, false, 2, 0, false, false, diedOnOne);
        }

        if (roll.Total >= DifficultyClass)
        {
            combatant.AddDeathSaveSuccess();
            var stable = combatant.DeathSaveSuccesses >= 3;

            if (stable)
            {
                combatant.MarkStable();
            }

            return new DeathSaveResult(roll, true, 0, 1, false, stable, false);
        }

        combatant.AddDeathSaveFailure();
        var died = combatant.DeathSaveFailures >= 3;

        if (died)
        {
            combatant.MarkDead();
        }

        return new DeathSaveResult(roll, false, 1, 0, false, false, died);
    }
}
