using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The health readout's band (#299): which of Healthy, Bloodied, Downed or Dead a
/// combatant draws as, and that the board's hit point bar (<c>FightScreen.BarColourFor</c>)
/// and the side panel's hp line (<c>FightScreen.RowFor</c>) agree on it for the same
/// combatant — the acceptance criterion the issue names by that word.
/// </summary>
/// <remarks>
/// The theories travel by band <em>name</em> rather than by <c>HealthBand</c>: that type
/// is <c>internal</c>, and a public test method may not name it — the same workaround
/// <c>PlayFocusTests</c> uses for <c>PlayFocus</c>.
/// </remarks>
public class HealthBandTests
{
    // ---- HealthReadout.BandFor: pure priority ordering over the three flags ----

    [Theory]
    [InlineData(false, false, false, "Healthy")]
    [InlineData(false, false, true, "Bloodied")]
    [InlineData(false, true, false, "Downed")]
    // Downed outranks Bloodied: 0 hit points satisfies "half or fewer" too, but Downed
    // is the state a watcher needs named — see `HealthBand`'s own remarks.
    [InlineData(false, true, true, "Downed")]
    [InlineData(true, false, false, "Dead")]
    [InlineData(true, false, true, "Dead")]
    [InlineData(true, true, false, "Dead")]
    [InlineData(true, true, true, "Dead")]
    public void BandFor_DeadOutranksDownedOutranksBloodied(
        bool isDead, bool isDown, bool isBloodied, string expectedBand) =>
        Assert.Equal(Enum.Parse<HealthBand>(expectedBand), HealthReadout.BandFor(isDead, isDown, isBloodied));

    /// <summary>
    /// Knockout for the exhaustive switch (#299): stubbing <c>ColourFor</c> back down to
    /// a <c>_ =&gt; Palette.Dim</c> catch-all would make this pass too, silently drawing
    /// the wrong colour for an added band — the real guard is
    /// <see cref="ColourFor_UsesThePaletteEntryNamedForEachBand"/> below, which pins the
    /// exact colour rather than merely that one was returned.
    /// </summary>
    [Theory]
    [InlineData("Healthy")]
    [InlineData("Bloodied")]
    [InlineData("Downed")]
    [InlineData("Dead")]
    public void ColourFor_IsDefinedForEveryBand(string bandName) =>
        HealthReadout.ColourFor(Enum.Parse<HealthBand>(bandName));

    [Fact]
    public void ColourFor_UsesThePaletteEntryNamedForEachBand()
    {
        Assert.Equal(Palette.HealthyColour, HealthReadout.ColourFor(HealthBand.Healthy));
        Assert.Equal(Palette.BloodiedColour, HealthReadout.ColourFor(HealthBand.Bloodied));
        Assert.Equal(Palette.DownColour, HealthReadout.ColourFor(HealthBand.Downed));
        Assert.Equal(Palette.DeadColour, HealthReadout.ColourFor(HealthBand.Dead));
    }

    /// <summary>
    /// The regression guard for "consistent across both board and panel" (#299): for
    /// every combination of flags a token can carry, the board's own bar colour
    /// (<c>FightScreen.BarColourFor</c>) and the panel's own row colour
    /// (<c>FightScreen.RowFor</c>, not hidden) are the identical value — because both
    /// call <see cref="HealthReadout.BandFor"/> and <see cref="HealthReadout.ColourFor"/>
    /// with the same three inputs, not two independent decisions that could drift.
    /// Knockout-verified: reintroducing the old team-identity bar fill
    /// (<c>token.IsDead ? DeadColour : token.IsDown ? DownColour : ...</c>) in
    /// <c>BarColourFor</c> alone fails this for a Bloodied, still-standing token, whose
    /// bar would then read <c>PartyColour</c>/<c>MonsterColour</c> against the panel's
    /// <c>BloodiedColour</c>.
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    public void BoardBarAndPanelRowAgreeOnColour(bool isDead, bool isDown, bool isBloodied)
    {
        var token = TokenWith(isDead: isDead, isDown: isDown, isBloodied: isBloodied);

        var barColour = FightScreen.BarColourFor(token);
        var row = FightScreen.RowFor(token, active: false, hidden: false);

        Assert.Equal(barColour, row.StateColour);
    }

