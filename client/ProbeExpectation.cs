namespace SRDCombat.Viewer;

/// <summary>
/// The facts a required probe step's predicate reads, captured after the step has
/// acted. Plain values only — no live scene, no <c>Node</c> — so evaluating a
/// <see cref="ProbeExpectation"/> against one needs no Godot engine at all, and
/// <c>tests/SRDCombat.Viewer.Tests</c> can pin the whole mechanism (#705) the same way
/// it already pins <c>PlayFocus</c> and <c>FocusStack</c> without a display.
/// </summary>
/// <param name="Focus">The top of the focus stack after the step acted.</param>
/// <param name="Notice">
/// The client's own notice line after the step (<c>PlayMode._notice</c>,
/// <c>RefreshAfterAction</c>'s <c>"{message}  [{code}]"</c>) — the last refusal's
/// message and code, or null when the last action went through clean, or when nothing
/// since the last successful action produced a notice at all.
/// </param>
internal readonly record struct ProbeSnapshot(PlayFocus Focus, string? Notice)
{
    /// <summary>
    /// The bracketed code off <see cref="Notice"/>, or null when there is no notice —
    /// which is the shape a step expecting a clean, unrefused action asserts against.
    /// </summary>
    internal string? NoticeCode =>
        Notice is null ? null : Notice[(Notice.LastIndexOf('[') + 1)..^1];
}

/// <summary>
/// What a required probe step must be true after it acts. A step marked required calls
/// <c>PlayMode.Assert</c> with one of these once it has acted; a predicate that fails is
/// a fault — <c>Assert</c> throws, so <see cref="ProbeFaults"/> reports the probe
/// crashed and exits 1 — never a silently-wrong capture, and never
/// <c>PlayMode.ReportSkip</c>, which stays reserved for the coverage a step could not
/// even attempt because the fight in progress did not offer it (#705).
/// </summary>
internal abstract record ProbeExpectation
{
    /// <summary>
    /// The failure message a step should report, or null when <paramref
    /// name="snapshot"/> satisfies this expectation.
    /// </summary>
    internal abstract string? Failure(ProbeSnapshot snapshot);

    /// <summary>The focus layer a step expects to have landed on once it has acted.</summary>
    /// <param name="Expected">
    /// The concrete <see cref="PlayFocus"/> type expected — compared by runtime type,
    /// not by value, since most layers (<c>Board</c>, the row menus) carry no state to
    /// compare and the ones that do (<c>Shop</c>, <c>Targeting</c>) are not what a probe
    /// step is asserting when it only cares which layer is up.
    /// </param>
    internal sealed record FocusIs(Type Expected) : ProbeExpectation
    {
        internal override string? Failure(ProbeSnapshot snapshot) =>
            snapshot.Focus.GetType() == Expected
                ? null
                : $"expected focus {Expected.Name}, got {snapshot.Focus.GetType().Name}";
    }

    /// <summary>
    /// The refusal code a step expects printed after it acts — or null, for a step that
    /// expects its action to have gone through clean, with no refusal at all.
    /// </summary>
    internal sealed record NoticeCodeIs(string? Expected) : ProbeExpectation
    {
        internal override string? Failure(ProbeSnapshot snapshot) =>
            snapshot.NoticeCode == Expected
                ? null
                : $"expected notice code {Expected ?? "(none)"}, got "
                    + $"{snapshot.NoticeCode ?? "(none)"} ({snapshot.Notice ?? "no notice printed"})";
    }

    /// <summary>
    /// A resource the step expects its own action to have left untouched — the before
    /// and after are read by the caller, outside the snapshot, since what counts as
    /// "the resource" varies step to step (a position, an hp total, a turn count).
    /// </summary>
    internal sealed record Unchanged<T>(string Resource, T Before, T After) : ProbeExpectation
    {
        internal override string? Failure(ProbeSnapshot snapshot) =>
            EqualityComparer<T>.Default.Equals(Before, After)
                ? null
                : $"expected {Resource} unchanged, was {Before} now {After}";
    }
}
