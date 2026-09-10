using SRDCombat.Core.Definitions;

namespace SRDCombat.Viewer.Tests;

/// <summary>
/// The side panel's own row (#299, <c>FightScreen.RowFor</c>) — the identity half (team,
/// unaffected by health) and the state half (the health readout, or the fog reading).
/// </summary>
public class PanelRowTests
{
    [Fact]
    public void StandingHealthyToken_ShowsHitPointsInHealthyColour()
    {
        var token = TokenWith(hp: 18, max: 20, isParty: true, isBloodied: false);

        var row = FightScreen.RowFor(token, active: false, hidden: false);

        Assert.Equal("18/20 hp", row.State);
        Assert.Equal(Palette.HealthyColour, row.StateColour);
        Assert.Equal(Palette.PartyColour, row.IdentityColour);
    }

    [Fact]
    public void StandingBloodiedToken_ShowsHitPointsInBloodiedColour()
    {
        var token = TokenWith(hp: 9, max: 20, isParty: false, isBloodied: true);

        var row = FightScreen.RowFor(token, active: false, hidden: false);

        Assert.Equal("9/20 hp", row.State);
        Assert.Equal(Palette.BloodiedColour, row.StateColour);
        // Team identity is untouched by the health band — a Bloodied enemy still reads
        // as an enemy, which the row's name half still needs to say.
        Assert.Equal(Palette.MonsterColour, row.IdentityColour);
    }

    [Fact]
    public void Conditions_AppearAfterTheHitPointsExactlyAsBefore()
    {
        var token = TokenWith(hp: 20, max: 20, isParty: true, isBloodied: false) with
        {
            Conditions = "Prone, Blinded",
        };

        var row = FightScreen.RowFor(token, active: false, hidden: false);

        Assert.Equal("20/20 hp — Prone, Blinded", row.State);
    }

    [Fact]
    public void DeadToken_ReadsDeadInBothHalvesDimmed()
    {
        var token = TokenWith(hp: 0, max: 20, isParty: true, isBloodied: true) with { IsDead = true };

        var row = FightScreen.RowFor(token, active: false, hidden: false);

        Assert.Equal("dead", row.State);
        Assert.Equal(Palette.DeadColour, row.StateColour);
        Assert.Equal(Palette.Dim, row.IdentityColour);
    }

    [Fact]
    public void ActiveTokenPrefix_CarriesTheTurnMarker()
    {
        var token = TokenWith(hp: 20, max: 20, isParty: true, isBloodied: false);

        var active = FightScreen.RowFor(token, active: true, hidden: false);
        var inactive = FightScreen.RowFor(token, active: false, hidden: false);

        Assert.StartsWith("▶ ", active.Prefix);
        Assert.StartsWith("  ", inactive.Prefix);
    }

    /// <summary>
    /// The fog rule (#299 acceptance criterion 3): a hidden row shows neither the health
    /// band nor the Death Save readout, whatever the token's own fields hold — the same
    /// standard <c>DrawTokens</c> holds a hidden creature's token and hit point bar to
    /// (it is simply never handed a hidden token; the panel keeps the row and must
    /// therefore gate it itself).
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Hidden_ReadsUnseenRegardlessOfHealthOrDownedState(bool isDown, bool isBloodied)
    {
        var token = TokenWith(hp: 3, max: 20, isParty: false, isBloodied: isBloodied) with { IsDown = isDown };

        var row = FightScreen.RowFor(token, active: false, hidden: true);

        Assert.Equal("unseen", row.State);
        Assert.Equal(Palette.Dim, row.StateColour);
        Assert.Equal(Palette.Dim, row.IdentityColour);
    }

    private static FightScreen.Token TokenWith(int hp, int max, bool isParty, bool isBloodied) => new(
        Id: "t1",
        Name: "Test",
        Label: 'A',
        IsParty: isParty,
        X: 0,
        Y: 0,
        HitPoints: hp,
        MaximumHitPoints: max,
        IsDead: false,
        IsDown: false,
        Conditions: string.Empty,
        ClassName: null,
        Size: CreatureSize.Medium,
        IsBloodied: isBloodied,
        IsStable: false,
        DeathSaveSuccesses: 0,
        DeathSaveFailures: 0);
}