    // ---- The threshold itself, read off a real Combatant rather than re-derived ----

    /// <summary>
    /// "A creature is Bloodied while it has half its Hit Points or fewer remaining"
    /// (SRD 5.2.1 p. 177, Rules Glossary, "Bloodied") — exercised through
    /// <c>Combatant.IsBloodied</c> and <c>FightScreen.TokenFrom</c>, the same path
    /// production code takes, rather than a threshold this test works out on its own.
    /// A max of 21 (odd) is included because integer division floors: 21 Bloodies at 10,
    /// not 10.5, which is the case <c>Combatant.IsBloodied</c>'s own doc comment names.
    /// </summary>
    [Theory]
    [InlineData(20, 11, "Healthy")] // one hit point above half: still Healthy.
    [InlineData(20, 10, "Bloodied")] // exactly half: Bloodied.
    [InlineData(20, 1, "Bloodied")] // far below half, still standing.
    [InlineData(21, 11, "Healthy")] // odd maximum, one above the floored half.
    [InlineData(21, 10, "Bloodied")] // odd maximum, exactly the floored half.
    public void BandFor_TheOnlyPrintedThresholdIsHalfHitPoints(int maximumHitPoints, int currentHitPoints, string expectedBand)
    {
        var combatant = FightTestData.Combatant(
            "Hurt Fighter",
            stats: FightTestData.Stats(maximumHitPoints: maximumHitPoints, diesAtZeroHitPoints: false));

        DamageRules.Apply(combatant, maximumHitPoints - currentHitPoints, DamageType.Slashing);

        Assert.Equal(currentHitPoints, combatant.CurrentHitPoints);

        var token = FightScreen.TokenFrom(combatant, Labels.For([combatant]));

        Assert.Equal(
            Enum.Parse<HealthBand>(expectedBand),
            HealthReadout.BandFor(token.IsDead, token.IsDown, token.IsBloodied));
    }

    [Fact]
    public void BandFor_ZeroHitPointsIsDownedNotBloodied()
    {
        var combatant = FightTestData.Combatant(
            "Downed Fighter",
            stats: FightTestData.Stats(maximumHitPoints: 20, diesAtZeroHitPoints: false));

        DamageRules.Apply(combatant, 20, DamageType.Slashing);

        Assert.Equal(0, combatant.CurrentHitPoints);
        Assert.False(combatant.IsDead);

        var token = FightScreen.TokenFrom(combatant, Labels.For([combatant]));

        Assert.True(token.IsDown);
        Assert.Equal(HealthBand.Downed, HealthReadout.BandFor(token.IsDead, token.IsDown, token.IsBloodied));
    }

    [Fact]
    public void BandFor_ZeroHitPointsThatKillsOutrightIsDeadNotDowned()
    {
        // DiesAtZeroHitPoints: true (FightTestData.Stats' default) is print's own rule
        // for a monster — no Death Save, no Downed state, straight to dead.
        var combatant = FightTestData.Combatant("Slain Goblin", stats: FightTestData.Stats(maximumHitPoints: 7));

        DamageRules.Apply(combatant, 7, DamageType.Slashing);

        Assert.True(combatant.IsDead);

        var token = FightScreen.TokenFrom(combatant, Labels.For([combatant]));

        Assert.Equal(HealthBand.Dead, HealthReadout.BandFor(token.IsDead, token.IsDown, token.IsBloodied));
    }

    private static FightScreen.Token TokenWith(bool isDead, bool isDown, bool isBloodied) => new(
        Id: "t1",
        Name: "Test",
        Label: 'A',
        IsParty: true,
        X: 0,
        Y: 0,
        HitPoints: 5,
        MaximumHitPoints: 10,
        IsDead: isDead,
        IsDown: isDown,
        Conditions: string.Empty,
        ClassName: null,
        Size: CreatureSize.Medium,
        IsBloodied: isBloodied,
        IsStable: false,
        DeathSaveSuccesses: 0,
        DeathSaveFailures: 0);
}
