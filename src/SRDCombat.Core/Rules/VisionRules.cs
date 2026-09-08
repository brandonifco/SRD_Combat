using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>
/// Line of sight: can a creature see a square, or another creature? Geometry plus open
/// eyes — the one narrow sense of "sight" this engine models — and, since #673, the
/// wider "seeing a creature" question the Invisible condition and special senses
/// disturb.
/// </summary>
/// <remarks>
/// <para>
/// <b>A stated interpretation, the same standing as <see cref="AreaTargeting"/> and
/// <see cref="CoverRules"/>.</b> SRD 5.2.1 prints no glossary entry for "line of sight"
/// at all — the phrase appears only in scattered rules (Hide, Fear, a handful of stat
/// blocks) — so reading "within line of sight" as an unblocked centre-to-centre line is
/// this engine's reading, written down here rather than derived. It reuses
/// <see cref="CoverRules.LineBlocked"/>, the very judgement Total Cover refuses attacks
/// with, so <em>what can be seen</em> and <em>what can be shot at</em> can never disagree
/// about a wall.
/// </para>
/// <para>
/// <b>Print uses "sight" two ways, and this type now answers both.</b>
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Line of sight</b> — geometry plus open eyes. Walls block; a viewer that is dead,
/// Unconscious or Blinded does not qualify (with one printed exception, Blindsight —
/// see <see cref="HasOpenEyes(Combat.Combatant, int?)"/>); creatures and distance never
/// obstruct. This is what the <see cref="GridPosition"/>-shaped overloads answer.
/// </item>
/// <item>
/// <b>Seeing a creature</b> — line of sight <em>and</em> the target not Heavily Obscured
/// (a light model this engine does not have, and so never applies) <em>and</em> the
/// target not Invisible, unless a special sense (Blindsight, Truesight) reaches it
/// within range (#673). The <see cref="Combatant"/>-shaped overloads answer this — the
/// only difference a creature-target makes over a square-target is that a creature can
/// be Invisible.
/// </item>
/// </list>
/// <para>
/// <b>The qualifying-viewer readings, migrated from <c>PartyVision</c>'s doc comment:</b>
/// </para>
/// <list type="bullet">
/// <item>
/// <b>A viewer looks out of its whole space.</b> Any square of the viewer's space to any
/// square of the target's — a Large creature can see round a pillar its anchor square
/// alone could not, the same whole-space reading <see cref="CoverRules.AgainstSpace"/>
/// uses for the attack that follows the look.
/// </item>
/// <item>
/// <b>A creature is seen when any square of its space is seen.</b> The second half of the
/// fog reading, so a Large creature is not hidden by a wall that only covers one corner
/// of it.
/// </item>
/// <item>
/// <b>Eyes open: alive, not Unconscious, not Blinded — with Blindsight's own printed
/// exception.</b> The two conditions whose printed text closes the eyes ("Can't See")
/// disqualify a viewer, as does death; every other viewer sees, and always sees its own
/// square. Distance does not dim sight and darkness is not modelled, because the engine
/// models neither a range of vision nor a light level. Blindsight is the one printed
/// exception: "you can see anything that isn't behind Total Cover even if you have the
/// Blinded condition" (p.177) — within its range, a Blinded viewer's eyes count as open
/// after all. That exception needs to know how far away the thing being looked at is,
/// which a bare "is this viewer disqualified" question does not carry — see
/// <see cref="HasOpenEyes(Combat.Combatant, int?)"/>.
/// </item>
/// </list>
/// <para>
/// <b>This slice (#671) was additive; #673 is the first to wire consumers past the
/// line-of-sight half.</b> #672 re-derived Frightened, Ranged Attacks in Close Combat,
/// Dodge and Opportunity Attacks against the line-of-sight predicate. #673 adds the
/// Invisible-aware half and Blindsight/Truesight, and every one of those four readings
/// now composes with it for free — none of their call sites changed, because they all
/// route through the <see cref="Combatant"/>-shaped <see cref="CanSee(Battlefield,
/// Combatant, Combatant)"/> overload this file already gave them.
/// </para>
/// </remarks>
public static class VisionRules
{
    /// <inheritdoc cref="HasOpenEyes(Combat.Combatant, int?)"/>
    public static bool HasOpenEyes(Combatant viewer) => HasOpenEyes(viewer, distanceFeet: null);

    /// <summary>
    /// Whether this combatant's eyes are open toward something <paramref
    /// name="distanceFeet"/> away — the qualifying-viewer test: alive, not Unconscious,
    /// not Blinded, unless Blindsight reaches that far. A viewer that fails this sees
    /// nothing there, not even its own square.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Blindsight's printed exception (#673):</b> "Within that range, you can see
    /// anything that isn't behind Total Cover even if you have the Blinded condition or
    /// are in Darkness" (p.177). A Blinded viewer with Blindsight therefore still has open
    /// eyes for anything within the sense's range — Total Cover still blocks it, exactly
    /// as it blocks an ordinary viewer, because the exception is to "Blinded", not to
    /// geometry; the per-square loop in <see cref="CanSee(Battlefield, Combatant,
    /// GridPosition)"/> still applies <see cref="CoverRules.LineBlocked"/> regardless of
    /// which gate let the viewer through.
    /// </para>
    /// <para>
    /// <b>Truesight grants no such exception.</b> Only Blindsight's own printed sentence
    /// mentions seeing "even if you have the Blinded condition"; Truesight's page (p.190)
    /// does not, so a Blinded viewer with Truesight and no Blindsight still fails here —
    /// Truesight only ever matters once eyes are already open (see the Invisible-defeat
    /// clause on the <see cref="Combatant"/>-shaped <c>CanSee</c> overloads).
    /// </para>
    /// <para>
    /// <paramref name="distanceFeet"/> is null when the caller has no specific distance to
    /// offer — <c>PartyVision</c>'s per-side "can this creature see at all" aggregate, and
    /// every call site that predates #673. Null reads as "out of any Blindsight's range",
    /// the hampering default and byte-identical to this method's pre-#673 behaviour: a
    /// Blinded viewer with no distance offered still simply fails.
    /// </para>
    /// </remarks>
    public static bool HasOpenEyes(Combatant viewer, int? distanceFeet)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        if (viewer.IsDead || viewer.HasCondition(ConditionType.Unconscious))
        {
            return false;
        }

