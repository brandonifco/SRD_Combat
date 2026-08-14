using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>Something a client can offer the player on their turn.</summary>
public enum TurnAction
{
    Dodge,
    Dash,
    Disengage,
    StandUp,
    Escape,
    EndTurn,
    Attacks,
    Cast,
    Drink,
    GivePotion,
    Rage,
    RecklessAttack,
    SecondWind,
    ActionSurge,
    SteadyAim,
    CunningDash,
    CunningDisengage,
    CunningStrikeTrip,
    DivineSparkHeal,
    DivineSparkHarm,
}

/// <summary>
/// Which actions are worth offering right now, and the key each one answers to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Offered only when usable.</b> A button for something the engine would refuse is
/// noise on a screen the player is reading under pressure, so the list shrinks as a turn
/// is spent: Dodge and Dash go when the Action does, Second Wind when the Bonus Action
/// does, Stand Up appears only while Prone. This is a deliberate change of stance from
/// the client's first draft, which showed everything and let refusals teach the rules —
/// the status line still says what is left (<c>Action ✓ Bonus ✗</c>), so <em>why</em> an
/// option went away stays legible, and the engine still refuses anything that reaches it
/// by another road.
/// </para>
/// <para>
/// <b>This mirrors the engine's refusals, and the engine remains the authority.</b>
/// That duplication is the real cost of the change and is worth stating plainly: a
/// predicate here that drifts from its refusal would hide an action the rules allow.
/// It lives in one place rather than two clients, and <c>TurnOptionsTests</c> guards it
/// from the direction that matters — anything this class hides, the engine must refuse.
/// </para>
/// <para>
/// <b>A key belongs to an action, not to a position.</b> Every key below is unique
/// across the whole set, so D is Dodge whenever Dodge is offered and never anything
/// else — no reshuffling as the row grows and shrinks, and nothing to relearn between
/// characters.
/// </para>
/// </remarks>
public static class TurnOptions
{
    /// <summary>The key an action answers to. Unique across every action.</summary>
    public static char Hotkey(TurnAction action) => action switch
    {
        TurnAction.Dodge => 'D',
        TurnAction.Dash => 'R',
        TurnAction.Disengage => 'G',
        TurnAction.StandUp => 'U',
        TurnAction.Escape => 'E',
        TurnAction.EndTurn => ' ',
        TurnAction.Attacks => 'A',
        TurnAction.Cast => 'C',
        TurnAction.Drink => 'Q',
        TurnAction.GivePotion => 'P',
        TurnAction.Rage => 'F',
        TurnAction.RecklessAttack => 'K',
        TurnAction.SecondWind => 'W',
        TurnAction.ActionSurge => 'S',
        TurnAction.SteadyAim => 'M',
        TurnAction.CunningDash => 'X',
        TurnAction.CunningDisengage => 'Z',
        TurnAction.CunningStrikeTrip => 'T',
        TurnAction.DivineSparkHeal => 'H',
        TurnAction.DivineSparkHarm => 'J',
        _ => '?',
    };

    /// <summary>The action's name on a button.</summary>
    public static string Caption(TurnAction action) => action switch
    {
        TurnAction.Dodge => "Dodge",
        TurnAction.Dash => "Dash",
        TurnAction.Disengage => "Disengage",
        TurnAction.StandUp => "Stand Up",
        TurnAction.Escape => "Escape",
        TurnAction.EndTurn => "End Turn",
        TurnAction.Attacks => "Attacks",
        TurnAction.Cast => "Cast",
        TurnAction.Drink => "Drink",
        TurnAction.GivePotion => "Give Potion",
        TurnAction.Rage => "Rage",
        TurnAction.RecklessAttack => "Reckless",
        TurnAction.SecondWind => "Second Wind",
        TurnAction.ActionSurge => "Action Surge",
        TurnAction.SteadyAim => "Steady Aim",
        TurnAction.CunningDash => "Cunning Dash",
        TurnAction.CunningDisengage => "Cunning Disengage",
        TurnAction.CunningStrikeTrip => "Trip",
        TurnAction.DivineSparkHeal => "Spark Heal",
        TurnAction.DivineSparkHarm => "Spark Harm",
        _ => action.ToString(),
    };

    /// <summary>How the key reads on a button — "Space" is not a character.</summary>
    public static string HotkeyLabel(TurnAction action) =>
        Hotkey(action) == ' ' ? "Space" : Hotkey(action).ToString();

