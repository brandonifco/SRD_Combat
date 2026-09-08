using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>
/// Line of sight: can a creature see a square, or another creature? Geometry plus open
/// eyes — the one narrow sense of "sight" this engine models.
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
/// <b>This models only the first of print's two senses of sight.</b> Print uses "sight"
/// two ways, and this predicate is the narrower:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Line of sight</b> — geometry plus open eyes. Walls block; a viewer that is dead,
/// Unconscious or Blinded does not qualify; creatures and distance never obstruct. This
/// is what <see cref="VisionRules"/> answers.
/// </item>
/// <item>
/// <b>Seeing a creature</b> — line of sight <em>and</em> the target not Heavily Obscured
/// (a light model this engine does not have), <em>and</em> the target not Invisible, or a
/// special sense (Blindsight, Truesight) overriding those within range. That wider
/// question is deliberately out of this seam; it is #673's, when the Invisible condition
/// and monster senses reach combat.
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
/// <b>Eyes open: alive, not Unconscious, not Blinded.</b> The two conditions whose
/// printed text closes the eyes ("Can't See") disqualify a viewer, as does death; every
/// other viewer sees, and always sees its own square. Distance does not dim sight and
/// darkness is not modelled, because the engine models neither a range of vision nor a
/// light level.
/// </item>
/// </list>
/// <para>
/// <b>This slice is additive.</b> The predicate exists so the engine has a seam to
/// consult; in the slice that introduces it, <em>nothing</em> in the turn loop,
/// conditions, targeting or initiative consults it — the only caller is
/// <c>SRDCombat.Game.PartyVision</c>, which already made this same geometric judgement as
/// a display concern and now delegates it here. The readings that become disturbable once
/// sight is a rule — Frightened's "within line of sight", Blinded's "who can see you",
/// the opportunity-attack "a creature that you can see" — are re-derived against this
/// predicate in #672, each in its own slice, not here. Until then this moves no combat
/// outcome, which the frozen transcript's byte-flatness proves.
/// </para>
/// </remarks>
public static class VisionRules
{
    /// <summary>
    /// Whether this combatant's eyes are open — the qualifying-viewer test: alive, not
    /// Unconscious, not Blinded. A viewer that fails this sees nothing, not even its own
    /// square.
    /// </summary>
    /// <remarks>
    /// This is the line-of-sight sense only. A Blinded creature printed with Blindsight
    /// still fails here, because monster senses are not carried into
    /// <see cref="Combatant"/> today; seeing-through-Blinded is #673's decision, when
    /// senses reach combat. Stated so the reading is a choice, not an oversight.
    /// </remarks>
    public static bool HasOpenEyes(Combatant viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        return !viewer.IsDead
            && !viewer.HasCondition(ConditionType.Unconscious)
            && !viewer.HasCondition(ConditionType.Blinded);
    }

    /// <summary>
    /// Whether a qualifying viewer has line of sight to a square: an unblocked
    /// centre-to-centre line from any square of the viewer's space to the target square.
    /// A viewer that does not qualify (<see cref="HasOpenEyes"/>) sees nothing.
    /// </summary>
    public static bool CanSee(Battlefield field, Combatant viewer, GridPosition square)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(viewer);

        if (!HasOpenEyes(viewer))
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
    /// Whether a qualifying viewer has line of sight to another creature: any square of
    /// the target's space is seen. A viewer that does not qualify
    /// (<see cref="HasOpenEyes"/>) sees no one.
    /// </summary>
    public static bool CanSee(Battlefield field, Combatant viewer, Combatant target)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(target);

        if (!HasOpenEyes(viewer))
        {
            return false;
        }

        foreach (var square in target.Space.Squares())
        {
            if (CanSee(field, viewer, square))
            {
                return true;
            }
        }

        return false;
    }
}
