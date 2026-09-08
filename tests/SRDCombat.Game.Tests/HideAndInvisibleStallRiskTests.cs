using SRDCombat.Content;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// #673's named risk, disproved directly rather than swept for: a Hidden creature that
/// can never be found and attacks with perpetual Advantage, and a stall from the AI
/// refusing to target the last, hidden foe.
/// </summary>
/// <remarks>
/// The reveal-after-attack-roll pin (<c>HideActionTests.AnAttackRoll_EndsTheAttackersOwnHiddenState</c>)
/// already disproves the first half directly: a hider that attacks stops being hidden
/// the instant it does. The second half needs no new mechanism here — the bot never
/// creates a Hidden creature in this slice (Hide's own tactical <em>use</em> by the AI
/// is #314's, and the AI's side-aware target filter is #673-AI's, gated on Brandon's
/// blind-state choice) — so this is the same "does an ordinary fight still complete"
/// proof <c>GauntletFullRunTests</c> already runs at 30-fight scale, at a cheaper,
/// several-seed scale: if Hide/Invisible landing had somehow broken turn resolution for
/// unrelated content, this would be the first place to see it stall.
/// </remarks>
public class HideAndInvisibleStallRiskTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(101)]
    [InlineData(4242)]
    [InlineData(9001)]
    public void ARepresentativeFightAlwaysCompletesWithinTheRoundLimit(int seed)
    {
        var party = PregeneratedParty.Build(Content, level: 3);
        var random = new SeededRandomSource(seed);
        var fight = EncounterFactory.Build(Content, party, EncounterDifficulty.Moderate, random);

        SimpleTacticsPolicy.RunToCompletion(fight.Encounter);

        Assert.True(fight.Encounter.IsComplete, $"Seed {seed} did not complete within the round limit.");
    }
}
