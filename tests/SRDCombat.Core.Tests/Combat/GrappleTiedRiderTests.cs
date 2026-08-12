using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// "... and it has the Restrained condition until the grapple ends" — a duration that is
/// no expiry at all but a tie to the sibling grapple.
/// </summary>
/// <remarks>
/// The Purple Worm's shape: one sentence imposing two conditions, the second living and
/// dying with the first. The dependent rider is imposed only while the same creature's
/// grapple holds the target, and <c>Encounter.EndGrapple</c> sweeps it away with the
/// grapple however it ends — which <c>GrappleTests</c> pins from the other side.
/// </remarks>
public class GrappleTiedRiderTests
{
    [Fact]
    public void TheTiedConditionLandsWithTheGrappleAndEndsWithIt()
    {
        var (encounter, victim) = Grabbed();
        var grappler = encounter.Combatants.Single(combatant => combatant.Id == "brute");

        Assert.True(victim.HasCondition(ConditionType.Grappled));
        Assert.True(victim.HasCondition(ConditionType.Restrained));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains(
                "victim has the Restrained condition until the grapple ends",
                StringComparison.Ordinal));

        // The tie has no clock of its own: the grapple breaking is what ends it.
        Assert.Null(victim.ConditionState(ConditionType.Restrained)?.Expiry);

        grappler.AddCondition(ConditionType.Incapacitated);
        encounter.EndTurn();

        Assert.False(victim.HasCondition(ConditionType.Grappled));
        Assert.False(victim.HasCondition(ConditionType.Restrained));
    }

    [Fact]
    public void TheTiedConditionNeverLandsWhenTheGrappleWasRefused()
    {
        // The grapple's size gate fails against a Medium target, so the Grappled rider
        // never lands — and the Restrained that would have ridden it must not land
        // alone, or the victim would be held by a grapple that does not exist.
        var (_, victim) = Grabbed(maximumGrappleSize: CreatureSize.Small);

        Assert.False(victim.HasCondition(ConditionType.Grappled));
        Assert.False(victim.HasCondition(ConditionType.Restrained));
    }

    [Fact]
    public void TheTiedDurationHasNoExpiryOfItsOwn()
    {
        var source = CombatTestData.Combatant("source");
        var bearer = CombatTestData.Combatant("bearer", x: 1);

        Assert.Null(ConditionRules.ExpiryFor(ConditionDuration.UntilTheGrappleEnds, source, bearer));
    }

    /// <summary>A fight in which the brute has hit the victim with its two-rider Grab.</summary>
    private static (Encounter Encounter, Combatant Victim) Grabbed(
        CreatureSize? maximumGrappleSize = null)
    {
        var grab = new CombatAttack(
            "Grab",
            AttackKind.Melee,
            AttackBonus: 20,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("1d4"), DamageType.Bludgeoning, 2)],
            [
                new AppliedCondition(ConditionType.Grappled, 13, maximumGrappleSize),
                new AppliedCondition(ConditionType.Restrained, Duration: ConditionDuration.UntilTheGrappleEnds),
            ]);

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [
                CombatTestData.Combatant(
                    "brute",
                    sideId: CombatTestData.Monsters,
                    stats: CombatTestData.Stats(initiativeBonus: 10, attacks: [grab])),
                CombatTestData.Combatant(
                    "victim",
                    stats: CombatTestData.Stats(maximumHitPoints: 60, initiativeBonus: -10, diesAtZeroHitPoints: false),
                    x: 1),
            ],
            new ScriptedRandomSource(12, 1, 15, 1));

        var victim = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        Assert.Null(encounter.Attack("Grab", victim));

        return (encounter, victim);
    }
}
