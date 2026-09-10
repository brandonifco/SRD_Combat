using Godot;

namespace SRDCombat.Viewer;

/// <summary>
/// The health readout's own state (#299) — the one thing the board's hit-point bar and
/// the side panel's hp line must agree on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Computed from facts the engine already carries, never from a threshold worked out
/// again in this layer</b> — the standing "client recomputes no rule" convention
/// (CLAUDE.md). <see cref="Bloodied"/> reads straight off <c>Combatant.IsBloodied</c>,
/// carried onto <see cref="FightScreen.Token"/> unchanged, which is itself the SRD's own
/// printed term: "A creature is Bloodied while it has half its Hit Points or fewer
/// remaining." (SRD 5.2.1 p. 177, Rules Glossary, "Bloodied"). That is the only health
/// threshold this SRD prints, so it is the only one this band adds — a further
/// "critical" tier below it (the review that opened #299 offered "healthy / hurt /
/// critical" only as an example) would be a display opinion wearing a rules reading,
/// not one. Two states reachable while standing is still a state change a watcher can
/// see at a glance, which is what the issue asks for.
/// </para>
/// <para>
/// <see cref="Downed"/> and <see cref="Dead"/> are not hit-point thresholds at all —
/// they are <c>Combatant.IsDead</c> and the zero-hit-points-and-not-dead state the
/// engine already tracks (<c>Combatant.IsDying</c>) — so <see cref="HealthReadout.BandFor"/>
/// reads them off the token's own flags, ahead of Bloodied in priority: a downed
/// creature's hit points still satisfy "half or fewer" (zero certainly is), but Downed
/// is the state a watcher needs named, not Bloodied.
/// </para>
/// </remarks>
internal enum HealthBand
{
    Healthy,
    Bloodied,
    Downed,
    Dead,
}

/// <summary>Maps a token's flags to the one <see cref="HealthBand"/> both surfaces draw.</summary>
internal static class HealthReadout
{
    /// <summary>
    /// The band for one token. <paramref name="isBloodied"/> is
    /// <c>Combatant.IsBloodied</c>'s own answer — see <see cref="HealthBand"/>'s remarks
    /// for why nothing here re-derives it from hit points.
    /// </summary>
    internal static HealthBand BandFor(bool isDead, bool isDown, bool isBloodied) => isDead
        ? HealthBand.Dead
        : isDown
            ? HealthBand.Downed
            : isBloodied
                ? HealthBand.Bloodied
                : HealthBand.Healthy;

    /// <summary>The one colour each band draws as, on the board and in the panel alike.</summary>
    internal static Color ColourFor(HealthBand band) => band switch
    {
        HealthBand.Healthy => Palette.HealthyColour,
        HealthBand.Bloodied => Palette.BloodiedColour,
        HealthBand.Downed => Palette.DownColour,
        HealthBand.Dead => Palette.DeadColour,
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, "No colour for this HealthBand."),
    };
}

/// <summary>
/// What a downed character's Death Saving Throw progress draws (#299) — three success
/// pips and three failure pips ("Three Successes/Failures", SRD 5.2.1 p. 17, "Death
/// Saving Throws"), read straight off <c>Combatant.DeathSaveSuccesses</c> /
/// <c>DeathSaveFailures</c>, or the single Stable state <c>Combatant.MarkStable</c>
/// leaves once a third success stops the rolling.
/// </summary>
/// <remarks>
/// <b><see cref="IsStable"/> is checked ahead of the counts on purpose.</b>
/// <c>MarkStable</c> also resets both counts to zero (p. 17: "The number of both is
/// reset to zero when you regain any Hit Points or become Stable") — so a Stable
/// creature's own zeroed counts must never be read as "no successes yet, no failures
/// yet" the way a freshly-downed creature's would be. The same reasoning applies to a
/// creature that regained hit points and is standing again: that combatant is not
/// <c>IsDown</c> any more, so <see cref="FightScreen.DrawTokens"/> never asks for its
/// pips at all.
/// </remarks>
internal readonly record struct DeathSavePips(bool IsStable, int Successes, int Failures)
{
    /// <summary>Three of each, per print (see this type's own remarks).</summary>
    internal const int PipsPerRow = 3;

    internal static DeathSavePips For(int successes, int failures, bool isStable) => new(
        isStable,
        Math.Clamp(successes, 0, PipsPerRow),
        Math.Clamp(failures, 0, PipsPerRow));

    /// <summary>Whether the success pip at <paramref name="index"/> (0-based) draws filled.</summary>
    internal bool SuccessFilled(int index) => !IsStable && index < Successes;

    /// <summary>Whether the failure pip at <paramref name="index"/> (0-based) draws filled.</summary>
    internal bool FailureFilled(int index) => !IsStable && index < Failures;

    /// <summary>The panel's own text for the same state the board draws as pips.</summary>
    internal string SummaryText() => IsStable
        ? "stable"
        : $"{Successes}/{PipsPerRow} successes, {Failures}/{PipsPerRow} failures";
}
