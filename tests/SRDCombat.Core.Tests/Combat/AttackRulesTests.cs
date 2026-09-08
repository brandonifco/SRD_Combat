using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

public class AttackRulesTests
{
    [Fact]
    public void ANatural20_HitsAndCrits_RegardlessOfArmorClass()
    {
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [CombatTestData.MeleeAttack(bonus: -5)]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, stats: CombatTestData.Stats(armorClass: 30), x: 1);

        var result = AttackRules.Resolve(
            new ScriptedRandomSource(20),
            attacker,
            attacker.Stats.Attacks[0],
            target);

        Assert.True(result.Hit);
        Assert.True(result.Critical);
    }

    [Fact]
    public void AdamantineArmor_DemotesEveryCriticalToANormalHit()
    {
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [CombatTestData.MeleeAttack(bonus: 5)]));
        var target = CombatTestData.Combatant(
            "b",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(armorClass: 12) with { CriticalHitsAgainstBecomeNormal = true },
            x: 1);

        // "any Critical Hit against you becomes a normal hit" — the natural 20 still
        // hits (that rule is about hitting), but the dice-doubling is denied.
        var result = AttackRules.Resolve(
            new ScriptedRandomSource(20),
            attacker,
            attacker.Stats.Attacks[0],
            target);

        Assert.True(result.Hit);
        Assert.False(result.Critical);
    }

    [Fact]
    public void ANatural1_MissesEvenAgainstAHopelesslyLowArmorClass()
    {
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [CombatTestData.MeleeAttack(bonus: 20)]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, stats: CombatTestData.Stats(armorClass: 1), x: 1);

        var result = AttackRules.Resolve(
            new ScriptedRandomSource(1),
            attacker,
            attacker.Stats.Attacks[0],
            target);

        Assert.False(result.Hit);
        Assert.False(result.Critical);
    }

    [Fact]
    public void AnAttackMeetingArmorClassExactly_Hits()
    {
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [CombatTestData.MeleeAttack(bonus: 3)]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, stats: CombatTestData.Stats(armorClass: 13), x: 1);

        // 10 + 3 = 13, which meets AC 13.
        var result = AttackRules.Resolve(new ScriptedRandomSource(10), attacker, attacker.Stats.Attacks[0], target);

        Assert.True(result.Hit);
        Assert.False(result.Critical);
    }

    [Fact]
    public void ADodgingTarget_ImposesDisadvantage()
    {
        var attacker = CombatTestData.Combatant("a");
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);
        target.Turn.BeginTurn(30);
        target.Turn.StartDodging();

        var circumstances = AttackRules.DescribeCircumstances(attacker, attacker.Stats.Attacks[0], target);

        Assert.True(circumstances.TargetIsDodging);
        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void ADodgingTargetThatIsIncapacitated_LosesTheBenefit()
    {
        var attacker = CombatTestData.Combatant("a");
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);
        target.Turn.BeginTurn(30);
        target.Turn.StartDodging();
        target.AddCondition(ConditionType.Incapacitated);

        var circumstances = AttackRules.DescribeCircumstances(attacker, attacker.Stats.Attacks[0], target);

        Assert.False(circumstances.TargetIsDodging);
    }

    [Fact]
    public void ADodgingTargetThatIsGrappled_LosesTheBenefit()
    {
        // The printed exception's second half: "or if your Speed is 0". Grappled is the
        // Speed-0 condition that does not also bring Incapacitated, so it is the case
        // the Incapacitated check alone would miss.
        var attacker = CombatTestData.Combatant("a");
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);
        target.Turn.BeginTurn(30);
        target.Turn.StartDodging();
        target.AddCondition(ConditionType.Grappled);

        var circumstances = AttackRules.DescribeCircumstances(attacker, attacker.Stats.Attacks[0], target);

        Assert.False(circumstances.TargetIsDodging);
        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void ADodgingTargetThatIsBlinded_DoesNotImposeDisadvantage()
    {
        // Dodge's attack-roll half is "if you can see the attacker", and a Blinded
        // dodger cannot — so the attacker keeps the plain Advantage Blinded grants
        // rather than Dodge cancelling it to a normal roll.
        var attacker = CombatTestData.Combatant("a");
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);
        target.Turn.BeginTurn(30);
        target.Turn.StartDodging();
        target.AddCondition(ConditionType.Blinded);

        var circumstances = AttackRules.DescribeCircumstances(attacker, attacker.Stats.Attacks[0], target);

        Assert.False(circumstances.TargetIsDodging);
        Assert.Equal(RollMode.Advantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void ADodgingBlindedTarget_WithABattlefieldOffered_StillGrantsNoBenefit()
    {
        // The same case, with a battlefield supplied (#672) so this actually consults
        // VisionRules.CanSee(dodger, attacker) rather than falling back to "not
        // Blinded" — proving the two readings agree rather than merely assuming it.
        var field = new Battlefield(8, 8);
        var attacker = CombatTestData.Combatant("a");
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);
        target.Turn.BeginTurn(30);
        target.Turn.StartDodging();
        target.AddCondition(ConditionType.Blinded);

        var circumstances = AttackRules.DescribeCircumstances(
            attacker, attacker.Stats.Attacks[0], target, [attacker, target], field);

        Assert.False(circumstances.TargetIsDodging);
    }

    [Fact]
    public void ADodgingTarget_WithNoLineOfSightToTheAttacker_GetsNoBenefitEither()
    {
        // Dodge's "if you can see the attacker" reading rests on line of sight, not
        // merely "not Blinded" — a wall between the dodger and the attacker denies the
        // benefit the same way Blindness does, even with open eyes.
        var field = new Battlefield(6, 6, blocked: [new(2, 0), new(2, 1), new(2, 2)]);
        var attacker = CombatTestData.Combatant("a", x: 0, y: 1);
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 4, y: 1);
        target.Turn.BeginTurn(30);
        target.Turn.StartDodging();

        var circumstances = AttackRules.DescribeCircumstances(
            attacker, attacker.Stats.Attacks[0], target, [attacker, target], field);

        Assert.False(circumstances.TargetIsDodging);
    }

    [Theory]
    // Prone gives the attacker Advantage up close and Disadvantage from further away.
    [InlineData(5, RollMode.Advantage)]
    [InlineData(10, RollMode.Disadvantage)]
    public void AProneTarget_DependsOnHowCloseTheAttackerIs(int distance, RollMode expected)
    {
        var circumstances = new AttackCircumstances(
            TargetIsDodging: false,
            TargetIsProne: true,
            TargetIsUnconscious: false,
            AttackerIsProne: false);

        Assert.Equal(expected, AttackRules.ResolveRollMode(circumstances, distance));
    }

    [Fact]
    public void AnUnconsciousTargetBeyondFiveFeet_RollsNormally()
    {
        // Worth pinning because it is genuinely counter-intuitive: Unconscious grants
        // Advantage, but Unconscious also means Prone, and Prone gives Disadvantage to
        // an attacker further than 5 feet away. They cancel.
        var circumstances = new AttackCircumstances(
            TargetIsDodging: false,
            TargetIsProne: true,
            TargetIsUnconscious: true,
            AttackerIsProne: false);

        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(circumstances, 15));
        Assert.Equal(RollMode.Advantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void AnyHitOnAnUnconsciousTargetWithinFiveFeet_IsACriticalHit()
    {
        var attacker = CombatTestData.Combatant("a");
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);
        target.AddCondition(ConditionType.Unconscious);

        // Two dice, because Unconscious also grants Advantage at this range. Neither is
        // a natural 20 — the Critical Hit comes from the condition alone.
        var result = AttackRules.Resolve(
            new ScriptedRandomSource(12, 11),
            attacker,
            attacker.Stats.Attacks[0],
            target);

        Assert.True(result.Hit);
        Assert.True(result.Critical);
    }

    [Fact]
    public void AProneAttacker_HasDisadvantage()
    {
        var circumstances = new AttackCircumstances(AttackerIsProne: true);

        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void APoisonedAttacker_HasDisadvantage()
    {
        var circumstances = new AttackCircumstances(AttackerIsPoisoned: true);

        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void PoisonedCancelsAgainstAdvantageRatherThanOverridingIt()
    {
        // Poisoned is Disadvantage like any other, so it cancels with Advantage instead
        // of winning. Worth pinning because a "poisoned creatures roll badly" shortcut
        // would look right and be wrong exactly here.
        var circumstances = new AttackCircumstances(
            TargetIsDodging: false,
            TargetIsProne: false,
            TargetIsUnconscious: true,
            AttackerIsPoisoned: true);

        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void ShootingBeyondNormalRange_HasDisadvantage()
    {
        var bow = CombatTestData.RangedAttack(normalFeet: 80, longFeet: 320);

        Assert.False(bow.IsAtLongRange(80));
        Assert.True(bow.IsAtLongRange(85));
        Assert.True(bow.IsAtLongRange(320));

        // Beyond long range the attack cannot be made at all, rather than being made
        // with Disadvantage.
        Assert.False(bow.IsAtLongRange(325));
        Assert.False(bow.CanReach(325));
    }

    [Fact]
    public void ConditionalDamage_IsOnlyDealtWhenItsConditionHolds()
    {
        // The Goblin Warrior's "plus 2 (1d4) Slashing damage if the attack roll had
        // Advantage". Treating this as unconditional silently makes the creature hit for
        // half again as much as the SRD says on every ordinary swing.
        var attack = new CombatAttack(
            "Scimitar",
            AttackKind.Melee,
            4,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [
                new AttackDamage(DiceExpression.Parse("1d6 + 2"), DamageType.Slashing, 5),
                new AttackDamage(
                    DiceExpression.Parse("1d4"),
                    DamageType.Slashing,
                    2,
                    AttackDamageCondition.AttackRollHadAdvantage),
            ]);

        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);

        // An ordinary hit: only the base component is rolled.
        var plain = AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target);
        Assert.True(plain.Hit);
        Assert.Equal(RollMode.Normal, plain.Roll.Mode);
        Assert.Single(AttackRules.RollDamage(new ScriptedRandomSource(3), attack, plain, attacker, target));

        // The same hit with Advantage: both components are rolled.
        target.AddCondition(ConditionType.Prone);
        var advantaged = AttackRules.Resolve(new ScriptedRandomSource(15, 9), attacker, attack, target);
        Assert.Equal(RollMode.Advantage, advantaged.Roll.Mode);
        Assert.Equal(2, AttackRules.RollDamage(new ScriptedRandomSource(3, 2), attack, advantaged, attacker, target).Count);
    }

    [Fact]
    public void AlternativeDamage_ReplacesTheBaseComponentInsteadOfJoiningIt()
    {
        // The Chimera's Bite (#371), simplified to one round trip: "Hit: 11 (2d6 + 4)
        // Piercing damage, or 18 (4d6 + 4) Piercing damage if the chimera had
        // Advantage on the attack roll." Unlike the goblins' "plus…if" rider above,
        // this is a replacement — the alternative's own damage stands in for the base
        // component whole, never alongside it.
        var attack = new CombatAttack(
            "Bite",
            AttackKind.Melee,
            7,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("2d6 + 4"), DamageType.Piercing, 11)])
        {
            Alternative = new AlternativeAttackDamage(
                DiceExpression.Parse("4d6 + 4"),
                DamageType.Piercing,
                18,
                AttackDamageCondition.AttackRollHadAdvantage),
        };

        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);

        // An ordinary hit: exactly the base component, never the alternative alongside it.
        var plain = AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target);
        Assert.Equal(RollMode.Normal, plain.Roll.Mode);
        var plainDamage = AttackRules.RollDamage(new ScriptedRandomSource(3, 4), attack, plain, attacker, target);
        var plainComponent = Assert.Single(plainDamage);
        Assert.Equal(11, plainComponent.Component.PrintedAverage);

        // With Advantage: the alternative replaces the base component — one
        // component rolled, not two, and it is the alternative's own dice.
        target.AddCondition(ConditionType.Prone);
        var advantaged = AttackRules.Resolve(new ScriptedRandomSource(15, 9), attacker, attack, target);
        Assert.Equal(RollMode.Advantage, advantaged.Roll.Mode);
        var advantagedDamage = AttackRules.RollDamage(new ScriptedRandomSource(3, 4, 2, 1), attack, advantaged, attacker, target);
        var advantagedComponent = Assert.Single(advantagedDamage);
        Assert.Equal(18, advantagedComponent.Component.PrintedAverage);
    }

    [Fact]
    public void AlternativeDamage_ACriticalHitDoublesTheAlternativesOwnDiceNotTheBaseComponents()
    {
        // #410: the same Chimera's Bite as above, but with a natural 20 among the
        // Advantage pair, which is always a Critical Hit regardless of AC. The
        // Critical Hit must double the ALTERNATIVE's own dice (4d6 + 4 becomes 8d6 +
        // 4) — the base component (2d6 + 4) is never rolled at all once the
        // alternative replaces it whole, so it has nothing to double.
        var attack = new CombatAttack(
            "Bite",
            AttackKind.Melee,
            7,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("2d6 + 4"), DamageType.Piercing, 11)])
        {
            Alternative = new AlternativeAttackDamage(
                DiceExpression.Parse("4d6 + 4"),
                DamageType.Piercing,
                18,
                AttackDamageCondition.AttackRollHadAdvantage),
        };

        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);
        target.AddCondition(ConditionType.Prone);

        var critical = AttackRules.Resolve(new ScriptedRandomSource(20, 9), attacker, attack, target);
        Assert.Equal(RollMode.Advantage, critical.Roll.Mode);
        Assert.True(critical.Critical);

        // Exactly eight scripted results — the alternative's 4d6 doubled to 8d6 by the
        // Critical Hit, and nothing more. If the base component's 2d6 were rolled as
        // well, or the doubling did not apply, either the scripted source would run dry
        // (ScriptedRandomSource throws) or the returned component/dice count would not
        // match what is asserted below.
        var criticalDamage = AttackRules.RollDamage(
            new ScriptedRandomSource(1, 2, 3, 4, 5, 6, 1, 2), attack, critical, attacker, target);

        var criticalComponent = Assert.Single(criticalDamage);
        Assert.Equal(18, criticalComponent.Component.PrintedAverage);
        Assert.True(criticalComponent.Result.WasCritical);
        Assert.Equal([1, 2, 3, 4, 5, 6, 1, 2], criticalComponent.Result.Dice);
    }

    [Fact]
    public void AlternativeDamage_AttackerIsBloodiedChecksTheAttackerNotTheTarget()
    {
        // A Swarm of Rats' Bites (#371): "Hit: 5 (2d4) Piercing damage, or 2 (1d4)
        // Piercing damage if the swarm is Bloodied." The condition reads the
        // attacker's own Hit Points, not the target's — halved, wounded prey does not
        // make the swarm bite softer.
        var attack = new CombatAttack(
            "Bites",
            AttackKind.Melee,
            2,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("2d4"), DamageType.Piercing, 5)])
        {
            Alternative = new AlternativeAttackDamage(
                DiceExpression.Parse("1d4"),
                DamageType.Piercing,
                2,
                AttackDamageCondition.AttackerIsBloodied),
        };

        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);

        Assert.False(attacker.IsBloodied);
        var full = AttackRules.RollDamage(
            new ScriptedRandomSource(3, 3),
            attack,
            AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target),
            attacker,
            target);
        Assert.Equal(5, Assert.Single(full).Component.PrintedAverage);

        // Wound the attacker below half its own maximum — the target is untouched.
        DamageRules.Apply(
            attacker,
            attacker.Stats.MaximumHitPoints - attacker.Stats.MaximumHitPoints / 2,
            DamageType.Bludgeoning);
        Assert.True(attacker.IsBloodied);
        Assert.False(target.IsBloodied);

        var halved = AttackRules.RollDamage(
            new ScriptedRandomSource(2),
            attack,
            AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target),
            attacker,
            target);
        Assert.Equal(2, Assert.Single(halved).Component.PrintedAverage);
    }

    [Fact]
    public void AlternativeDamage_TargetIsBloodiedChecksTheTargetNotTheAttacker()
    {
        // The Blood Hawk's Beak (#371): "Hit: 4 (1d4 + 2) Piercing damage, or 6
        // (1d8 + 2) Piercing damage if the target is Bloodied." The opposite reading
        // from the swarms above — a wounded target draws the hawk's bigger hit.
        var attack = new CombatAttack(
            "Beak",
            AttackKind.Melee,
            4,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [new AttackDamage(DiceExpression.Parse("1d4 + 2"), DamageType.Piercing, 4)])
        {
            Alternative = new AlternativeAttackDamage(
                DiceExpression.Parse("1d8 + 2"),
                DamageType.Piercing,
                6,
                AttackDamageCondition.TargetIsBloodied),
        };

        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);

        Assert.False(target.IsBloodied);
        var full = AttackRules.RollDamage(
            new ScriptedRandomSource(3),
            attack,
            AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target),
            attacker,
            target);
        Assert.Equal(4, Assert.Single(full).Component.PrintedAverage);

        DamageRules.Apply(
            target,
            target.Stats.MaximumHitPoints - target.Stats.MaximumHitPoints / 2,
            DamageType.Bludgeoning);
        Assert.True(target.IsBloodied);
        Assert.False(attacker.IsBloodied);

        var bigger = AttackRules.RollDamage(
            new ScriptedRandomSource(4),
            attack,
            AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target),
            attacker,
            target);
        Assert.Equal(6, Assert.Single(bigger).Component.PrintedAverage);
    }

    // A Swarm of Venomous Snakes' Bites (#409): "8 (1d8 + 4) Piercing damage—or 6
    // (1d4 + 4) Piercing damage if the swarm is Bloodied—plus 10 (3d6) Poison damage."
    // The Piercing base sits at index 0, the unconditional Poison "plus" at index 1, and
    // the alternative replaces only the Piercing (ReplacesComponentIndex: 0).
    private static CombatAttack VenomousSnakesBites() =>
        new(
            "Bites",
            AttackKind.Melee,
            6,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [
                new AttackDamage(DiceExpression.Parse("1d8 + 4"), DamageType.Piercing, 8),
                new AttackDamage(DiceExpression.Parse("3d6"), DamageType.Poison, 10),
            ])
        {
            Alternative = new AlternativeAttackDamage(
                DiceExpression.Parse("1d4 + 4"),
                DamageType.Piercing,
                6,
                AttackDamageCondition.AttackerIsBloodied)
            {
                ReplacesComponentIndex = 0,
            },
        };

    [Fact]
    public void EmDashAlternative_BloodiedSwarmDealsTheLowerPiercingTierPlusTheUnconditionalPoison()
    {
        // The exact scenario #409 names: a Bloodied Swarm of Venomous Snakes deals its
        // lower Piercing tier (6, not 8) AND its full Poison (10) — never 6 Piercing
        // alone, never 8+6 both Piercing tiers, never the Poison dropped. The Poison is
        // unconditional and survives the tier swap.
        var attack = VenomousSnakesBites();
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);

        // Not yet Bloodied: the base Piercing tier (8) plus the Poison (10).
        Assert.False(attacker.IsBloodied);
        var full = AttackRules.RollDamage(
            new ScriptedRandomSource(4, 5, 5, 5),
            attack,
            AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target),
            attacker,
            target);
        Assert.Collection(
            full,
            piercing =>
            {
                Assert.Equal(DamageType.Piercing, piercing.Component.Type);
                Assert.Equal(8, piercing.Component.PrintedAverage);
                Assert.Equal(8, piercing.Result.Total); // 1d8=4, +4
            },
            poison =>
            {
                Assert.Equal(DamageType.Poison, poison.Component.Type);
                Assert.Equal(10, poison.Component.PrintedAverage);
                Assert.Equal(15, poison.Result.Total); // 3d6 = 5+5+5
            });

        // Wound the swarm below half its own maximum, then bite again.
        DamageRules.Apply(
            attacker,
            attacker.Stats.MaximumHitPoints - attacker.Stats.MaximumHitPoints / 2,
            DamageType.Bludgeoning);
        Assert.True(attacker.IsBloodied);

        var bloodied = AttackRules.RollDamage(
            new ScriptedRandomSource(2, 5, 5, 5),
            attack,
            AttackRules.Resolve(new ScriptedRandomSource(15), attacker, attack, target),
            attacker,
            target);

        // Exactly two components: the LOWER Piercing tier and the SAME Poison. Two
        // components — not one (Poison dropped) and not three (both Piercing tiers).
        Assert.Collection(
            bloodied,
            piercing =>
            {
                Assert.Equal(DamageType.Piercing, piercing.Component.Type);
                Assert.Equal(6, piercing.Component.PrintedAverage); // the 1d4+4 tier, not 1d8+4
                Assert.Equal(6, piercing.Result.Total); // 1d4=2, +4
            },
            poison =>
            {
                Assert.Equal(DamageType.Poison, poison.Component.Type);
                Assert.Equal(10, poison.Component.PrintedAverage);
                Assert.Equal(15, poison.Result.Total);
            });
    }

    [Fact]
    public void EmDashAlternative_MimicHeavierBiteRequiresTheGrappleBeItsOwnAndAlwaysAddsTheAcid()
    {
        // The Mimic's Bite (#409): "7 (1d8 + 3) Piercing damage—or 12 (2d8 + 3) Piercing
        // damage if the target is Grappled by the mimic—plus 4 (1d8) Acid damage." The
        // heavier Piercing tier lands only when the target is Grappled BY THE MIMIC
        // (the attacker); a target held by someone else gets the base tier. The Acid is
        // always dealt.
        var attack = new CombatAttack(
            "Bite",
            AttackKind.Melee,
            5,
            ReachFeet: 5,
            NormalRangeFeet: null,
            LongRangeFeet: null,
            [
                new AttackDamage(DiceExpression.Parse("1d8 + 3"), DamageType.Piercing, 7),
                new AttackDamage(DiceExpression.Parse("1d8"), DamageType.Acid, 4),
            ])
        {
            Alternative = new AlternativeAttackDamage(
                DiceExpression.Parse("2d8 + 3"),
                DamageType.Piercing,
                12,
                AttackDamageCondition.TargetIsGrappledByAttacker)
            {
                ReplacesComponentIndex = 0,
            },
        };

        var mimic = CombatTestData.Combatant("mimic", stats: CombatTestData.Stats(attacks: [attack]));
        var ally = CombatTestData.Combatant("ally");
        var target = CombatTestData.Combatant("t", sideId: CombatTestData.Monsters, x: 1);

        // Not Grappled at all: base Piercing (7) plus Acid (4).
        var loose = AttackRules.RollDamage(
            new ScriptedRandomSource(4, 4),
            attack,
            AttackRules.Resolve(new ScriptedRandomSource(15), mimic, attack, target),
            mimic,
            target);
        Assert.Collection(
            loose,
            p => { Assert.Equal(DamageType.Piercing, p.Component.Type); Assert.Equal(7, p.Component.PrintedAverage); },
            a => { Assert.Equal(DamageType.Acid, a.Component.Type); Assert.Equal(4, a.Component.PrintedAverage); });

        // Grappled by an ALLY, not the mimic: still the base tier — the source matters.
        target.AddCondition(ConditionType.Grappled, ally.Id);
        var grappledByOther = AttackRules.RollDamage(
            new ScriptedRandomSource(4, 4),
            attack,
            AttackRules.Resolve(new ScriptedRandomSource(15), mimic, attack, target),
            mimic,
            target);
        Assert.Equal(7, grappledByOther[0].Component.PrintedAverage);

        // Grappled by the mimic itself: the heavier Piercing tier (12), still plus Acid.
        target.RemoveCondition(ConditionType.Grappled);
        target.AddCondition(ConditionType.Grappled, mimic.Id);
        var grappledByMimic = AttackRules.RollDamage(
            new ScriptedRandomSource(4, 4, 4),
            attack,
            AttackRules.Resolve(new ScriptedRandomSource(15), mimic, attack, target),
            mimic,
            target);
        Assert.Collection(
            grappledByMimic,
            p =>
            {
                Assert.Equal(DamageType.Piercing, p.Component.Type);
                Assert.Equal(12, p.Component.PrintedAverage);
                Assert.Equal(11, p.Result.Total); // 2d8 = 4+4, +3
            },
            a =>
            {
                Assert.Equal(DamageType.Acid, a.Component.Type);
                Assert.Equal(4, a.Component.PrintedAverage);
                Assert.Equal(4, a.Result.Total); // 1d8 = 4
            });
    }

    [Fact]
    public void EmDashAlternative_ACriticalHitDoublesBothTheAlternativeTierAndTheUnconditionalPlus()
    {
        // #410's concern applied to #409: a Critical Hit doubles the dice of EVERY
        // component rolled — both the swapped-in alternative Piercing tier (1d4 becomes
        // 2d4) and the unconditional Poison "plus" (3d6 becomes 6d6). The flat modifiers
        // (+4) are never doubled.
        var attack = VenomousSnakesBites();
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("b", sideId: CombatTestData.Monsters, x: 1);

        DamageRules.Apply(
            attacker,
            attacker.Stats.MaximumHitPoints - attacker.Stats.MaximumHitPoints / 2,
            DamageType.Bludgeoning);
        Assert.True(attacker.IsBloodied);

        var critical = AttackRules.Resolve(new ScriptedRandomSource(20), attacker, attack, target);
        Assert.True(critical.Critical);

        // 2 dice for the doubled 1d4 tier, then 6 for the doubled 3d6 Poison — eight in
        // all. If either component failed to double, the scripted source would run dry or
        // the asserted dice counts would not match.
        var rolled = AttackRules.RollDamage(
            new ScriptedRandomSource(1, 2, 1, 1, 1, 1, 1, 1), attack, critical, attacker, target);

        Assert.Collection(
            rolled,
            piercing =>
            {
                Assert.Equal(DamageType.Piercing, piercing.Component.Type);
                Assert.Equal(6, piercing.Component.PrintedAverage);
                Assert.True(piercing.Result.WasCritical);
                Assert.Equal([1, 2], piercing.Result.Dice); // 1d4 doubled to 2d4
                Assert.Equal(7, piercing.Result.Total); // (1+2) + 4
            },
            poison =>
            {
                Assert.Equal(DamageType.Poison, poison.Component.Type);
                Assert.True(poison.Result.WasCritical);
                Assert.Equal([1, 1, 1, 1, 1, 1], poison.Result.Dice); // 3d6 doubled to 6d6
                Assert.Equal(6, poison.Result.Total);
            });
    }

    #region Attack-roll Advantage circumstances (#666)

    [Fact]
    public void AdvantageCondition_TargetGrappledByAttacker_GrantsAdvantage()
    {
        // Ankheg's Bite, Bugbear Stalker's Morningstar, Bugbear Warrior's Light
        // Hammer and Mimic's Bite all print this on the attack's own header.
        var attack = CombatTestData.MeleeAttack() with
        {
            AdvantageCondition = AttackRollAdvantageCondition.TargetIsGrappledByAttacker,
        };
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("t", sideId: CombatTestData.Monsters, x: 1);

        target.AddCondition(ConditionType.Grappled, attacker.Id);

        var circumstances = AttackRules.DescribeCircumstances(attacker, attack, target);

        Assert.True(circumstances.AttacksOwnAdvantageConditionHolds);
        Assert.Equal(RollMode.Advantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void AdvantageCondition_TargetGrappledByAnAlly_GrantsNothing()
    {
        // "By the attacker" is load-bearing: a target held by an ally does not
        // satisfy this attacker's own printed circumstance.
        var attack = CombatTestData.MeleeAttack() with
        {
            AdvantageCondition = AttackRollAdvantageCondition.TargetIsGrappledByAttacker,
        };
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var ally = CombatTestData.Combatant("ally");
        var target = CombatTestData.Combatant("t", sideId: CombatTestData.Monsters, x: 1);

        target.AddCondition(ConditionType.Grappled, ally.Id);

        var circumstances = AttackRules.DescribeCircumstances(attacker, attack, target);

        Assert.False(circumstances.AttacksOwnAdvantageConditionHolds);
        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void AdvantageCondition_TargetNotGrappledAtAll_GrantsNothing()
    {
        var attack = CombatTestData.MeleeAttack() with
        {
            AdvantageCondition = AttackRollAdvantageCondition.TargetIsGrappledByAttacker,
        };
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("t", sideId: CombatTestData.Monsters, x: 1);

        var circumstances = AttackRules.DescribeCircumstances(attacker, attack, target);

        Assert.False(circumstances.AttacksOwnAdvantageConditionHolds);
        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void AdvantageCondition_IsNotRetroactiveWithinTheSameAttack()
    {
        // The Ankheg's own sequence: the first Bite on an ungrappled target rolls
        // Normal; the hit imposes the grapple; the Ankheg's NEXT Bite rolls with
        // Advantage. Nothing is stored on the attack or the roll itself — this pins
        // that the circumstance is read fresh, from live state, every time.
        var attack = CombatTestData.MeleeAttack() with
        {
            AdvantageCondition = AttackRollAdvantageCondition.TargetIsGrappledByAttacker,
        };
        var attacker = CombatTestData.Combatant("ankheg", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("t", sideId: CombatTestData.Monsters, x: 1);

        var beforeGrapple = AttackRules.DescribeCircumstances(attacker, attack, target);
        Assert.False(beforeGrapple.AttacksOwnAdvantageConditionHolds);

        // The first Bite hits and its rider grapples the target — modelled directly
        // here rather than through the rider machinery, which #390/#409 already pin.
        target.AddCondition(ConditionType.Grappled, attacker.Id);

        var afterGrapple = AttackRules.DescribeCircumstances(attacker, attack, target);
        Assert.True(afterGrapple.AttacksOwnAdvantageConditionHolds);
    }

    [Fact]
    public void AdvantageCondition_TargetOneHitPointBelowMaximum_GrantsAdvantage()
    {
        // "Doesn't have all its Hit Points" is any shortfall — one point is enough,
        // a strictly wider gate than Bloodied.
        var attack = CombatTestData.MeleeAttack() with
        {
            AdvantageCondition = AttackRollAdvantageCondition.TargetIsMissingHitPoints,
        };
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("t", sideId: CombatTestData.Monsters, x: 1);

        target.ReduceHitPoints(1);
        Assert.False(target.IsBloodied);

        var circumstances = AttackRules.DescribeCircumstances(attacker, attack, target);

        Assert.True(circumstances.AttacksOwnAdvantageConditionHolds);
        Assert.Equal(RollMode.Advantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void AdvantageCondition_TargetAtFullHitPointsWithTemporaryHitPoints_GrantsNothing()
    {
        // Temporary Hit Points are "a buffer against losing real Hit Points" (glossary
        // p. 190), not Hit Points themselves — a full-HP target carrying them still
        // has all its (real) Hit Points.
        var attack = CombatTestData.MeleeAttack() with
        {
            AdvantageCondition = AttackRollAdvantageCondition.TargetIsMissingHitPoints,
        };
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("t", sideId: CombatTestData.Monsters, x: 1);

        target.SetTemporaryHitPoints(10);
        Assert.False(target.IsMissingHitPoints);

        var circumstances = AttackRules.DescribeCircumstances(attacker, attack, target);

        Assert.False(circumstances.AttacksOwnAdvantageConditionHolds);
        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void AdvantageCondition_TargetHealedBackToFull_StopsGrantingAdvantage()
    {
        var attack = CombatTestData.MeleeAttack() with
        {
            AdvantageCondition = AttackRollAdvantageCondition.TargetIsMissingHitPoints,
        };
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("t", sideId: CombatTestData.Monsters, x: 1);

        target.ReduceHitPoints(5);
        Assert.True(AttackRules.DescribeCircumstances(attacker, attack, target).AttacksOwnAdvantageConditionHolds);

        target.RegainHitPoints(5);

        var healed = AttackRules.DescribeCircumstances(attacker, attack, target);
        Assert.False(healed.AttacksOwnAdvantageConditionHolds);
        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(healed, 5));
    }

    [Fact]
    public void AdvantageCondition_CancelsAgainstDisadvantageRatherThanOverriding()
    {
        // A missing-Hit-Points target (Advantage) attacked by a Poisoned attacker
        // (Disadvantage) resolves Normal — the two sources are combined through
        // D20Test.Combine like every other pair, never treated as if this one wins.
        var attack = CombatTestData.MeleeAttack() with
        {
            AdvantageCondition = AttackRollAdvantageCondition.TargetIsMissingHitPoints,
        };
        var attacker = CombatTestData.Combatant("a", stats: CombatTestData.Stats(attacks: [attack]));
        var target = CombatTestData.Combatant("t", sideId: CombatTestData.Monsters, x: 1);

        target.ReduceHitPoints(1);
        attacker.AddCondition(ConditionType.Poisoned);

        var circumstances = AttackRules.DescribeCircumstances(attacker, attack, target);

        Assert.True(circumstances.AttacksOwnAdvantageConditionHolds);
        Assert.True(circumstances.AttackerIsPoisoned);
        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(circumstances, 5));
    }

    [Fact]
    public void AdvantageCondition_CarriesThroughAnOpportunityAttack()
    {
        // "Every roll of that attack" (#666's spec) — the printed rule names the
        // attack roll, not the Attack action, so an Opportunity Attack made with the
        // same attack reads the same circumstance. The Advantage roll costs two d20s
        // (D20Test.Roll) rather than the Normal path's one.
        var monsterAttack = CombatTestData.MeleeAttack(bonus: 4) with
        {
            AdvantageCondition = AttackRollAdvantageCondition.TargetIsMissingHitPoints,
        };
        var hero = CombatTestData.Combatant(
            "hero",
            stats: CombatTestData.Stats(initiativeBonus: 10),
            x: 1);
        var monster = CombatTestData.Combatant(
            "monster",
            sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(attacks: [monsterAttack]));

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [hero, monster],
            new ScriptedRandomSource(
                10, 1,      // initiative: hero first
                15, 18, 4)); // the Advantage opportunity attack (two d20s, higher wins), then its damage die

        hero.ReduceHitPoints(1);
        Assert.True(hero.IsMissingHitPoints);

        Assert.Null(encounter.Move(new GridPosition(5, 0)));

        // The OpportunityAttack-kind step narrates the provocation itself; the roll's
        // own step — the one that would carry "with Advantage" — is the ordinary
        // Attack-kind step ResolveAttack records regardless of which path called it.
        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.OpportunityAttack);
        var swing = Assert.Single(encounter.Log, step => step.Kind == CombatStepKind.Attack);
        Assert.Contains("with Advantage", swing.Narration, StringComparison.Ordinal);
    }

    #endregion

    [Fact]
    public void ADualModeAttackUsedInMelee_IsNotAtLongRange()
    {
        // Nineteen SRD attacks are "Melee or Ranged". Used in melee they carry both a
        // reach and a range, and must not pick up long-range Disadvantage.
        var attack = new CombatAttack(
            "Spear",
            AttackKind.Melee,
            5,
            ReachFeet: 5,
            NormalRangeFeet: 20,
            LongRangeFeet: 60,
            [new AttackDamage(DiceExpression.Parse("1d6 + 3"), DamageType.Piercing, 6)]);

        Assert.False(attack.IsAtLongRange(5));
        Assert.True(attack.IsAtLongRange(40));
        Assert.Equal(60, attack.MaximumRangeFeet);
    }
}
