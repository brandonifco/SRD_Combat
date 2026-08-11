using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

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
        // The Ankheg's Grappled and the Giant Centipede's Poisoned are both extracted,
        // and neither may travel any further: one is a condition the engine does not
        // execute, the other prints a duration nothing can end. They stay counted on the
        // stat block entry instead.
        foreach (var id in new[] { "monster.ankheg", "monster.giant-centipede" })
        {
            var monster = Content.MonstersById[id];
            var stats = CombatantStats.FromMonster(monster);

            Assert.Contains(monster.Entries, entry => entry.AppliedConditions.Count > 0);
            Assert.All(stats.Attacks, attack => Assert.Empty(attack.AppliedConditions));
        }
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

    private static Combatant Spawn(MonsterDefinition monster, string id, string side, GridPosition position) =>
        new(id, monster.Name, side, CombatantStats.FromMonster(monster), position);
}
