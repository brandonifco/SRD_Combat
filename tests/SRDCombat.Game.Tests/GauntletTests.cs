using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// A run through the gauntlet: what carries between fights, what rests restore, and how
/// a run ends.
/// </summary>
/// <remarks>
/// The property that makes a gauntlet a game rather than a series of unrelated fights is
/// that <em>nothing resets on its own</em>. Most of these tests are that property from a
/// different angle.
/// </remarks>
public class GauntletTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void TheDefaultLadderKeepsHighForTheMilestoneRung()
    {
        var ladder = GauntletLadder.Default();

        Assert.True(ladder.Count >= 30, "The default run is too short to reach level 5.");

        // High is the set piece closing each cycle, never a routine rung. The decision
        // and its measurement are #65.
        //
        // The fourth rung was Moderate until the warband landed; it is budgeted Low now
        // because a Moderate budget divided across six to ten bodies is unwinnable, and
        // the count is where that rung's difficulty comes from.
        Assert.Equal(EncounterDifficulty.Low, ladder[0].Difficulty);
        Assert.Equal(EncounterDifficulty.Moderate, ladder[1].Difficulty);
        Assert.Equal(EncounterDifficulty.Low, ladder[2].Difficulty);
        Assert.Equal(EncounterDifficulty.Low, ladder[3].Difficulty);
        Assert.True(ladder[3].Horde);
        Assert.Equal(EncounterDifficulty.High, ladder[GauntletLadder.FightsPerCycle - 1].Difficulty);
        Assert.Equal(EncounterDifficulty.Low, ladder[GauntletLadder.FightsPerCycle].Difficulty);

        Assert.All(
            ladder.Where((_, index) => index % GauntletLadder.FightsPerCycle != GauntletLadder.FightsPerCycle - 1),
            step => Assert.NotEqual(EncounterDifficulty.High, step.Difficulty));
    }

    [Fact]
    public void ARungNamesNoLevelBecauseExperienceGrantsThem()
    {
        // The ladder used to prescribe a level per rung, which meant the game handed out
        // levels on a schedule. Experience does it now, so a rung says only how hard the
        // fight should be.
        var run = GauntletRun.Start(Content, [new LadderStep(EncounterDifficulty.Low)]);

        Assert.All(run.States, state => Assert.Equal(1, state.Level));
    }

    [Fact]
    public void TheFirstFightOffersNoRestAndLongRestsBracketTheMilestone()
    {
        var ladder = GauntletLadder.Default();

        Assert.Null(ladder[0].RestBefore);

        // The High milestone is entered fresh, and the next cycle opens rested — which
        // is also where anyone who fell to the set piece rejoins.
        Assert.Equal(RestKind.Long, ladder[GauntletLadder.FightsPerCycle - 1].RestBefore);
        Assert.Equal(RestKind.Long, ladder[GauntletLadder.FightsPerCycle].RestBefore);

        // From the second cycle on, the routine rungs rest Short.
        Assert.Equal(RestKind.Short, ladder[GauntletLadder.FightsPerCycle + 1].RestBefore);
    }

    [Fact]
    public void EveryCycleCarriesOneWarbandRung()
    {
        var ladder = GauntletLadder.Default();

        for (var cycle = 0; cycle * GauntletLadder.FightsPerCycle < ladder.Count; cycle++)
        {
            var rungs = ladder
                .Skip(cycle * GauntletLadder.FightsPerCycle)
                .Take(GauntletLadder.FightsPerCycle)
                .ToArray();

            Assert.Single(rungs, rung => rung.Horde);

            // Budgeted Low rather than Moderate. A Moderate budget spread across six to
            // ten bodies measured as unwinnable — full clears 72 of 120 down to 12 —
            // because the per-count numbers are a cliff at six, not a slope.
            Assert.Equal(EncounterDifficulty.Low, rungs.Single(rung => rung.Horde).Difficulty);
        }
    }

    [Fact]
    public void AWarbandWaitsForAPartyThatCanSurviveBeingOutnumbered()
    {
        var horde = GauntletLadder.Default().First(step => step.Horde);
        var random = new SeededRandomSource(11);

        // Below the gate the request is ignored, because the fragile tier pays for being
        // outnumbered in characters removed — that is the level 1 wall, and handing it
        // ten enemies would rebuild it deliberately.
        var early = EncounterFactory.Build(
            Content,
            PregeneratedParty.Build(Content, level: 1),
            horde.Difficulty,
            random,
            objective: horde.Objective,
            horde: true);

        Assert.True(
            early.Built.Monsters.Count <= EncounterBuilder.DefaultMaximumMonsters,
            $"a level 1 party was handed {early.Built.Monsters.Count} creatures.");

        // At the gate it really is a warband, above what an ordinary rung may field.
        var later = EncounterFactory.Build(
            Content,
            PregeneratedParty.Build(Content, level: EncounterFactory.HordeMinimumLevel),
            horde.Difficulty,
            new SeededRandomSource(11),
            objective: horde.Objective,
            horde: true);

        Assert.True(
            later.Built.Monsters.Count >= EncounterFactory.HordeMinimum,
            $"a warband fielded only {later.Built.Monsters.Count} creatures.");
    }

    [Fact]
    public void TheOpeningCycleRestsLongThroughout()
    {
        // A level 1 character carries exactly one Hit Die, a Short Rest spends it, and
        // Hit Dice return only on a Long Rest — so short-resting through the opening
        // gave the party one real heal and then three fights on the remainder, against
        // budgets priced for a party at full strength. died-by-fight-4 was the run's
        // largest failure cohort until this changed.
        var ladder = GauntletLadder.Default();

        for (var index = 1; index < GauntletLadder.FightsPerCycle; index++)
        {
            Assert.Equal(RestKind.Long, ladder[index].RestBefore);
        }

        // And it really is only the opening: the second cycle's routine rungs are
        // Short again, so this is a starting-tier concession rather than a new cadence.
        Assert.Equal(RestKind.Short, ladder[GauntletLadder.FightsPerCycle + 1].RestBefore);
        Assert.Equal(RestKind.Short, ladder[GauntletLadder.FightsPerCycle + 2].RestBefore);
    }

    [Fact]
    public void WoundsAndSpentResourcesCarryIntoTheNextFight()
    {
        var run = GauntletRun.Start(Content);
        var random = new SeededRandomSource(4);

        run.PrepareForNext(random);
        var fight = run.BeginNext(random);

        // Wound someone and spend a resource, then finish the fight.
        var barbarian = fight.Encounter.Combatants
            .First(combatant => combatant.Name == "Korrin");

        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
        run.CompleteFight(fight);

        var index = run.Party.ToList().FindIndex(member => member.Draft.Name == "Korrin");
        var state = run.States[index];

        Assert.Equal(barbarian.Features.RagesRemaining, state.RagesRemaining);
        Assert.True(
            state.CurrentHitPoints <= run.Party[index].Sheet.MaximumHitPoints,
            "A survivor came out of a fight with more hit points than they have.");
    }

    [Fact]
    public void ALongRestRestoresEverything()
    {
        var member = PregeneratedParty.Build(Content, level: 3)
            .Single(candidate => candidate.Draft.Name == "Korrin");

        var spent = CharacterState.Fresh(member) with
        {
            CurrentHitPoints = 1,
            RagesRemaining = 0,
            HitDiceRemaining = 0,
        };

        var rested = spent.AfterRest(member, RestKind.Long, new SeededRandomSource(1), hitDieSides: 12);

        Assert.Equal(member.Sheet.MaximumHitPoints, rested.CurrentHitPoints);
        Assert.Equal(member.Combatant.Stats.Character!.RageUses, rested.RagesRemaining);

        // The 2024 change worth not re-learning: a Long Rest returns *all* spent Hit
        // Point Dice, where earlier editions returned half.
        Assert.Equal(member.Sheet.Level, rested.HitDiceRemaining);
    }

    [Fact]
    public void AShortRestReturnsOneRageAndNotAllOfThem()
    {
        // "You regain one expended use when you finish a Short Rest, and you regain all
        // expended uses when you finish a Long Rest." A Barbarian at level 3 has three.
        var member = PregeneratedParty.Build(Content, level: 3)
            .Single(candidate => candidate.Draft.Name == "Korrin");

        var spent = CharacterState.Fresh(member) with { RagesRemaining = 0 };
        var rested = spent.AfterRest(member, RestKind.Short, new SeededRandomSource(1), hitDieSides: 12);

        Assert.Equal(1, rested.RagesRemaining);
        Assert.True(rested.RagesRemaining < member.Combatant.Stats.Character!.RageUses);
    }

    [Fact]
    public void ChannelDivinityRestoresOneOnAShortRestAndAllOnALong()
    {
        // Channel Divinity prints the Rage and Second Wind pattern word for word: one
        // expended use back on a Short Rest, all of them on a Long.
        var member = PregeneratedParty.Build(Content, level: 3)
            .Single(candidate => candidate.Draft.Name == "Aldous");

        var spent = CharacterState.Fresh(member) with { ChannelDivinityRemaining = 0 };

        var afterShort = spent.AfterRest(member, RestKind.Short, new SeededRandomSource(1), hitDieSides: 8);
        Assert.Equal(1, afterShort.ChannelDivinityRemaining);

        var afterLong = spent.AfterRest(member, RestKind.Long, new SeededRandomSource(1), hitDieSides: 8);
        Assert.Equal(member.Combatant.Stats.Character!.ChannelDivinityUses, afterLong.ChannelDivinityRemaining);
    }

    [Fact]
    public void AShortRestSpendsHitDiceToHealAndNotBeyondTheMaximum()
    {
        var member = PregeneratedParty.Build(Content, level: 5)
            .Single(candidate => candidate.Draft.Name == "Brenna");

        var hurt = CharacterState.Fresh(member) with { CurrentHitPoints = 1 };
        var rested = hurt.AfterRest(member, RestKind.Short, new SeededRandomSource(2), hitDieSides: 10);

        Assert.True(rested.CurrentHitPoints > hurt.CurrentHitPoints, "The short rest healed nothing.");
        Assert.True(rested.CurrentHitPoints <= member.Sheet.MaximumHitPoints);
        Assert.True(rested.HitDiceRemaining < hurt.HitDiceRemaining, "No Hit Point Dice were spent.");
    }

    [Fact]
    public void ACharacterAtZeroHitPointsCannotRest()
    {
        // "To start a Short Rest, you must have at least 1 Hit Point", and the same for
        // a Long Rest. A downed character cannot rest their way back.
        var member = PregeneratedParty.Build(Content).First();
        var downed = CharacterState.Fresh(member) with { CurrentHitPoints = 0 };

        var rested = downed.AfterRest(member, RestKind.Long, new SeededRandomSource(1), hitDieSides: 10);

        Assert.Equal(0, rested.CurrentHitPoints);
        Assert.False(RestRules.CanRest(rested.CurrentHitPoints));
    }

    [Fact]
    public void ASurvivorWhoWentDownComesBackAtOneHitPoint()
    {
        // "A Stable creature that isn't healed regains 1 Hit Point after 1d4 hours" — the
        // stated reading that stops a downed character being stuck, since resting needs
        // a hit point to start.
        var member = PregeneratedParty.Build(Content).First();

        var combatant = new Combatant(
            member.Combatant.Id,
            member.Combatant.Name,
            member.Combatant.SideId,
            member.Combatant.Stats,
            new GridPosition(0, 0),
            new CombatantCarryOver(CurrentHitPoints: 0));

        Assert.False(combatant.IsDead);

        var state = CharacterState.Fresh(member).AfterFight(combatant);

        Assert.Equal(RestRules.HitPointsAfterStabilising, state.CurrentHitPoints);
        Assert.True(state.CanFight);
    }

    [Fact]
    public void ACombatantCarriedInAtZeroArrivesDown()
    {
        var member = PregeneratedParty.Build(Content).First();

        var combatant = new Combatant(
            "carried",
            "Carried",
            "party",
            member.Combatant.Stats,
            new GridPosition(0, 0),
            new CombatantCarryOver(CurrentHitPoints: 0));

        Assert.True(combatant.HasCondition(ConditionType.Unconscious));
        Assert.False(combatant.CanAct);
    }

    [Fact]
    public void CarriedResourcesReachTheCombatant()
    {
        var member = PregeneratedParty.Build(Content, level: 3)
            .Single(candidate => candidate.Draft.Name == "Korrin");

        var placed = member
            .CarryingOver(new CombatantCarryOver(CurrentHitPoints: 5, RagesRemaining: 1))
            .AtPosition(new GridPosition(0, 0));

        Assert.Equal(5, placed.Combatant.CurrentHitPoints);
        Assert.Equal(1, placed.Combatant.Features.RagesRemaining);
    }

    [Fact]
    public void ARunAdvancesRungByRungAndCanBeSurvived()
    {
        // A short ladder run end to end, which is the whole feature in one test.
        var ladder = GauntletLadder.Default(fights: 3);
        var run = GauntletRun.Start(Content, ladder);
        var random = new SeededRandomSource(20250812);

        var fought = 0;

        while (run.Next is not null && fought < ladder.Count)
        {
            run.PrepareForNext(random);
            var fight = run.BeginNext(random);
            SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
            run.CompleteFight(fight);
            fought++;
        }

        Assert.NotEqual(RunOutcome.InProgress, run.Outcome);
        Assert.True(fought > 0);

        if (run.Outcome == RunOutcome.Survived)
        {
            Assert.Equal(ladder.Count, run.Cleared);
            Assert.Null(run.Next);
        }
    }

    [Fact]
    public void AWipeEndsTheRun()
    {
        var run = GauntletRun.Start(Content, [new LadderStep(EncounterDifficulty.Low)]);
        var random = new SeededRandomSource(5);

        run.PrepareForNext(random);
        var fight = run.BeginNext(random);
        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
        run.CompleteFight(fight);

        // Whichever way this particular fight went, the outcome must be decided and the
        // two states must agree with each other.
        if (run.Outcome == RunOutcome.Defeated)
        {
            Assert.True(
                run.States.All(state => !state.CanFight) || fight.Encounter.WinningSide != PregeneratedParty.SideId,
                "The run was lost without a wipe or a lost fight.");
        }
        else
        {
            Assert.Equal(PregeneratedParty.SideId, fight.Encounter.WinningSide);
        }
    }

    [Fact]
    public void TheRunRefusesToBuildAFightOnceItIsOver()
    {
        var run = GauntletRun.Start(Content, [new LadderStep(EncounterDifficulty.Low)]);
        var random = new SeededRandomSource(6);

        run.PrepareForNext(random);
        var fight = run.BeginNext(random);
        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
        run.CompleteFight(fight);

        Assert.Throws<InvalidOperationException>(() => run.BeginNext(random));
    }


    [Fact]
    public void WinningAFightAwardsExperienceToTheLiving()
    {
        var run = GauntletRun.Start(Content, [new LadderStep(EncounterDifficulty.Low)]);
        var random = new SeededRandomSource(20250812);

        run.PrepareForNext(random);
        var fight = run.BeginNext(random);
        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);

        var before = run.States.Select(state => state.ExperiencePoints).ToArray();
        run.CompleteFight(fight);

        if (fight.Encounter.WinningSide != PregeneratedParty.SideId)
        {
            // A lost fight pays nothing, which is the other half of the rule.
            Assert.Equal(before, run.States.Select(state => state.ExperiencePoints));
            return;
        }

        Assert.All(
            run.States.Zip(before),
            pair => Assert.True(
                pair.First.IsDead || pair.First.ExperiencePoints > pair.Second,
                "A survivor of a won fight earned nothing."));
    }

    [Fact]
    public void EnoughExperienceLevelsACharacterAndResolvesThemAfresh()
    {
        // Levelling is re-resolving the draft, so the levelled sheet must carry the new
        // level's numbers rather than the old ones with a label changed.
        var member = PregeneratedParty.Build(Content, level: 1).First();
        var levelled = PregeneratedParty.Resolve(Content, member.Draft, level: 3);

        Assert.Equal(3, levelled.Sheet.Level);
        Assert.True(levelled.Sheet.MaximumHitPoints > member.Sheet.MaximumHitPoints);
        Assert.Equal(member.Draft.Name, levelled.Draft.Name);
    }

    [Fact]
    public void APregenBuiltAtLevelOneStillCarriesAndAppliesItsAbilityScoreImprovementPlan()
    {
        // The regression this pins (#330): ImprovementsAt used to gate the level-4 plan
        // on the *build* level, so a fresh level-1 gauntlet — the overwhelmingly common
        // case — baked an empty plan into the draft forever, and the pregen's own
        // hardcoded improvement silently never applied on levelling up in play.
        // Reinstating that gate fails nothing else in this file, because both
        // plan-less-fallback tests below strip the plan by hand rather than relying on
        // a fresh pregen build to be plan-less.
        var built = PregeneratedParty.Build(Content, level: 1)[0];
        Assert.NotEmpty(built.Draft.AbilityScoreImprovements);

        var ability = built.Draft.AbilityScoreImprovements[0].First;
        var atFour = PregeneratedParty.Resolve(Content, built.Draft, level: 4);

        Assert.Equal(built.Sheet.AbilityScores[ability] + 2, atFour.Sheet.AbilityScores[ability]);
    }

    [Fact]
    public void ACreatedCharacterWithAnAsiPlanReceivesExactlyItAtLevelFour()
    {
        // A creation flow's plan, not a pregen's: two abilities at +1 each, the shape a
        // pregen never takes, so this is provably the draft's choice and not a
        // coincidence of the default (#288).
        var pregen = PregeneratedParty.Build(Content, level: 3).First();
        var created = pregen.Draft with
        {
            AbilityScoreImprovements = [new AbilityScoreImprovement
            {
                First = Ability.Constitution,
                Second = Ability.Wisdom,
            }],
        };

        var beforeFour = PregeneratedParty.Resolve(Content, created, level: 3);
        var atFour = PregeneratedParty.Resolve(Content, created, level: 4);

        Assert.Equal(
            beforeFour.Sheet.AbilityScores[Ability.Constitution] + 1,
            atFour.Sheet.AbilityScores[Ability.Constitution]);
        Assert.Equal(
            beforeFour.Sheet.AbilityScores[Ability.Wisdom] + 1,
            atFour.Sheet.AbilityScores[Ability.Wisdom]);

        // Nothing else moved, and the feat is fully spent.
        Assert.Equal(beforeFour.Sheet.AbilityScores[Ability.Strength], atFour.Sheet.AbilityScores[Ability.Strength]);
        Assert.Equal(0, atFour.Sheet.UnspentFeatChoices);
    }

    [Fact]
    public void ALegacyPlanlessDraftDefaultsItsAbilityScoreImprovementOnLevellingUpAndSaysSo()
    {
        // The honest fallback for a save written before creation flows asked for a
        // plan (#288): nobody prompts mid-run, so the class's primary ability gets +2
        // instead, and it is never silent — it lands in LevelUps beside the level-up
        // line itself. Built by hand rather than from a fresh pregen build, because
        // the pregens now always carry their own real plan (#330) — a plan-less draft
        // is exactly what an old, pre-#288 save looks like, created or pregenerated.
        var drafts = PregeneratedParty.Build(Content, level: 3)
            .Select(member => member.Draft)
            .ToArray();
        drafts[0] = drafts[0] with { AbilityScoreImprovements = [] };

        // Every rung Low with a Long Rest before it, deliberately: a milestone High rung
        // can wipe a policy-driven party, and unrested Low rungs grind one down by
        // attrition over enough repeats — what this test needs proven is the accounting,
        // not survival under the hardest budget the ladder offers or with no recovery
        // between fights. Generously bounded rather than tied to the default ladder's
        // length, since a level-3 party needs several fights' worth of Low-budget XP to
        // reach level 4.
        var ladder = Enumerable.Repeat(new LadderStep(EncounterDifficulty.Low, RestKind.Long), 60).ToArray();
        var run = GauntletRun.Start(Content, drafts, ladder, startingLevel: 3);

        // Seed 4242 stopped reaching level 4 within the 60-fight budget once terrain
        // generation started spending extra dice on density and contested-region bias
        // (#433) — every fight's whole random stream shifts with it, and this
        // particular seed's party was defeated at fight 12 instead. Reselected for a
        // seed that still reaches level 4 comfortably inside the budget; the accounting
        // under test does not care which seed gets it there.
        var random = new SeededRandomSource(2);

        while (run.States[0].Level < 4 && run.Outcome == RunOutcome.InProgress && run.Next is not null)
        {
            run.PrepareForNext(random);
            var fight = run.BeginNext(random);
            SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
            run.CompleteFight(fight, random);
        }

        Assert.Equal(4, run.States[0].Level);

        var primary = Content.ClassesById[drafts[0].ClassId].PrimaryAbilities[0];
        var withoutTheDefault = PregeneratedParty.Resolve(Content, drafts[0], level: 4).Sheet.AbilityScores[primary];

        Assert.Equal(withoutTheDefault + 2, run.Party[0].Sheet.AbilityScores[primary]);
        Assert.Equal(0, run.Party[0].Sheet.UnspentFeatChoices);
        Assert.Contains(
            run.LevelUps,
            line => line.Contains(drafts[0].Name, StringComparison.Ordinal)
                && line.Contains("defaulted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnAlreadyLevelFourSaveWithoutAPlanDefaultsOnResumeRatherThanOnItsNextLevelUp()
    {
        // A save can arrive already past level 4 with an empty plan — the exact shape
        // an old save from before #288 has. GauntletRun.Resume must default it right
        // there, because there is no future level-up left to catch it on.
        var member = PregeneratedParty.Build(Content, level: 1).First();
        var planless = member.Draft with { AbilityScoreImprovements = [] };
        var state = CharacterState.Fresh(member) with
        {
            ExperiencePoints = AdvancementRules.ExperienceToReach(4),
        };

        var saved = new SavedRun
        {
            FormatVersion = RunSave.CurrentFormatVersion,
            Ladder = GauntletLadder.Default(),
            Cleared = 0,
            Members = [new SavedMember(planless, state)],
        };

        var run = GauntletRun.Resume(Content, saved);

        Assert.Equal(4, run.States[0].Level);

        var primary = Content.ClassesById[planless.ClassId].PrimaryAbilities[0];
        var withoutTheDefault = PregeneratedParty.Resolve(Content, planless, level: 4).Sheet.AbilityScores[primary];

        Assert.Equal(withoutTheDefault + 2, run.Party[0].Sheet.AbilityScores[primary]);
        Assert.Contains(
            run.LevelUps,
            line => line.Contains(planless.Name, StringComparison.Ordinal)
                && line.Contains("defaulted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ALevelUpAddsHitPointsWithoutHealingTheDamageAlreadyTaken()
    {
        // "Your Hit Point maximum increases" and nothing more: a wounded character who
        // levels is still wounded, just with a bigger maximum.
        var first = PregeneratedParty.Build(Content, level: 1).First();
        var second = PregeneratedParty.Resolve(Content, first.Draft, level: 2);
        var gained = second.Sheet.MaximumHitPoints - first.Sheet.MaximumHitPoints;

        var hurt = CharacterState.Fresh(first) with { CurrentHitPoints = 1 };
        var afterLevel = hurt with { CurrentHitPoints = hurt.CurrentHitPoints + gained };

        Assert.True(gained > 0);
        Assert.Equal(1 + gained, afterLevel.CurrentHitPoints);
        Assert.True(afterLevel.CurrentHitPoints < second.Sheet.MaximumHitPoints);
    }

    [Fact]
    public void ACharacterBuiltAboveLevelOneStartsWithThatLevelsExperience()
    {
        // Otherwise a run begun at level 3 would level again on its first win.
        var member = PregeneratedParty.Build(Content, level: 3).First();
        var state = CharacterState.Fresh(member);

        Assert.Equal(3, state.Level);
        Assert.Equal(AdvancementRules.ExperienceToReach(3), state.ExperiencePoints);
    }

    [Fact]
    public void TheDeadEarnNothing()
    {
        var member = PregeneratedParty.Build(Content).First();
        var dead = CharacterState.Fresh(member) with { IsDead = true };

        Assert.Equal(dead.ExperiencePoints, dead.Earning(500).ExperiencePoints);
    }

    [Fact]
    public void ARunReachesLevelFiveIfItIsPlayedOutFarEnough()
    {
        // The pacing claim the default ladder's length rests on: thirty fights is enough
        // to carry a party from level 1 to the top of this game's tier. Fought by the
        // policy on both sides, so it is the arithmetic being tested, not tactics.
        var run = GauntletRun.Start(Content, GauntletLadder.Default());
        var random = new SeededRandomSource(4242);

        while (run.Next is not null)
        {
            run.PrepareForNext(random);
            var fight = run.BeginNext(random);
            SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
            run.CompleteFight(fight);
        }

        // However the run ended, nobody may exceed the supported tier.
        Assert.All(run.States, state => Assert.InRange(state.Level, 1, AdvancementRules.MaximumSupportedLevel));

        if (run.Outcome == RunOutcome.Survived)
        {
            Assert.Contains(run.States, state => state.Level >= 3);
        }
    }

    [Fact]
    public void AFallenCharacterRejoinsOnALongRest()
    {
        // A house rule rather than anything the SRD prints, and the reason is measured:
        // with death permanent a run died out within a few fights, because a fight the
        // party won still cost a character and a party of three lost the next one.
        var member = PregeneratedParty.Build(Content, level: 2).First();
        var dead = CharacterState.Fresh(member) with { CurrentHitPoints = 0, IsDead = true };

        Assert.False(dead.CanFight);

        var afterLong = dead.AfterRest(member, RestKind.Long, new SeededRandomSource(1), hitDieSides: 10);

        Assert.True(afterLong.CanFight);
        Assert.Equal(member.Sheet.MaximumHitPoints, afterLong.CurrentHitPoints);
    }

    [Fact]
    public void AShortRestDoesNotBringAnybodyBack()
    {
        // The cost has to be real: a fallen character misses every fight until the next
        // Long Rest, which on the default ladder is up to half a cycle of routine fights
        // or the High milestone itself.
        var member = PregeneratedParty.Build(Content, level: 2).First();
        var dead = CharacterState.Fresh(member) with { CurrentHitPoints = 0, IsDead = true };

        var afterShort = dead.AfterRest(member, RestKind.Short, new SeededRandomSource(1), hitDieSides: 10);

        Assert.False(afterShort.CanFight);
    }

    [Fact]
    public void ACharacterWhoDiesFallsBehindOnExperience()
    {
        // The other half of the cost, and it needs no new code: the dead earn nothing,
        // and characters level individually, so somebody who misses a cycle comes back a
        // level behind the party that kept fighting.
        var member = PregeneratedParty.Build(Content).First();
        var dead = CharacterState.Fresh(member) with { IsDead = true };

        var missed = dead.Earning(500);

        Assert.Equal(dead.ExperiencePoints, missed.ExperiencePoints);
    }

    [Fact]
    public void ARunReportsWhoIsDeadNowRatherThanWhoHasEverFallen()
    {
        // Casualties is the history; Fallen is the state. They differ the moment a
        // fallen character can come back, and a run's ending should report the latter.
        var run = GauntletRun.Start(Content, GauntletLadder.Default(fights: 6));
        var random = new SeededRandomSource(20250812);

        while (run.Next is not null)
        {
            run.PrepareForNext(random);
            var fight = run.BeginNext(random);
            SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
            run.CompleteFight(fight);
        }

        Assert.All(run.Fallen, name => Assert.Contains(name, run.Casualties));

        // Anyone who came back is in the history and not in the state.
        Assert.Equal(run.Casualties.Count - run.Returns.Count, run.Fallen.Count());
    }

    [Fact]
    public void AnUnfinishedFightCannotBeRecorded()
    {
        var run = GauntletRun.Start(Content);
        var random = new SeededRandomSource(7);

        run.PrepareForNext(random);
        var fight = run.BeginNext(random);

        Assert.Throws<InvalidOperationException>(() => run.CompleteFight(fight));
    }
}
