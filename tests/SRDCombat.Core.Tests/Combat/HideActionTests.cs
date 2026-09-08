using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The Hide action (#673): its two refusals, the R1 prerequisite collapse, the Stealth
/// check's three modifying modes, Tactical Mind, and the reveal that ends it.
/// </summary>
/// <remarks>
/// See <c>Encounter.Hiding.cs</c> for the R1 reading this pins: Hide's prerequisite
/// collapses to "no enemy viewer can see the hider" under this engine's sight model,
/// because Heavily Obscured and Three-Quarters Cover are both inert against a predicate
/// with no second sight tier.
/// </remarks>
public class HideActionTests
{
    [Fact]
    public void HiderBehindAWallFromEveryEnemy_SucceedsOnASeeded15Plus()
    {
        var (encounter, hider, _) = HiderAndEnemy(wallBetween: true, new ScriptedRandomSource(20, 1, 15));

        Assert.Null(encounter.Hide());

        Assert.True(hider.HasCondition(ConditionType.Invisible));
        Assert.Equal(17, hider.ConditionState(ConditionType.Invisible)!.FindDifficultyClass);
        Assert.Equal("hider", hider.ConditionState(ConditionType.Invisible)!.SourceId);
    }

    [Fact]
    public void TheSameGeometryWithOneEnemyGivenAClearLine_RefusesHideInSight()
    {
        var (encounter, hider, enemy) = HiderAndEnemy(wallBetween: false, new ScriptedRandomSource(20, 1));

        var refusal = encounter.Hide();

        Assert.Equal("hide.in_sight", refusal?.Code);
        Assert.Contains(enemy.Name, refusal!.Message, StringComparison.Ordinal);
        Assert.True(hider.Turn.HasAction, "Hide spent the Action despite being refused.");
        Assert.False(hider.HasCondition(ConditionType.Invisible));
    }

    [Fact]
    public void ABlindedEnemyWithTheClearLine_DoesNotDenyTheHide()
    {
        var (encounter, hider, enemy) = HiderAndEnemy(wallBetween: false, new ScriptedRandomSource(20, 1, 15));
        enemy.AddCondition(ConditionType.Blinded);

        Assert.Null(encounter.Hide());

        Assert.True(hider.HasCondition(ConditionType.Invisible));
    }

    [Fact]
    public void ABlindedEnemyWithBlindsightInRange_StillDeniesTheHide()
    {
        var (encounter, hider, enemy) = HiderAndEnemy(
            wallBetween: false, new ScriptedRandomSource(20, 1), enemyBlindsightFeet: 30);
        enemy.AddCondition(ConditionType.Blinded);

        // The hider and enemy stand 20 ft apart (HiderAndEnemy's default), inside the
        // enemy's printed 30-ft Blindsight.
        var refusal = encounter.Hide();

        Assert.Equal("hide.in_sight", refusal?.Code);
        Assert.False(hider.HasCondition(ConditionType.Invisible));
    }

    [Fact]
    public void BehindThreeQuartersCoverAndNothingElse_RefusesHideInSight()
    {
        // The R1 pin: two low obstacles give Three-Quarters Cover but do not block the
        // centre-to-centre line this engine's sight model reads — so the enemy still
        // sees the hider, and the printed "Heavily Obscured or Three-Quarters Cover or
        // Total Cover" disjunct buys nothing here.
        var field = new Battlefield(9, 5, lowObstacles: [new GridPosition(1, 2), new GridPosition(2, 2)]);
        var hider = CombatTestData.Combatant("hider", stats: CombatTestData.Stats(initiativeBonus: 10), x: 4, y: 2);
        var enemy = CombatTestData.Combatant(
            "enemy", sideId: CombatTestData.Monsters, stats: CombatTestData.Stats(initiativeBonus: -10), x: 0, y: 2);

        var encounter = Encounter.Start(field, [hider, enemy], new ScriptedRandomSource(20, 1));

        Assert.Equal(CoverDegree.ThreeQuarters, CoverRules.Between(field, enemy.Position, hider.Position));

        var refusal = encounter.Hide();

        Assert.Equal("hide.in_sight", refusal?.Code);
    }

    [Fact]
    public void HidingWhileAlreadyHiddenIsRefused()
    {
        var (encounter, hider, _) = HiderAndEnemy(wallBetween: true, new ScriptedRandomSource(20, 1));
        hider.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: hider.Id, FindDifficultyClass: 17));

        var refusal = encounter.Hide();

        Assert.Equal("hide.already_hidden", refusal?.Code);
    }

    [Fact]
    public void APoisonedHider_RollsAtDisadvantage()
    {
        // No enemies at all, so the prerequisite always passes and only the roll's mode
        // is under test. Rolls of 20 and 10: Advantage would keep the 20 (total 22,
        // hidden); Disadvantage keeps the 10 (total 12, seen) — proving which one fired.
        var hider = CombatTestData.Combatant("hider", stats: CombatTestData.Stats(initiativeBonus: 10));
        hider.AddCondition(ConditionType.Poisoned);
        var encounter = Encounter.Start(new Battlefield(8, 8), [hider], new ScriptedRandomSource(20, 20, 10));

        Assert.Null(encounter.Hide());

        Assert.False(hider.HasCondition(ConditionType.Invisible));
    }

    [Fact]
    public void ArmourWithStealthDisadvantage_RollsAtDisadvantageEvenUnpoisoned()
    {
        var stats = CombatTestData.Stats(initiativeBonus: 10) with { StealthDisadvantageFromArmor = true };
        var hider = CombatTestData.Combatant("hider", stats: stats);
        var encounter = Encounter.Start(new Battlefield(8, 8), [hider], new ScriptedRandomSource(20, 20, 10));

        Assert.Null(encounter.Hide());

        Assert.False(hider.HasCondition(ConditionType.Invisible));
    }

    [Fact]
    public void UnarmouredAndUnpoisoned_RollsNormally()
    {
        // The same two dice (20, 10) with neither modifier present: Normal mode consumes
        // only one die, so the script would throw if a second were rolled — the 10 is
        // never touched, and the 20 alone (total 22) succeeds.
        var hider = CombatTestData.Combatant("hider", stats: CombatTestData.Stats(initiativeBonus: 10));
        var encounter = Encounter.Start(new Battlefield(8, 8), [hider], new ScriptedRandomSource(20, 20));

        Assert.Null(encounter.Hide());

        Assert.True(hider.HasCondition(ConditionType.Invisible));
        Assert.Equal(22, hider.ConditionState(ConditionType.Invisible)!.FindDifficultyClass);
    }

    [Fact]
    public void SkillRulesBonusForIsWhatThisChecksAgainst_NotTheBareAbilityModifier()
    {
        // A doubled Expertise bonus (already folded in, per SkillRules.BonusFor's own
        // remarks) beats a DC the bare Dexterity modifier (+2) alone could not: an 8
        // total from a d20 of 8 fails at +2 (10) but succeeds at +8 (16).
        var stats = CombatTestData.Stats(initiativeBonus: 10) with
        {
            SkillBonuses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Stealth"] = 8 },
        };
        var hider = CombatTestData.Combatant("hider", stats: stats);
        var encounter = Encounter.Start(new Battlefield(8, 8), [hider], new ScriptedRandomSource(20, 8));

        Assert.Null(encounter.Hide());

        Assert.True(hider.HasCondition(ConditionType.Invisible));
        Assert.Equal(16, hider.ConditionState(ConditionType.Invisible)!.FindDifficultyClass);
    }

    [Fact]
    public void TacticalMindTurnsAFailedHideIntoASuccessAndRecordsTheBoostedFindDC()
    {
        // The Stealth check rolls 5 + 2 = 7 against DC 15 and fails; Tactical Mind's
        // 1d10 rolls 9, taking the total to 16 — a success, and the find-DC that
        // FindDifficultyClass must carry, not the original 7.
        var fighter = TacticalMindHider();
        var encounter = Encounter.Start(new Battlefield(8, 8), [fighter], new ScriptedRandomSource(20, 5, 9));

        Assert.Null(encounter.Hide());

        Assert.True(fighter.HasCondition(ConditionType.Invisible));
        Assert.Equal(16, fighter.ConditionState(ConditionType.Invisible)!.FindDifficultyClass);
        Assert.Equal(0, fighter.Features.SecondWindRemaining);
        Assert.Contains(encounter.Log, step => step.Narration.Contains("Tactical Mind", StringComparison.Ordinal));
    }

    [Fact]
    public void AStillFailedHideDoesNotSpendTheSecondWindUse()
    {
        var fighter = TacticalMindHider();
        var encounter = Encounter.Start(new Battlefield(8, 8), [fighter], new ScriptedRandomSource(20, 5, 1));

        Assert.Null(encounter.Hide());

        Assert.False(fighter.HasCondition(ConditionType.Invisible));
        Assert.Equal(1, fighter.Features.SecondWindRemaining);
    }

    // ── Cunning Action Hide: the same reading, the Bonus Action economy ─────────────

    [Fact]
    public void CunningActionHide_SharesTheSamePrerequisiteRefusal()
    {
        var field = new Battlefield(9, 5);
        var rogue = Rogue(0, 0);
        var enemy = CombatTestData.Combatant(
            "enemy", sideId: CombatTestData.Monsters, stats: CombatTestData.Stats(initiativeBonus: -10), x: 4, y: 0);
        var encounter = Encounter.Start(field, [rogue, enemy], new ScriptedRandomSource(20, 1));

        var refusal = encounter.CunningAction(CunningActionKind.Hide);

        Assert.Equal("hide.in_sight", refusal?.Code);
        Assert.True(rogue.Turn.HasBonusAction);
    }

    [Fact]
    public void CunningActionHide_SpendsTheBonusActionNotTheAction()
    {
        var field = new Battlefield(9, 5, blocked: [new GridPosition(2, 0)]);
        var rogue = Rogue(0, 0);
        var enemy = CombatTestData.Combatant(
            "enemy", sideId: CombatTestData.Monsters, stats: CombatTestData.Stats(initiativeBonus: -10), x: 4, y: 0);
        var encounter = Encounter.Start(field, [rogue, enemy], new ScriptedRandomSource(20, 1, 15));

        Assert.Null(encounter.CunningAction(CunningActionKind.Hide));

        Assert.True(rogue.HasCondition(ConditionType.Invisible));
        Assert.True(rogue.Turn.HasAction, "Cunning Action Hide must spend the Bonus Action, not the Action.");
        Assert.False(rogue.Turn.HasBonusAction);
    }

    [Fact]
    public void CunningActionHide_RefusesWhenTheBonusActionIsAlreadySpent()
    {
        var rogue = Rogue(0, 0);
        var encounter = Encounter.Start(new Battlefield(8, 8), [rogue], new ScriptedRandomSource(20));

        // Spend the Bonus Action through the printed economy (Cunning Action Dash)
        // rather than poking Turn's field directly.
        Assert.Null(encounter.CunningAction(CunningActionKind.Dash));

        var refusal = encounter.CunningAction(CunningActionKind.Hide);

        Assert.Equal("bonus_action.spent", refusal?.Code);
    }

    // ── The four ending triggers ─────────────────────────────────────────────────

    [Fact]
    public void RevealHidden_EndsAHideConferredInvisibleAndNarrates()
    {
        var target = CombatTestData.Combatant("target");
        target.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: target.Id, FindDifficultyClass: 17));
        var finder = CombatTestData.Combatant("finder", sideId: CombatTestData.Monsters, x: 1);
        var encounter = Encounter.Start(new Battlefield(8, 8), [target, finder], new ScriptedRandomSource(20, 1));

        Assert.True(encounter.RevealHidden(target, finder));

        Assert.False(target.HasCondition(ConditionType.Invisible));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("finder", StringComparison.Ordinal)
                && step.Narration.Contains("no longer hidden", StringComparison.Ordinal));
    }

    [Fact]
    public void RevealHidden_NeverTouchesASpellsInvisible()
    {
        // A spell's Invisible carries no find-DC and has its own ending inside the
        // spell — RevealHidden must not treat it as Hide's, per ActiveCondition's own
        // FindDifficultyClass reading.
        var target = CombatTestData.Combatant("target");
        target.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: "invisibility-spell"));
        var finder = CombatTestData.Combatant("finder", sideId: CombatTestData.Monsters, x: 1);
        var encounter = Encounter.Start(new Battlefield(8, 8), [target, finder], new ScriptedRandomSource(20, 1));

        Assert.False(encounter.RevealHidden(target, finder));

        Assert.True(target.HasCondition(ConditionType.Invisible));
    }

    [Fact]
    public void AnAttackRoll_EndsTheAttackersOwnHiddenStateAfterTheRollAndSneakAttackLands()
    {
        // Codex's own review of an earlier version of this pin (2026-09): a hider with
        // no Sneak Attack could not distinguish "reveal after the roll" from "reveal
        // before it" — either ordering would still hit and still pass. A hidden Rogue
        // with Extra-Attack-shaped Multiattack (two swings from one Attack action)
        // closes that gap: the first swing must roll with Advantage (proving the
        // Invisible condition was still present *during* the roll) and land Sneak
        // Attack damage, and only *then* does the reveal fire — which the second swing,
        // rolling Normal, is the direct evidence of.
        var (encounter, hider, target) = HiddenRogueVsTarget(
            new ScriptedRandomSource(
                20, 1, // initiative: hider, target
                18, 3, // swing 1: Advantage attack roll (natural 18 kept)
                4, // swing 1: 1d6 base damage
                3, 3, 3, // swing 1: 3d6 Sneak Attack damage
                10, // swing 2: Normal attack roll — a second d20 here would mean it
                    // never stopped being Advantage, and the script would throw
                4)); // swing 2: 1d6 base damage, no Sneak Attack (already used this turn)

        Assert.Null(encounter.Attack("Sword", target));
        Assert.Null(encounter.Attack("Sword", target));

        var swings = encounter.Log.Where(step => step.Kind == CombatStepKind.Attack).ToArray();
        Assert.Equal(2, swings.Length);
        Assert.Contains("with Advantage", swings[0].Narration, StringComparison.Ordinal);
        Assert.DoesNotContain("with Advantage", swings[1].Narration, StringComparison.Ordinal);
        Assert.DoesNotContain("with Disadvantage", swings[1].Narration, StringComparison.Ordinal);

        Assert.Contains(encounter.Log, step => step.Narration.Contains("Sneak Attack", StringComparison.Ordinal));

        Assert.False(hider.HasCondition(ConditionType.Invisible));
        Assert.Contains(
            encounter.Log,
            step => step.ActorId == "hider" && step.Narration.Contains("no longer hidden", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAttackRollAgainstAHiddenCreature_DoesNotEndTheTargetsOwnHiddenState()
    {
        // Being hit does not "give away your location" — only the hider's own attack
        // roll, spell cast or being found does.
        var attacker = CombatTestData.Combatant("attacker", stats: CombatTestData.Stats(initiativeBonus: 10));
        var target = CombatTestData.Combatant(
            "target", sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(initiativeBonus: -10, armorClass: 5), x: 1);
        target.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: target.Id, FindDifficultyClass: 17));

        // Initiative (2), then the Disadvantage attack roll (2 d20s) — a natural 1 among
        // them forces an automatic miss, so no damage die is needed either way: whether
        // it hits or misses, the target's own Invisible must survive being attacked.
        var encounter = Encounter.Start(
            new Battlefield(8, 8), [attacker, target], new ScriptedRandomSource(20, 1, 1, 20));

        Assert.Null(encounter.Attack("Sword", target));

        Assert.True(target.HasCondition(ConditionType.Invisible));
    }

    [Fact]
    public void ASomaticOnlyCast_KeepsTheCasterHidden()
    {
        var (encounter, caster, target) = HiddenCaster(somaticOnly: true, new ScriptedRandomSource(20, 1, 4));

        Assert.Null(encounter.CastSpell("spell.test-cure", target));

        Assert.True(caster.HasCondition(ConditionType.Invisible));
    }

    [Fact]
    public void AVerbalCast_EndsTheCastersHiddenState()
    {
        var (encounter, caster, target) = HiddenCaster(somaticOnly: false, new ScriptedRandomSource(20, 1, 4));

        Assert.Null(encounter.CastSpell("spell.test-cure", target));

        Assert.False(caster.HasCondition(ConditionType.Invisible));
        Assert.Contains(
            encounter.Log,
            step => step.ActorId == "caster" && step.Narration.Contains("no longer hidden", StringComparison.Ordinal));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A hider at (0,0) and one enemy 20 ft away at (4,0), optionally behind a wall at
    /// (2,0) — the geometry Test 1 (#673) needs for every one of its four sub-cases.
    /// </summary>
    private static (Encounter Encounter, Combatant Hider, Combatant Enemy) HiderAndEnemy(
        bool wallBetween,
        IRandomSource random,
        int? enemyBlindsightFeet = null)
    {
        var field = wallBetween
            ? new Battlefield(9, 5, blocked: [new GridPosition(2, 0)])
            : new Battlefield(9, 5);

        var hider = CombatTestData.Combatant(
            "hider", stats: CombatTestData.Stats(initiativeBonus: 10), x: 0, y: 0);

        var enemyStats = CombatTestData.Stats(initiativeBonus: -10) with { BlindsightFeet = enemyBlindsightFeet };
        var enemy = CombatTestData.Combatant("enemy", sideId: CombatTestData.Monsters, stats: enemyStats, x: 4, y: 0);

        var encounter = Encounter.Start(field, [hider, enemy], random);

        return (encounter, hider, enemy);
    }

    private static Combatant TacticalMindHider()
    {
        var stats = CombatTestData.Stats(initiativeBonus: 10) with
        {
            Character = new CombatantFeatures(
                [ClassFeature.TacticalMind, ClassFeature.SecondWind],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 1,
                ActionSurgeUses: 0,
                Level: 5),
        };

        return new Combatant("fighter", "fighter", CombatTestData.Heroes, stats, new GridPosition(0, 0));
    }

    private static Combatant Rogue(int x, int y)
    {
        var stats = CombatTestData.Stats(initiativeBonus: 10) with
        {
            Character = new CombatantFeatures(
                [ClassFeature.CunningAction],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 3),
        };

        return new Combatant("rogue", "rogue", CombatTestData.Heroes, stats, new GridPosition(x, y));
    }

    /// <summary>
    /// A hider already Hide-hidden (Invisible, find-DC 17), adjacent to a monster
    /// target with no Blindsight/Truesight, so its attack rolls with Advantage from
    /// <see cref="AttackCircumstances.AttackerIsUnseenByTarget"/> — a Rogue with Sneak
    /// Attack and Extra-Attack-shaped Multiattack (two swings per Attack action), so
    /// the reveal-after-the-roll ordering and Sneak Attack's composition with it are
    /// both provable rather than merely plausible.
    /// </summary>
    private static (Encounter Encounter, Combatant Hider, Combatant Target) HiddenRogueVsTarget(IRandomSource random)
    {
        var hiderStats = CombatTestData.Stats(
            initiativeBonus: 10, attacks: [CombatTestData.MeleeAttack(name: "Sword", bonus: 10, damage: "1d6")]) with
        {
            Character = new CombatantFeatures(
                [ClassFeature.SneakAttack],
                AttacksPerAction: 2,
                SneakAttackDamage: DiceExpression.Parse("3d6"),
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 5),
        };

        var hider = new Combatant("hider", "hider", CombatTestData.Heroes, hiderStats, new GridPosition(0, 0));
        hider.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: hider.Id, FindDifficultyClass: 17));

        var target = CombatTestData.Combatant(
            "target", sideId: CombatTestData.Monsters,
            stats: CombatTestData.Stats(armorClass: 5, maximumHitPoints: 60, initiativeBonus: -10), x: 1);

        var encounter = Encounter.Start(new Battlefield(8, 8), [hider, target], random);

        return (encounter, hider, target);
    }

    /// <summary>
    /// A hidden caster and a wounded ally to heal — a non-attack spell shape, so the
    /// Verbal-cast reveal trigger is tested in isolation from the attack-roll trigger
    /// rather than conflated with it (a spell that is both would have its own Advantage
    /// stripped by the Verbal reveal firing before its own roll, an interaction #673's
    /// acceptance spec does not ask this pin to untangle).
    /// </summary>
    private static (Encounter Encounter, Combatant Caster, Combatant Target) HiddenCaster(bool somaticOnly, IRandomSource random)
    {
        var cure = new SpellDefinition
        {
            Id = "spell.test-cure",
            Name = "Test Cure",
            Level = 1,
            School = MagicSchool.Abjuration,
            Classes = ["class.cleric"],
            CastingTime = SpellCastingTime.Action,
            CastingTimeText = "1 Action",
            RangeText = "Touch",
            Components = somaticOnly ? SpellComponents.Somatic : SpellComponents.Verbal | SpellComponents.Somatic,
            DurationText = "Instantaneous",
            Text = "Restores a small number of hit points.",
            Mechanics = EntryMechanics.Healing,
            SourcePage = 0,
            Heal = new SpellHeal(DiceExpression.Parse("1d8"), AddsSpellcastingModifier: false),
        };

        var casterStats = CombatTestData.Stats(initiativeBonus: 10, attacks: []) with
        {
            Character = new CombatantFeatures(
                [],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 0,
                ActionSurgeUses: 0,
                Level: 5,
                Spells: [cure],
                SpellSlots: new Dictionary<int, int> { [1] = 4 },
                SpellcastingAbility: Ability.Wisdom),
        };

        var caster = new Combatant("caster", "caster", CombatTestData.Heroes, casterStats, new GridPosition(0, 0));
        caster.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: caster.Id, FindDifficultyClass: 17));

        var target = new Combatant(
            "target", "target", CombatTestData.Heroes,
            CombatTestData.Stats(maximumHitPoints: 20, initiativeBonus: -10, diesAtZeroHitPoints: false),
            new GridPosition(0, 1),
            new CombatantCarryOver(CurrentHitPoints: 5));

        var encounter = Encounter.Start(new Battlefield(8, 8), [caster, target], random);

        return (encounter, caster, target);
    }
}
