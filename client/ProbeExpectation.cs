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
    /// A refusal code the step expects to belong to a curated set, not one exact code.
    /// </summary>
    /// <remarks>
    /// Replaces an earlier <c>NoticeCodeStartsWith("spell.")</c> (#719, third review),
    /// found false at the fourth: <c>Encounter.CastSpell</c> can return
    /// <c>target.unseen</c> (Concealed's shared mechanism, #673 — reachable from a
    /// Blinded caster targeting an ally-only-visible enemy) and the
    /// Action/Bonus-Action/Reaction "already spent" codes shared with every other
    /// action, none of which start with <c>"spell."</c> — so the prefix would have
    /// wrongly faulted a probe run that hit one of those, exactly the legitimate-refusal
    /// shape this predicate exists not to reject. The set is curated from the engine's
    /// own source (<c>PlayMode.Probe.cs</c>'s <c>AttackRefusalCodes</c> and
    /// <c>CastSpellRefusalCodes</c>, each citing where every code in it comes from) —
    /// not derived automatically, and not shrunk to a shared prefix that does not
    /// actually hold.
    /// </remarks>
    internal sealed record NoticeCodeIsOneOf(IReadOnlyCollection<string> Codes) : ProbeExpectation
    {
        internal override string? Failure(ProbeSnapshot snapshot) =>
            snapshot.NoticeCode is { } code && Codes.Contains(code)
                ? null
                : $"expected notice code to be one of [{string.Join(", ", Codes)}], got "
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

    /// <summary>
    /// The mirror of <see cref="Unchanged{T}"/>: a resource the step expects its own
    /// action to have actually moved, rather than stayed exactly where it started.
    /// </summary>
    /// <remarks>
    /// This is what a no-op click needs to be caught by and <see cref="NoticeCodeIs"/>
    /// plus <see cref="FocusIs"/> alone cannot (#719 review): a button that found
    /// nothing to click — the same shape #705's own play-2 step pinned — leaves the
    /// board's focus and its refusal notice exactly as they already were, so both of
    /// those predicates hold just as truly before the click as after it. Ending a turn
    /// or moving to a square is not "no refusal happened"; it is "something specific
    /// now differs" — the active combatant, the round, a position.
    /// </remarks>
    internal sealed record Changed<T>(string Resource, T Before, T After) : ProbeExpectation
    {
        internal override string? Failure(ProbeSnapshot snapshot) =>
            EqualityComparer<T>.Default.Equals(Before, After)
                ? $"expected {Resource} to change, was {Before} both times"
                : null;
    }

    /// <summary>
    /// An observed value the step expects to equal a specific target — the actor's
    /// position landing exactly on the square that was clicked, a hover's hint text
    /// naming exactly the button that was hovered. Distinct from <see
    /// cref="Unchanged{T}"/>: that compares the same resource read at two points in
    /// time and expects no drift; this compares one observed value against a
    /// caller-computed expectation that need never have existed before the action ran.
    /// </summary>
    internal sealed record EqualsExpected<T>(string Resource, T Actual, T Expected) : ProbeExpectation
    {
        internal override string? Failure(ProbeSnapshot snapshot) =>
            EqualityComparer<T>.Default.Equals(Actual, Expected)
                ? null
                : $"expected {Resource} to be {Expected}, was {Actual}";
    }

    /// <summary>A count the step expects to have gone down — movement actually spent by a move that actually happened, not merely "no refusal".</summary>
    internal sealed record Decreased(string Resource, int Before, int After) : ProbeExpectation
    {
        internal override string? Failure(ProbeSnapshot snapshot) =>
            After < Before
                ? null
                : $"expected {Resource} to decrease from {Before}, was {After}";
    }

    /// <summary>
    /// A string a step expects to be present and non-empty — checked before the caller
    /// trusts it as an expectation to compare something else against.
    /// </summary>
    /// <remarks>
    /// Closes a specific hole (#719, second review): <c>play-2b-hint</c> compares the
    /// hint actually produced against the hovered button's <em>registered</em> hint via
    /// <see cref="EqualsExpected{T}"/>. If registration itself broke — the dictionary
    /// lookup failing, or a future <c>TurnOptions.Hint</c> case returning empty — the
    /// registered hint and the produced hint would both independently be null, and
    /// <c>null == null</c> would pass <see cref="EqualsExpected{T}"/> exactly as if the
    /// hint had actually matched. This predicate is run first, against the registered
    /// value alone, so a broken registration is a fault before it ever reaches the
    /// comparison that a broken pair of nulls could slip past.
    /// </remarks>
    internal sealed record NonEmpty(string Resource, string? Value) : ProbeExpectation
    {
        internal override string? Failure(ProbeSnapshot snapshot) =>
            string.IsNullOrEmpty(Value)
                ? $"expected {Resource} to be a non-empty string, was {(Value is null ? "null" : "empty")}"
                : null;
    }

    /// <summary>Some notice must have been printed — any code, unspecified. The complement of <see cref="NoticeCodeIs"/> with a null expectation.</summary>
    internal sealed record NoticePresent(string Resource) : ProbeExpectation
    {
        internal override string? Failure(ProbeSnapshot snapshot) =>
            snapshot.Notice is null
                ? $"expected {Resource} to have printed a notice, got none"
                : null;
    }

    /// <summary>
    /// Passes when at least one of <paramref name="Options"/> passes — the logical OR
    /// <c>PlayMode.Assert</c>'s implicit AND across its own parameter list cannot
    /// express on its own.
    /// </summary>
    /// <remarks>
    /// The shape this exists for (#719, second review): a bare board click either
    /// resolves (the combat log gains an entry) or is refused (a notice is printed) —
    /// never neither, unless the click found nothing to act on at all, which is exactly
    /// the no-op this is meant to catch. Neither branch alone is the right assertion,
    /// since either can legitimately be the one that happens.
    /// </remarks>
    internal sealed record AnyOf(string Resource, ProbeExpectation[] Options) : ProbeExpectation
    {
        internal override string? Failure(ProbeSnapshot snapshot)
        {
            var failures = Options.Select(option => option.Failure(snapshot)).ToArray();

            return Array.TrueForAll(failures, failure => failure is not null)
                ? $"expected at least one of {Resource}'s conditions to hold — {string.Join("; ", failures)}"
                : null;
        }
    }
}
