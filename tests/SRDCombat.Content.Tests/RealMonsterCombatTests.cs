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
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

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

        // The quasit's turn ends; the bandit's own turn comes and goes, and the
        // repeat save at its end rolls the scripted 11 against the printed DC 10.
        encounter.EndTurn();
        encounter.EndTurn();

        Assert.False(bandit.HasCondition(ConditionType.Frightened));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("repeats the Wisdom saving throw", StringComparison.Ordinal));
    }

    private static Combatant Spawn(MonsterDefinition monster, string id, string side, GridPosition position) =>
        new(id, monster.Name, side, CombatantStats.FromMonster(monster), position);
}
