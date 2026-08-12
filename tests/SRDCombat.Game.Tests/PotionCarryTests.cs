using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Potions across a run: where they drop, that a rest does not conjure more, and that
/// they survive a save.
/// </summary>
public class PotionCarryTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void AModerateRungDropsAPotionAndALowRungDoesNot()
    {
        var moderate = GauntletRun.Start(Content, [new LadderStep(EncounterDifficulty.Moderate)]);
        PlayOut(moderate, seed: 5);

        // The seed is chosen because the party wins on it: an assertion behind an "if
        // they survived" would quietly stop testing the day the policy got worse.
        Assert.Equal(RunOutcome.Survived, moderate.Outcome);
        Assert.Single(moderate.LootFound);
        Assert.Contains("Potion of Healing", moderate.LootFound[0], StringComparison.Ordinal);
        Assert.Equal(1, moderate.States.Sum(state => state.Potions.Values.Sum()));

        var low = GauntletRun.Start(Content, [new LadderStep(EncounterDifficulty.Low)]);
        PlayOut(low, seed: 5);

        Assert.Empty(low.LootFound);
        Assert.Equal(0, low.States.Sum(state => state.Potions.Values.Sum()));
    }

    [Fact]
    public void PotionsSpreadAcrossThePartyRatherThanPilingOnOnePerson()
    {
        // Four Moderate rungs, so four potions drop if the party keeps winning: the
        // "fewest carried" rule should hand one to each rather than four to anybody.
        var run = GauntletRun.Start(Content, [
            new LadderStep(EncounterDifficulty.Moderate),
            new LadderStep(EncounterDifficulty.Moderate, RestKind.Long),
            new LadderStep(EncounterDifficulty.Moderate, RestKind.Long),
            new LadderStep(EncounterDifficulty.Moderate, RestKind.Long),
        ]);

        PlayOut(run, seed: 4242);

        var carried = run.States.Select(state => state.Potions.Values.Sum()).ToArray();

        // However far the run got, nobody may be two ahead of anybody else.
        Assert.True(
            carried.Max() - carried.Min() <= 1,
            $"Potions bunched up: [{string.Join(", ", carried)}].");
    }

    [Fact]
    public void ARestRestoresNoPotions()
    {
        // Every other resource on the state comes back; a consumable is gone for good,
        // which is the whole reason finding one is worth anything.
        var member = PregeneratedParty.Build(Content).First();
        var state = CharacterState.Fresh(member)
            .Carrying(HealingPotion.Standard)
            .Carrying(HealingPotion.Greater);

        var rested = state.AfterRest(member, RestKind.Long, new SeededRandomSource(1), hitDieSides: 10);

        Assert.Equal(state.Potions, rested.Potions);

        var spent = state with { Potions = new Dictionary<HealingPotion, int>() };

        Assert.Empty(spent.AfterRest(member, RestKind.Long, new SeededRandomSource(1), hitDieSides: 10).Potions);
    }

    [Fact]
    public void PotionsRideTheSaveByName()
    {
        var run = GauntletRun.Start(Content, [new LadderStep(EncounterDifficulty.Moderate)]);
        PlayOut(run, seed: 5);

        var json = RunSave.ToJson(run);
        var reloaded = GauntletRun.Resume(Content, RunSave.FromJson(json));

        Assert.Equal(RunOutcome.Survived, run.Outcome);
        Assert.Equal(1, run.States.Sum(state => state.Potions.Values.Sum()));

        Assert.Equal(
            run.States.Select(state => state.Potions.Values.Sum()),
            reloaded.States.Select(state => state.Potions.Values.Sum()));

        // The potency has to survive as its name, not as an index — a save that wrote
        // "0" would reload the wrong potion the day the enum gains a row above it.
        Assert.Contains("\"Standard\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void APotionCarriedIntoAFightIsThereToDrink()
    {
        var run = GauntletRun.Start(Content);

        // The party starts with none; hand the fighter one and check it arrives on the
        // combatant the fight is actually built from.
        Assert.Equal(0, run.States[0].Potions.Values.Sum());

        var carrying = run.States[0].Carrying(HealingPotion.Greater);

        Assert.Equal(1, carrying.Potions[HealingPotion.Greater]);

        var member = run.Party[0].CarryingOver(new CombatantCarryOver(
            carrying.CurrentHitPoints,
            Potions: carrying.Potions));

        var combatant = member.AtPosition(new GridPosition(0, 0)).Combatant;

        Assert.Equal(HealingPotion.Greater, combatant.Inventory.Weakest);
        Assert.Equal(1, combatant.Inventory.TotalPotions);
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
