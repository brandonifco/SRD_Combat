using SRDCombat.Core.Definitions;

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
        const int Floor = 340;

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
    public void ACapturedConditionWithAnUnmodelledGateIsStillReportedAsIncomplete()
    {
        // The condition is extracted, but "if the target is a Large or smaller creature"
        // is a gate the engine cannot evaluate. Applying the condition anyway would
        // impose it in more cases than the SRD allows — the same shape of bug as the
        // goblin's conditional damage. So the entry must report itself incomplete.
        var bite = Content
            .MonstersById["monster.ankheg"]
            .Entries
            .Single(entry => entry.Name == "Bite");

        Assert.Equal(EntryMechanics.Attack, bite.Mechanics);
        Assert.NotEmpty(bite.AppliedConditions);
        Assert.False(bite.IsFullyModelled);
        Assert.Contains(bite.UnmodelledClauses, clause => clause.Contains("Grappled", StringComparison.Ordinal));
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
