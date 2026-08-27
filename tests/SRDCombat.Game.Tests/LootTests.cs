using SRDCombat.Content;
using SRDCombat.Core.Characters;
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

    /// <summary>
    /// #534: a client reading which <see cref="LootFound"/> line was a permanent item
    /// and whom it landed on used to have nothing but the announcement's prose to go
    /// on. <see cref="MagicItemFinders"/> names the party index directly, so the
    /// interlude can read that finder's resolved sheet for the readout rather than
    /// parsing "X finds Y!" to guess. A Moderate rung's potion must never appear here —
    /// it is not equipped, and <c>MagicItemNames</c> would have nothing to say about it.
    /// </summary>
    [Fact]
    public void AMagicItemDropIsRecordedByFinderAndAPotionIsNot()
    {
        var high = GauntletRun.Start(Content, [new LadderStep(EncounterDifficulty.High)]);
        PlayOut(high, seed: 3);

        if (high.Outcome == RunOutcome.Survived)
        {
            var finder = Assert.Single(high.MagicItemFinders);
            Assert.True(high.Party[finder].Draft.MagicItems.Count > 0);
        }

        var moderate = GauntletRun.Start(Content, [new LadderStep(EncounterDifficulty.Moderate)]);
        PlayOut(moderate, seed: 3);

        if (moderate.Outcome == RunOutcome.Survived)
        {
            Assert.Single(moderate.LootFound);
        }

        Assert.Empty(moderate.MagicItemFinders);
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

    /// <summary>
    /// #350: <c>CandidatesFor</c>'s weapon-enchantment candidate used to read
    /// <c>content.WeaponsById[weaponId]</c> directly — a raw indexer reachable during
    /// live play (an equip resolved from a save whose weapon id has since drifted out
    /// of the loaded content), unlike the provably-unreachable <c>Gauntlet</c> siblings
    /// #348 converted for consistency. It now refuses through
    /// <see cref="ContentDrift.Require{TValue}"/> instead of throwing a bare
    /// <see cref="KeyNotFoundException"/>.
    /// </summary>
    [Fact]
    public void ARollRefusesWhenACarriedWeaponIdHasDrifted()
    {
        var run = GauntletRun.Start(Content);
        var fighter = run.Party.Single(member => member.Draft.Name == "Brenna");
        var drifted = fighter with
        {
            Draft = fighter.Draft with { WeaponIds = ["weapon.nonexistent", .. fighter.Draft.WeaponIds.Skip(1)] },
        };
        var party = run.Party.Select(member => member == fighter ? drifted : member).ToArray();

        var failure = Assert.Throws<InvalidDataException>(
            () => LootTable.Roll(Content, party, run.States, new SeededRandomSource(1)));

        Assert.Contains(
            "the save names weapon 'weapon.nonexistent' (for Brenna)", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// #350: <c>CandidatesFor</c>'s armour-enchantment candidate used to read
    /// <c>content.ArmorById[armorId]</c> directly, the same bug class as the weapon
    /// site above. Armour drifts independently of a carried weapon, so this exercises
    /// the converted lookup on its own rather than piggybacking on the weapon test.
    /// </summary>
    [Fact]
    public void ARollRefusesWhenWornArmorIdHasDrifted()
    {
        var run = GauntletRun.Start(Content);
        var fighter = run.Party.Single(member => member.Draft.Name == "Brenna");
        var drifted = fighter with { Draft = fighter.Draft with { ArmorId = "armor.nonexistent" } };
        var party = run.Party.Select(member => member == fighter ? drifted : member).ToArray();

        var failure = Assert.Throws<InvalidDataException>(
            () => LootTable.Roll(Content, party, run.States, new SeededRandomSource(1)));

        Assert.Contains(
            "the save names armor 'armor.nonexistent' (for Brenna)", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// #366: unlike the sibling above, this member already owns an armour-applying
    /// magic item, so <c>CandidatesFor</c>'s own armour block is skipped entirely —
    /// its guard is <c>!OwnsAnyArmorItem(content, draft)</c> — and Loot's own
    /// <see cref="ContentDrift.Require{TValue}"/> call for armour never runs. The
    /// drifted id is only reached indirectly: this member's *other* candidates (a
    /// weapon enchant, here) still carry the untouched, already-equipped armour magic
    /// item in their <see cref="LootAward.NewDraft"/>, so <see cref="Resolves"/>
    /// re-resolving them walks into <see cref="CharacterResolver"/>'s own armour
    /// lookup inside its <c>ValidatePlacement</c> helper. That lookup used to be a raw
    /// <c>content.Armor[armorId]</c> indexer (<c>CharacterResolver.cs:253</c>) that
    /// threw a bare <see cref="KeyNotFoundException"/> past this method's catch
    /// clause; fixed to <see cref="ArgumentException"/> alongside the Resume-path pin
    /// <c>RunSaveTests.ResumingRefusesADraftWhoseArmorMagicItemPointsAtAMissingArmorId</c>.
    /// Unlike every sibling test in this fixture, the expectation here is the
    /// opposite of a refusal: <see cref="LootTable.Roll"/> must not throw at all —
    /// <see cref="Resolves"/>'s catch clause already absorbs <see cref="ArgumentException"/>,
    /// silently dropping the poisoned candidate rather than crashing the whole roll.
    /// </summary>
    [Fact]
    public void ARollDoesNotCrashWhenAnArmorMagicItemsOwnerHasADriftedArmorId()
    {
        var run = GauntletRun.Start(Content);
        var fighter = run.Party.Single(member => member.Draft.Name == "Brenna");
        Assert.NotNull(fighter.Draft.ArmorId);

        var poisoned = fighter with
        {
            Draft = fighter.Draft with
            {
                ArmorId = "armor.nonexistent",
                MagicItems =
                [
                    new EquippedMagicItem
                    {
                        ItemId = "magic-item.armor-plus-1-plus-2-or-plus-3",
                        Variant = "+1",
                    },
                ],
            },
        };
        var party = run.Party.Select(member => member == fighter ? poisoned : member).ToArray();

        for (var seed = 1; seed <= 20; seed++)
        {
            var exception = Record.Exception(
                () => LootTable.Roll(Content, party, run.States, new SeededRandomSource(seed)));

            Assert.Null(exception);
        }
    }

    /// <summary>
    /// #350's third site: the Gauntlets-of-Ogre-Power candidate's own melee check
    /// re-reads <c>content.WeaponsById[draft.WeaponIds[0]]</c> — the identical id the
    /// weapon-enchantment candidate above already reads first in <c>CandidatesFor</c>'s
    /// fixed order, so no input can reach this site's converted lookup without the
    /// earlier one refusing first. This pins that the same drifted id still refuses
    /// cleanly with a party built to qualify for the Gauntlets branch specifically
    /// (Strength under 19, a carried melee weapon, no existing Gauntlets) — documented
    /// rather than left implicit, so a future reordering of <c>CandidatesFor</c> that
    /// made this site reachable independently would still be covered.
    /// </summary>
    [Fact]
    public void ARollRefusesForAGauntletsOfOgrePowerCandidateWhoseWeaponIdHasDrifted()
    {
        var run = GauntletRun.Start(Content);
        var fighter = run.Party.Single(member => member.Draft.Name == "Brenna");

        Assert.True(fighter.Sheet.AbilityScores[Core.Definitions.Ability.Strength] < 19);

        var drifted = fighter with
        {
            Draft = fighter.Draft with { WeaponIds = ["weapon.nonexistent", .. fighter.Draft.WeaponIds.Skip(1)] },
        };
        var party = run.Party.Select(member => member == fighter ? drifted : member).ToArray();

        var failure = Assert.Throws<InvalidDataException>(
            () => LootTable.Roll(Content, party, run.States, new SeededRandomSource(1)));

        Assert.Contains("weapon 'weapon.nonexistent'", failure.Message, StringComparison.Ordinal);
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
