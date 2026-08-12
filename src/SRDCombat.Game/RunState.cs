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
/// <param name="ExperiencePoints">Experience earned so far. Stops accruing at death.</param>
/// <param name="IsDead">Dead for good. The gauntlet does not raise the dead.</param>
public sealed record CharacterState(
    int CurrentHitPoints,
    int HitDiceRemaining,
    int RagesRemaining,
    int SecondWindRemaining,
    int ActionSurgeRemaining,
    IReadOnlyDictionary<int, int> SpellSlotsRemaining,
    int ExperiencePoints,
    bool IsDead)
{
    /// <summary>
    /// Potions of Healing carried, by potency.
    /// </summary>
    /// <remarks>
    /// The one thing on this record that <b>no rest restores</b>: a spent potion is gone
    /// for the rest of the run, which is what makes finding one worth anything. It rides
    /// <c>AfterRest</c>'s <c>with</c> untouched for exactly that reason.
    /// </remarks>
    public IReadOnlyDictionary<HealingPotion, int> Potions { get; init; } =
        new Dictionary<HealingPotion, int>();

    /// <summary>The level this character has earned.</summary>
    public int Level => ExperienceRules.LevelFor(ExperiencePoints);

    /// <summary>The same state with one more potion of a potency in the pack.</summary>
    public CharacterState Carrying(HealingPotion potency)
    {
        var potions = new Dictionary<HealingPotion, int>(Potions)
        {
            [potency] = Potions.GetValueOrDefault(potency) + 1,
        };

        return this with { Potions = potions };
    }

    /// <summary>The same state with an award added. The dead earn nothing.</summary>
    public CharacterState Earning(int experience) =>
        IsDead ? this : this with { ExperiencePoints = ExperiencePoints + experience };

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
            // A character built at a level above 1 starts with the experience that level
            // costs, so a run begun partway up the ladder advances from the right place
            // rather than levelling again immediately.
            AdvancementRules.ExperienceToReach(member.Sheet.Level),
            IsDead: false);
    }

    /// <summary>True when this character can still be put in a fight.</summary>
    public bool CanFight => !IsDead;

    /// <summary>
    /// Whether a fallen character rejoins the party, and when.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a house rule, not a rule and not a reading of one.</b> The SRD has
    /// nothing to say about it: recovering the dead means Revivify or Raise Dead, which
    /// need diamonds, a caster of the right level and a world outside the fight, none of
    /// which this game has. Everywhere else in this project a decision is either printed
    /// or a documented interpretation of something printed; this one is neither, and is
    /// marked so nobody later mistakes it for the book.
    /// </para>
    /// <para>
    /// The reason it exists is measured rather than felt. With death permanent, a run
    /// died out with a median of three fights cleared of thirty across every combination
    /// of tactics and encounter shape tried — not because the party wore down, but
    /// because a fight it won still cost a character, and a party of three then lost the
    /// next one. Death as an ending made the ladder unplayable; death as a setback keeps
    /// the cost real, because a fallen character misses every fight until the next Long
    /// Rest and earns no experience while down, so they come back a level behind.
    /// </para>
    /// <para>
    /// They return at <b>1 hit point</b> and then take the rest normally, which restores
    /// them — the Long Rest is doing the healing, and the rule here only decides that
    /// there is somebody there to heal.
    /// </para>
    /// </remarks>
    public const RestKind ReturnsFromTheDead = RestKind.Long;

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
            // Drunk is drunk: what came out of the fight is what is left.
            Potions = new Dictionary<HealingPotion, int>(combatant.Inventory.Potions),
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

        // A fallen character rejoins the party on a Long Rest, and then takes the rest
        // like anyone else. See ReturnsFromTheDead for why this is a house rule rather
        // than a reading of anything printed.
        var state = IsDead && rest == RestKind.Long
            ? this with { IsDead = false, CurrentHitPoints = 1 }
            : this;

        if (state.IsDead || !RestRules.CanRest(state.CurrentHitPoints))
        {
            return state;
        }

        var character = member.Combatant.Stats.Character;
        var maximumHitPoints = member.Sheet.MaximumHitPoints;

        var rested = state with
        {
            RagesRemaining = RestRules.OneOnShortAllOnLong(rest, state.RagesRemaining, character?.RageUses ?? 0),
            SecondWindRemaining = RestRules.OneOnShortAllOnLong(
                rest,
                state.SecondWindRemaining,
                character?.SecondWindUses ?? 0),
            ActionSurgeRemaining = RestRules.AllOnEitherRest(rest, character?.ActionSurgeUses ?? 0),
            HitDiceRemaining = RestRules.HitDiceAfter(rest, state.HitDiceRemaining, member.Sheet.Level),
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
