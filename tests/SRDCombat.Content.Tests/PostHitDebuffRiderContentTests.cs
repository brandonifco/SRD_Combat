using SRDCombat.Core.Combat;

namespace SRDCombat.Content.Tests;

/// <summary>
/// The content half of #665 (shape 3 of the #390 ledger): that the real extracted
/// Ettin and Steam Mephit actually carry the structured riders, not merely that a
/// hand-authored fixture could exercise the engine's machinery for them. The engine
/// half — that the machinery itself imposes the right effect on the right clock — is
/// <c>PostHitDebuffRiderTests</c> in <c>SRDCombat.Core.Tests</c>.
/// </summary>
public class PostHitDebuffRiderContentTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    [Fact]
    public void TheEttinsMorningstarCarriesTheDisadvantageRider()
    {
        var ettin = Content.MonstersById["monster.ettin"];
        var morningstar = Assert.Single(ettin.Entries, entry => entry.Name == "Morningstar");

        Assert.NotNull(morningstar.Attack);
        Assert.True(morningstar.Attack!.ImposesDisadvantageOnTargetsNextAttack);
        Assert.Empty(morningstar.UnmodelledClauses);

        // The end-to-end conversion a real fight actually swings with.
        var stats = CombatantStats.FromMonster(ettin);
        var combatAttack = Assert.Single(stats.Attacks, attack => attack.Name == "Morningstar");
        Assert.True(combatAttack.ImposesDisadvantageOnTargetsNextAttack);
    }

    [Fact]
    public void TheSteamMephitsSteamBreathCarriesTheSpeedDecreaseRider()
    {
        var mephit = Content.MonstersById["monster.steam-mephit"];
        var breath = Assert.Single(mephit.Entries, entry => entry.Name == "Steam Breath");

        Assert.NotNull(breath.Save);
        Assert.Equal(10, breath.Save!.TargetSpeedDecreaseFeet);

        // The underwater-resistance clause is a different, unrelated shape (out of
        // #665's scope) and is expected to remain residue — Steam Breath does not
        // become fully modelled by this fix alone.
        Assert.Contains(
            "Failure or Success: Being underwater doesn't grant Resistance to this Fire damage",
            breath.UnmodelledClauses);
        Assert.DoesNotContain(
            breath.UnmodelledClauses,
            clause => clause.Contains("Speed decreases", StringComparison.Ordinal));
    }
}
