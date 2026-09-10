using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// <see cref="PlayMode.AttackRangeEnvelope"/> and <see cref="PlayMode.AttackOutOfRangeCode"/>
/// (#302) — the plain-value seams behind the board's armed-attack range preview. Like
/// <see cref="PlayMode.ThreatenedSteps"/> before them, the geometry these ask
/// (<c>CombatAttack.CanReach</c>, <c>CombatAttack.IsAtLongRange</c>) touches no live
/// Godot node, so it is pinned here directly against hand-worked square counts rather
/// than trusted.
/// </summary>
public class AttackRangeEnvelopeTests
{
    /// <summary>
    /// A battlefield large enough that a 12-square-radius envelope never clips a board
    /// edge, with the actor dead centre — so every count below is the clean, unclipped
    /// geometry rather than a board-boundary special case.
    /// </summary>
    private static (Battlefield Field, Combatant Actor) Fixture(CombatAttack attack)
    {
        var field = new Battlefield(33, 33);
        var actor = new Combatant(
            "Actor", "Actor", FightTestData.Heroes, FightTestData.Stats(attacks: [attack]), new GridPosition(16, 16));

        return (field, actor);
    }

    private static CombatAttack Melee(int reachFeet) =>
        new(
            "Weapon",
            AttackKind.Melee,
            AttackBonus: 4,
            ReachFeet: reachFeet,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("1d6 + 2"), DamageType.Slashing, 5)]);

    private static CombatAttack Ranged(int normalFeet, int longFeet) =>
        new(
            "Bow",
            AttackKind.Ranged,
            AttackBonus: 4,
            ReachFeet: null,
            NormalRangeFeet: normalFeet,
            LongRangeFeet: longFeet,
            [new AttackDamage(DiceExpression.Parse("1d8 + 2"), DamageType.Piercing, 6)]);

    /// <summary>Every square whose Chebyshev distance from the centre is at most <paramref name="squares"/>.</summary>
    private static int UnclippedSquareCount(int squares) => ((2 * squares) + 1) * ((2 * squares) + 1);

    [Fact]
    public void MeleeReachFiveCoversTheEightNeighboursAndNoLongBand()
    {
        var (field, actor) = Fixture(Melee(5));

        var envelope = PlayMode.AttackRangeEnvelope(field, actor, Melee(5));

        // 5 feet is one square (Battlefield.FeetPerSquare) — the 3x3 block centred on
        // the actor, including the actor's own square.
        Assert.Equal(UnclippedSquareCount(1), envelope.Normal.Count);
        Assert.Empty(envelope.Long);
        Assert.Contains(new GridPosition(17, 17), envelope.Normal);
        Assert.DoesNotContain(new GridPosition(18, 16), envelope.Normal);
    }

    [Fact]
    public void MeleeReachTenCoversTwoSquaresOut()
    {
        var (field, actor) = Fixture(Melee(10));

        var envelope = PlayMode.AttackRangeEnvelope(field, actor, Melee(10));

        Assert.Equal(UnclippedSquareCount(2), envelope.Normal.Count);
        Assert.Empty(envelope.Long);
        Assert.Contains(new GridPosition(18, 16), envelope.Normal);
        Assert.DoesNotContain(new GridPosition(19, 16), envelope.Normal);
    }

    [Fact]
    public void RangedNormalAndLongBandsAreDistinctRings()
    {
        var attack = Ranged(normalFeet: 30, longFeet: 60);
        var (field, actor) = Fixture(attack);

        var envelope = PlayMode.AttackRangeEnvelope(field, actor, attack);

        // Normal: Chebyshev <= 6 (30 ft). Long: 6 < Chebyshev <= 12 (30-60 ft).
        Assert.Equal(UnclippedSquareCount(6), envelope.Normal.Count);
        Assert.Equal(UnclippedSquareCount(12) - UnclippedSquareCount(6), envelope.Long.Count);

        // Boundary squares, checked by hand: exactly 30 ft is normal, 35 ft is long,
        // 60 ft is still long, and past 60 ft is neither.
        Assert.Contains(new GridPosition(22, 16), envelope.Normal); // 6 squares east = 30 ft
        Assert.Contains(new GridPosition(23, 16), envelope.Long); // 7 squares east = 35 ft
        Assert.Contains(new GridPosition(28, 16), envelope.Long); // 12 squares east = 60 ft
        Assert.DoesNotContain(new GridPosition(29, 16), envelope.Normal); // 13 squares = 65 ft
        Assert.DoesNotContain(new GridPosition(29, 16), envelope.Long);
    }

    [Fact]
    public void NoChosenAttackUnionsEveryCarriedAttacksReachWithNoLongBand()
    {
        // Tab's cold arm: no weapon named, so the envelope is generous — the union of
        // every carried attack's own reach (TargetChoice's own reading, restated here
        // for the same reason). The ranged attack's own 30 ft reach dominates the melee
        // weapon's 5 ft, so the union collapses to the ranged attack's own circle.
        var melee = Melee(5);
        var ranged = Ranged(normalFeet: 15, longFeet: 30);
        var field = new Battlefield(33, 33);
        var actor = new Combatant(
            "Actor", "Actor", FightTestData.Heroes,
            FightTestData.Stats(attacks: [melee, ranged]), new GridPosition(16, 16));

        var envelope = PlayMode.AttackRangeEnvelope(field, actor, attack: null);

        Assert.Equal(UnclippedSquareCount(6), envelope.Normal.Count);
        Assert.Empty(envelope.Long);
        Assert.Contains(new GridPosition(22, 16), envelope.Normal); // 30 ft, the ranged weapon's own max
        Assert.DoesNotContain(new GridPosition(23, 16), envelope.Normal);
    }

    [Theory]
    [InlineData(25, true)] // 5 squares = 25 ft, inside a 30 ft normal range
    [InlineData(35, true)] // 35 ft is beyond the printed 30 ft normal range but still
                            // reachable at Disadvantage inside the 60 ft long band —
                            // CanReach (what the engine's own attack.out_of_range check
                            // asks) says yes; IsAtLongRange is a different question.
    public void AttackOutOfRangeCodeForAChosenWeaponMatchesCanReach(int distanceFeet, bool shouldReach)
    {
        var attack = Ranged(normalFeet: 30, longFeet: 60);

        var code = PlayMode.AttackOutOfRangeCode([attack], attack, distanceFeet);

        Assert.Equal(shouldReach ? null : "attack.out_of_range", code);
    }

    [Fact]
    public void AttackOutOfRangeCodeBeyondEvenLongRangeStillNamesAttackOutOfRange()
    {
        var attack = Ranged(normalFeet: 30, longFeet: 60);

        var code = PlayMode.AttackOutOfRangeCode([attack], attack, distanceFeet: 65);

        Assert.Equal("attack.out_of_range", code);
    }

    [Fact]
    public void NoChosenWeaponAndNothingCarriedReachesNamesClientNoAttack()
    {
        var melee = Melee(5);

        var code = PlayMode.AttackOutOfRangeCode([melee], chosen: null, distanceFeet: 30);

        Assert.Equal("client.no_attack", code);
    }

    [Fact]
    public void NoChosenWeaponButSomethingCarriedReachesIsNotOutOfRange()
    {
        var melee = Melee(5);
        var ranged = Ranged(normalFeet: 30, longFeet: 60);

        var code = PlayMode.AttackOutOfRangeCode([melee, ranged], chosen: null, distanceFeet: 30);

        Assert.Null(code);
    }
}
