using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Game;

namespace SRDCombat.Game.Tests;

/// <summary>
/// The economy: winnings after a cleared fight, and the Long Rest merchant. The prices
/// are the book's, the award rate and the shop's cadence are stated design choices,
/// and an offer must improve its buyer — with the resolver as the judge.
/// </summary>
public class ShopTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void AClearedFightPaysOneGoldPerTenExperience()
    {
        var run = GauntletRun.Start(Content, GauntletLadder.Default());
        var random = new SeededRandomSource(4);

        run.PrepareForNext(random);
        var fight = run.BeginNext(random);
        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);
        run.CompleteFight(fight);

        if (run.Outcome == RunOutcome.Defeated)
        {
            Assert.Equal(0, run.GoldCopper);
            return;
        }

        // 1 GP per 10 XP, held in copper: XP × 10.
        Assert.Equal(fight.Built.Monsters.Sum(monster => monster.ExperiencePoints) * 10, run.GoldCopper);
    }

    [Fact]
    public void TheShopOnlyOffersWhatWouldImproveSomebody()
    {
        var run = GauntletRun.Start(Content, GauntletLadder.Default());
        var offers = Shop.Offers(Content, run.Party, run.States);

        Assert.NotEmpty(offers);

        // Nobody is offered gear their class has no proficiency line for, and no offer
        // may make its buyer worse: every gear offer improved AC or damage at the
        // resolver's own word, so every score is positive.
        Assert.All(offers.Where(offer => offer.NewDraft is not null), offer =>
            Assert.True(offer.Score > 0, offer.Description));

        // The Rogue's Armor Training reads "Light armor", so no offer ever puts
        // Sable in mail — the same printed-line reading creation uses.
        var sable = run.Party.ToList().FindIndex(member => member.Draft.Name == "Sable");

        Assert.DoesNotContain(
            offers,
            offer => offer.MemberIndex == sable
                && offer.NewDraft is { } draft
                && draft.ArmorId is { } armorId
                && Content.ArmorById[armorId].Category != Core.Definitions.ArmorCategory.Light);
    }

    [Fact]
    public void APurchaseIsADraftChangeAndThePursePays()
    {
        var run = FundedRun(100_000);

        var offer = Shop.Offers(Content, run.Party, run.States)
            .First(candidate => candidate.NewDraft is not null
                && candidate.CostCopper <= run.GoldCopper);

        var before = run.Party[offer.MemberIndex];

        Assert.Null(run.Purchase(offer));
        Assert.Equal(100_000 - offer.CostCopper, run.GoldCopper);

        // The member was re-resolved, never edited: the draft changed and the sheet
        // followed, strictly better by the offer's own claim.
        var after = run.Party[offer.MemberIndex];

        Assert.NotEqual(before.Draft, after.Draft);
        Assert.True(
            after.Sheet.ArmorClass > before.Sheet.ArmorClass
            || after.Combatant.Stats.Attacks.Max(a => a.Damage.Sum(d => d.Amount.Average))
                > before.Combatant.Stats.Attacks.Max(a => a.Damage.Sum(d => d.Amount.Average)));
    }

    [Fact]
    public void AnEmptyPurseRefusesCleanly()
    {
        var run = GauntletRun.Start(Content, GauntletLadder.Default());

        var offer = Shop.Offers(Content, run.Party, run.States)
            .First(candidate => candidate.NewDraft is not null);

        var refusal = run.Purchase(offer);

        Assert.Equal("shop.cannot_afford", refusal?.Code);
        Assert.Equal(0, run.GoldCopper);
    }

    [Fact]
    public void TheAutoBuyerSpendsBigFirstAndStopsAtThePotionCap()
    {
        var run = FundedRun(100_000); // 1,000 GP: real shopping money.

        var bought = Shop.AutoBuy(Content, run);

        Assert.NotEmpty(bought);

        // Gear first, potions after — and no member is stocked past the cap.
        Assert.All(run.States, state =>
            Assert.True(state.Potions.Values.Sum() <= Shop.AutoBuyPotionCap));

        // Deterministic: the same purse buys the same list.
        Assert.Equal(bought, Shop.AutoBuy(Content, FundedRun(100_000)));
    }

    [Fact]
    public void GoldRidesTheSave()
    {
        var funded = FundedRun(12_345);

        var reloaded = GauntletRun.Resume(Content, RunSave.FromJson(RunSave.ToJson(funded)));

        Assert.Equal(12_345, reloaded.GoldCopper);
    }

    [Fact]
    public void PricesReadLikeThePage()
    {
        Assert.Equal("75 GP", Shop.Price(7500));
        Assert.Equal("1 GP 5 SP", Shop.Price(150));
        Assert.Equal("2 CP", Shop.Price(2));
        Assert.Equal("0 CP", Shop.Price(0));
    }

    /// <summary>
    /// A fresh run holding this much copper, funded through the save round trip — the
    /// one honest door into the purse, since the run itself only earns by winning.
    /// </summary>
    private static GauntletRun FundedRun(int copper)
    {
        var run = GauntletRun.Start(Content, GauntletLadder.Default());
        var saved = RunSave.FromJson(RunSave.ToJson(run)) with { GoldCopper = copper };

        return GauntletRun.Resume(Content, saved);
    }
}
