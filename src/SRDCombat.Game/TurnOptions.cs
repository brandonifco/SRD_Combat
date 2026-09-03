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
    TurnUndead,
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
        TurnAction.TurnUndead => 'N',
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
        TurnAction.Attacks => "Attack",
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
        TurnAction.TurnUndead => "Turn Undead",
        _ => action.ToString(),
    };

    /// <summary>
    /// What the action actually does, in one sentence, for a hint a player can read
    /// without leaving the fight.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Beside <see cref="Caption"/> rather than in a client, the same reason
    /// <c>TurnBanner</c> and <c>ShopOffer.Effect</c> are: a second client wording these
    /// separately would be a second place for them to drift from the rules they describe.
    /// </para>
    /// <para>
    /// <b>These paraphrase the printed rules; they are not a second copy of them.</b> The
    /// engine remains the authority on what an action does, and a hint saying otherwise
    /// is a bug in the hint. Each names the concrete consequence a player is choosing
    /// between — what it costs, and what changes — because "Dodge: you dodge" is the kind
    /// of tooltip that teaches nobody anything.
    /// </para>
    /// <para>
    /// <b>Where an action has a right moment in the turn, the hint says so.</b> Several
    /// of these buy nothing at all taken in the wrong order — Reckless Attack after the
    /// swing it was meant to help, Disengage after the movement that already provoked,
    /// Rage after the attacks its damage would have ridden — and none of that is visible
    /// from a name on a button. Two orderings are worth stating everywhere they apply:
    /// <em>move before you act</em>, because acting is what leaves only End Turn on the
    /// row and the turn then ends itself; and <em>arm the free riders first</em>, since
    /// Reckless, Rage and a Cunning Strike all attach to attacks made after them.
    /// </para>
    /// </remarks>
    public static string Hint(TurnAction action) => action switch
    {
        TurnAction.Dodge =>
            "Action. Until your next turn, attacks against you have Disadvantage and your "
            + "Dexterity saves have Advantage.",
        TurnAction.Dash => "Action. Move again this turn — double your Speed. Take it before you walk, not after.",
        TurnAction.Disengage => "Action. Your movement provokes no Opportunity Attacks this turn. Take it before you walk away, or the swing has already happened.",
        TurnAction.StandUp => "Costs half your Speed. Gets you up from Prone.",
        TurnAction.Escape =>
            "Action. Athletics or Acrobatics against the grapple's DC to break free.",
        TurnAction.EndTurn => "Finish here and pass to the next creature.",
        TurnAction.Attacks =>
            "Action. Attack a creature in reach. A bow within 5 feet of an enemy rolls at "
            + "Disadvantage. Move first — the turn ends itself once nothing but End Turn is left.",
        TurnAction.Cast => "Action. Cast a spell you have prepared, spending a slot unless it is a cantrip. Move first — the turn ends itself once nothing but End Turn is left.",
        TurnAction.Drink => "Bonus Action. Drink a Potion of Healing from your own pack.",
        TurnAction.GivePotion =>
            "Bonus Action. Pour a potion into an ally within 5 feet — this is how somebody "
            + "at 0 hit points gets back up.",
        TurnAction.Rage =>
            "Bonus Action. Bonus damage on Strength attacks and resistance to Bludgeoning, "
            + "Piercing and Slashing. Rage first, then attack — the bonus only reaches swings made after it. "
            + "Attack or force a save each turn to keep it going.",
        TurnAction.RecklessAttack =>
            "Free. Advantage on your Strength attacks this turn — and every attack against "
            + "you has Advantage until your next. Declare it before your first swing; after it, it buys nothing.",
        TurnAction.SecondWind => "Bonus Action. Heal yourself 1d10 plus your Fighter level. Worth spending before you end a turn on low hit points, not after somebody drops you.",
        TurnAction.ActionSurge => "Free. Take one more Action this turn. Only once your Action is spent — that is what it surges past.",
        TurnAction.SteadyAim =>
            "Bonus Action. Advantage on your next attack, but your Speed drops to 0 for the "
            + "turn. Only before you have moved.",
        TurnAction.CunningDash => "Bonus Action. Dash, without spending your Action. Before you walk, like any Dash.",
        TurnAction.CunningDisengage => "Bonus Action. Disengage, without spending your Action. Before you walk away, not after.",
        TurnAction.CunningStrikeTrip =>
            "Spends a Sneak Attack die. On a hit, a Large or smaller target makes a Dexterity "
            + "save or falls Prone. Arm it before the attack it rides on.",
        TurnAction.DivineSparkHeal =>
            "Bonus Action, one Channel Divinity use. Heal a creature within 30 feet 1d8 plus "
            + "your Wisdom modifier.",
        TurnAction.DivineSparkHarm =>
            "Bonus Action, one Channel Divinity use. A creature within 30 feet makes a "
            + "Constitution save or takes 1d8 plus your Wisdom modifier, half on a success.",
        TurnAction.TurnUndead =>
            "Action, one Channel Divinity use. Each chosen Undead within 30 feet makes a "
            + "Wisdom save or is Frightened and Incapacitated for a minute, ending early if "
            + "it takes damage.",
        _ => string.Empty,
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

            // Offered whenever the character can swing at all. It used to appear only
            // with more than one attack to choose between, on the reasoning that a
            // click on the enemy already swings — but that left attacking as the one
            // thing with no button, discoverable only by knowing to click the board.
            TurnAction.Attacks =>
                actor.Stats.Attacks.Count > 0
                && (turn.HasAction || features.AttacksRemainingThisAction > 0),

            // Offered only while there is a spell that could actually be cast. Asking
            // "is either action free?" was not enough: after an Action spell the Bonus
            // Action is still in hand, so Cast stayed on the row, opened the list, and
            // met "action.spent" on whatever was picked.
            TurnAction.Cast => character is { CanCast: true }
                && character.Spells.Any(spell => CanCastNow(actor, spell)),

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

            // The last clause — a turnable Undead actually in range — keeps the button
            // off the row when nothing can be turned, matching the class's "offer only
            // when usable" doctrine; the interactive target-picking loop stays a client
            // concern (#317).
            TurnAction.TurnUndead =>
                actor.Stats.Has(ClassFeature.ChannelDivinity)
                && turn.HasAction
                && features.ChannelDivinityRemaining > 0
                && AnyTurnableUndeadWithinReach(encounter, actor),

            _ => false,
        };
    }

    /// <summary>
    /// Whether this caster could cast this spell right now — the casting time is still
    /// in hand, and a slot of its level or higher is left.
    /// </summary>
    /// <remarks>
    /// The menu shows what this admits and the Cast button appears only while something
    /// does, so a spell list can no longer offer a row whose only possible answer is a
    /// refusal. Extended casting times are excluded outright: a spell taking a minute
    /// is refused as too slow in a fight, and the engine says so.
    /// </remarks>
    public static bool CanCastNow(Combatant actor, SpellDefinition spell)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(spell);

        var affordable = spell.CastingTime switch
        {
            SpellCastingTime.Action => actor.Turn.HasAction,
            SpellCastingTime.BonusAction => actor.Turn.HasBonusAction,
            SpellCastingTime.Reaction => actor.Turn.HasReaction,
            _ => false,
        };

        if (!affordable)
        {
            return false;
        }

        if (spell.IsCantrip)
        {
            return true;
        }

        // "You must use a spell slot of the spell's level or higher."
        for (var level = spell.Level; level <= 9; level++)
        {
            if (actor.Features.SpellSlotsRemaining.GetValueOrDefault(level) > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool PotionWithinReach(Encounter encounter, Combatant actor) =>
        encounter.Combatants.Any(other => other.SideId == actor.SideId
            && !ReferenceEquals(other, actor)
            && !other.IsDead
            && other.Inventory.TotalPotions > 0
            && actor.DistanceFeetTo(other) <= PotionRules.ReachFeet);

    /// <summary>Turn Undead's own range and validity, read for the offer rather than the cast.</summary>
    private const int TurnUndeadRangeFeet = 30;

    /// <summary>Whether at least one Undead is actually turnable right now.</summary>
    /// <remarks>
    /// Mirrors <c>Encounter.TurnUndead</c>'s per-target validation (AC-3), all five
    /// refusal checks (#618, item 6: the fifth, already-turned, was missing until now —
    /// the button could show available against the only reachable Undead when it was
    /// already turned by a different Cleric; the engine refused it correctly regardless,
    /// since the offer is a hint and the engine is the authority, but the row should not
    /// have shown it in the first place).
    /// </remarks>
    private static bool AnyTurnableUndeadWithinReach(Encounter encounter, Combatant actor) =>
        encounter.Combatants.Any(other =>
            other.IsActive
            && other.Stats.Type == CreatureType.Undead
            && actor.DistanceFeetTo(other) <= TurnUndeadRangeFeet
            && CoverRules.AgainstSpace(encounter.Battlefield, actor.Space, other.Space, encounter.Combatants)
                != CoverDegree.Total
            && !AlreadyTurnedByAnotherCleric(other, actor));

    /// <summary>
    /// Whether <paramref name="target"/> is still held by a different Cleric's earlier
    /// Turn Undead — <c>Encounter.TurnUndead</c>'s own already_turned guard, read here
    /// for the offer rather than the cast.
    /// </summary>
    /// <remarks>
    /// Checked on Frightened alone, not Incapacitated, for the same reason the engine's
    /// own guard now is: <c>other.IsActive</c> in <see cref="AnyTurnableUndeadWithinReach"/>
    /// already guarantees <c>!other.HasCondition(ConditionType.Incapacitated)</c> — that
    /// is CanAct's own definition — so an Incapacitated condition flagged by a different
    /// source can never coexist with a live IsActive here either.
    /// </remarks>
    private static bool AlreadyTurnedByAnotherCleric(Combatant target, Combatant actor) =>
        target.ConditionState(ConditionType.Frightened) is
            { EndsEarlyOnDamageOrSourceDown: true, SourceId: { } turnedBy }
        && !string.Equals(turnedBy, actor.Id, StringComparison.Ordinal);
}
