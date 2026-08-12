using SRDCombat.Core.Combat;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>
/// What a character carries out of one fight and into the next.
/// </summary>
/// <remarks>
/// <para>
/// The whole point of a gauntlet rather than a series of unrelated fights: hit points,
/// spent resources and the dead stay spent and dead until something restores them. A run
/// where every fight begins fresh is four separate fights wearing a ladder's clothes.
/// </para>
/// <para>
/// Deliberately only the things that <em>change</em>. Everything derived — armour class,
/// attack bonuses, the maxima these are measured against — still comes from the sheet, so
/// a run cannot drift from the rules that made the character.
/// </para>
/// </remarks>
/// <param name="CurrentHitPoints">Hit points now. Zero and not dead means downed and stable.</param>
/// <param name="HitDiceRemaining">Hit Point Dice left to spend on a Short Rest.</param>
/// <param name="RagesRemaining">Rages left.</param>
/// <param name="SecondWindRemaining">Second Wind uses left.</param>
/// <param name="ActionSurgeRemaining">Action Surge uses left.</param>
/// <param name="SpellSlotsRemaining">Spell slots left, by level.</param>
/// <param name="IsDead">Dead for good. The gauntlet does not raise the dead.</param>
public sealed record CharacterState(
    int CurrentHitPoints,
    int HitDiceRemaining,
    int RagesRemaining,
    int SecondWindRemaining,
    int ActionSurgeRemaining,
    IReadOnlyDictionary<int, int> SpellSlotsRemaining,
    bool IsDead)
{
    /// <summary>A character at full strength, as they begin the gauntlet.</summary>
    public static CharacterState Fresh(PartyMember member)
    {
        ArgumentNullException.ThrowIfNull(member);

        var character = member.Combatant.Stats.Character;

        return new CharacterState(
            member.Sheet.MaximumHitPoints,
            // One Hit Point Die per class level.
            member.Sheet.Level,
            character?.RageUses ?? 0,
            character?.SecondWindUses ?? 0,
            character?.ActionSurgeUses ?? 0,
            new Dictionary<int, int>(member.Sheet.SpellSlots),
            IsDead: false);
    }

    /// <summary>True when this character can still be put in a fight.</summary>
    public bool CanFight => !IsDead;

    /// <summary>Reads the state back off a combatant once a fight has ended.</summary>
    public CharacterState AfterFight(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        return this with
        {
            // A character who went down but survived is Stable at 0. RestRules records
            // the reading that brings them back to 1 before the next fight.
            CurrentHitPoints = combatant.IsDead
                ? 0
                : Math.Max(combatant.CurrentHitPoints, RestRules.HitPointsAfterStabilising),
            RagesRemaining = combatant.Features.RagesRemaining,
            SecondWindRemaining = combatant.Features.SecondWindRemaining,
            ActionSurgeRemaining = combatant.Features.ActionSurgeRemaining,
            SpellSlotsRemaining = new Dictionary<int, int>(combatant.Features.SpellSlotsRemaining),
            IsDead = combatant.IsDead,
        };
    }

    /// <summary>
    /// The state after a rest, applying each resource's own printed recharge.
    /// </summary>
    /// <remarks>
    /// A Short Rest spends Hit Point Dice greedily up to the character's maximum, which
    /// is a stated interpretation: the SRD lets a player "decide to spend an additional
    /// Hit Point Die after each roll", and with no clock and no reason to hoard, healing
    /// to full is what a player would choose every time.
    /// </remarks>
    public CharacterState AfterRest(PartyMember member, RestKind rest, IRandomSource random, int hitDieSides)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(random);

        if (IsDead || !RestRules.CanRest(CurrentHitPoints))
        {
            return this;
        }

        var character = member.Combatant.Stats.Character;
        var maximumHitPoints = member.Sheet.MaximumHitPoints;

        var rested = this with
        {
            RagesRemaining = RestRules.OneOnShortAllOnLong(rest, RagesRemaining, character?.RageUses ?? 0),
            SecondWindRemaining = RestRules.OneOnShortAllOnLong(
                rest,
                SecondWindRemaining,
                character?.SecondWindUses ?? 0),
            ActionSurgeRemaining = RestRules.AllOnEitherRest(rest, character?.ActionSurgeUses ?? 0),
            HitDiceRemaining = RestRules.HitDiceAfter(rest, HitDiceRemaining, member.Sheet.Level),
        };

        if (rest == RestKind.Long)
        {
            return rested with
            {
                CurrentHitPoints = RestRules.HitPointsAfterLongRest(maximumHitPoints),
                SpellSlotsRemaining = new Dictionary<int, int>(member.Sheet.SpellSlots),
            };
        }

        // Short Rest: spend dice until full or out.
        var hitPoints = rested.CurrentHitPoints;
        var dice = rested.HitDiceRemaining;
        var constitution = member.Sheet.Modifier(Core.Definitions.Ability.Constitution);

        while (dice > 0 && hitPoints < maximumHitPoints)
        {
            hitPoints = Math.Min(maximumHitPoints, hitPoints + RestRules.SpendHitDie(random, hitDieSides, constitution));
            dice--;
        }

        return rested with { CurrentHitPoints = hitPoints, HitDiceRemaining = dice };
    }
}
