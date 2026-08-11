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
    public void APrintedDurationIsAnUnmodelledRequirement()
    {
        // "... and the target has the Poisoned condition until the start of the
        // centipede's next turn." The engine has no clock to end a condition on, so the
        // duration makes the rider unusable rather than approximate — a Poisoned that
        // never wears off is not a smaller error than one that never lands.
        //
        // This entry is a single sentence containing "Attack Roll:", which is why it read
        // as fully modelled before the rider was examined at all.
        var bite = Content.MonstersById["monster.giant-centipede"].Entries.Single(entry => entry.Name == "Bite");

        var poisoned = Assert.Single(bite.AppliedConditions);

        Assert.Equal(ConditionType.Poisoned, poisoned.Condition);
        Assert.False(poisoned.IsFullyModelled);
        Assert.Contains("until the start of", poisoned.UnmodelledRequirement!, StringComparison.Ordinal);
        Assert.False(bite.IsFullyModelled);
    }

    [Fact]
    public void ACompleteRiderTheEngineCannotExecuteIsStillReportedAsIncomplete()
    {
        // "If the target is a Large or smaller creature, it has the Grappled condition
        // (escape DC 13)." Every word of that is now modelled — but Grappled needs a
        // speed of 0 and an Escape action against the printed DC, and the engine has
        // neither. Imposing it would put a condition on the target that changes nothing,
        // which is the quietest possible way to be wrong, so the entry reports itself
        // incomplete instead.
        var bite = Content.MonstersById["monster.ankheg"].Entries.Single(entry => entry.Name == "Bite");

        var grappled = Assert.Single(bite.AppliedConditions);

        Assert.Equal(EntryMechanics.Attack, bite.Mechanics);
        Assert.Equal(CreatureSize.Large, grappled.MaximumTargetSize);
        Assert.True(grappled.IsFullyModelled);
        Assert.False(ConditionRules.CanBeImposed(grappled));

        Assert.False(bite.IsFullyModelled);
        Assert.Contains(bite.UnmodelledClauses, clause => clause.Contains("Grappled", StringComparison.Ordinal));
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
