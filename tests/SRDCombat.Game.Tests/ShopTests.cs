using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
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
    public void EveryOfferSaysWhatItWouldChange()
    {
        var run = FundedRun(100_000);
        var offers = Shop.Offers(Content, run.Party, run.States);

        Assert.NotEmpty(offers);

        // A price with nothing beside it is a price tag with no goods behind it:
        // every offer explains itself, and the numbers agree with the sheets.
        Assert.All(offers, offer =>
        {
            Assert.NotEmpty(offer.Effect.Lines);
            Assert.Equal(run.Party[offer.MemberIndex].Sheet.ArmorClass, offer.Effect.ArmorClassBefore);
            Assert.Equal(run.Party[offer.MemberIndex].Sheet.SpeedFeet, offer.Effect.SpeedFeetBefore);
        });

        // A gear offer's "after" is the re-resolved sheet, never a guess.
        Assert.All(offers.Where(offer => offer.NewDraft is not null), offer =>
        {
            var resolved = PregeneratedParty.Resolve(
                Content,
                offer.NewDraft!,
                run.States[offer.MemberIndex].Level);

            Assert.Equal(resolved.Sheet.ArmorClass, offer.Effect.ArmorClassAfter);
            Assert.Equal(resolved.Sheet.SpeedFeet, offer.Effect.SpeedFeetAfter);
        });
    }

    [Fact]
    public void AnArmorOfferShowsItsArmorClassAndAWeaponOfferItsDamageRange()
    {
        var run = FundedRun(100_000);
        var offers = Shop.Offers(Content, run.Party, run.States);

        var mail = offers.Single(offer => offer.NewDraft is { ArmorId: "armor.chain-mail" }
            && run.Party[offer.MemberIndex].Draft.Name == "Aldous");

        // Chain Shirt and Shield at 15 becomes Chain Mail and Shield at 18.
        Assert.Equal(3, mail.Effect.ArmorClassDelta);
        Assert.Contains(mail.Effect.Lines, line => line.Contains("AC 15 to 18", StringComparison.Ordinal));
        Assert.Null(mail.Effect.Attack);

        // A weapon offer names both attacks with their whole damage expressions, the
        // range each rolls, and what the swap is worth per hit.
        var weapon = offers.First(offer => offer.Effect.Attack is not null);
        var change = weapon.Effect.Attack!;

        Assert.NotEqual(change.FromName, change.ToName);
        Assert.True(change.AverageDelta > 0, "the gate only offers a harder-hitting weapon");

        var line = Assert.Single(weapon.Effect.Lines, candidate => candidate.Contains("per hit", StringComparison.Ordinal));

        Assert.Contains($"{change.ToDamage.Minimum}-{change.ToDamage.Maximum}", line, StringComparison.Ordinal);
        Assert.Contains($"{change.FromDamage.Minimum}-{change.FromDamage.Maximum}", line, StringComparison.Ordinal);
        Assert.Contains($"+{change.AverageDelta}", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AWeaponSwapSaysWhichMasteryItWouldCost()
    {
        // The damage numbers hide the larger half of this trade: Korrin's Greataxe
        // carries Cleave, a whole second attack, and a Maul buys one point of average
        // damage by selling it.
        var run = FundedRun(100_000);
        var korrin = run.Party.ToList().FindIndex(member => member.Draft.Name == "Korrin");

        var maul = Shop.Offers(Content, run.Party, run.States)
            .Single(offer => offer.MemberIndex == korrin
                && offer.NewDraft is { } draft
                && draft.WeaponIds.Contains("weapon.maul", StringComparer.Ordinal));

        Assert.True(maul.Effect.Attack!.ChangesMastery);
        Assert.Equal(WeaponMastery.Cleave, maul.Effect.Attack.FromMastery);
        Assert.Equal(WeaponMastery.Topple, maul.Effect.Attack.ToMastery);
        Assert.Contains(
            maul.Effect.Lines,
            line => line.Contains("mastery Cleave becomes Topple", StringComparison.Ordinal));

        // A swap that keeps the property says nothing about mastery — Sable's
        // Shortsword and a Rapier both carry Vex.
        var sable = run.Party.ToList().FindIndex(member => member.Draft.Name == "Sable");

        var rapier = Shop.Offers(Content, run.Party, run.States)
            .Single(offer => offer.MemberIndex == sable
                && offer.NewDraft is { } draft
                && draft.WeaponIds.Contains("weapon.rapier", StringComparer.Ordinal));

        Assert.False(rapier.Effect.Attack!.ChangesMastery);
        Assert.DoesNotContain(rapier.Effect.Lines, line => line.Contains("mastery", StringComparison.Ordinal));
    }

    [Fact]
    public void APotionOfferShowsWhatItRestores()
    {
        var run = FundedRun(100_000);

        var potion = Shop.Offers(Content, run.Party, run.States)
            .Single(offer => offer.Potion is not null);

        Assert.Equal(PotionRules.Healing(HealingPotion.Standard), potion.Effect.Healing);

        // "2d4 + 2" restores between 4 and 10.
        Assert.Contains(
            potion.Effect.Lines,
            line => line.Contains("2d4 + 2", StringComparison.Ordinal)
                && line.Contains("(4-10)", StringComparison.Ordinal));
    }

    [Fact]
    public void ProtectorPutsTheClericInHeavyArmor()
    {
        // The slice's whole point (#157): the pregen Cleric takes Protector, so the
        // merchant may sell it the Chain Mail its printed role trains it for — Chain
        // Shirt and Shield at AC 15 becomes Chain Mail and Shield at AC 18, bought
        // with the run's own gold.
        var run = FundedRun(100_000);
        var aldous = run.Party.ToList().FindIndex(member => member.Draft.Name == "Aldous");

        var mail = Shop.Offers(Content, run.Party, run.States)
            .Single(offer => offer.MemberIndex == aldous
                && offer.NewDraft is { ArmorId: "armor.chain-mail" });

        var before = run.Party[aldous].Sheet.ArmorClass;

        Assert.Null(run.Purchase(mail));
        Assert.Equal(18, run.Party[aldous].Sheet.ArmorClass);
        Assert.True(run.Party[aldous].Sheet.ArmorClass > before);

        // Strength 13 meets Chain Mail's printed requirement exactly, so nothing else
        // got worse — the offer gate's own strictly-better claim, spot-checked.
        Assert.Equal(30, run.Party[aldous].Sheet.SpeedFeet);
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

    [Fact]
    public void TheShopNeverSellsAHandThePartyDoesNotHave()
    {
        // Brenna carries a shield, Korrin a Greataxe: no two-hander is offered to
        // her, and no shield to him — the resolver's hands rule, seen through the
        // stall. The gap this pins: the shop once sold both.
        var run = GauntletRun.Start(Content, GauntletLadder.Default());
        var offers = Shop.Offers(Content, run.Party, run.States);
        var brenna = run.Party.ToList().FindIndex(member => member.Draft.Name == "Brenna");
        var korrin = run.Party.ToList().FindIndex(member => member.Draft.Name == "Korrin");

        Assert.DoesNotContain(offers, offer => offer.MemberIndex == brenna
            && offer.NewDraft is { } draft
            && draft.WeaponIds.Any(id =>
                Content.WeaponsById[id].Properties.HasFlag(Core.Definitions.WeaponProperty.TwoHanded)));

        Assert.DoesNotContain(offers, offer => offer.MemberIndex == korrin
            && offer.NewDraft is { HasShield: true });
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
