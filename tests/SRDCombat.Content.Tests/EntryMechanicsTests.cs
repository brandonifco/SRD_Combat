using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Content.Tests;

/// <summary>
/// Covers the effect model against real stat blocks: that every entry is classified,
/// that the structured forms match what the SRD prints, and that the amount the model
/// does <em>not</em> express is a tracked number rather than a silence.
/// </summary>
public class EntryMechanicsTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    private static IReadOnlyList<MonsterEntry> TierOneEntries { get; } = Content.Monsters
        .Where(monster => monster.ChallengeRating <= 4m)
        .SelectMany(monster => monster.Entries)
        .ToArray();

    [Fact]
    public void EveryEntryCarriesAClassification()
    {
        // The load-bearing invariant. An entry can be Unmodelled, but it can never be
        // unexamined — there is no "just prose" state to fall into.
        Assert.All(
            Content.Monsters.SelectMany(monster => monster.Entries),
            entry => Assert.True(Enum.IsDefined(entry.Mechanics)));
    }

    [Fact]
    public void NothingIsBothStructuredAndSilentlyIncomplete()
    {
        // A structured entry with leftover clauses must say so. This is the check that
        // would have caught the goblin conditional-damage bug: the attack was structured
        // and the qualifier was not, and nothing recorded the difference.
        var structuredButIncomplete = TierOneEntries
            .Where(entry => entry.Mechanics != EntryMechanics.Unmodelled && !entry.IsFullyModelled)
            .ToList();

        Assert.All(structuredButIncomplete, entry => Assert.NotEmpty(entry.UnmodelledClauses));
    }

    [Fact]
    public void NarrativeEntriesHaveNoUnmodelledClauses()
    {
        // Narrative is a recorded decision that an entry does nothing in a fight. If one
        // carried an unmodelled clause, the decision was wrong.
        Assert.All(
            Content.Monsters
                .SelectMany(monster => monster.Entries)
                .Where(entry => entry.Mechanics == EntryMechanics.Narrative),
            entry => Assert.Empty(entry.UnmodelledClauses));
    }

    [Fact]
    public void TierOneCoverageDoesNotRegress()
    {
        // A floor, not a target. Raise it when coverage improves; a failure here means
        // either real progress worth recording or a parser regression worth finding.
        //
        // Lowered from 340 to 330 when Multiattack parsing was corrected. The number went
        // *down* while correctness went *up*: six entries had been recorded as one-attack
        // Multiattacks, which is not a Multiattack at all, and are now honestly reported
        // as not understood. Worth remembering that this metric can move the wrong way
        // for the right reason.
        //
        // Lowered again from 330 to 320 when condition riders were gated. Nine entries
        // gained — the size-gated Prone riders the engine now imposes — and twenty-three
        // were lost, every one of them an entry that had been claiming to be fully
        // modelled while carrying a condition nothing would ever apply. Thirteen were
        // attacks whose whole entry is a single sentence containing "Attack Roll:", so
        // "and the target has the Poisoned condition until the start of its next turn"
        // was accounted for by a clause that says nothing about it. The rest were
        // saving-throw entries whose Failure line imposes a condition; that is issue #6's
        // work, and until it lands the entries say so.
        const int Floor = 320;

        var modelled = TierOneEntries.Count(entry => entry.IsFullyModelled);

        Assert.True(
            modelled >= Floor,
            $"Only {modelled} of {TierOneEntries.Count} tier-one entries are fully modelled; the floor is {Floor}.");
    }

    [Fact]
    public void MultiattackReplaceClauseAccountingIsExact()
    {
        // The census the #290 fix earns: the bestiary's count of Multiattack entries
        // and how many of them carry a clause the model does not express are both fixed
        // by the source, exactly like the book's monster and spell totals — the
        // wrapped-class-list lesson says a floor is the wrong shape for a count the
        // source fixes. Pinning the exact pair here is what stops a future parser tweak
        // from silently re-swallowing what #290 and #342 surfaced (`DescribesTheComposition`
        // waving a replace-clause through again would drop this test's count without
        // touching `TierOneCoverageDoesNotRegress`, whose floor has slack to absorb it).
        //
        // Raised from 45 to 48 by #342: the Barbed Devil, the Clay Golem and the Medusa
        // each print a second whole composition ("or it makes ...") that
        // `ParseMultiattack` was summing into `AttackCount` instead of setting aside —
        // the Clay Golem's Multiattack swung five Slams against a printed maximum of
        // three. All three are CR 5+, so the tier-one pair is unchanged.
        var multiattacks = Content.Monsters
            .SelectMany(monster => monster.Entries
                .Where(entry => entry.Mechanics == EntryMechanics.Multiattack)
                .Select(entry => (monster.ChallengeRating, Entry: entry)))
            .ToList();

        Assert.Equal(170, multiattacks.Count);
        Assert.Equal(48, multiattacks.Count(multiattack => multiattack.Entry.UnmodelledClauses.Count > 0));

        var tierOne = multiattacks.Where(multiattack => multiattack.ChallengeRating <= 4m).ToList();

        Assert.Equal(64, tierOne.Count);
        Assert.Equal(8, tierOne.Count(multiattack => multiattack.Entry.UnmodelledClauses.Count > 0));
    }

    [Fact]
    public void AlternativeCompositionIsNotSummed()
    {
        // The fifth occurrence of the goblin's conditional-damage bug, and a real engine
        // bug rather than only an accounting one: the Clay Golem's "The golem makes two
        // Slam attacks, or it makes three Slam attacks if it used Hasten this turn" used
        // to sum both branches into `AttackCount: 5`, so the engine swung five Slams a
        // turn against a printed maximum of three. The Barbed Devil and the Medusa print
        // the same "or it/he/she/they makes ..." shape.
        //
        // The designer's standing reading: when print offers alternatives conditioned on
        // state the model lacks (here, whether Hasten was used this turn, or simply which
        // branch the creature picks), take the unconditional, first-printed branch as the
        // composition and account the rest — not approximate it by summing.
        var golem = Content.MonstersById["monster.clay-golem"];
        var golemMultiattack = golem.Entries.Single(entry => entry.Name == "Multiattack");
        var golemEffect = Assert.IsType<MultiattackEffect>(golemMultiattack.Multiattack);

        Assert.Equal(2, golemEffect.AttackCount);
        Assert.False(golemEffect.AnyCombination);
        Assert.Equal("Slam", Assert.Single(golemEffect.AttackNames));
        Assert.Equal(
            "Or it makes three Slam attacks if it used Hasten this turn.",
            Assert.Single(golemMultiattack.UnmodelledClauses));

        var devil = Content.MonstersById["monster.barbed-devil"];
        var devilMultiattack = devil.Entries.Single(entry => entry.Name == "Multiattack");
        var devilEffect = Assert.IsType<MultiattackEffect>(devilMultiattack.Multiattack);

        Assert.Equal(2, devilEffect.AttackCount);
        Assert.True(devilEffect.AnyCombination);
        Assert.Equal(["Claws", "Tail"], devilEffect.AttackNames);
        Assert.Equal(
            "Or it makes two Hurl Flame attacks.",
            Assert.Single(devilMultiattack.UnmodelledClauses));

        var medusa = Content.MonstersById["monster.medusa"];
        var medusaMultiattack = medusa.Entries.Single(entry => entry.Name == "Multiattack");
        var medusaEffect = Assert.IsType<MultiattackEffect>(medusaMultiattack.Multiattack);

        Assert.Equal(3, medusaEffect.AttackCount);
        Assert.True(medusaEffect.AnyCombination);
        Assert.Equal(["Claw", "Snake Hair"], medusaEffect.AttackNames);
        Assert.Equal(
            "Or it makes three Poison Ray attacks.",
            Assert.Single(medusaMultiattack.UnmodelledClauses));
    }

    [Fact]
    public void SavingThrowEffectsAreStructured()
    {
        // "Dexterity Saving Throw: DC 12, each creature in a 30-foot-long, 5-foot-wide
        // Line. Failure: 14 (4d6) Acid damage. Success: Half damage."
        var spray = Content
            .MonstersById["monster.ankheg"]
            .Entries
            .Single(entry => entry.Name == "Acid Spray");

        var save = Assert.IsType<SaveEffect>(spray.Save);

        Assert.Equal(EntryMechanics.SavingThrow, spray.Mechanics);
        Assert.Equal(Ability.Dexterity, save.Ability);
        Assert.Equal(12, save.DifficultyClass);
        Assert.Equal(SaveSuccessOutcome.HalfDamage, save.SuccessOutcome);

        var area = Assert.IsType<EffectArea>(save.Area);
        Assert.Equal(AreaShape.Line, area.Shape);
        Assert.Equal(30, area.SizeFeet);
        Assert.Equal(5, area.WidthFeet);

        var damage = Assert.Single(save.FailureDamage);
        Assert.Equal("4d6", damage.Amount.ToString());
        Assert.Equal(DamageType.Acid, damage.Type);
    }

    [Fact]
    public void UsageLimitsComeOffTheEntryName()
    {
        var spray = Content
            .MonstersById["monster.ankheg"]
            .Entries
            .Single(entry => entry.Name == "Acid Spray");

        var usage = Assert.IsType<UsageLimit>(spray.Usage);
        Assert.Equal(UsageLimitKind.Recharge, usage.Kind);
        Assert.Equal(6, usage.RechargeMinimum);

        // The name itself is stored without the suffix, so it reads as an action.
        Assert.Equal("Acid Spray", spray.Name);
    }

    [Fact]
    public void MultiattackIsStructured()
    {
        var bandit = Content.MonstersById["monster.bandit-captain"];
        var multiattack = bandit.Entries.Single(entry => entry.Name == "Multiattack");

        var effect = Assert.IsType<MultiattackEffect>(multiattack.Multiattack);

        Assert.Equal(EntryMechanics.Multiattack, multiattack.Mechanics);
        Assert.Equal(2, effect.AttackCount);
        Assert.True(effect.AnyCombination);
        Assert.Equal(["Scimitar", "Pistol"], effect.AttackNames);
    }

    [Fact]
    public void ARepeatedAttackMultiattackNamesTheOneAttack()
    {
        var armor = Content.MonstersById["monster.animated-armor"];
        var effect = Assert.IsType<MultiattackEffect>(
            armor.Entries.Single(entry => entry.Name == "Multiattack").Multiattack);

        // "The armor makes two Slam attacks."
        Assert.Equal(2, effect.AttackCount);
        Assert.False(effect.AnyCombination);
        Assert.Equal("Slam", Assert.Single(effect.AttackNames));
    }

    [Fact]
    public void AMultiattackReplaceClauseIsCountedNotDropped()
    {
        // The fourth occurrence of the goblin's conditional-damage bug: Multiattack used
        // to answer "fully modelled" for every one of its sentences, so "It can replace
        // one attack with a use of Roar/Enthralling Panache/..." vanished from the
        // accounting while the engine never executed it (#290). The composition sentence
        // itself — the part the model does express — must still read as structured; only
        // the replace-clause is unmodelled.
        var lion = Content.MonstersById["monster.lion"];
        var lionMultiattack = lion.Entries.Single(entry => entry.Name == "Multiattack");

        Assert.Equal(EntryMechanics.Multiattack, lionMultiattack.Mechanics);
        Assert.False(lionMultiattack.IsFullyModelled);
        Assert.Equal(
            "It can replace one attack with a use of Roar.",
            Assert.Single(lionMultiattack.UnmodelledClauses));

        var pirate = Content.MonstersById["monster.pirate"];
        var pirateMultiattack = pirate.Entries.Single(entry => entry.Name == "Multiattack");

        Assert.Equal(EntryMechanics.Multiattack, pirateMultiattack.Mechanics);
        Assert.False(pirateMultiattack.IsFullyModelled);
        Assert.Equal(
            "It can replace one attack with a use of Enthralling Panache.",
            Assert.Single(pirateMultiattack.UnmodelledClauses));
    }

    [Fact]
    public void ConditionsImposedByAnEntryAreCaptured()
    {
        // "... plus 3 (1d6) Acid damage. If the target is a Large or smaller creature, it
        // has the Grappled condition (escape DC 13)."
        var bite = Content
            .MonstersById["monster.ankheg"]
            .Entries
            .Single(entry => entry.Name == "Bite");

        var grappled = Assert.Single(bite.AppliedConditions);
        Assert.Equal(ConditionType.Grappled, grappled.Condition);
        Assert.Equal(13, grappled.EscapeDifficultyClass);
    }

    [Fact]
    public void ASizeGateIsReadOffTheRider()
    {
        // "If the target is a Medium or smaller creature, it has the Prone condition."
        // The gate is the whole qualifier, so the rider is complete and the engine can
        // impose it on exactly the creatures the SRD names.
        var bite = Content.MonstersById["monster.wolf"].Entries.Single(entry => entry.Name == "Bite");

        var prone = Assert.Single(bite.AppliedConditions);

        Assert.Equal(ConditionType.Prone, prone.Condition);
        Assert.Equal(CreatureSize.Medium, prone.MaximumTargetSize);
        Assert.True(prone.IsFullyModelled);
        Assert.True(bite.IsFullyModelled);

        Assert.True(prone.AllowsTargetSize(CreatureSize.Small));
        Assert.True(prone.AllowsTargetSize(CreatureSize.Medium));
        Assert.False(prone.AllowsTargetSize(CreatureSize.Large));
    }

    [Fact]
    public void AGateWithMoreThanSizeInItIsNotReducedToASizeGate()
    {
        // "If the target is a Large or smaller creature and the allosaurus moved 30+ feet
        // straight toward it immediately before the hit ..." — reading the size and
        // discarding the charge would knock targets Prone on every hit instead of on a
        // charge, which is the goblin conditional-damage bug in a new place. So the whole
        // sentence comes back as unmodelled and no gate is claimed at all.
        var claws = Content.MonstersById["monster.allosaurus"].Entries.Single(entry => entry.Name == "Claws");

        var prone = Assert.Single(claws.AppliedConditions);

        Assert.Equal(ConditionType.Prone, prone.Condition);
        Assert.Null(prone.MaximumTargetSize);
        Assert.False(prone.IsFullyModelled);
        Assert.Contains("moved 30+ feet", prone.UnmodelledRequirement!, StringComparison.Ordinal);
        Assert.False(claws.IsFullyModelled);
    }

    [Fact]
    public void ATurnBoundaryDurationIsModelled()
    {
        // "... and the target has the Poisoned condition until the start of the
        // centipede's next turn."
        var bite = Content.MonstersById["monster.giant-centipede"].Entries.Single(entry => entry.Name == "Bite");

        var poisoned = Assert.Single(bite.AppliedConditions);
        var duration = Assert.IsType<ConditionDuration>(poisoned.Duration);

        Assert.Equal(ConditionType.Poisoned, poisoned.Condition);
        Assert.Equal(ConditionClock.StartOfTurn, duration.Clock);
        Assert.Equal(ConditionDurationOwner.Source, duration.Owner);
        Assert.True(poisoned.IsFullyModelled);
        Assert.True(bite.IsFullyModelled);
    }

    [Fact]
    public void TheDurationOwnerComesFromThePossessive()
    {
        // The distinction that is easy to get backwards and costs most of a round when
        // you do. "its" is the creature carrying the condition; a name is the creature
        // that imposed it.
        var giant = Content.MonstersById["monster.hill-giant"].Entries.Single(entry => entry.Name == "Trash Lob");
        var devil = Content.MonstersById["monster.bearded-devil"].Entries.Single(entry => entry.Name == "Beard");

        // "... the target has the Poisoned condition until the end of its next turn."
        Assert.Equal(
            new ConditionDuration(ConditionClock.EndOfTurn, ConditionDurationOwner.Bearer),
            Assert.Single(giant.AppliedConditions).Duration);

        // "... until the start of the devil's next turn."
        Assert.Equal(
            new ConditionDuration(ConditionClock.StartOfTurn, ConditionDurationOwner.Source),
            Assert.Single(devil.AppliedConditions).Duration);
    }

    [Fact]
    public void AGatedRiderIsStillARequirementWhateverItsDuration()
    {
        // The Phase Spider's bite poisons "for 1 hour" — a duration the model now
        // expresses — and the rider still cannot be imposed, because its sentence opens
        // with "If this damage reduces the target to 0 Hit Points" and closes into
        // "While Poisoned, the target also has the Paralyzed condition": a gate and a
        // chained condition the model has no vocabulary for. (Until timed durations
        // landed, this entry was described as refused on the duration alone; the gate
        // was always there too, and it is what still holds the rider.)
        var bite = Content.MonstersById["monster.phase-spider"].Entries.Single(entry => entry.Name == "Bite");

        var poisoned = bite.AppliedConditions.Single(applied => applied.Condition == ConditionType.Poisoned);

        Assert.True(ConditionRules.IsExecutable(ConditionType.Poisoned));
        Assert.Null(poisoned.Duration);
        Assert.False(poisoned.IsFullyModelled);
        Assert.False(ConditionRules.CanBeImposed(poisoned));
        Assert.False(bite.IsFullyModelled);
    }

    [Fact]
    public void TimedDurationsLandWhereTheSentenceIsComplete()
    {
        // The two riders a timed duration unlocks, one of each shape. The Solar's
        // Blinding Gaze blinds "for 1 minute" — ten of the bearer's turns — and the
        // Pseudodragon's Sting poisons "for 1 hour", which no fight reaches, so the
        // rider lands with no expiry at all.
        var gaze = Content.MonstersById["monster.solar"].Entries.Single(entry => entry.Name == "Blinding Gaze");
        var blinded = gaze.Save!.AppliedConditions.Single(applied => applied.Condition == ConditionType.Blinded);

        Assert.Equal(ConditionDuration.ForMinutes(1), blinded.Duration);
        Assert.True(ConditionRules.CanBeImposed(blinded));

        var sting = Content.MonstersById["monster.pseudodragon"].Entries.Single(entry => entry.Name == "Sting");
        var poisoned = sting.Save!.AppliedConditions.Single(applied => applied.Condition == ConditionType.Poisoned);

        Assert.Equal(ConditionDuration.BeyondTheFight, poisoned.Duration);
        Assert.True(ConditionRules.CanBeImposed(poisoned));
    }

    [Fact]
    public void AGrappleTiedRiderIsOnlyAsModelledAsItsGrapple()
    {
        // The Purple Worm's one sentence imposes two conditions, and both ride: the
        // Grappled with its escape DC and size gate, the Restrained tied to it. The
        // Chain Devil prints the same pair with "from one of two chains" on the grapple
        // — limb bookkeeping the model does not express — so its Grappled is refused,
        // and the Restrained that would ride a grapple that can never land is refused
        // with it.
        var bite = Content.MonstersById["monster.purple-worm"].Entries.Single(entry => entry.Name == "Bite");
        var grappled = bite.AppliedConditions.Single(applied => applied.Condition == ConditionType.Grappled);
        var restrained = bite.AppliedConditions.Single(applied => applied.Condition == ConditionType.Restrained);

        Assert.True(ConditionRules.CanBeImposed(grappled));
        Assert.True(ConditionRules.CanBeImposed(restrained));
        Assert.Equal(ConditionDuration.UntilTheGrappleEnds, restrained.Duration);

        var chain = Content.MonstersById["monster.chain-devil"].Entries.Single(entry => entry.Name == "Chain");

        Assert.All(
            chain.AppliedConditions,
            applied => Assert.False(ConditionRules.CanBeImposed(applied)));
    }

    [Fact]
    public void ACompanionEffectInTheSentenceRefusesItsRiders()
    {
        // "If the target is a Huge or smaller creature, the balor pulls the target up
        // to 25 feet straight toward itself, and the target has the Prone condition."
        // The Prone clause alone is clean; the pull is not, and knocking the target
        // Prone without pulling it would fire part of a printed sentence.
        var whip = Content.MonstersById["monster.balor"].Entries.Single(entry => entry.Name == "Flame Whip");
        var prone = whip.AppliedConditions.Single(applied => applied.Condition == ConditionType.Prone);

        Assert.False(prone.IsFullyModelled);
        Assert.Contains("pulls the target", prone.UnmodelledRequirement, StringComparison.Ordinal);
    }

    [Fact]
    public void ARiderBehindADeeperFailureTierIsRefused()
    {
        // "Second Failure: The target has the Unconscious condition for 1 minute." The
        // save model rolls one failure, so a rider printed behind a deeper tier would
        // land a whole tier early — a wyrmling's breath putting targets to sleep on the
        // first failed save. The duration alone would parse; the tier is why it must not.
        var breath = Content.MonstersById["monster.brass-dragon-wyrmling"].Entries
            .Single(entry => entry.Name == "Sleep Breath");
        var unconscious = breath.Save!.AppliedConditions
            .Single(applied => applied.Condition == ConditionType.Unconscious);

        Assert.False(unconscious.IsFullyModelled);
        Assert.False(ConditionRules.CanBeImposed(unconscious));
        Assert.Contains("Second Failure", unconscious.UnmodelledRequirement, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTwoTierGazeStructuresAsAnEscalatingRider()
    {
        // The carved exception to the tier rule above: "First Failure: The target has
        // the Restrained condition and repeats the save at the end of its next turn if
        // it is still Restrained, ending the effect on itself on a success. Second
        // Failure: The target has the Petrified condition instead of the Restrained
        // condition." — matched to the letter, and structured as a single escalating
        // rider rather than two refused ones. The corpus prints it twice; both must
        // land, and only the reflection clause stays counted.
        foreach (var id in new[] { "monster.basilisk", "monster.medusa" })
        {
            var gaze = Content.MonstersById[id].Entries.Single(entry => entry.Name == "Petrifying Gaze");

            Assert.Equal(MonsterEntrySection.BonusAction, gaze.Section);
            Assert.Equal(EntryMechanics.SavingThrow, gaze.Mechanics);

            var rider = Assert.Single(gaze.Save!.AppliedConditions);

            Assert.Equal(ConditionType.Restrained, rider.Condition);
            Assert.Equal(ConditionType.Petrified, rider.EscalatesTo);
            Assert.True(rider.Duration is { RepeatSaveAtTurnEnd: true, OutlastsFight: true });
            Assert.True(ConditionRules.CanBeImposed(rider));

            var leftover = Assert.Single(gaze.UnmodelledClauses);

            Assert.Contains("reflection", leftover, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ACompleteRiderTheEngineCannotExecuteIsStillReportedAsIncomplete()
    {
        // "... the target has the Deafened condition until the start of the swarm's
        // next turn." Every word of that is modelled — the duration included — and
        // Deafened is still not a condition the engine executes, so imposing it would
        // put a label on the target that changes nothing. That is the quietest possible
        // way to be wrong, so the entry reports itself incomplete instead. (The Sprite's
        // Charmed held this role until Charmed joined the allowlist.)
        var cacophony = Content.MonstersById["monster.swarm-of-ravens"].Entries
            .Single(entry => entry.Name == "Cacophony");

        var deafened = Assert.Single(cacophony.AppliedConditions);

        Assert.Equal(EntryMechanics.SavingThrow, cacophony.Mechanics);
        Assert.True(deafened.IsFullyModelled);
        Assert.NotNull(deafened.Duration);
        Assert.False(ConditionRules.IsExecutable(ConditionType.Deafened));
        Assert.False(ConditionRules.CanBeImposed(deafened));

        Assert.False(cacophony.IsFullyModelled);
        Assert.Contains(cacophony.UnmodelledClauses, clause => clause.Contains("Deafened", StringComparison.Ordinal));
    }

    [Fact]
    public void AGrappleRiderCarriesItsEscapeDifficultyClass()
    {
        // "If the target is a Large or smaller creature, it has the Grappled condition
        // (escape DC 13)." The DC is part of the condition rather than of the creature,
        // and without it the grapple would be inescapable.
        var bite = Content.MonstersById["monster.ankheg"].Entries.Single(entry => entry.Name == "Bite");

        var grappled = Assert.Single(bite.AppliedConditions);

        Assert.Equal(ConditionType.Grappled, grappled.Condition);
        Assert.Equal(13, grappled.EscapeDifficultyClass);
        Assert.Equal(CreatureSize.Large, grappled.MaximumTargetSize);
        Assert.True(ConditionRules.CanBeImposed(grappled));
        Assert.True(bite.IsFullyModelled);
    }

    [Fact]
    public void EveryImposableRiderIsAConditionTheEngineExecutes()
    {
        // The gap between "the model expresses this" and "the engine does this" is the
        // one that has to stay countable. Everything that survives both checks must be on
        // ConditionRules' curated list — nothing reaches a fight by another route.
        var imposable = Content.Monsters
            .SelectMany(monster => monster.Entries)
            .SelectMany(entry => entry.AppliedConditions)
            .Where(ConditionRules.CanBeImposed)
            .ToList();

        Assert.NotEmpty(imposable);
        Assert.All(imposable, condition => Assert.True(ConditionRules.IsExecutable(condition.Condition)));
    }

    [Fact]
    public void ReactionsSplitTriggerFromResponse()
    {
        var parry = Content
            .MonstersById["monster.bandit-captain"]
            .Entries
            .Single(entry => entry.Name == "Parry");

        var reaction = Assert.IsType<ReactionEffect>(parry.Reaction);

        Assert.Equal(EntryMechanics.Reaction, parry.Mechanics);
        Assert.Equal(MonsterEntrySection.Reaction, parry.Section);
        Assert.Contains("hit by a melee attack", reaction.Trigger, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adds 2", reaction.Response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RealMechanicsAreNeverQuietlyDismissed()
    {
        // Traits that look like flavour but are not: each of these changes how a fight
        // goes, and none may be classified Narrative.
        string[] mustNotBeInert = ["Pack Tactics", "Magic Resistance", "Sunlight Sensitivity", "Flyby"];

        var wronglyInert = Content.Monsters
            .SelectMany(monster => monster.Entries)
            .Where(entry => entry.Mechanics == EntryMechanics.Narrative)
            .Where(entry => mustNotBeInert.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
            .Select(entry => entry.Name)
            .Distinct()
            .ToList();

        Assert.Empty(wronglyInert);
    }
}
