using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Pins the target-aware ordering #339 put into <c>SimpleTacticsPolicy.ReachOf</c> — the
/// one #339 call site its own PR could not pin, because the reach it produces feeds a
/// full 2D pathfinding search (<c>ScoreSquares</c>) whose multi-key tie-break chain
/// swamps any single square's dependence on the exact reach, so bug and fix converged on
/// the same square on every hand-derived board (#661). The seam #661 introduced —
/// <c>SimpleTacticsPolicy.PlanningAttack</c>, the pure attack choice <c>ReachOf</c>
/// returns the <c>MaximumRangeFeet</c> of — lets that choice be asserted directly with a
/// constructed actor, no board and no pathfinder in the way.
/// </summary>
/// <remarks>
/// The knockout for these tests is #339's own before/after: replace
/// <c>PlanningAttack</c>'s <c>OrderByDescending(attack =&gt; ValueAgainst(attack,
/// target))</c> with the pre-#339 raw ordering — either raw average
/// (<c>attack.Damage.Sum(d =&gt; d.Amount.Average)</c>) or the literal pre-#339
/// <c>usable.Max(a =&gt; a.MaximumRangeFeet)</c> — and every assertion here flips to the
/// Immune/Resisted long-range attack. Both scenarios give that attack the higher raw
/// average <b>and</b> the longer reach, so the target-aware factor is the only thing that
/// demotes it.
/// </remarks>
public class PlanningAttackOrderingTests
{
    [Fact]
    public void PlansAroundTheWorkingAttackNotAnImmuneLongerHarderOne()
    {
        // Longbow: raw 9 average and 600-foot reach, but Fire against a Fire-Immune
        // target — worth zero. Shortbow: raw 3.5 and only 160-foot reach, but Piercing,
        // which lands. The creature must plan to walk in to Shortbow range (160), not
        // stand off at the Longbow's 600 planning a shot that can never deal a point.
        var actor = CombatTestData.Combatant(
            "planner",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(attacks:
            [
                CombatTestData.RangedAttack("Longbow", normalFeet: 150, longFeet: 600, damage: "2d8", type: DamageType.Fire),
                CombatTestData.RangedAttack("Shortbow", normalFeet: 40, longFeet: 160, damage: "1d6", type: DamageType.Piercing),
            ]));

        var target = CombatTestData.Combatant(
            "elemental",
            stats: CombatTestData.Stats(damageResponses: new Dictionary<DamageType, DamageResponse>
            {
                [DamageType.Fire] = DamageResponse.Immunity,
            }),
            x: 20);

        var planned = SimpleTacticsPolicy.PlanningAttack(actor, target);

        Assert.NotNull(planned);
        Assert.Equal("Shortbow", planned!.Name);
        // The reach ReachOf hands the pathfinder: the working attack's, not the Immune
        // one's 600.
        Assert.Equal(160, planned.MaximumRangeFeet);
    }

    [Fact]
    public void ResistanceIsAGradedComparisonNotAnImmunityShortcut()
    {
        // Resistance halves rather than zeroes, so the ordering has to be a genuine
        // comparison and not an Immunity-only special case. Longbow: raw 4.5, Fire, but
        // the target is Fire-Resistant — worth 2.25. Shortbow: raw 3.5, Piercing — worth
        // its full 3.5. The halved 2.25 is the only figure that demotes the Longbow: its
        // raw 4.5 beats the Shortbow blind, and it would still beat it if Resistance were
        // treated as a no-op, so a pass here proves PlanningAttack routes through the
        // graded ResponseFactor.
        var actor = CombatTestData.Combatant(
            "planner",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(attacks:
            [
                CombatTestData.RangedAttack("Longbow", normalFeet: 150, longFeet: 600, damage: "1d8", type: DamageType.Fire),
                CombatTestData.RangedAttack("Shortbow", normalFeet: 40, longFeet: 160, damage: "1d6", type: DamageType.Piercing),
            ]));

        var target = CombatTestData.Combatant(
            "salamander",
            stats: CombatTestData.Stats(damageResponses: new Dictionary<DamageType, DamageResponse>
            {
                [DamageType.Fire] = DamageResponse.Resistance,
            }),
            x: 20);

        var planned = SimpleTacticsPolicy.PlanningAttack(actor, target);

        Assert.NotNull(planned);
        Assert.Equal("Shortbow", planned!.Name);
        Assert.Equal(160, planned.MaximumRangeFeet);
    }

    [Fact]
    public void TiesBreakTowardTheLongerReachSoAnArcherStaysAtRange()
    {
        // No target-awareness in play: both attacks work and average the same (3.5), the
        // Rogue's Shortsword-and-Shortbow case named in ReachOf's own doc. The tie must
        // break toward the longer reach so a genuine archer keeps shooting rather than
        // planning to close for the blade.
        var actor = CombatTestData.Combatant(
            "rogue",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(attacks:
            [
                CombatTestData.MeleeAttack("Shortsword", reachFeet: 5, damage: "1d6", type: DamageType.Piercing),
                CombatTestData.RangedAttack("Shortbow", normalFeet: 80, longFeet: 320, damage: "1d6", type: DamageType.Piercing),
            ]));

        var target = CombatTestData.Combatant("goblin", x: 20);

        var planned = SimpleTacticsPolicy.PlanningAttack(actor, target);

        Assert.NotNull(planned);
        Assert.Equal("Shortbow", planned!.Name);
        Assert.Equal(320, planned.MaximumRangeFeet);
    }
}
