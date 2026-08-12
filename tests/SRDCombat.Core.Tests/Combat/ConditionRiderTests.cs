using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Conditions imposed by a hit — "If the target is a Medium or smaller creature, it has
/// the Prone condition."
/// </summary>
/// <remarks>
/// Two questions decide whether a rider lands, and they are deliberately kept apart. The
/// model asks whether it expresses everything printed with the condition
/// (<see cref="AppliedCondition.IsFullyModelled"/>); the engine asks whether it executes
/// that condition at all (<see cref="ConditionRules.IsExecutable"/>). Only the size gate
/// depends on who was hit, and only it is evaluated during the fight.
/// </remarks>
public class ConditionRiderTests
{
    /// <summary>An attack that always hits, so the rider is the only thing under test.</summary>
    private static CombatAttack Knockdown(params AppliedCondition[] riders) =>
        new(
            "Slam",
            AttackKind.Melee,
            AttackBonus: 20,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("1d4"), DamageType.Bludgeoning, 2)],
            riders);

    [Theory]
    [InlineData(CreatureSize.Tiny, true)]
    [InlineData(CreatureSize.Small, true)]
    [InlineData(CreatureSize.Medium, true)]
    [InlineData(CreatureSize.Large, false)]
    [InlineData(CreatureSize.Gargantuan, false)]
    public void ASizeGateDecidesWhoIsKnockedDown(CreatureSize targetSize, bool expected)
    {
        var rider = new AppliedCondition(ConditionType.Prone, MaximumTargetSize: CreatureSize.Medium);
        var encounter = Fight(rider, targetSize);

        Assert.Null(encounter.Attack("Slam", Target(encounter)));
        Assert.Equal(expected, Target(encounter).HasCondition(ConditionType.Prone));
    }

    [Fact]
    public void AnUngatedRiderLandsOnAnythingItHits()
    {
        var encounter = Fight(new AppliedCondition(ConditionType.Prone), CreatureSize.Gargantuan);

        Assert.Null(encounter.Attack("Slam", Target(encounter)));
        Assert.True(Target(encounter).HasCondition(ConditionType.Prone));
    }

    [Fact]
    public void ARiderWithAnUnmodelledRequirementIsNeverImposed()
    {
        // The charge riders: "If the target is a Large or smaller creature and the
        // allosaurus moved 30+ feet straight toward it ...". The engine cannot tell
        // whether the charge happened, so imposing the condition would knock the target
        // down on every hit rather than on a charge.
        var rider = new AppliedCondition(
            ConditionType.Prone,
            UnmodelledRequirement: "and the allosaurus moved 30+ feet straight toward it");

        var encounter = Fight(rider, CreatureSize.Medium);

        Assert.False(ConditionRules.CanBeImposed(rider));
        Assert.Null(encounter.Attack("Slam", Target(encounter)));
        Assert.False(Target(encounter).HasCondition(ConditionType.Prone));
    }

    [Fact]
    public void AConditionTheEngineDoesNotExecuteIsNeverImposed()
    {
        // Deafened is completely modelled — a size gate, a duration, nothing else — and
        // still must not land. Nothing in the engine gives it an effect, so a Deafened
        // creature would carry a label that changes nothing in a fight.
        var rider = new AppliedCondition(
            ConditionType.Deafened,
            MaximumTargetSize: CreatureSize.Large,
            Duration: new ConditionDuration(ConditionClock.StartOfTurn, ConditionDurationOwner.Source));

        var encounter = Fight(rider, CreatureSize.Medium);

        Assert.True(rider.IsFullyModelled);
        Assert.False(ConditionRules.IsExecutable(ConditionType.Deafened));
        Assert.Null(encounter.Attack("Slam", Target(encounter)));
        Assert.False(Target(encounter).HasCondition(ConditionType.Deafened));
    }

    [Fact]
    public void ImmunityStillRefusesAnOtherwiseValidRider()
    {
        var encounter = Fight(
            new AppliedCondition(ConditionType.Prone, MaximumTargetSize: CreatureSize.Large),
            CreatureSize.Medium,
            immunities: [ConditionType.Prone]);

        Assert.Null(encounter.Attack("Slam", Target(encounter)));
        Assert.False(Target(encounter).HasCondition(ConditionType.Prone));
        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.Condition);
    }

    [Fact]
    public void AnImposedConditionIsNarrated()
    {
        var encounter = Fight(
            new AppliedCondition(ConditionType.Prone, MaximumTargetSize: CreatureSize.Large),
            CreatureSize.Medium);

        Assert.Null(encounter.Attack("Slam", Target(encounter)));

        var step = Assert.Single(encounter.Log, entry => entry.Kind == CombatStepKind.Condition);
        Assert.Equal("victim has the Prone condition.", step.Narration);
    }

    [Fact]
    public void KnockingATargetDownReallyChangesWhatItCanDo()
    {
        // The point of imposing the condition at all: Prone is not a label, it stops the
        // target moving until it spends half its Speed standing up.
        var encounter = Fight(
            new AppliedCondition(ConditionType.Prone, MaximumTargetSize: CreatureSize.Large),
            CreatureSize.Medium);

        Assert.Null(encounter.Attack("Slam", Target(encounter)));
        encounter.EndTurn();

        var victim = Target(encounter);
        Assert.Same(victim, encounter.ActiveCombatant);

        var refusal = encounter.Move(new GridPosition(4, 0));
        Assert.Equal("combatant.prone", refusal?.Code);

        Assert.Null(encounter.StandUp());
        Assert.False(victim.HasCondition(ConditionType.Prone));
    }

    [Fact]
    public void AMissImposesNothing()
    {
        var attack = new CombatAttack(
            "Slam",
            AttackKind.Melee,
            AttackBonus: -20,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("1d4"), DamageType.Bludgeoning, 2)],
            [new AppliedCondition(ConditionType.Prone)]);

        var encounter = Fight(attack, CreatureSize.Medium);

        Assert.Null(encounter.Attack("Slam", Target(encounter)));
        Assert.False(Target(encounter).HasCondition(ConditionType.Prone));
    }

    [Fact]
    public void ADurationOnTheSourceRunsOutAtTheStartOfTheSourcesNextTurn()
    {
        // "... until the start of the devil's next turn." Applied on the attacker's own
        // turn, so it has to survive the victim's whole turn and end when the attacker
        // comes round again — a full round, not until the end of the current one.
        var encounter = Fight(
            new AppliedCondition(
                ConditionType.Poisoned,
                Duration: new ConditionDuration(ConditionClock.StartOfTurn, ConditionDurationOwner.Source)),
            CreatureSize.Medium);

        var victim = Target(encounter);

        Assert.Null(encounter.Attack("Slam", victim));
        Assert.True(victim.HasCondition(ConditionType.Poisoned));

        encounter.EndTurn();
        Assert.True(victim.HasCondition(ConditionType.Poisoned));

        // The victim's own turn passes and it is still Poisoned.
        encounter.EndTurn();
        Assert.False(victim.HasCondition(ConditionType.Poisoned));
    }

    [Fact]
    public void ADurationOnTheBearerRunsOutAtTheEndOfTheBearersNextTurn()
    {
        // "... until the end of its next turn" — the creature carrying it, not the one
        // that imposed it.
        var encounter = Fight(
            new AppliedCondition(
                ConditionType.Poisoned,
                Duration: new ConditionDuration(ConditionClock.EndOfTurn, ConditionDurationOwner.Bearer)),
            CreatureSize.Medium);

        var victim = Target(encounter);

        Assert.Null(encounter.Attack("Slam", victim));
        encounter.EndTurn();

        // The victim's turn is now running, and the condition lasts through it.
        Assert.Same(victim, encounter.ActiveCombatant);
        Assert.True(victim.HasCondition(ConditionType.Poisoned));

        encounter.EndTurn();
        Assert.False(victim.HasCondition(ConditionType.Poisoned));
    }

    [Fact]
    public void TheClockTicksThroughATurnTheOwnerCannotTake()
    {
        // The trap this guards: a condition timed against a creature that is Unconscious
        // when its turn arrives. Its turn still happens — it just cannot act — so the
        // clock has to run, or the condition never ends at all.
        var encounter = Fight(
            new AppliedCondition(
                ConditionType.Poisoned,
                Duration: new ConditionDuration(ConditionClock.StartOfTurn, ConditionDurationOwner.Source)),
            CreatureSize.Medium);

        var brute = encounter.Combatants.Single(combatant => combatant.Id == "brute");
        var victim = Target(encounter);

        Assert.Null(encounter.Attack("Slam", victim));
        Assert.True(victim.HasCondition(ConditionType.Poisoned));

        // The attacker is put out of the fight before its next turn comes round.
        brute.AddCondition(ConditionType.Incapacitated);

        encounter.EndTurn();
        encounter.EndTurn();

        Assert.False(brute.CanAct);
        Assert.False(victim.HasCondition(ConditionType.Poisoned));
    }

    [Fact]
    public void ExpiryIsNarrated()
    {
        var encounter = Fight(
            new AppliedCondition(
                ConditionType.Poisoned,
                Duration: new ConditionDuration(ConditionClock.StartOfTurn, ConditionDurationOwner.Source)),
            CreatureSize.Medium);

        Assert.Null(encounter.Attack("Slam", Target(encounter)));

        Assert.Contains(
            encounter.Log,
            step => step.Narration == "victim has the Poisoned condition until the start of brute's next turn.");

        encounter.EndTurn();
        encounter.EndTurn();

        Assert.Contains(encounter.Log, step => step.Narration == "victim is no longer Poisoned.");
    }

    [Fact]
    public void ARiderWithATimerNeverShortensOneWithout()
    {
        // A wolf knocking an already-Prone creature Prone again must not hand it an
        // expiry it did not have, or the second bite stands the target up for free.
        var victim = CombatTestData.Combatant("v");
        var expiry = new ConditionExpiry("brute", ConditionClock.StartOfTurn, 1);

        Assert.True(victim.AddCondition(ConditionType.Prone));
        Assert.False(victim.AddCondition(ConditionType.Prone, "brute", expiry));

        Assert.Null(victim.ConditionState(ConditionType.Prone)?.Expiry);
    }

    [Fact]
    public void AnImposedConditionRemembersWhoImposedIt()
    {
        // Not used by anything today. It is here because the grapple needs it — a grapple
        // ends when its grappler does — and adding it later would mean reopening every
        // call site that applies a condition.
        var encounter = Fight(
            new AppliedCondition(ConditionType.Prone, MaximumTargetSize: CreatureSize.Large),
            CreatureSize.Medium);

        Assert.Null(encounter.Attack("Slam", Target(encounter)));

        Assert.Equal("brute", Target(encounter).ConditionState(ConditionType.Prone)?.SourceId);
    }

    private static Combatant Target(Encounter encounter) =>
        encounter.Combatants.Single(combatant => combatant.Id == "victim");

    private static Encounter Fight(
        AppliedCondition rider,
        CreatureSize targetSize,
        IReadOnlyList<ConditionType>? immunities = null) =>
        Fight(Knockdown(rider), targetSize, immunities);

    /// <summary>
    /// The attacker acts first and the target is adjacent, so one call to
    /// <see cref="Encounter.Attack"/> resolves the whole thing.
    /// </summary>
    private static Encounter Fight(
        CombatAttack attack,
        CreatureSize targetSize,
        IReadOnlyList<ConditionType>? immunities = null) =>
        Encounter.Start(
            new Battlefield(10, 10),
            [
                CombatTestData.Combatant(
                    "brute",
                    sideId: CombatTestData.Monsters,
                    stats: CombatTestData.Stats(initiativeBonus: 10, attacks: [attack])),
                CombatTestData.Combatant(
                    "victim",
                    sideId: CombatTestData.Heroes,
                    stats: CombatTestData.Stats(
                        maximumHitPoints: 60,
                        initiativeBonus: -10,
                        diesAtZeroHitPoints: false,
                        conditionImmunities: immunities,
                        size: targetSize),
                    x: 1),
            ],
            new SeededRandomSource(5));
}
