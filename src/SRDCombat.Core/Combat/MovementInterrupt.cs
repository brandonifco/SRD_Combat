namespace SRDCombat.Core.Combat;

/// <summary>
/// One step of a walk, offered to a <see cref="MovementInterrupt"/> after the mover has
/// entered <see cref="At"/> and before any remaining step is taken.
/// </summary>
/// <param name="Mover">The walking combatant, already standing in <see cref="At"/>.</param>
/// <param name="At">
/// The square just entered — where the mover stops if the interrupt halts it. Equal to
/// <c>Mover.Position</c> at the moment of the call (the engine consults the interrupt after
/// <c>MoveTo</c>); carried explicitly so the contract does not depend on that coincidence.
/// </param>
/// <param name="Remaining">
/// The planned squares not yet walked, in order, excluding <see cref="At"/>. The engine does
/// not consult the interrupt on the final step (an arrived move has nothing to interrupt),
/// so this is never queried empty.
/// </param>
public readonly record struct MovementStep(
    Combatant Mover,
    GridPosition At,
    IReadOnlyList<GridPosition> Remaining);

/// <summary>
/// A caller's chance to halt a multi-square move mid-route — the seam #493 adds so a client
/// with a fog model can stop a walk the moment it brings a previously-hidden hostile into
/// view. Returns the hostile whose reveal warrants the stop, or null to let the walk go on.
/// </summary>
/// <remarks>
/// <para><b>The engine owns the walk; the caller owns only the judgement Core cannot make.</b>
/// Core has no visibility model, so it cannot decide "is this a reveal"; it walks, and after
/// each non-final step asks this delegate. Where the mover stops, how much movement it keeps,
/// and the narration are all <see cref="Encounter.WalkPath"/>'s — the delegate supplies only
/// the visibility verdict and the hostile to name.</para>
/// <para><b>Read-only by contract — a contract the engine does not enforce</b> (corrected
/// after qc round 1 on PR #622 flagged the original wording as overclaiming). It is invoked
/// mid-walk against live encounter state and must be a pure visibility query: it must not
/// mutate the encounter, spend movement or resources, roll dice, or call back into the
/// encounter's action methods (re-entrancy). Honouring that is the caller's responsibility —
/// the sole caller today is the party's clicked move, whose closure only reads visibility.
/// <b>So long as the contract is honoured</b>, supplying an interrupt perturbs neither the dice
/// stream nor any other resolution; the frozen transcript is unaffected <em>unconditionally</em>
/// because it supplies no interrupt at all. The engine's one enforced guard is on
/// <see cref="Encounter.WalkPath"/>: a delegate that <b>throws</b> is caught and treated as
/// "no stop", so a faulting visibility query completes the move as though none was supplied
/// rather than leaving the mover half-moved. A delegate that instead violates the contract
/// <em>silently</em> (mutating, rolling dice) can corrupt the walk and nothing stops it —
/// which is exactly why the contract is stated rather than assumed.</para>
/// </remarks>
public delegate Combatant? MovementInterrupt(MovementStep step);
