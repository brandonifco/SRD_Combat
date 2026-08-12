using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The gauntlet's loot: one executed item after each High milestone, equipped by
/// re-resolving the finder's draft, deterministic from the seed.
/// </summary>
public class LootTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void ADropAlwaysImprovesSomebodyAndResolves()
    {
        var run = GauntletRun.Start(Content);

        for (var seed = 1; seed <= 20; seed++)
        {
            var award = LootTable.Roll(Content, run.Party, run.States, new SeededRandomSource(seed));

            Assert.NotNull(award);
            Assert.NotEmpty(award.Description);
            Assert.True(award.NewDraft.MagicItems.Count
                > run.Party[award.MemberIndex].Draft.MagicItems.Count);
        }
    }

    [Fact]
    public void TheSameSeedFindsTheSameLoot()
    {
        var run = GauntletRun.Start(Content);

        var first = LootTable.Roll(Content, run.Party, run.States, new SeededRandomSource(7));
        var second = LootTable.Roll(Content, run.Party, run.States, new SeededRandomSource(7));

        Assert.Equal(first!.Description, second!.Description);
        Assert.Equal(first.MemberIndex, second.MemberIndex);
    }

    [Fact]
    public void RareItemsWaitForLevelThree()
    {
        var levelOne = GauntletRun.Start(Content);

        // At level 1 only Uncommon drops exist; nothing offered may be Rare or dearer.
        for (var seed = 1; seed <= 30; seed++)
        {
            var award = LootTable.Roll(Content, levelOne.Party, levelOne.States, new SeededRandomSource(seed));
            var equipped = award!.NewDraft.MagicItems[^1];
            var definition = Content.MagicItemsById[equipped.ItemId];

            var rarity = definition.Variants.Count > 0
                ? definition.Variants.Single(variant => variant.Suffix == equipped.Variant).Rarity
                : definition.Rarity;

            Assert.True(
                rarity is Core.Definitions.MagicItemRarity.Common or Core.Definitions.MagicItemRarity.Uncommon,
                $"{award.Description} is {rarity} and dropped at level 1.");
        }
    }

    [Fact]
    public void AnOwnedPlusOneUpgradesInPlaceRatherThanDuplicating()
    {
        var run = GauntletRun.Start(Content, startingLevel: 3);
        var fighter = run.Party[0];

        var withPlusOne = fighter.Draft with
        {
            MagicItems =
            [
                new Core.Characters.EquippedMagicItem
                {
                    ItemId = "magic-item.weapon-plus-1-plus-2-or-plus-3",
                    Variant = "+1",
                    BoundWeaponId = fighter.Draft.WeaponIds[0],
                },
            ],
        };

        var upgraded = PregeneratedParty.Resolve(Content, withPlusOne, 3);
        var party = new[] { upgraded };
        var states = new[] { run.States[0] with { ExperiencePoints = AdvancementRules.ExperienceToReach(3) } };

        // Roll until the weapon upgrade comes up; it must replace the +1, not stack.
        for (var seed = 1; seed <= 50; seed++)
        {
            var award = LootTable.Roll(Content, party, states, new SeededRandomSource(seed));

            if (award is null || !award.Description.StartsWith("a +2 Longsword", StringComparison.Ordinal))
            {
                continue;
            }

            var weaponItems = award.NewDraft.MagicItems
                .Where(item => item.ItemId == "magic-item.weapon-plus-1-plus-2-or-plus-3")
                .ToArray();

            Assert.Single(weaponItems);
            Assert.Equal("+2", weaponItems[0].Variant);
            return;
        }

        Assert.Fail("No weapon upgrade dropped in 50 seeds.");
    }

    [Fact]
    public void AMilestoneDropsLootAndARoutineFightDoesNot()
    {
        // A one-rung High ladder: complete it with a random source and loot lands.
        var high = GauntletRun.Start(Content, [new LadderStep(EncounterDifficulty.High)]);
        PlayOut(high, seed: 3);

        if (high.Outcome == RunOutcome.Survived)
        {
            Assert.Single(high.LootFound);
            Assert.Contains(high.Party, member => member.Draft.MagicItems.Count > 0);
        }

        // Low rungs never drop, however many are cleared.
        var low = GauntletRun.Start(Content, [
            new LadderStep(EncounterDifficulty.Low),
            new LadderStep(EncounterDifficulty.Low, RestKind.Short),
        ]);
        PlayOut(low, seed: 3);

        Assert.Empty(low.LootFound);
    }

    [Fact]
    public void FoundLootSurvivesASaveAndReload()
    {
        var run = GauntletRun.Start(Content, [new LadderStep(EncounterDifficulty.High)]);
        PlayOut(run, seed: 11);

        if (run.LootFound.Count == 0)
        {
            return; // The party lost the milestone on this seed; nothing to save.
        }

        var reloaded = GauntletRun.Resume(Content, RunSave.FromJson(RunSave.ToJson(run)));
        var finder = reloaded.Party.Single(member => member.Draft.MagicItems.Count > 0);
        var original = run.Party.Single(member => member.Draft.MagicItems.Count > 0);

        Assert.Equal(original.Draft.MagicItems, finder.Draft.MagicItems);
        Assert.Equal(original.Sheet.ArmorClass, finder.Sheet.ArmorClass);
        Assert.Equal(original.Sheet.Attacks[0].AttackBonus, finder.Sheet.Attacks[0].AttackBonus);
    }

    [Fact]
    public void ASeededRunWithLootPlaysToItsEndWithoutRefusals()
    {
        // The long-haul guard: thirty rungs of drops, upgrades and re-resolves must
        // never produce a draft the resolver refuses mid-run.
        for (var seed = 1; seed <= 10; seed++)
        {
            var run = GauntletRun.Start(Content);
            PlayOut(run, seed);

            Assert.NotEqual(RunOutcome.InProgress, run.Outcome);
        }
    }

    private static void PlayOut(GauntletRun run, int seed)
    {
        var random = new SeededRandomSource(seed);

        while (run.Next is not null)
        {
            run.PrepareForNext(random);
            var fight = run.BeginNext(random);
            SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
            run.CompleteFight(fight, random);
        }
    }
}
