using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// Blinded, Charmed, Frightened, Paralyzed and Stunned — the five conditions that joined
/// <c>ConditionRules.Executable</c> together.
/// </summary>
/// <remarks>
/// The rules text these pin, verbatim from the glossary, because several are easy to get
/// wrong from memory:
/// <list type="bullet">
/// <item>
/// Stunned is Incapacitated, auto-failed Strength and Dexterity saves, and Advantage
/// against — and <em>nothing else</em>. No Speed 0 and no automatic Critical Hits;
/// memory adds both, the glossary has neither.
/// </item>
/// <item>
/// Paralyzed prints the same clause Unconscious does: "Any attack roll that hits you is
/// a Critical Hit if the attacker is within 5 feet of you."
/// </item>
/// <item>
/// Charmed is "You can't attack the charmer or target the charmer with damaging
/// abilities or magical effects" — attacking anyone else is unhampered.
/// </item>
/// <item>
/// Frightened is Disadvantage on ability checks and attack rolls "while the source of
/// fear is within line of sight" — consulting <c>VisionRules.CanSee</c> since #672,
/// with no battlefield offered here so the source reads as in sight, the fixtures'
/// existing behaviour — and "You can't willingly move closer to the source of fear."
/// </item>
/// </list>
/// </remarks>
public class ExecutedConditionTests
{
    [Fact]
    public void BlindedHampersItsAttacksAndHelpsAttacksAgainstIt()
    {
        var attacker = CombatTestData.Combatant("attacker");
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 1);

        attacker.AddCondition(ConditionType.Blinded);