        if (!viewer.HasCondition(ConditionType.Blinded))
        {
            return true;
        }

        return distanceFeet is { } feet
            && viewer.Stats.BlindsightFeet is { } blindsight
            && blindsight >= feet;
    }

    /// <summary>
    /// Whether a qualifying viewer has line of sight to a square: an unblocked
    /// centre-to-centre line from any square of the viewer's space to the target square.
    /// A viewer that does not qualify (<see cref="HasOpenEyes(Combat.Combatant, int?)"/>,
    /// judged at this square's distance) sees nothing.
    /// </summary>
    public static bool CanSee(Battlefield field, Combatant viewer, GridPosition square)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(viewer);

        if (!HasOpenEyes(viewer, viewer.DistanceFeetTo(square)))
        {
            return false;
        }

        foreach (var eye in viewer.Space.Squares())
        {
            // The viewer's own square is always seen; otherwise the line must be
            // unblocked. LineBlocked already reports a zero-length line as clear, so the
            // first clause only makes the "always sees its own square" reading explicit.
            if (square == eye || !CoverRules.LineBlocked(field, eye, square))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a qualifying viewer can see another creature: line of sight to any square
    /// of the target's space, and — since #673 — not defeated by the target being
    /// Invisible unless the viewer's Blindsight or Truesight reaches it. A viewer that
    /// does not qualify sees no one.
    /// </summary>
    public static bool CanSee(Battlefield field, Combatant viewer, Combatant target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return CanSee(field, viewer, target, target.Space);
    }

    /// <summary>
    /// Whether a qualifying viewer has line of sight to a given space: any square of it
    /// is seen. This is the space-shaped sibling of <see cref="CanSee(Battlefield,
    /// Combatant, Combatant)"/>, for a caller that has a bare <see cref="CreatureSpace"/>
    /// with no creature identity behind it and so cannot be Invisible.
    /// </summary>
    public static bool CanSee(Battlefield field, Combatant viewer, CreatureSpace targetSpace)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(viewer);

        foreach (var square in targetSpace.Squares())
        {
            if (CanSee(field, viewer, square))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a qualifying viewer can see <paramref name="target"/>, judged at a
    /// hypothetical <paramref name="targetSpace"/> rather than wherever
    /// <see cref="Combatant.Space"/> has already moved to — #672's Opportunity Attack
    /// reading, which judges the mover's space at the square it is leaving.
    /// </summary>
    /// <remarks>
    /// The Invisible-defeat clause (#673) is asked of <paramref name="target"/>'s own
    /// condition regardless of which space it is being judged at — invisibility is a
    /// property of the creature, not of where it happens to stand — and the distance
    /// Blindsight/Truesight measure against is the distance to <paramref
    /// name="targetSpace"/>, matching whichever position line of sight was judged at.
    /// </remarks>
    public static bool CanSee(Battlefield field, Combatant viewer, Combatant target, CreatureSpace targetSpace)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(target);

        if (!CanSee(field, viewer, targetSpace))
        {
            return false;
        }

        if (!target.HasCondition(ConditionType.Invisible))
        {
            return true;
        }

        // "If a creature can somehow see you, you don't gain this benefit against that
        // creature" (Invisible, p.184) — the only two printed ways to somehow see an
        // Invisible creature are Blindsight and Truesight in range (p.177, p.190).
        var distance = viewer.Space.DistanceFeetTo(targetSpace);

        return (viewer.Stats.BlindsightFeet is { } blindsight && blindsight >= distance)
            || (viewer.Stats.TruesightFeet is { } truesight && truesight >= distance);
    }

    /// <summary>
    /// Whether any qualifying viewer on <paramref name="sideId"/> can see
    /// <paramref name="target"/> — the same "seen by at least one viewer" aggregate
    /// <c>PartyVision</c> already draws the fog with, lifted to a side-neutral
    /// <c>Core</c> rule (#673) so a client's withholding and a future tactics filter
    /// (#673-AI) can never disagree with each other about who a side can see.
    /// </summary>
    /// <remarks>
    /// No caller ships in this slice: #673-E is the engine half, and both of this
    /// predicate's intended consumers — the Godot client's withholding of an Invisible
    /// enemy, and the sight-aware target filter #673-AI adds once Brandon picks the
    /// blind-state fallback (SRD_Combat #673, designer reading §4) — are out of scope
    /// here. Added regardless, the same way #671 added <see cref="CanSee(Battlefield,
    /// Combatant, GridPosition)"/> with no consumer of its own: a <c>Core</c> rule earns
    /// its place by being the right seam, not by having a caller on day one.
    /// </remarks>
    public static bool SideCanSee(
        Battlefield field,
        string sideId,
        Combatant target,
        IReadOnlyCollection<Combatant> combatants)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(sideId);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(combatants);

        return combatants.Any(viewer =>
            string.Equals(viewer.SideId, sideId, StringComparison.Ordinal) && CanSee(field, viewer, target));
    }
}
