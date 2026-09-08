using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Content.Tests;

/// <summary>
/// Runs real fights with real SRD stat blocks, joining Phase 0's content pipeline to
/// Phase 1's combat engine.
/// </summary>
/// <remarks>
/// The engine's own frozen transcript deliberately uses hand-authored combatants so it
/// does not churn when content is re-extracted. That leaves a gap this closes: proof
/// that the extracted bestiary actually produces usable combatants, and that the whole
/// CR 0–4 band the gauntlet will draw from can fight without falling over.
/// </remarks>
public class RealMonsterCombatTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    [Fact]
    public void AStatBlockBecomesAUsableCombatant()
    {
        var stats = CombatantStats.FromMonster(Content.MonstersById["monster.bandit"]);

        Assert.Equal(12, stats.ArmorClass);
        Assert.Equal(11, stats.MaximumHitPoints);
        Assert.Equal(30, stats.SpeedFeet);
        Assert.True(stats.DiesAtZeroHitPoints);

        // Scimitar and Light Crossbow both survive the conversion, with their ranges.
        Assert.Equal(2, stats.Attacks.Count);

        var scimitar = stats.Attacks.Single(attack => attack.Name == "Scimitar");
        Assert.Equal(5, scimitar.ReachFeet);

        var crossbow = stats.Attacks.Single(attack => attack.Name == "Light Crossbow");
        Assert.Equal(320, crossbow.LongRangeFeet);
    }

    [Fact]
    public void TwoRealSidesFightToAConclusion()
    {
        var encounter = BanditsVersusGoblins(seed: 7);

        SimpleTacticsPolicy.RunToCompletion(encounter);

        Assert.True(encounter.IsComplete);
        Assert.NotNull(encounter.WinningSide);
        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.Damage);
    }

    [Fact]
    public void TheSameSeedProducesTheSameFightWithRealContent() =>
        Assert.Equal(RunAndRender(11), RunAndRender(11));

    [Fact]
    public void EveryTierOneMonsterCanTakeATurnWithoutFalling()
    {
        // The gauntlet spends its encounter budget in the CR 0-4 band, so every creature
        // in it has to survive contact with the engine. This is a smoke test over the
        // whole band rather than a rules assertion: what it catches is a stat block whose
        // extracted shape the engine cannot cope with at all.
        var tierOne = Content.Monsters
            .Where(monster => monster.ChallengeRating <= 4m)
            .OrderBy(monster => monster.Id, StringComparer.Ordinal)
            .ToList();

        Assert.True(tierOne.Count >= 150);

        var failures = new List<string>();

        foreach (var monster in tierOne)
        {
            try
            {
                var encounter = Encounter.Start(
                    new Battlefield(14, 14),
                    [
                        Spawn(monster, "subject", "left", new GridPosition(1, 7)),
                        Spawn(Content.MonstersById["monster.bandit"], "sparring-partner", "right", new GridPosition(11, 7)),
                    ],
                    new SeededRandomSource(monster.Id.Length + 3));

                // A handful of rounds is enough to reach movement, attacks and death.
                for (var turn = 0; turn < 24 && !encounter.IsComplete; turn++)
                {
                    SimpleTacticsPolicy.TakeTurn(encounter);
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{monster.Id}: {exception.GetType().Name} — {exception.Message}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void TheRealSwarmOfVenomousSnakesFieldsItsEmDashTierAndUnconditionalPoison()
    {
        // #409: the em-dash "8 (1d8 + 4) Piercing damage—or 6 (1d4 + 4) Piercing damage
        // if the swarm is Bloodied—plus 10 (3d6) Poison damage" chain, end to end from
        // the real stat block. The whole route matters: the alternative and the
        // unconditional Poison have to survive extraction and reach the combatant, and
        // the newly pool-eligible swarm (CR 2, re-admitted to the theme map) has to fight
        // to a conclusion without stalling or throwing.
        var swarm = CombatantStats.FromMonster(Content.MonstersById["monster.swarm-of-venomous-snakes"]);
        var bites = swarm.Attacks.Single(attack => attack.Name == "Bites");

        // Two unconditional components — the Piercing base and the always-on Poison —
        // plus the Bloodied alternative that replaces only the Piercing (index 0).
        Assert.Collection(
            bites.Damage,
            piercing => Assert.Equal(DamageType.Piercing, piercing.Type),
            poison => Assert.Equal(DamageType.Poison, poison.Type));
        Assert.NotNull(bites.Alternative);
        Assert.Equal(0, bites.Alternative!.ReplacesComponentIndex);
        Assert.Equal(AttackDamageCondition.AttackerIsBloodied, bites.Alternative.Condition);

        // A single bite deals its Piercing AND its unconditional Poison — the component
        // #371 dropped, now dealt. Driven deterministically: the swarm wins initiative
        // (rolls 20 to the bandit's 1), then lands a scripted hit whose base Piercing
        // (1d8+4) and Poison (3d6) each render their own Damage step.
        var scripted = Encounter.Start(
            new Battlefield(12, 12),
            [
                Spawn(Content.MonstersById["monster.swarm-of-venomous-snakes"], "swarm", "vermin", new GridPosition(0, 5)),
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "bandits", new GridPosition(1, 5)),
            ],
            new ScriptedRandomSource(20, 1, 15, 4, 5, 5, 5));

        var bandit = scripted.Combatants.Single(combatant => combatant.Id == "bandit");
        Assert.Null(scripted.Attack("Bites", bandit));

        Assert.Contains(
            scripted.Log,
            step => step.Kind == CombatStepKind.Damage
                && step.Narration.Contains("Piercing damage", StringComparison.Ordinal));
        Assert.Contains(
            scripted.Log,
            step => step.Kind == CombatStepKind.Damage
                && step.Narration.Contains("Poison damage", StringComparison.Ordinal));

        // The severe-risk pin: a whole seeded fight fielding the swarm resolves — no
        // stall, no exception — to a decided conclusion.
        var seeded = Encounter.Start(
            new Battlefield(14, 14),
            [
                Spawn(Content.MonstersById["monster.swarm-of-venomous-snakes"], "swarm", "vermin", new GridPosition(1, 7)),
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "bandits", new GridPosition(11, 7)),
            ],
            new SeededRandomSource(409));

        SimpleTacticsPolicy.RunToCompletion(seeded);
        Assert.True(seeded.IsComplete);
        Assert.NotNull(seeded.WinningSide);
    }

    [Fact]
    public void RealMultiattacksGrantRealExtraSwings()
    {
        // "The bandit makes two attacks, using Scimitar and Pistol in any combination."
        var captain = CombatantStats.FromMonster(Content.MonstersById["monster.bandit-captain"]);

        Assert.Equal(2, captain.AttacksPerAction);
        Assert.True(captain.AllowsInMultiattack("Scimitar"));
        Assert.True(captain.AllowsInMultiattack("Pistol"));

        // "The armor makes two Slam attacks" names one attack to repeat.
        var armor = CombatantStats.FromMonster(Content.MonstersById["monster.animated-armor"]);
        Assert.Equal(2, armor.AttacksPerAction);
        Assert.Equal(["Slam"], armor.Multiattack?.AttackNames);
    }

    [Fact]
    public void AMultiattackSpanningTwoClausesCountsBoth()
    {
        // "The devil makes one Beard attack and one Infernal Glaive attack." Reading only
        // the first clause gave one attack instead of two, and dropped the Glaive.
        var devil = CombatantStats.FromMonster(Content.MonstersById["monster.bearded-devil"]);

        Assert.Equal(2, devil.AttacksPerAction);
        Assert.True(devil.AllowsInMultiattack("Beard"));
        Assert.True(devil.AllowsInMultiattack("Infernal Glaive"));
    }

    [Fact]
    public void MostTierOneMultiattacksResolveToUsableSwings()
    {
        // A floor, not a target. A Multiattack naming an attack the creature has no way
        // to make is deliberately dropped rather than granting phantom swings.
        var withMultiattack = Content.Monsters
            .Where(monster => monster.ChallengeRating <= 4m)
            .Select(CombatantStats.FromMonster)
            .Count(stats => stats.Multiattack is not null);

        Assert.True(withMultiattack >= 55, $"Only {withMultiattack} tier-one Multiattacks are usable.");
    }

    [Fact]
    public void ARealWolfCarriesItsProneRiderIntoTheFight()
    {
        // "Hit: 7 (2d4 + 2) Piercing damage. If the target is a Medium or smaller
        // creature, it has the Prone condition." The whole route matters here: the rider
        // hangs off the stat block entry rather than off the attack grammar, and has to
        // survive extraction, the gate check, and conversion into a combatant.
        var wolf = CombatantStats.FromMonster(Content.MonstersById["monster.wolf"]);

        var rider = Assert.Single(wolf.Attacks.Single(attack => attack.Name == "Bite").AppliedConditions);

        Assert.Equal(ConditionType.Prone, rider.Condition);
        Assert.Equal(CreatureSize.Medium, rider.MaximumTargetSize);
    }

    [Fact]
    public void ARiderTheEngineWillNotImposeNeverReachesACombatant()
    {
        // The Phase Spider's Poisoned is a condition the engine executes, printed with
        // a duration ("for 1 hour") that is not a turn boundary — so it stays counted
        // on the stat block entry rather than travelling into a fight. The Sprite,
        // which once sat beside it as the other refusal (Charmed, completely modelled,
        // not executable), is now the mirror image: its rider rides the bow.
        var spider = Content.MonstersById["monster.phase-spider"];
        var spiderStats = CombatantStats.FromMonster(spider);

        Assert.Contains(spider.Entries, entry => entry.AppliedConditions.Count > 0);
        Assert.All(spiderStats.Attacks, attack => Assert.Empty(attack.AppliedConditions));

        var sprite = CombatantStats.FromMonster(Content.MonstersById["monster.sprite"]);
        var bow = sprite.Attacks.Single(attack => attack.Name == "Enchanting Bow");
        var charmed = Assert.Single(bow.AppliedConditions);

        Assert.Equal(ConditionType.Charmed, charmed.Condition);
        Assert.True(ConditionRules.CanBeImposed(charmed));
    }

    [Fact]
    public void ARealCentipedePoisonsAndThePoisonWearsOff()
    {
        // "Hit: 4 (1d4 + 2) Piercing damage, and the target has the Poisoned condition
        // until the start of the centipede's next turn." The whole loop against real
        // content: extracted duration, imposed with an expiry, and ended by the clock at
        // the boundary the stat block names.
        var centipede = Content.MonstersById["monster.giant-centipede"];

        var rider = Assert.Single(
            CombatantStats.FromMonster(centipede).Attacks.Single(attack => attack.Name == "Bite").AppliedConditions);

        Assert.Equal(ConditionType.Poisoned, rider.Condition);
        Assert.Equal(
            new ConditionDuration(ConditionClock.StartOfTurn, ConditionDurationOwner.Source),
            rider.Duration);

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [
                Spawn(centipede, "centipede", "vermin", new GridPosition(0, 4)),
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "bandits", new GridPosition(9, 4)),
            ],
            new SeededRandomSource(9));

        SimpleTacticsPolicy.RunToCompletion(encounter);

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Condition
                && step.Narration.Contains("has the Poisoned condition until", StringComparison.Ordinal));

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Condition
                && step.Narration.Contains("is no longer Poisoned", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRealApeThrowsItsRockOnceAndWaitsForTheRecharge()
    {
        // "Rock (Recharge 6)" sits under Actions but outside the Ape's "two Fist
        // attacks" Multiattack, so before UseEntry existed the engine had no way to
        // throw it at all — and without the usage gate it could be thrown every round.
        var ape = Content.MonstersById["monster.ape"];
        var stats = CombatantStats.FromMonster(ape);

        Assert.Contains(stats.Entries, entry => entry.Name == "Rock" && entry.Usage is not null);

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [
                Spawn(ape, "ape", "beasts", new GridPosition(0, 5)),
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "bandits", new GridPosition(4, 5)),
            ],
            new ScriptedRandomSource(20, 1, 10, 3, 4));

        var thrower = encounter.Combatants.Single(combatant => combatant.Id == "ape");
        var bandit = encounter.Combatants.Single(combatant => combatant.Id == "bandit");

        Assert.Equal("attack.not_in_multiattack", encounter.Attack("Rock", bandit)?.Code);

        Assert.Null(encounter.UseEntry("Rock", bandit));

        Assert.Contains(encounter.Log, step => step.Narration.Contains("with Rock", StringComparison.Ordinal));
        Assert.False(thrower.Uses.IsAvailable("Rock"));
        Assert.Equal("entry.not_recharged", encounter.UseEntry("Rock", bandit)?.Code);
    }

    [Fact]
    public void TheRealAnkhegSpraysAcidThroughItsPrintedLine()
    {
        // "Dexterity Saving Throw: DC 12, each creature in a 30-foot-long, 5-foot-wide
        // Line. Failure: 14 (4d6) Acid damage. Success: Half damage." — executed from
        // the stat block's own words, and gated by its (Recharge 6).
        var ankheg = Content.MonstersById["monster.ankheg"];

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [
                Spawn(ankheg, "ankheg", "vermin", new GridPosition(0, 5)),
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "bandits", new GridPosition(4, 5)),
            ],
            new ScriptedRandomSource(20, 1, 1, 1, 1, 1, 1));

        var sprayer = encounter.Combatants.Single(combatant => combatant.Id == "ankheg");
        var bandit = encounter.Combatants.Single(combatant => combatant.Id == "bandit");

        Assert.Null(encounter.UseEntry("Acid Spray", bandit));

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Entry
                && step.Narration.Contains("Acid Spray fills a 30-foot Line, catching 1 creature(s)", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("Bandit makes a Dexterity saving throw", StringComparison.Ordinal)
                && step.Narration.Contains("vs DC 12 — failure", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("Acid damage", StringComparison.Ordinal));

        Assert.False(sprayer.Uses.IsAvailable("Acid Spray"));
        Assert.Equal("entry.not_recharged", encounter.UseEntry("Acid Spray", bandit)?.Code);
    }

    [Fact]
    public void TheRealAnkhegsSecondBiteRollsWithAdvantageOnceItsGrappleLands()
    {
        // #666, end to end against the real stat block. The Ankheg's first Bite finds
        // an ungrappled target and rolls Normal; the hit's own printed rider grapples
        // the target (escape DC 13); the Ankheg's SECOND Bite — a full turn later,
        // nothing retroactive within the first — reads the live Grappled state and
        // rolls with Advantage. Driven turn by turn rather than through the tactics
        // policy so the sequence is exact rather than incidental.
        var ankheg = Content.MonstersById["monster.ankheg"];
        var bandit = Content.MonstersById["monster.bandit"];

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [
                Spawn(ankheg, "ankheg", "vermin", new GridPosition(0, 5)),
                Spawn(bandit, "bandit", "bandits", new GridPosition(1, 5)),
            ],
            new ScriptedRandomSource(
                20, 1,      // initiative: the ankheg (bonus +0) beats the bandit (bonus +1)
                15,         // first Bite: Normal mode, one d20, +5 vs AC 12 hits
                1, 1, 1,    // 2d6 Slashing (1+1+3=5) + 1d6 Acid (1) — 6 total, the 11-HP bandit survives
                10, 15,     // second Bite: Advantage, two d20s, the higher (15) is kept
                1, 1, 1));  // the same damage dice shape

        var attacker = encounter.Combatants.Single(combatant => combatant.Id == "ankheg");
        var target = encounter.Combatants.Single(combatant => combatant.Id == "bandit");

        Assert.Null(encounter.Attack("Bite", target));

        // The rider landed: the printed "If the target is a Large or smaller
        // creature, it has the Grappled condition (escape DC 13)."
        var grapple = target.ConditionState(ConditionType.Grappled);
        Assert.NotNull(grapple);
        Assert.Equal(attacker.Id, grapple!.SourceId);

        var firstSwing = Assert.Single(encounter.Log, step => step.Kind == CombatStepKind.Attack);
        Assert.DoesNotContain("with Advantage", firstSwing.Narration, StringComparison.Ordinal);

        // The bandit's own turn passes without escaping — the grapple, and therefore
        // the circumstance, is still live for the ankheg's next Bite.
        encounter.EndTurn();
        encounter.EndTurn();

        Assert.Null(encounter.Attack("Bite", target));

        var secondSwing = encounter.Log.Last(step => step.Kind == CombatStepKind.Attack);
        Assert.Contains("with Advantage", secondSwing.Narration, StringComparison.Ordinal);

        // The severe-risk pin: a whole seeded fight fielding the newly re-admitted
        // Ankheg resolves — no stall, no exception — to a decided conclusion.
        var seeded = Encounter.Start(
            new Battlefield(14, 14),
            [
                Spawn(ankheg, "ankheg", "vermin", new GridPosition(1, 7)),
                Spawn(bandit, "bandit", "bandits", new GridPosition(11, 7)),
            ],
            new SeededRandomSource(666));

        SimpleTacticsPolicy.RunToCompletion(seeded);
        Assert.True(seeded.IsComplete);
        Assert.NotNull(seeded.WinningSide);
    }

    [Fact]
    public void TheRealGladiatorShieldBashKnocksProneOnAFailedSave()
    {
        // "Strength Saving Throw: DC 15, one creature within 5 feet ... Failure: 9
        // (2d4 + 4) Bludgeoning damage. If the target is a Medium or smaller creature,
        // it has the Prone condition." — the damage and the rider both from print.
        var gladiator = Content.MonstersById["monster.gladiator"];

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [
                Spawn(gladiator, "gladiator", "pit", new GridPosition(0, 5)),
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "bandits", new GridPosition(1, 5)),
            ],
            new ScriptedRandomSource(20, 1, 1, 1, 1));

        var bandit = encounter.Combatants.Single(combatant => combatant.Id == "bandit");

        Assert.Null(encounter.UseEntry("Shield Bash", bandit));

        Assert.True(bandit.HasCondition(ConditionType.Prone));
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Condition
                && step.Narration.Contains("Bandit has the Prone condition", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRealWolvesHuntWithPackTactics()
    {
        // "The wolf has Advantage on an attack roll against a creature if at least one
        // of the wolf's allies is within 5 feet of the creature..." — the ×18 trait in
        // the tier-1 band, straight from the printed name.
        var wolf = Content.MonstersById["monster.wolf"];

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [
                Spawn(wolf, "wolf-1", "pack", new GridPosition(0, 5)),
                Spawn(wolf, "wolf-2", "pack", new GridPosition(1, 4)),
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "bandits", new GridPosition(1, 5)),
            ],
            new ScriptedRandomSource(20, 1, 1, 10, 3, 1));

        var bandit = encounter.Combatants.Single(combatant => combatant.Id == "bandit");

        Assert.Null(encounter.Attack("Bite", bandit));

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack
                && step.Narration.Contains("with Advantage", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRealGargoyleFliesOutOfReachWithoutProvoking()
    {
        // Flyby, from the printed trait name: leaving the bandit's reach provokes no
        // Opportunity Attack.
        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [
                Spawn(Content.MonstersById["monster.gargoyle"], "gargoyle", "stone", new GridPosition(1, 5)),
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "bandits", new GridPosition(0, 5)),
            ],
            new ScriptedRandomSource(20, 1));

        Assert.Null(encounter.Move(new GridPosition(5, 5)));

        Assert.DoesNotContain(encounter.Log, step => step.Kind == CombatStepKind.OpportunityAttack);
    }

    [Fact]
    public void TheRealWaterElementalWhelmGrapplesButCannotRestrain()
    {
        // Whelm's failed save imposes two printed riders. Grappled (escape DC 14) is
        // fully modelled and lands; Restrained hangs off "until the grapple ends", a
        // duration the model does not express, so it is refused rather than
        // approximated — the two-questions rule exercised on one entry.
        var elemental = Content.MonstersById["monster.water-elemental"];

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [
                Spawn(elemental, "elemental", "elementals", new GridPosition(0, 5)),
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "bandits", new GridPosition(1, 5)),
            ],
            new ScriptedRandomSource(20, 1, 1, 1, 1, 1, 1));

        var bandit = encounter.Combatants.Single(combatant => combatant.Id == "bandit");

        Assert.Null(encounter.UseEntry("Whelm", bandit));

        var grapple = bandit.ConditionState(ConditionType.Grappled);
        Assert.NotNull(grapple);
        Assert.Equal(14, grapple!.EscapeDifficultyClass);
        Assert.Null(grapple.GrappleRangeFeet);
        Assert.False(bandit.HasCondition(ConditionType.Restrained));
    }

    [Fact]
    public void ARealGiantFrogGrapplesWithItsPrintedEscapeDifficultyClass()
    {
        // "If the target is a Medium or smaller creature, it has the Grappled condition
        // (escape DC 11)." The escape DC has to survive extraction and reach the
        // combatant, or the grapple would be inescapable.
        var frog = CombatantStats.FromMonster(Content.MonstersById["monster.giant-frog"]);

        var rider = Assert.Single(frog.Attacks.Single(attack => attack.Name == "Bite").AppliedConditions);

        Assert.Equal(ConditionType.Grappled, rider.Condition);
        Assert.Equal(11, rider.EscapeDifficultyClass);
        Assert.Equal(CreatureSize.Medium, rider.MaximumTargetSize);
    }

    [Fact]
    public void AGrappleIsImposedAndEscapedInARealFight()
    {
        // End to end against real content: the frog bites, the bandit is held, and the
        // grapple ends — either escaped or broken when one of them drops. A grapple that
        // could be imposed but never lifted would be worse than none at all.
        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [
                Spawn(Content.MonstersById["monster.giant-frog"], "frog", "vermin", new GridPosition(0, 5)),
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "bandits", new GridPosition(11, 5)),
            ],
            new SeededRandomSource(2));

        SimpleTacticsPolicy.RunToCompletion(encounter);

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Condition
                && step.Narration.Contains("has the Grappled condition", StringComparison.Ordinal));

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Condition
                && step.Narration.Contains("escape the grapple", StringComparison.Ordinal));
    }

    [Fact]
    public void AWolfPackKnocksSomebodyDownOverAWholeFight()
    {
        // The end-to-end proof, run against real content: wolves bite Medium creatures
        // and Medium creatures go down. Enough wolves and enough rounds that the seed
        // does not have to be lucky.
        var wolf = Content.MonstersById["monster.wolf"];
        var bandit = Content.MonstersById["monster.bandit"];

        var encounter = Encounter.Start(
            new Battlefield(12, 12),
            [
                Spawn(wolf, "wolf-1", "wolves", new GridPosition(0, 4)),
                Spawn(wolf, "wolf-2", "wolves", new GridPosition(0, 5)),
                Spawn(wolf, "wolf-3", "wolves", new GridPosition(0, 6)),
                Spawn(bandit, "bandit-1", "bandits", new GridPosition(11, 4)),
                Spawn(bandit, "bandit-2", "bandits", new GridPosition(11, 6)),
            ],
            new SeededRandomSource(4));

        SimpleTacticsPolicy.RunToCompletion(encounter);

        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Condition && step.Narration.Contains("Prone", StringComparison.Ordinal));
    }

    [Fact]
    public void AMonsterWithNoParsedAttacksStillTakesItsTurn()
    {
        // The Shrieker Fungus genuinely has no attack in the SRD — it shrieks, and that
        // is all — so it is real content rather than a contrived case. A combatant with
        // nothing to attack with must still take its turn rather than deadlocking the
        // turn loop.
        var shrieker = Content.MonstersById["monster.shrieker-fungus"];

        Assert.All(shrieker.Entries, entry => Assert.Null(entry.Attack));

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [
                Spawn(shrieker, "quiet", "left", new GridPosition(0, 0)),
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "right", new GridPosition(9, 9)),
            ],
            new SeededRandomSource(3));

        for (var turn = 0; turn < 12 && !encounter.IsComplete; turn++)
        {
            SimpleTacticsPolicy.TakeTurn(encounter);
        }

        // It never needs to win — only to keep the fight moving rather than hanging.
        Assert.True(encounter.Round > 1);
    }

    private static string RunAndRender(int seed)
    {
        var encounter = BanditsVersusGoblins(seed);
        SimpleTacticsPolicy.RunToCompletion(encounter);

        return string.Join('\n', encounter.Log.Select(step => step.Narration));
    }

    private static Encounter BanditsVersusGoblins(int seed)
    {
        var bandit = Content.MonstersById["monster.bandit"];
        var goblin = Content.MonstersById["monster.goblin-warrior"];

        return Encounter.Start(
            new Battlefield(14, 10),
            [
                Spawn(bandit, "bandit-1", "bandits", new GridPosition(1, 4)),
                Spawn(bandit, "bandit-2", "bandits", new GridPosition(1, 5)),
                Spawn(goblin, "goblin-1", "goblins", new GridPosition(12, 4)),
                Spawn(goblin, "goblin-2", "goblins", new GridPosition(12, 5)),
            ],
            new SeededRandomSource(seed));
    }

    [Fact]
    public void TheQuasitsScareFrightensAndTheVictimShakesItselfFree()
    {
        // The rider CLAUDE.md carried for a whole era as the two-sentence refusal:
        // "Failure: The target has the Frightened condition." with the way out printed
        // one sentence later. Joined at extraction, it now rides with the repeat-save
        // clock — the whole loop against real content: the printed DC 10, the failed
        // save, the Frightened round, and the end-of-turn repeat that shakes it off.
        var quasit = Content.MonstersById["monster.quasit"];
        var scare = quasit.Entries.Single(entry => entry.Name.StartsWith("Scare", StringComparison.Ordinal));
        var rider = Assert.Single(scare.Save!.AppliedConditions);

        Assert.Equal(ConditionType.Frightened, rider.Condition);
        Assert.Equal(ConditionDuration.RepeatSaveUpToOneMinute, rider.Duration);
        Assert.True(ConditionRules.CanBeImposed(rider));

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [
                Spawn(quasit, "quasit", "fiends", new GridPosition(0, 4)),
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "bandits", new GridPosition(3, 4)),
            ],
            // Initiatives; the bandit's failed save (2 + 0 vs DC 10); the bandit's
            // end-of-turn repeat, an 11 that clears it.
            new ScriptedRandomSource(20, 1, 2, 11));

        var bandit = encounter.Combatants.Single(combatant => combatant.Id == "bandit");

        Assert.Null(encounter.UseEntry(scare.Name, bandit));
        Assert.True(bandit.HasCondition(ConditionType.Frightened));

        // T3 (#672): every Frightened the engine imposes carries a SourceId — the
        // rider-application path (Encounter's condition-imposition code) always passes
        // the imposer's own id, which is what lets AttackerIsFrightened/
        // FrightenedSourceInSight resolve a source to ask VisionRules about rather than
        // falling back to "no source recorded".
        Assert.Equal("quasit", bandit.ConditionState(ConditionType.Frightened)!.SourceId);

        // The quasit's turn ends; the bandit's own turn comes and goes, and the
        // repeat save at its end rolls the scripted 11 against the printed DC 10.
        encounter.EndTurn();
        encounter.EndTurn();

        Assert.False(bandit.HasCondition(ConditionType.Frightened));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("repeats the Wisdom saving throw", StringComparison.Ordinal));
    }

    [Fact]
    public void TheOnisNightmareRayFrightensThroughTheAttackHitPathToo()
    {
        // T3's second shape (#672, Codex round 1): Nightmare Ray is Attack-mechanics,
        // not a save — its Frightened rider lives on the entry's own
        // AppliedConditions and is imposed by ImposeRiders/ImposeConditions on a hit,
        // the other of the two routes into the shared, always-sourced
        // rider-application code (the Quasit's Scare above exercises the
        // save-failure route). Both must agree on SourceId, or the T3 census below
        // would be vouching for a path this suite never actually ran.
        var oni = Content.MonstersById["monster.oni"];
        var rayEntry = oni.Entries.Single(entry => entry.Name == "Nightmare Ray");
        var rider = Assert.Single(rayEntry.AppliedConditions);

        Assert.Equal(ConditionType.Frightened, rider.Condition);
        Assert.True(ConditionRules.CanBeImposed(rider));

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [
                Spawn(oni, "oni", "fiends", new GridPosition(0, 4)),
                // Five squares off (25 ft, within the printed 60-foot range) — clear
                // of Ranged Attacks in Close Combat, whose own Disadvantage would
                // otherwise consume the extra die a plain hit does not need here.
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "bandits", new GridPosition(5, 4)),
            ],
            // Initiatives (oni first); a 15 + 5 attack bonus beats the bandit's AC 12
            // without a natural 20 (whose crit would double the damage dice); 2d6 damage.
            new ScriptedRandomSource(20, 1, 15, 3, 4));

        var bandit = encounter.Combatants.Single(combatant => combatant.Id == "bandit");

        Assert.Null(encounter.Attack("Nightmare Ray", bandit));
        Assert.True(bandit.HasCondition(ConditionType.Frightened));
        Assert.Equal("oni", bandit.ConditionState(ConditionType.Frightened)!.SourceId);
    }

    [Fact]
    public void T3_EveryExecutableCorpusFrightenedRiderIsImposedThroughTheSourcedPath()
    {
        // T3 (#672): the designer's own census — eleven executable Frightened riders
        // across the corpus, each one of exactly two shapes, and both are proven
        // (by the two tests directly above) to land through the shared,
        // always-sourced rider-application code — ImposeConditions, called either
        // from UseSaveEntry (a SavingThrow-mechanics entry's failed save; the
        // Quasit's Scare) or from ImposeRiders (an Attack-mechanics entry's hit; the
        // Oni's Nightmare Ray). Asserting the shape here, not just the count, is
        // what rules out a THIRD, unsourced rider-imposition path answering for any
        // of the other nine (Codex round 1, #672) — a rider of neither shape would
        // be a mechanism this test does not vouch for and would need its own executed
        // pin before joining the count below.
        var withExecutableFrightened = Content.MonstersById.Values
            .SelectMany(monster => monster.Entries.Select(entry => (monster.Id, entry)))
            .Where(pair => pair.entry.AppliedConditions
                .Concat(pair.entry.Save?.AppliedConditions ?? [])
                .Any(rider => rider.Condition == ConditionType.Frightened && ConditionRules.CanBeImposed(rider)))
            .ToArray();

        foreach (var (id, entry) in withExecutableFrightened)
        {
            Assert.True(
                entry.Mechanics is EntryMechanics.SavingThrow or EntryMechanics.Attack,
                $"{id} | {entry.Name} carries an executable Frightened rider through an unproven shape: {entry.Mechanics}.");
        }

        Assert.Equal(
            new[]
            {
                "monster.cloaker",
                "monster.doppelganger",
                "monster.ghost",
                "monster.lion",
                "monster.mummy",
                "monster.oni",
                "monster.pit-fiend",
                "monster.quasit",
                "monster.rakshasa",
                "monster.sea-hag",
                "monster.tarrasque",
            },
            withExecutableFrightened.Select(pair => pair.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void T4_ABlindedCombatantBuiltFromABlindsightMonsterStillDoesNotQualify()
    {
        // T4 (#672), against a real corpus monster rather than an unattached
        // condition (Codex round 1: a prior version of this test built an ordinary
        // combatant and never actually attached Blindsight to anything). The Animated
        // Armor prints Blindsight 60 ft (SRD 5.2.1's own "you can see anything that
        // isn't behind Total Cover even if you have the Blinded condition", p. 177) —
        // and CombatantStats.FromMonster never reads MonsterDefinition.Senses at all,
        // so the resulting combatant carries no trace of it. VisionRules.HasOpenEyes
        // therefore still disqualifies it once Blinded, print's exception
        // notwithstanding. Stated as a decision, not an oversight: when #673 carries
        // senses into combat, this test is meant to go red — the planned decision
        // point, not a regression — and should be retired alongside the new senses
        // tests that replace it.
        var armor = Content.MonstersById["monster.animated-armor"];
        Assert.Contains(armor.Senses, sense => sense.Type == SenseType.Blindsight);

        var viewer = new Combatant(
            "armor", armor.Name, "constructs", CombatantStats.FromMonster(armor), new GridPosition(0, 0));
        viewer.AddCondition(ConditionType.Blinded);

        var field = new Battlefield(6, 6);

        Assert.False(VisionRules.CanSee(field, viewer, new GridPosition(1, 0)));
    }

    [Fact]
    public void TheGhastsClawParalyzesTheLivingAndSparesTheDead()
    {
        // The embedded attack save, whole and against real content: "If the target is
        // a non-Undead creature, it is subjected to the following effect. Constitution
        // Saving Throw: DC 10. Failure: The target has the Paralyzed condition until
        // the end of its next turn." The Ghast is Complete now — the first creature
        // the embedded-save model brought into the pool.
        var ghast = Content.MonstersById["monster.ghast"];
        var claw = CombatantStats.FromMonster(ghast).Attacks.Single(attack => attack.Name == "Claw");

        Assert.NotNull(claw.EmbeddedSave);
        Assert.Equal(CreatureType.Undead, claw.EmbeddedSave!.ExcludedTargetType);
        Assert.Equal(10, claw.EmbeddedSave.Save.DifficultyClass);

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [
                Spawn(ghast, "ghast", "undead", new GridPosition(0, 4)),
                Spawn(Content.MonstersById["monster.bandit"], "bandit", "bandits", new GridPosition(1, 4)),
            ],
            // Initiatives; the claw's d20 and its two damage dice; the bandit's failed
            // Constitution save (3 + 0 vs DC 10); a fourth, trailing d20 (20) for the
            // Stench aura's own Constitution save the bandit rolls at its own turn's
            // start (#676) — adjacent to the still-living ghast, within the printed
            // 5-foot Emanation. Scripted to succeed so the bandit banks immunity
            // rather than also taking Poisoned, which this test does not assert on.
            new ScriptedRandomSource(20, 1, 15, 1, 1, 3, 20));

        var bandit = encounter.Combatants.Single(combatant => combatant.Id == "bandit");

        Assert.Null(encounter.Attack("Claw", bandit));
        Assert.True(bandit.HasCondition(ConditionType.Paralyzed));

        // "Until the end of its next turn": the ghast's turn ends, the bandit's own
        // turn comes round — a skip, since Paralyzed brings Incapacitated — and the
        // clock frees it at that turn's end. The Stench aura fires first, ahead of
        // that skip (Encounter.BeginTurn's own ordering), and the scripted 20 above
        // is what it consumes.
        encounter.EndTurn();

        Assert.False(bandit.HasCondition(ConditionType.Paralyzed));
    }

    [Fact]
    public void AnUndeadTargetNeverRollsTheGhastsSave()
    {
        // The printed gate: a zombie clawed by a ghast takes the damage and nothing
        // else. The script carries no save die, and would throw if one were asked for.
        var ghast = Content.MonstersById["monster.ghast"];

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [
                Spawn(ghast, "ghast", "undead", new GridPosition(0, 4)),
                Spawn(Content.MonstersById["monster.zombie"], "zombie", "walkers", new GridPosition(1, 4)),
            ],
            new ScriptedRandomSource(20, 1, 15, 1, 1));

        var zombie = encounter.Combatants.Single(combatant => combatant.Id == "zombie");

        Assert.Null(encounter.Attack("Claw", zombie));
        Assert.False(zombie.HasCondition(ConditionType.Paralyzed));
    }

    [Fact]
    public void TheRealGhastStenchesAnAdjacentVictimAtTheStartOfItsTurn()
    {
        // #676: the extractor now recognises Stench's printed shape ("any creature
        // that starts its turn in a 5-foot Emanation originating from the ghast")
        // and populates MonsterEntry.Aura, so the real Ghast actually emits it —
        // #670's Encounter.FireAuras, exercised against the real stat block rather
        // than TraitAuraTests's hand-authored fixture. The bandit wins initiative
        // outright, so its own turn begins the instant the fight starts
        // (RollInitiative calls BeginTurn immediately) and the aura fires before
        // either creature has taken a single action.
        var ghast = Content.MonstersById["monster.ghast"];
        var bandit = Content.MonstersById["monster.bandit"];

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [
                Spawn(ghast, "ghast", "undead", new GridPosition(0, 4)),
                Spawn(bandit, "victim", "bandits", new GridPosition(1, 4)),
            ],
            // Initiatives: the ghast's 1 (+3 = 4) loses to the bandit's 20 (+1 = 21),
            // so the bandit acts first; then its Constitution save against the
            // printed DC 10 — a 3 (+1 = 4) fails.
            new ScriptedRandomSource(1, 20, 3));

        var victim = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        Assert.True(victim.HasCondition(ConditionType.Poisoned));
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Entry
                && step.Narration.Contains("Ghast's Stench washes over Bandit", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Condition
                && step.Narration.Contains("Bandit has the Poisoned condition", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRealGhastGrantsRestOfFightImmunityOnASuccessfulStenchSave()
    {
        // The other half of #676's win condition: a save that succeeds banks the
        // printed "immune to this ghast's Stench for 24 hours" — read as immune for
        // the rest of the encounter (AuraEffect's own reading) — so the bandit takes
        // no Poisoned and, a full round later, does not roll again. The scripted die
        // carries exactly one Constitution save; a re-roll on the second turn would
        // overrun it.
        var ghast = Content.MonstersById["monster.ghast"];
        var bandit = Content.MonstersById["monster.bandit"];

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [
                Spawn(ghast, "ghast", "undead", new GridPosition(0, 4)),
                Spawn(bandit, "victim", "bandits", new GridPosition(1, 4)),
            ],
            // Initiatives (bandit first, as above), then a saving 20 (+1 = 21 vs DC 10).
            new ScriptedRandomSource(1, 20, 20));

        var victim = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        Assert.False(victim.HasCondition(ConditionType.Poisoned));

        encounter.EndTurn(); // the ghast's turn
        encounter.EndTurn(); // round two: the bandit starts its turn in range again

        Assert.False(victim.HasCondition(ConditionType.Poisoned));
        Assert.Equal(
            1,
            encounter.Log.Count(step => step.Narration.Contains("washes over", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheRealMagmaMephitBurstsOnDeathAndCatchesAnAdjacentVictim()
    {
        // #679: the extractor now recognises Death Burst's printed leading sentence
        // ("The mephit explodes when it dies") and populates MonsterEntry.DeathBurst,
        // so the real Magma Mephit actually explodes — Encounter.FireDeathBurst,
        // exercised against the real stat block rather than DeathBurstTests's
        // hand-authored fixture. Two Bandits (Light Crossbow, 1d8+1) at range finish
        // the mephit's 18 hit points exactly (9 + 9 = 18) so neither attacker stands
        // anywhere near the blast; a third Bandit stands in the printed 5-foot
        // Emanation to take it. Both bandits' attack rolls (13 vs AC 11) and the
        // victim's save (6 vs DC 11) are ordinary, unforced numbers — nothing here
        // depends on a critical hit or a natural 20.
        var mephit = Content.MonstersById["monster.magma-mephit"];
        var bandit = Content.MonstersById["monster.bandit"];

        var encounter = Encounter.Start(
            new Battlefield(12, 10),
            [
                Spawn(bandit, "bandit1", "bandits", new GridPosition(0, 5)),
                Spawn(bandit, "bandit2", "bandits", new GridPosition(1, 5)),
                Spawn(mephit, "mephit", "elementals", new GridPosition(5, 5)),
                Spawn(bandit, "victim", "bandits", new GridPosition(6, 5)),
            ],
            // Initiatives (bandit1 and bandit2 first, by roll); bandit1's Crossbow
            // hits (10 + 3 = 13 vs AC 11) for 8 + 1 = 9; bandit2's identical hit for
            // another 9 — 18 total, exactly the mephit's hit points, so it dies at
            // exactly 0 rather than by overkill. The mephit's own Death Burst then
            // fires: the victim's Dexterity save (5 + 1 = 6) fails DC 11, and its 2d6
            // Fire (3 + 4 = 7 — the printed average) lands in full.
            new ScriptedRandomSource(15, 14, 1, 1, 10, 8, 10, 8, 5, 3, 4));

        var mephitCombatant = encounter.Combatants.Single(combatant => combatant.Id == "mephit");
        var victim = encounter.Combatants.Single(combatant => combatant.Id == "victim");

        encounter.Attack("Light Crossbow", mephitCombatant);
        encounter.EndTurn(); // bandit1's turn ends; bandit2 acts next
        encounter.Attack("Light Crossbow", mephitCombatant);

        Assert.True(mephitCombatant.IsDead);
        Assert.Equal(4, victim.CurrentHitPoints);
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("Death Burst bursts outward as it dies", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Damage
                && step.Narration.Contains("takes 7 Fire damage", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("monster.bandit-captain", 15, 13)]
    [InlineData("monster.knight", 18, 16)]
    [InlineData("monster.warrior-veteran", 17, 15)]
    [InlineData("monster.noble", 15, 13)]
    public void TheRealParryBearersFlipAWouldHitMeleeAttackIntoAMiss(
        string monsterId,
        int armorClass,
        int naturalRollThatWouldHitByOne)
    {
        // #678: the extractor now populates ReactionEffect.Executable from the printed
        // "Parry" Response ("adds 2 to its AC against that attack") for these four —
        // already-Playable pool residents (#677's own acceptance) whose signature
        // defence was, until this slice, inert. The attacker is a real Bandit
        // (Scimitar, +3 to hit, verified against data/srd) so this exercises the whole
        // pipeline against two real stat blocks, not a hand-authored fixture. The
        // scripted natural roll totals exactly one over the defender's own printed AC —
        // a hit Parry's +2 flips to a miss — and no damage die is scripted, so a Parry
        // that failed to fire (and so needed a damage roll the script doesn't supply)
        // would throw rather than silently pass.
        Assert.Equal(armorClass + 1, naturalRollThatWouldHitByOne + 3);

        var attackerMonster = Content.MonstersById["monster.bandit"];
        var defenderMonster = Content.MonstersById[monsterId];

        // Guards the InlineData's own printed AC against drift from a future
        // regeneration — if the stat block's AC ever changes, this fails loudly
        // instead of the scripted roll silently testing the wrong margin.
        Assert.Equal(armorClass, defenderMonster.ArmorClass);

        var encounter = Encounter.Start(
            new Battlefield(10, 10),
            [
                Spawn(attackerMonster, "attacker", "bandits", new GridPosition(0, 4)),
                Spawn(defenderMonster, "defender", "guards", new GridPosition(1, 4)),
            ],
            // Initiatives (attacker first), then the one attack roll — no damage die,
            // since a real Parry must fire and turn this hit into a miss.
            new ScriptedRandomSource(20, 1, naturalRollThatWouldHitByOne));

        var defender = encounter.Combatants.Single(combatant => combatant.Id == "defender");

        Assert.Null(encounter.Attack("Scimitar", defender));

        Assert.False(defender.Turn.HasReaction);
        Assert.Equal(defender.Stats.MaximumHitPoints, defender.CurrentHitPoints);
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Feature
                && step.Narration.Contains("Parries", StringComparison.Ordinal));
        Assert.Contains(
            encounter.Log,
            step => step.Kind == CombatStepKind.Attack && step.Hit == false);
    }

    private static Combatant Spawn(MonsterDefinition monster, string id, string side, GridPosition position) =>
        new(id, monster.Name, side, CombatantStats.FromMonster(monster), position);
}
