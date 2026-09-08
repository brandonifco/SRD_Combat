using SRDCombat.Core.Combat;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>
/// Which squares a side can see — the fog of war's one question.
/// </summary>
/// <remarks>
/// <para>
/// <b>A side-aggregate over a Core rule.</b> The per-viewer geometry — walls block,
/// creatures and distance do not, a viewer looks out of its whole space, eyes open means
/// alive and neither Unconscious nor Blinded — now lives in
/// <see cref="VisionRules"/> as a real <c>Core</c> predicate the engine can consult
/// (#671). This type keeps only the <em>aggregation</em>: a square is seen by a side when
/// at least one viewer on the side sees it, the union that draws the fog.
/// </para>
/// <para>
/// <b>The aggregation is still a display judgement, not a rule.</b> The engine consults
/// no sight when it resolves an attack, a spell or a condition — that boundary crossing
/// is #672's, wiring the individual readings to <see cref="VisionRules"/> one at a time.
/// What this type decides is only what a client is entitled to <i>show</i> the player: a
/// monster standing where no party member could see it is drawn as absent, because
/// showing it would be the display scouting through walls on the player's behalf.
/// </para>
/// </remarks>
public static class PartyVision
{
    /// <summary>Every square at least one qualifying viewer on the side can see.</summary>
    public static HashSet<GridPosition> VisibleSquares(
        Battlefield field,
        IEnumerable<Combatant> combatants,
        string sideId)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(combatants);

        var viewers = combatants
            .Where(combatant => combatant.SideId == sideId && VisionRules.HasOpenEyes(combatant))
            .ToList();

        var visible = new HashSet<GridPosition>();

        foreach (var square in field.AllSquares())
        {
            foreach (var viewer in viewers)
            {
                if (VisionRules.CanSee(field, viewer, square))
                {
                    visible.Add(square);
                    break;
                }
            }
        }

        return visible;
    }
}
