using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Grappled and Restrained, and the four ways the SRD ends a grapple.
/// </summary>
/// <remarks>
/// The rules text these pin, verbatim from the glossary, because three of them are easy
/// to get wrong from memory:
/// <list type="bullet">
/// <item>
/// Grappled is "Disadvantage on attack rolls against any target <em>other than the
/// grappler</em>" — not a blanket penalty. Hitting back at whatever has hold of you is
/// the one attack a grapple does not hamper.
/// </item>
/// <item>
/// "The condition also ends if the grappler has the Incapacitated condition or if the
/// distance between the Grappled target and the grappler exceeds the grapple's range."
/// </item>
/// <item>
/// Escaping is "a Strength (Athletics) or Dexterity (Acrobatics) check against the
/// grapple's escape DC" — a flat DC, not a contest.
/// </item>
/// </list>
/// </remarks>
public class GrappleTests
{
    private const int EscapeDifficultyClass = 13;

    [Fact]
    public void AGrappledCreatureHasASpeedOfZero()
    {
        var (encounter, victim) = Grappled();

        encounter.EndTurn();

        Assert.Same(victim, encounter.ActiveCombatant);
        Assert.Equal("combatant.speed_zero", encounter.Move(new GridPosition(5, 0))?.Code);
    }

    [Fact]
    public void AGrappledCreatureCannotStandUpEither()
    {
        // Standing costs half your Speed, and half of 0 is 0 — so the arithmetic alone
        // would let a grappled creature stand for free.
        var (encounter, victim) = Grappled();

        victim.AddCondition(ConditionType.Prone);
        encounter.EndTurn();

        Assert.Equal("combatant.speed_zero", encounter.StandUp()?.Code);
    }