    /// <summary>Everything this creature could usefully be offered right now.</summary>
    public static IReadOnlyList<TurnAction> For(Encounter encounter, Combatant actor)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(actor);

        return Enum.GetValues<TurnAction>()
            .Where(action => IsAvailable(encounter, actor, action))
            .ToArray();
    }

    /// <summary>Whether one action is worth offering. Mirrors the engine's own refusals.</summary>
    public static bool IsAvailable(Encounter encounter, Combatant actor, TurnAction action)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(actor);

        if (encounter.IsComplete || !actor.CanAct)
        {
            return false;
        }

        var turn = actor.Turn;
        var features = actor.Features;
        var character = actor.Stats.Character;

        return action switch
        {
            // Ending a turn is always on the table; it is the one way out of every
            // state, including one where nothing else is offered at all.
            TurnAction.EndTurn => true,

            TurnAction.Dodge or TurnAction.Dash or TurnAction.Disengage => turn.HasAction,

            // Standing costs half your Speed, and a grapple makes that half of nothing.
            TurnAction.StandUp =>
                actor.HasCondition(ConditionType.Prone) && ConditionRules.ImmobilisedBy(actor) is null,

            TurnAction.Escape => actor.ConditionState(ConditionType.Grappled) is { EscapeDifficultyClass: not null },

            // The menu is only worth opening when there is a choice inside it; with one
            // attack the click on the enemy already swings it.
            TurnAction.Attacks =>
                actor.Stats.Attacks.Count > 1
                && (turn.HasAction || features.AttacksRemainingThisAction > 0),

            // Some spells are cast with a Bonus Action, so either will do.
            TurnAction.Cast => character is { CanCast: true } && (turn.HasAction || turn.HasBonusAction),

            TurnAction.Drink => turn.HasBonusAction && actor.Inventory.TotalPotions > 0,

            // A potion in reach is usable whoever carries it, so this is offered to a
            // character carrying none while an ally beside them has one.
            TurnAction.GivePotion =>
                turn.HasBonusAction
                && (actor.Inventory.TotalPotions > 0 || PotionWithinReach(encounter, actor)),

            // Raging again while raging is the printed Bonus Action extension, so the
            // button stays while a Rage is running even with no uses left.
            TurnAction.Rage =>
                actor.Stats.Has(ClassFeature.Rage)
                && turn.HasBonusAction
                && (features.IsRaging || features.RagesRemaining > 0),

            TurnAction.RecklessAttack =>
                actor.Stats.Has(ClassFeature.RecklessAttack) && !features.IsRecklessThisTurn,

            TurnAction.SecondWind =>
                actor.Stats.Has(ClassFeature.SecondWind) && turn.HasBonusAction && features.SecondWindRemaining > 0,

            // Action Surge buys an Action, so it is only worth offering once the Action
            // is gone — the engine refuses it outright while one is still in hand.
            TurnAction.ActionSurge =>
                actor.Stats.Has(ClassFeature.ActionSurge) && !turn.HasAction && features.ActionSurgeRemaining > 0,

            TurnAction.SteadyAim =>
                actor.Stats.Has(ClassFeature.SteadyAim) && turn.HasBonusAction && !turn.HasMoved,

            TurnAction.CunningDash or TurnAction.CunningDisengage =>
                actor.Stats.Has(ClassFeature.CunningAction) && turn.HasBonusAction,

            // A die is forgone, so there has to be more than one to forgo it from.
            TurnAction.CunningStrikeTrip =>
                actor.Stats.Has(ClassFeature.CunningStrike)
                && !features.SneakAttackUsedThisTurn
                && character?.SneakAttackDamage is { Count: > 1 },

            TurnAction.DivineSparkHeal or TurnAction.DivineSparkHarm =>
                actor.Stats.Has(ClassFeature.ChannelDivinity)
                && turn.HasAction
                && features.ChannelDivinityRemaining > 0,

            _ => false,
        };
    }

    private static bool PotionWithinReach(Encounter encounter, Combatant actor) =>
        encounter.Combatants.Any(other => other.SideId == actor.SideId
            && !ReferenceEquals(other, actor)
            && !other.IsDead
            && other.Inventory.TotalPotions > 0
            && actor.Position.DistanceFeetTo(other.Position) <= PotionRules.ReachFeet);
}
