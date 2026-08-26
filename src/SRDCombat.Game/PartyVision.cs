using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>
/// Which squares a side can see — the fog of war's one question.
/// </summary>
/// <remarks>
/// <para>
/// <b>A display judgement, not a rule</b> — the same standing as
/// <see cref="TargetChoice"/>. The engine models no sight: nothing here changes what an
/// attack, a spell or a condition may do, and the engine refuses or allows everything
/// exactly as it did. What this decides is what a client is entitled to <i>show</i> the
/// player: a monster standing where no party member could see it is drawn as absent,
/// because showing it would be the display scouting through walls on the player's
/// behalf.
/// </para>
/// <para>
/// <b>The readings, stated:</b> a square is seen when at least one qualifying viewer on
/// the side has an unblocked centre-to-centre line to it —
/// <see cref="CoverRules.LineBlocked"/>, the very judgement Total Cover refuses attacks
/// with, so what can be seen and what can be shot at never disagree about a wall.
/// Creatures never block sight (the cover table's own reading: crowds are not walls),
/// and distance does not dim it, because the engine models neither darkness nor range
/// of vision. A viewer qualifies while alive and neither Unconscious nor Blinded — the
/// two conditions whose printed text closes the eyes — and always sees its own square.
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

        // A viewer looks out of its whole space, not out of one corner of it: a Large
        // creature can see round a pillar its anchor square cannot. The other half of
        // the fog reading — a creature is *seen* when any square of its space is seen —
        // belongs to whoever asks whether a creature is visible, which today is the
        // client (#430); nothing in this project asks it engine-side.
        var viewers = combatants
            .Where(combatant => combatant.SideId == sideId && CanSee(combatant))
            .SelectMany(combatant => combatant.Space.Squares())
            .ToList();

        var visible = new HashSet<GridPosition>();

        foreach (var square in field.AllSquares())
        {
            foreach (var viewer in viewers)
            {
                if (square == viewer || !CoverRules.LineBlocked(field, viewer, square))
                {
                    visible.Add(square);
                    break;
                }
            }
        }

        return visible;
    }

    /// <summary>Whether this combatant's eyes are open: alive, not Unconscious, not Blinded.</summary>
    private static bool CanSee(Combatant combatant) =>
        !combatant.IsDead
        && !combatant.HasCondition(ConditionType.Unconscious)
        && !combatant.HasCondition(ConditionType.Blinded);
}