        var outgoing = AttackRules.DescribeCircumstances(attacker, attacker.Stats.Attacks[0], target);
        Assert.True(outgoing.AttackerIsBlinded);
        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(outgoing, 5));

        var incoming = AttackRules.DescribeCircumstances(target, target.Stats.Attacks[0], attacker);
        Assert.True(incoming.TargetIsBlinded);
        Assert.Equal(RollMode.Advantage, AttackRules.ResolveRollMode(incoming, 5));
    }

    [Fact]
    public void StunnedBringsIncapacitatedAndTakesItBackOut()
    {
        var creature = CombatTestData.Combatant();

        creature.AddCondition(ConditionType.Stunned);
        Assert.True(creature.HasCondition(ConditionType.Incapacitated));
        Assert.False(creature.CanAct);

        creature.RemoveCondition(ConditionType.Stunned);
        Assert.False(creature.HasCondition(ConditionType.Incapacitated));
        Assert.True(creature.CanAct);
    }

    [Fact]
    public void StunnedGivesAdvantageAgainstButNoAutomaticCritical()
    {
        // A 15 hits AC 13 from 5 feet away and is still not a Critical Hit — Stunned
        // prints no such clause. Advantage consumes both scripted dice.
        var attacker = CombatTestData.Combatant("attacker");
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 1);

        target.AddCondition(ConditionType.Stunned);

        var roll = AttackRules.Resolve(
            new ScriptedRandomSource(15, 15, 4),
            attacker,
            attacker.Stats.Attacks[0],
            target);

        Assert.Equal(RollMode.Advantage, roll.Roll.Mode);
        Assert.True(roll.Hit);
        Assert.False(roll.Critical);
    }

    [Fact]
    public void AHitOnAParalyzedCreatureWithinFiveFeetIsCritical()
    {
        var attacker = CombatTestData.Combatant("attacker");
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 1);

        target.AddCondition(ConditionType.Paralyzed);

        var roll = AttackRules.Resolve(
            new ScriptedRandomSource(15, 15, 4),
            attacker,
            attacker.Stats.Attacks[0],
            target);

        Assert.True(roll.Hit);
        Assert.True(roll.Critical);
    }

    [Fact]
    public void AHitOnAParalyzedCreatureFromRangeIsNotCritical()
    {
        // The same clause Unconscious carries: the automatic Critical Hit needs the
        // attacker within 5 feet. From 30 feet the hit stands and the crit does not.
        var attacker = CombatTestData.Combatant(
            "attacker",
            stats: CombatTestData.Stats(attacks: [CombatTestData.RangedAttack()]));
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 6);

        target.AddCondition(ConditionType.Paralyzed);

        var roll = AttackRules.Resolve(
            new ScriptedRandomSource(15, 15, 4),
            attacker,
            attacker.Stats.Attacks[0],
            target);

        Assert.True(roll.Hit);
        Assert.False(roll.Critical);
    }

    [Theory]
    [InlineData(ConditionType.Paralyzed)]
    [InlineData(ConditionType.Stunned)]
    [InlineData(ConditionType.Unconscious)]
    public void StrengthAndDexteritySavesAutoFailAndWisdomDoesNot(ConditionType condition)
    {
        var creature = CombatTestData.Combatant();
        creature.AddCondition(condition);

        Assert.Equal(condition, ConditionRules.AutoFailingSaveCondition(creature, Ability.Strength));
        Assert.Equal(condition, ConditionRules.AutoFailingSaveCondition(creature, Ability.Dexterity));
        Assert.Null(ConditionRules.AutoFailingSaveCondition(creature, Ability.Wisdom));
    }

    [Fact]
    public void OtherConditionsDoNotAutoFailSaves()
    {
        var creature = CombatTestData.Combatant();
        creature.AddCondition(ConditionType.Poisoned);
        creature.AddCondition(ConditionType.Restrained);

        Assert.Null(ConditionRules.AutoFailingSaveCondition(creature, Ability.Dexterity));
    }

    [Fact]
    public void AStunnedCreatureAutoFailsASaveWithoutConsumingADie()
    {
        // Initiative for two, then the 1d4 damage — and nothing else. The scripted die
        // throws on any extra roll, so this passing proves the save rolled no d20.
        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 2),
            Roarer(StrengthSave()),
            Hero("victim", x: 1));
        var victim = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        victim.AddCondition(ConditionType.Stunned);

        Assert.Null(encounter.UseEntry("Roar", victim));

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains(
                "victim automatically fails the Strength saving throw (Stunned)",
                StringComparison.Ordinal));
        Assert.True(victim.CurrentHitPoints < victim.Stats.MaximumHitPoints);
    }

    [Fact]
    public void AnUnconsciousCreatureAutoFailsADexteritySave()
    {
        // The gap this closed: before Paralyzed and Stunned joined the allowlist, an
        // Unconscious creature rolled its Dexterity save like anyone else, though its
        // glossary entry prints the same auto-fail clause.
        var encounter = Fight(
            new ScriptedRandomSource(20, 1, 2),
            Roarer(DexteritySave()),
            Hero("victim", x: 1));
        var victim = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        victim.AddCondition(ConditionType.Unconscious);

        Assert.Null(encounter.UseEntry("Roar", victim));

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains(
                "automatically fails the Dexterity saving throw (Unconscious)",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ACharmedCreatureCannotAttackItsCharmerAndAnyoneElseFreely()
    {
        var encounter = CharmFight(new ScriptedRandomSource(20, 5, 1, 10, 4));
        var (victim, charmer, bystander) = CharmParties(encounter);

        victim.AddCondition(ConditionType.Charmed, charmer.Id);

        Assert.Same(victim, encounter.ActiveCombatant);
        Assert.Equal("attack.charmed", encounter.Attack("Sword", charmer)?.Code);

        // "against any target other than the charmer" is nowhere in the clause — the
        // attack on the bystander is an ordinary swing, refused by nothing.
        Assert.Null(encounter.Attack("Sword", bystander));
    }

    [Fact]
    public void ACharmedCreatureMakesNoOpportunityAttackAgainstTheCharmer()
    {
        // The charmer starts adjacent and walks out of the charmed creature's reach.
        // "You can't attack the charmer" names the attack, not the action, so no
        // Reaction is spent and no swing happens — the script carries no dice for one
        // and would throw.
        var encounter = CharmFight(new ScriptedRandomSource(1, 20, 5), bruteX: 1, bystanderX: 0, bystanderY: 5);
        var (victim, charmer, _) = CharmParties(encounter);

        victim.AddCondition(ConditionType.Charmed, charmer.Id);

        Assert.Same(charmer, encounter.ActiveCombatant);
        Assert.Null(encounter.Move(new GridPosition(6, 0)));

        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.OpportunityAttack);
        Assert.True(victim.Turn.HasReaction);
    }

    [Fact]
    public void ACharmedCreatureCannotCatchTheCharmerInADamagingArea()
    {
        // A 15-foot Cone aimed past the charmer covers them, and the entry is refused
        // before the action or the use is spent.
        var encounter = Fight(
            new ScriptedRandomSource(20, 1),
            Roarer(ConeSave()),
            Hero("charmer", x: 2));
        var roarer = encounter.Combatants.Single(combatant => combatant.Id == "roarer");
        var charmer = encounter.Combatants.Single(combatant => combatant.Id == "charmer");

        roarer.AddCondition(ConditionType.Charmed, charmer.Id);

        var refusal = encounter.UseEntry("Roar", new GridPosition(3, 0), charmer);

        Assert.Equal("entry.charmed", refusal?.Code);
        Assert.True(roarer.Turn.HasAction);
    }

    [Fact]
    public void AFrightenedCreatureAttacksWithDisadvantage()
    {
        var attacker = CombatTestData.Combatant("attacker");
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 1);

        attacker.AddCondition(ConditionType.Frightened, target.Id);

        var circumstances = AttackRules.DescribeCircumstances(attacker, attacker.Stats.Attacks[0], target);

        Assert.True(circumstances.AttackerIsFrightened);
        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(circumstances, 5));
    }

    // ── A1 (#672): Frightened's Disadvantage now switches off out of line of sight ──

    [Fact]
    public void AFrightenedCreatureThatCannotSeeItsSourceAttacksNormally()
    {
        // The same wall shape VisionRulesTests.AWallBlocksTheLine uses: a column at
        // x=2 between the attacker at (0,1) and the source at (4,1).
        var field = new Battlefield(6, 6, blocked: [new(2, 0), new(2, 1), new(2, 2)]);
        var attacker = CombatTestData.Combatant("attacker", x: 0, y: 1);
        var source = CombatTestData.Combatant("source", sideId: CombatTestData.Monsters, x: 4, y: 1);
        attacker.AddCondition(ConditionType.Frightened, source.Id);

        var circumstances = AttackRules.DescribeCircumstances(
            attacker, attacker.Stats.Attacks[0], source, [attacker, source], field);

        Assert.False(circumstances.AttackerIsFrightened);
        Assert.Equal(RollMode.Normal, AttackRules.ResolveRollMode(circumstances, 20));
    }

    [Fact]
    public void TheSameFrightenedCreatureAttacksWithDisadvantageOnceTheWallIsGone()
    {
        // Identical to the wall test above but on an open field: isolates the wall,
        // not the field or the positions, as what made the difference.
        var field = new Battlefield(6, 6);
        var attacker = CombatTestData.Combatant("attacker", x: 0, y: 1);
        var source = CombatTestData.Combatant("source", sideId: CombatTestData.Monsters, x: 4, y: 1);
        attacker.AddCondition(ConditionType.Frightened, source.Id);

        var circumstances = AttackRules.DescribeCircumstances(
            attacker, attacker.Stats.Attacks[0], source, [attacker, source], field);

        Assert.True(circumstances.AttackerIsFrightened);
        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(circumstances, 20));
    }

    [Fact]
    public void ABlindedFrightenedAttackerHasNoSourceInSightButIsStillAtDisadvantageFromBlindness()
    {
        // Blinded closes the attacker's own eyes (VisionRules.HasOpenEyes), so it has
        // no source in sight either — but the roll is still at Disadvantage, from
        // Blinded alone, not from Frightened. The two must not stack into something
        // print does not describe (Advantage and Disadvantage never stack anyway, but
        // AttackerIsFrightened itself must read false, not merely "moot").
        var field = new Battlefield(6, 6);
        var attacker = CombatTestData.Combatant("attacker", x: 0, y: 1);
        var source = CombatTestData.Combatant("source", sideId: CombatTestData.Monsters, x: 4, y: 1);
        attacker.AddCondition(ConditionType.Frightened, source.Id);
        attacker.AddCondition(ConditionType.Blinded);

        var circumstances = AttackRules.DescribeCircumstances(
            attacker, attacker.Stats.Attacks[0], source, [attacker, source], field);

        Assert.False(circumstances.AttackerIsFrightened);
        Assert.True(circumstances.AttackerIsBlinded);
        Assert.Equal(RollMode.Disadvantage, AttackRules.ResolveRollMode(circumstances, 20));
    }

    [Fact]
    public void AFrightenedConditionWithNoRecordedSourceReadsAsInSight()
    {
        // No SourceId at all — not even an id that fails to resolve. The hampering
        // default applies regardless of the battlefield offered.
        var field = new Battlefield(6, 6, blocked: [new(2, 0), new(2, 1), new(2, 2)]);
        var attacker = CombatTestData.Combatant("attacker", x: 0, y: 1);
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 4, y: 1);
        attacker.AddCondition(ConditionType.Frightened);

        var circumstances = AttackRules.DescribeCircumstances(
            attacker, attacker.Stats.Attacks[0], target, [attacker, target], field);

        Assert.True(circumstances.AttackerIsFrightened);
    }

    [Fact]
    public void AFrightenedCreatureStillSeesItsDeadSource()
    {
        // "Dead or alive" survives #672: the predicate qualifies the viewer, not the
        // target, and a corpse is seen like any other occupied square.
        var field = new Battlefield(6, 6);
        var attacker = CombatTestData.Combatant("attacker", x: 0, y: 1);
        var source = CombatTestData.Combatant("source", sideId: CombatTestData.Monsters, x: 4, y: 1);
        DamageRules.Apply(source, 1_000, DamageType.Slashing);
        Assert.True(source.IsDead);
        attacker.AddCondition(ConditionType.Frightened, source.Id);

        var circumstances = AttackRules.DescribeCircumstances(
            attacker, attacker.Stats.Attacks[0], source, [attacker, source], field);

        Assert.True(circumstances.AttackerIsFrightened);
    }

    // ── #681: the Nalfeshnee's sight-keyed ending (a duration shape, not the
    // Disadvantage gate above) ──

    [Fact]
    public void AFrightenedWithTheNalfeshneesShapeEndsAtTurnEndOnceTheSourceIsOutOfSight()
    {
        // The wall shape the A1 pair above uses: a single blocked square at (2,0)
        // between the victim at (0,0) and the source (brute) at (5,0).
        var encounter = CharmFight(new ScriptedRandomSource(20, 5, 1), blocked: [new GridPosition(2, 0)]);
        var (victim, brute, _) = CharmParties(encounter);

        var duration = ConditionDuration.ForMinutesUntilDamageOrSourceOutOfSight(1);
        victim.AddCondition(new ActiveCondition(
            ConditionType.Frightened,
            brute.Id,
            ConditionRules.ExpiryFor(duration, brute, victim),
            EndsEarlyOnDamage: duration.EndsEarlyOnDamage,
            EndsWhenBearerCannotSeeSource: duration.EndsWhenBearerCannotSeeSource));

        Assert.Same(victim, encounter.ActiveCombatant);

        // The victim's own turn ends with the wall standing — the source is out of
        // its line of sight — so the printed clause ends the condition right here,
        // long before the 1-minute cap or a hit would.
        encounter.EndTurn();

        Assert.False(victim.HasCondition(ConditionType.Frightened));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("out of line of sight", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSameShapeSurvivesTurnEndWhileTheSourceIsStillInSight()
    {
        // Identical to the test above but on an open field: isolates the wall, not the
        // field or the positions, as what made the difference — "not before" the
        // condition the reading names, the same pairing A1's wall tests use.
        var encounter = CharmFight(new ScriptedRandomSource(20, 5, 1));
        var (victim, brute, _) = CharmParties(encounter);

        var duration = ConditionDuration.ForMinutesUntilDamageOrSourceOutOfSight(1);
        victim.AddCondition(new ActiveCondition(
            ConditionType.Frightened,
            brute.Id,
            ConditionRules.ExpiryFor(duration, brute, victim),
            EndsEarlyOnDamage: duration.EndsEarlyOnDamage,
            EndsWhenBearerCannotSeeSource: duration.EndsWhenBearerCannotSeeSource));

        Assert.Same(victim, encounter.ActiveCombatant);
        encounter.EndTurn();

        Assert.True(victim.HasCondition(ConditionType.Frightened));
    }

    [Fact]
    public void AnOrdinaryFrightenedWithoutTheSightKeyedFlagSurvivesTurnEndEvenBehindAWall()
    {
        // The no-op proof: every other printed Frightened (Turn Undead's own compound,
        // the eleven corpus riders #672 counted) carries a plain
        // ConditionDuration.ForMinutes-shaped expiry with EndsWhenBearerCannotSeeSource
        // false, and #681's new turn-end check must leave it alone — a wall between
        // bearer and source ends nothing for it, on this same field where the test
        // above ends the Nalfeshnee-shaped one outright.
        var encounter = CharmFight(new ScriptedRandomSource(20, 5, 1), blocked: [new GridPosition(2, 0)]);
        var (victim, brute, _) = CharmParties(encounter);

        victim.AddCondition(new ActiveCondition(
            ConditionType.Frightened,
            brute.Id,
            ConditionRules.ExpiryFor(ConditionDuration.ForMinutes(1), brute, victim)));

        Assert.Same(victim, encounter.ActiveCombatant);
        encounter.EndTurn();

        Assert.True(victim.HasCondition(ConditionType.Frightened));
    }

    [Fact]
    public void ASightKeyedConditionWithNoRecordedSourceReadsAsInSightAndSurvivesTurnEnd()
    {
        // #412 trip-wire: production never actually imposes this shape without a
        // SourceId — ImposeConditions always passes the imposer's own id, exactly
        // as T3 (#672) established for Frightened generally — but the turn-end
        // check must not misread an absent source as "out of sight" if that
        // invariant is ever broken. It reads as in sight, the same hampering
        // default FrightenedSourceInSight already takes.
        var encounter = CharmFight(new ScriptedRandomSource(20, 5, 1), blocked: [new GridPosition(2, 0)]);
        var (victim, _, _) = CharmParties(encounter);

        victim.AddCondition(new ActiveCondition(
            ConditionType.Frightened,
            SourceId: null,
            EndsEarlyOnDamage: true,
            EndsWhenBearerCannotSeeSource: true));

        Assert.Same(victim, encounter.ActiveCombatant);
        encounter.EndTurn();

        Assert.True(victim.HasCondition(ConditionType.Frightened));
    }

    [Fact]
    public void AFrightenedWithTheNalfeshneesShapeEndsOnDamageEvenWhileTheSourceIsInSight()
    {
        // Isolates EndsEarlyOnDamage from EndsWhenBearerCannotSeeSource: an open
        // field, so the source stays in sight throughout, and the only way out
        // exercised here is the hit landing — the printed clause's other early-out
        // ("until it takes damage") "still applies alongside" the sight-keyed one.
        var attacker = CombatTestData.Combatant("attacker", sideId: CombatTestData.Monsters, x: 1);
        var bearer = CombatTestData.Combatant("bearer", x: 0);
        var encounter = Encounter.Start(
            new Battlefield(6, 6),
            [attacker, bearer],
            // Initiatives (attacker first); an attack roll of 15 (+4 to hit AC 13);
            // a damage die of 4 (+2 modifier, non-zero, so the blow really "takes
            // damage").
            new ScriptedRandomSource(20, 5, 15, 4));

        var duration = ConditionDuration.ForMinutesUntilDamageOrSourceOutOfSight(1);
        bearer.AddCondition(new ActiveCondition(
            ConditionType.Frightened,
            attacker.Id,
            ConditionRules.ExpiryFor(duration, attacker, bearer),
            EndsEarlyOnDamage: duration.EndsEarlyOnDamage,
            EndsWhenBearerCannotSeeSource: duration.EndsWhenBearerCannotSeeSource));

        Assert.Same(attacker, encounter.ActiveCombatant);
        Assert.Null(encounter.Attack(attacker.Stats.Attacks[0].Name, bearer));

        Assert.False(bearer.HasCondition(ConditionType.Frightened));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("the damage breaks it", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOrdinaryFrightenedIsRefusedOverANalfeshneeShapedOneAndTheOccupantIsUntouched()
    {
        // Codex review of #681: the collision guard in Combatant.AddCondition must
        // widen to the two new flags, or an ordinary Frightened landing on top of
        // the Nalfeshnee's shape would silently merge into a condition carrying the
        // wrong SourceId under the right flags (or the right SourceId under no
        // flags at all — stored but never executed).
        var bearer = CombatTestData.Combatant("bearer");
        var nalfeshneeShaped = new ActiveCondition(
            ConditionType.Frightened,
            "nalfeshnee",
            EndsEarlyOnDamage: true,
            EndsWhenBearerCannotSeeSource: true);
        Assert.True(bearer.AddCondition(nalfeshneeShaped));

        var ordinary = new ActiveCondition(ConditionType.Frightened, "someone-else");
        Assert.False(bearer.AddCondition(ordinary));

        var occupant = bearer.ConditionState(ConditionType.Frightened)!;
        Assert.Equal("nalfeshnee", occupant.SourceId);
        Assert.True(occupant.EndsEarlyOnDamage);
        Assert.True(occupant.EndsWhenBearerCannotSeeSource);
    }

    [Fact]
    public void ANalfeshneeShapedFrightenedIsRefusedOverAnOrdinaryOneAndTheOccupantIsUntouched()
    {
        // The reverse order of the test above: the incumbent ordinary Frightened
        // must not inherit the Nalfeshnee's damage/sight endings either.
        var bearer = CombatTestData.Combatant("bearer");
        var ordinary = new ActiveCondition(ConditionType.Frightened, "someone-else");
        Assert.True(bearer.AddCondition(ordinary));

        var nalfeshneeShaped = new ActiveCondition(
            ConditionType.Frightened,
            "nalfeshnee",
            EndsEarlyOnDamage: true,
            EndsWhenBearerCannotSeeSource: true);
        Assert.False(bearer.AddCondition(nalfeshneeShaped));

        var occupant = bearer.ConditionState(ConditionType.Frightened)!;
        Assert.Equal("someone-else", occupant.SourceId);
        Assert.False(occupant.EndsEarlyOnDamage);
        Assert.False(occupant.EndsWhenBearerCannotSeeSource);
    }

    [Fact]
    public void AFrightenedCreatureCannotWillinglyMoveCloserToTheSource()
    {
        var encounter = CharmFight(new ScriptedRandomSource(20, 5, 1), bystanderX: 0, bystanderY: 5);
        var (victim, source, _) = CharmParties(encounter);

        victim.AddCondition(ConditionType.Frightened, source.Id);

        Assert.Same(victim, encounter.ActiveCombatant);

        // The source stands at (5,0); the victim at (0,0). Two squares closer is
        // refused, and the same distance away sideways is not.
        Assert.Equal("movement.frightened", encounter.Move(new GridPosition(2, 0))?.Code);
        Assert.Null(encounter.Move(new GridPosition(0, 2)));
    }

    [Fact]
    public void AFrightenedCreatureEscapesAGrappleWithDisadvantage()
    {
        // Frightened's Disadvantage on ability checks, on the one check a fight rolls.
        // Two dice are scripted for the escape and the worse one loses to DC 13.
        var encounter = CharmFight(new ScriptedRandomSource(20, 5, 1, 18, 2));
        var (victim, brute, _) = CharmParties(encounter);

        victim.AddCondition(new ActiveCondition(
            ConditionType.Grappled,
            brute.Id,
            EscapeDifficultyClass: 13));
        victim.AddCondition(ConditionType.Frightened, brute.Id);

        Assert.Same(victim, encounter.ActiveCombatant);
        Assert.Null(encounter.Escape());

        Assert.True(victim.HasCondition(ConditionType.Grappled));
    }

    [Fact]
    public void AFrightenedCreatureThatCannotSeeItsSourceEscapesNormally()
    {
        // The Escape variant of A1's wall pair: ConditionRules.AbilityCheckMode drops
        // Frightened's Disadvantage the same way DescribeCircumstances does. Only one
        // die is scripted for the escape roll — if the mode were still Disadvantage,
        // the second d20 it needs would find the scripted source exhausted and throw,
        // failing the test loudly rather than silently passing at the wrong mode.
        var encounter = CharmFight(
            new ScriptedRandomSource(20, 5, 1, 18), blocked: [new GridPosition(2, 0)]);
        var (victim, brute, _) = CharmParties(encounter);

        victim.AddCondition(new ActiveCondition(
            ConditionType.Grappled,
            brute.Id,
            EscapeDifficultyClass: 13));
        victim.AddCondition(ConditionType.Frightened, brute.Id);

        Assert.Same(victim, encounter.ActiveCombatant);
        Assert.Null(encounter.Escape());

        Assert.False(victim.HasCondition(ConditionType.Grappled));
    }

    [Fact]
    public void AParalyzedProneCreatureCannotStandUp()
    {
        var encounter = CharmFight(new ScriptedRandomSource(20, 5, 1));
        var (victim, _, _) = CharmParties(encounter);

        victim.AddCondition(ConditionType.Prone);
        victim.AddCondition(ConditionType.Paralyzed);

        Assert.Same(victim, encounter.ActiveCombatant);
        Assert.Equal("combatant.cannot_act", encounter.StandUp()?.Code);
    }

    [Fact]
    public void AnIncapacitatedImposedInItsOwnRightSurvivesWakingUp()
    {
        // A Ghast's Claw imposes Incapacitated with its own clock. Being knocked out and
        // healed must not clear it: the Incapacitated that leaves with Unconscious is
        // recognised by the source and expiry it inherited, and this one has its own.
        var creature = CombatTestData.Combatant();
        var expiry = new ConditionExpiry("ghast", ConditionClock.StartOfTurn, 2);

        creature.AddCondition(ConditionType.Incapacitated, "ghast", expiry);
        creature.AddCondition(ConditionType.Unconscious);
        creature.RemoveCondition(ConditionType.Unconscious);

        Assert.True(creature.HasCondition(ConditionType.Incapacitated));
        Assert.Equal(expiry, creature.ConditionState(ConditionType.Incapacitated)?.Expiry);
    }

    [Fact]
    public void IncapacitatedStaysWhileAnotherBringerRemains()
    {
        var creature = CombatTestData.Combatant();

        creature.AddCondition(ConditionType.Stunned);
        creature.AddCondition(ConditionType.Unconscious);

        creature.RemoveCondition(ConditionType.Unconscious);
        Assert.True(creature.HasCondition(ConditionType.Incapacitated));

        creature.RemoveCondition(ConditionType.Stunned);
        Assert.False(creature.HasCondition(ConditionType.Incapacitated));
    }

    private static SaveEffect StrengthSave() => new(
        Ability.Strength,
        15,
        null,
        [new AttackDamage(DiceExpression.Parse("1d4"), DamageType.Thunder, 2)],
        SaveSuccessOutcome.NoEffect,
        []);

    private static SaveEffect DexteritySave() => StrengthSave() with { Ability = Ability.Dexterity };

    private static SaveEffect ConeSave() => new(
        Ability.Dexterity,
        13,
        new EffectArea(AreaShape.Cone, 15),
        [new AttackDamage(DiceExpression.Parse("2d6"), DamageType.Fire, 7)],
        SaveSuccessOutcome.HalfDamage,
        []);

    /// <summary>A monster at (0,0) whose only action is the given saving-throw entry.</summary>
    private static Combatant Roarer(SaveEffect save)
    {
        var stats = CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
        {
            Entries =
            [
                new MonsterEntry("Roar", MonsterEntrySection.Action, "Roar.",
                    Mechanics: EntryMechanics.SavingThrow,
                    Save: save),
            ],
        };

        return CombatTestData.Combatant("roarer", sideId: CombatTestData.Monsters, stats: stats);
    }

    private static Combatant Hero(string id, int x) =>
        CombatTestData.Combatant(
            id,
            sideId: CombatTestData.Heroes,
            stats: CombatTestData.Stats(maximumHitPoints: 60, initiativeBonus: -10, attacks: []),
            x: x);

    private static Encounter Fight(IRandomSource random, params Combatant[] combatants) =>
        Encounter.Start(new Battlefield(12, 12), combatants, random);

    /// <summary>
    /// A hero at (0,0) and two enemies, a brute and a bystander, wherever the test
    /// needs them — initiative bonuses put them in scripted-die order. <paramref
    /// name="blocked"/> is empty by default (open field); the Frightened line-of-sight
    /// tests (#672) populate it to put a wall between the victim and its fear source.
    /// </summary>
    private static Encounter CharmFight(
        IRandomSource random,
        int bruteX = 5,
        int bystanderX = 1,
        int bystanderY = 0,
        IEnumerable<GridPosition>? blocked = null) =>
        Encounter.Start(
            new Battlefield(12, 12, blocked: blocked),
            [
                CombatTestData.Combatant("victim", sideId: CombatTestData.Heroes),
                CombatTestData.Combatant(
                    "brute",
                    sideId: CombatTestData.Monsters,
                    stats: CombatTestData.Stats(maximumHitPoints: 60),
                    x: bruteX),
                CombatTestData.Combatant(
                    "bystander",
                    sideId: CombatTestData.Monsters,
                    stats: CombatTestData.Stats(maximumHitPoints: 60, initiativeBonus: -10),
                    x: bystanderX,
                    y: bystanderY),
            ],
            random);

    private static (Combatant Victim, Combatant Brute, Combatant Bystander) CharmParties(Encounter encounter) =>
        (
            encounter.Combatants.Single(combatant => combatant.Id == "victim"),
            encounter.Combatants.Single(combatant => combatant.Id == "brute"),
            encounter.Combatants.Single(combatant => combatant.Id == "bystander")
        );
}