    [Fact]
    public void GrappledHampersEveryAttackExceptTheOneAgainstTheGrappler()
    {
        var (encounter, victim) = Grappled();
        var grappler = encounter.Combatants.Single(combatant => combatant.Id == "brute");
        var bystander = encounter.Combatants.Single(combatant => combatant.Id == "bystander");

        var atGrappler = AttackRules.DescribeCircumstances(victim, victim.Stats.Attacks[0], grappler);
        var atOther = AttackRules.DescribeCircumstances(victim, victim.Stats.Attacks[0], bystander);

        Assert.False(atGrappler.AttackerIsGrappledByAnother);
        Assert.True(atOther.AttackerIsGrappledByAnother);

        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(atGrappler, 5));
        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(atOther, 5));
    }

    [Fact]
    public void EscapingBeatsTheEscapeDifficultyClass()
    {
        var (encounter, victim) = Grappled();
        encounter.EndTurn();

        // Athletics +5 on the victim, so a 9 clears DC 13 and an 7 does not.
        Assert.Null(encounter.Escape());

        Assert.False(victim.HasCondition(ConditionType.Grappled));
        Assert.False(victim.Turn.HasAction);
    }

    [Fact]
    public void FailingTheEscapeSpendsTheActionAndLeavesTheGrapple()
    {
        var (encounter, victim) = Grappled(escapeRoll: 3);
        encounter.EndTurn();

        Assert.Null(encounter.Escape());

        Assert.True(victim.HasCondition(ConditionType.Grappled));
        Assert.False(victim.Turn.HasAction);
    }

    [Fact]
    public void TheBetterOfAthleticsAndAcrobaticsIsUsed()
    {
        // The SRD lets the creature choose; the engine takes the better, which is the
        // choice a player would always make.
        var (encounter, _) = Grappled(athletics: 1, acrobatics: 7);
        encounter.EndTurn();

        Assert.Null(encounter.Escape());

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("Dexterity (Acrobatics)", StringComparison.Ordinal));
    }

    [Fact]
    public void APoisonedCreatureEscapesWithDisadvantage()
    {
        // The loop closing on itself: Poisoned imposes Disadvantage on ability checks,
        // and until the Escape action existed the engine rolled none. Two dice are
        // scripted, and the escape uses the worse.
        var (encounter, victim) = Grappled(escapeRoll: 18, secondEscapeRoll: 2);
        victim.AddCondition(ConditionType.Poisoned);
        encounter.EndTurn();

        Assert.Null(encounter.Escape());

        Assert.True(victim.HasCondition(ConditionType.Grappled));
    }

    [Fact]
    public void TheGrappleEndsWhenTheGrapplerIsIncapacitated()
    {
        var (encounter, victim) = Grappled();
        var grappler = encounter.Combatants.Single(combatant => combatant.Id == "brute");

        grappler.AddCondition(ConditionType.Incapacitated);
        encounter.EndTurn();

        Assert.False(victim.HasCondition(ConditionType.Grappled));
        Assert.Contains(encounter.Log, step => step.Narration.Contains("no longer hold on", StringComparison.Ordinal));
    }

    [Fact]
    public void TheGrappleEndsWhenTheGrapplerWalksOutOfRange()
    {
        // Leaving the victim's reach provokes an Opportunity Attack from it — a grapple
        // does not cost you your Reaction — so the script carries a natural 1 for that
        // swing, which misses and rolls no damage.
        var (encounter, victim) = Grappled(opportunityAttackRoll: 1);
        var grappler = encounter.Combatants.Single(combatant => combatant.Id == "brute");

        // The grappler still has its own turn and its own movement; the victim has none.
        Assert.Null(encounter.Move(new GridPosition(0, 5)));

        Assert.True(grappler.Position.DistanceFeetTo(victim.Position) > 5);
        Assert.False(victim.HasCondition(ConditionType.Grappled));
        Assert.Contains(encounter.Log, step => step.Narration.Contains("too far away", StringComparison.Ordinal));
    }

    [Fact]
    public void EndingAGrappleAlsoEndsTheRestrainedItWasHolding()
    {
        // "... and it has the Restrained condition until the grapple ends." A grapple that
        // ended while leaving the target Restrained would be worse than never grappling.
        var (encounter, victim) = Grappled();
        var grappler = encounter.Combatants.Single(combatant => combatant.Id == "brute");

        victim.AddCondition(ConditionType.Restrained, grappler.Id);
        grappler.AddCondition(ConditionType.Incapacitated);
        encounter.EndTurn();

        Assert.False(victim.HasCondition(ConditionType.Grappled));
        Assert.False(victim.HasCondition(ConditionType.Restrained));
    }

    [Fact]
    public void RestrainedGivesAdvantageAgainstAndDisadvantageWith()
    {
        var (encounter, victim) = Grappled();
        var grappler = encounter.Combatants.Single(combatant => combatant.Id == "brute");

        victim.AddCondition(ConditionType.Restrained, grappler.Id);

        var incoming = AttackRules.DescribeCircumstances(grappler, grappler.Stats.Attacks[0], victim);
        Assert.True(incoming.TargetIsRestrained);
        Assert.Equal(RollMode.Advantage, AttackRules.ResolveRollMode(incoming, 5));

        // Its own attack on the grappler: Restrained's Disadvantage, and no Grappled
        // penalty because the grappler is the target. One source, so it stands.
        var outgoing = AttackRules.DescribeCircumstances(victim, victim.Stats.Attacks[0], grappler);
        Assert.True(outgoing.AttackerIsRestrained);
        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(outgoing, 5));
    }

    [Fact]
    public void EscapeIsRefusedWhenThereIsNothingToEscape()
    {
        var encounter = Fight(new ScriptedRandomSource(12, 1, 1));

        Assert.Equal("combatant.not_grappled", encounter.Escape()?.Code);
    }

    /// <summary>A fight in which the brute has already grappled the victim.</summary>
    private static (Encounter Encounter, Combatant Victim) Grappled(
        int escapeRoll = 9,
        int? secondEscapeRoll = null,
        int athletics = 5,
        int acrobatics = 0,
        int? opportunityAttackRoll = null)
    {
        // Initiative for three, then the attack roll, its damage die, then the escape.
        // Deliberately not a natural 20 on the attack: a Critical Hit doubles the damage
        // dice and would eat one more scripted roll than the escape expects.
        List<int> scripted = [12, 1, 1, 15, 1];

        if (opportunityAttackRoll is { } provoked)
        {
            scripted.Add(provoked);
        }

        scripted.Add(escapeRoll);

        if (secondEscapeRoll is { } second)
        {
            scripted.Add(second);
        }

        var encounter = Fight(new ScriptedRandomSource([.. scripted]), athletics, acrobatics);
        var victim = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        Assert.Null(encounter.Attack("Grab", victim));
        Assert.True(victim.HasCondition(ConditionType.Grappled));

        return (encounter, victim);
    }

    private static Encounter Fight(IRandomSource random, int athletics = 5, int acrobatics = 0)
    {
        var grab = new CombatAttack(
            "Grab",
            AttackKind.Melee,
            AttackBonus: 20,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("1d4"), DamageType.Bludgeoning, 2)],
            [new AppliedCondition(ConditionType.Grappled, EscapeDifficultyClass)]);

        var victimStats = CombatTestData.Stats(maximumHitPoints: 60, initiativeBonus: -10, diesAtZeroHitPoints: false) with
        {
            SkillBonuses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Athletics"] = athletics,
                ["Acrobatics"] = acrobatics,
            },
        };

        return Encounter.Start(
            new Battlefield(12, 12),
            [
                CombatTestData.Combatant(
                    "brute",
                    sideId: CombatTestData.Monsters,
                    stats: CombatTestData.Stats(initiativeBonus: 10, attacks: [grab])),
                CombatTestData.Combatant("victim", sideId: CombatTestData.Heroes, stats: victimStats, x: 1),
                CombatTestData.Combatant(
                    "bystander",
                    sideId: CombatTestData.Monsters,
                    stats: CombatTestData.Stats(initiativeBonus: -20),
                    x: 2),
            ],
            random);
    }
}
