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
/// <para><b>Read-only, by contract.</b> It is invoked mid-walk against live encounter state;
/// it must not mutate the encounter, roll dice, or spend anything. A pure visibility query is
/// its whole contract — which is also why supplying one cannot perturb the dice stream: the
/// frozen transcript supplies none, and a pure query changes no roll even when one is
/// supplied.</para>
/// </remarks>
public delegate Combatant? MovementInterrupt(MovementStep step);
