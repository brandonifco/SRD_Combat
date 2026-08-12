using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>
/// Casting spells.
/// </summary>
/// <remarks>
/// <para>
/// A spell resolves one of two ways, and which one is decided by the content rather than
/// by the caller: an attack spell rolls a spell attack against AC, and a save spell makes
/// every creature in its area roll against the caster's spell save DC. A spell that does
/// neither is refused with a reason rather than silently doing nothing — the same rule
/// the whole project runs on.
/// </para>
/// <para>
/// Deliberately not implemented: upcasting (the scaling text is carried but not applied),
/// spells whose effect is neither damage nor a condition, and Cylinder areas. Each is
/// refused with a named code so a client can say why.
/// </para>
/// </remarks>
public sealed partial class Encounter
{
    /// <summary>Casts a spell at a creature.</summary>
    public ActionRefusal? CastSpell(string spellId, Combatant target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return CastSpell(spellId, target.Position, target);
    }

    /// <summary>Casts a spell at a point, for an area effect.</summary>
    public ActionRefusal? CastSpell(string spellId, GridPosition point, Combatant? target = null)
    {
        if (ActiveCombatant is not { } caster)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (caster.Stats.Character is not { CanCast: true } character)
        {
            return new ActionRefusal("spell.not_a_caster", $"{caster.Name} cannot cast spells.");
        }

        var spell = character.Spells.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, spellId, StringComparison.Ordinal));

        if (spell is null)
        {
            return new ActionRefusal("spell.unknown", $"{caster.Name} does not know '{spellId}'.");
        }

        if (CheckCastingCost(caster, spell) is { } refusal)
        {
            return refusal;
        }

        // A Self spell is not aimed at anybody — its area is centred on the caster — so
        // only a spell with a real target range is checked against the target.
        if (target is not null && !spell.IsSelfRanged && spell.TargetRangeFeet is { } range
            && caster.Position.DistanceFeetTo(target.Position) > range)
        {
            return new ActionRefusal(
                "spell.out_of_range",
                $"{target.Name} is beyond {spell.Name}'s {range} ft. range.");
        }

        var resolution = ResolveSpellShape(spell, target);

        if (resolution is not null)
        {
            return resolution;
        }

        // "Can't Harm the Charmer": an attack spell aimed at the charmer is an attack,
        // and a damaging save spell whose area catches the charmer targets them — the
        // glossary's definition of a target includes a creature forced to make a saving
        // throw. Checked before anything is spent.
        if (spell.IsSpellAttack && target is not null && spell.Damage.Count > 0 && CharmedBy(caster, target))
        {
            return new ActionRefusal(
                "spell.charmed",
                $"{caster.Name} is Charmed by {target.Name} and cannot attack them.");
        }

        if (spell.Save is { } saveShape
            && CharmedHarmRefusal(caster, "spell.charmed", spell.Name, saveShape, point, target) is { } charmed)
        {
            return charmed;
        }

        SpendCastingCost(caster, spell);

        Add(
            CombatStepKind.Spell,
            $"{caster.Name} casts {spell.Name}" +
            (spell.IsCantrip ? " (cantrip)." : $" (level {spell.Level} slot)."),
            caster);

        if (spell.RequiresConcentration)
        {
            StartConcentrating(caster, spell);
        }

        if (spell.Heal is { } heal && target is not null)
        {
            ResolveHeal(caster, spell, heal, target, character);
        }
        else if (spell.IsSpellAttack && target is not null)
        {
            ResolveSpellAttack(caster, spell, target, character);
        }
        else if (spell.Save is { } save)
        {
            ResolveSpellSave(caster, spell, save, point, target, character);
        }

        CheckForCompletion();
        return null;
    }

    /// <summary>Whether the spell has a shape this engine can resolve at all.</summary>
    private static ActionRefusal? ResolveSpellShape(SpellDefinition spell, Combatant? target)
    {
        if (spell.Heal is not null)
        {
            return target is null
                ? new ActionRefusal("spell.needs_target", $"{spell.Name} needs a creature to heal.")
                : null;
        }

        if (spell.IsSpellAttack)
        {
            return target is null
                ? new ActionRefusal("spell.needs_target", $"{spell.Name} needs a creature to attack.")
                : null;
        }

        if (spell.Save is not { } save)
        {
            return new ActionRefusal(
                "spell.not_implemented",
                $"{spell.Name} neither attacks nor forces a save; its effect is not modelled.");
        }

        if (save.Area is { } area && !AreaTargeting.CanResolve(area.Shape))
        {
            return new ActionRefusal(
                "spell.area_not_modelled",
                $"{spell.Name} uses a {area.Shape}, which is not modelled.");
        }

        // Forcing a save is not an effect. Sixty-six of the book's spells make a target
        // roll and then do something this engine cannot express — Hold Person paralyses,
        // Bane subtracts a d4, Sanctuary redirects attacks — and until this check existed
        // every one of them spent its slot, printed a failed save, and did nothing at
        // all. That is bug 1's shape at spell scale: the structured half (the save) made
        // the missing half (the effect) invisible.
        if (spell.Damage.Count == 0
            && save.FailureDamage.Count == 0
            && !spell.AppliedConditions.Any(ConditionRules.CanBeImposed)
            && !save.AppliedConditions.Any(ConditionRules.CanBeImposed))
        {
            return new ActionRefusal(
                "spell.save_effect_not_modelled",
                $"{spell.Name} forces a saving throw, but what a failure does is not modelled.");
        }

        return save.Area is null && target is null
            ? new ActionRefusal("spell.needs_target", $"{spell.Name} needs a creature to target.")
            : null;
    }

    /// <summary>
    /// Restores hit points: "regains a number of Hit Points equal to 2d8 plus your
    /// spellcasting ability modifier".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Healing a creature at 0 hit points brings it back up.</b>
    /// <c>Combatant.RegainHitPoints</c> already clears the dying state, resets Death
    /// Saving Throws and removes Unconscious, so the rule that matters most for a run —
    /// a dropped character can be brought back into the fight — falls out of the
    /// existing model rather than needing a special case here.
    /// </para>
    /// <para>
    /// The dead stay dead. The SRD's healing spells restore hit points, and none of them
    /// says anything about a creature that has died; raising the dead is its own spell
    /// and its own piece of work.
    /// </para>
    /// </remarks>
    private void ResolveHeal(
        Combatant caster,
        SpellDefinition spell,
        SpellHeal heal,
        Combatant target,
        CombatantFeatures character)
    {
        if (target.IsDead)
        {
            Add(
                CombatStepKind.Spell,
                $"{spell.Name} cannot help {target.Name}, who is dead.",
                caster,
                target);
            return;
        }

        var rolled = DiceRoller.Roll(_random, heal.Dice);

        var modifier = heal.AddsSpellcastingModifier && character.SpellcastingAbility is { } ability
            ? caster.Stats.ModifierFor(ability)
            : 0;

        var wasDown = target.CurrentHitPoints == 0;
        var restored = DamageRules.Heal(target, rolled.Total + modifier);

        Add(
            CombatStepKind.Spell,
            $"{target.Name} regains {restored} hit points [{rolled}" +
            (modifier != 0 ? $" {(modifier > 0 ? "+" : "-")} {Math.Abs(modifier)}" : string.Empty) +
            $"] — {DescribeHealth(target)}." +
            (wasDown && target.CurrentHitPoints > 0 ? $" {target.Name} is back on their feet." : string.Empty),
            caster,
            target);
    }

    private static ActionRefusal? CheckCastingCost(Combatant caster, SpellDefinition spell)
    {
        switch (spell.CastingTime)
        {
            case SpellCastingTime.Action when !caster.Turn.HasAction:
                return new ActionRefusal("action.spent", $"{caster.Name} has already used its action.");
            case SpellCastingTime.BonusAction when !caster.Turn.HasBonusAction:
                return new ActionRefusal("bonus_action.spent", $"{caster.Name} has used its Bonus Action.");
            case SpellCastingTime.Reaction when !caster.Turn.HasReaction:
                return new ActionRefusal("reaction.spent", $"{caster.Name} has used its Reaction.");
            case SpellCastingTime.Extended:
                return new ActionRefusal(
                    "spell.too_slow",
                    $"{spell.Name} takes {spell.CastingTimeText} and cannot be cast in a fight.");
            default:
                break;
        }

        // A cantrip costs no slot; everything else needs one of its own level or higher.
        if (spell.IsCantrip)
        {
            return null;
        }

        return HighestAvailableSlot(caster, spell.Level) is null
            ? new ActionRefusal(
                "spell.no_slot",
                $"{caster.Name} has no level {spell.Level} or higher spell slot left.")
            : null;
    }

    private static void SpendCastingCost(Combatant caster, SpellDefinition spell)
    {
        switch (spell.CastingTime)
        {
            case SpellCastingTime.Action:
                caster.Turn.SpendAction();
                break;
            case SpellCastingTime.BonusAction:
                caster.Turn.SpendBonusAction();
                break;
            case SpellCastingTime.Reaction:
                caster.Turn.SpendReaction();
                break;
            default:
                break;
        }

        if (spell.IsCantrip)
        {
            return;
        }

        // Spend the lowest slot that will do. Upcasting is not implemented, so a higher
        // slot would buy nothing and burning it would be strictly worse for the player.
        if (HighestAvailableSlot(caster, spell.Level) is { } level)
        {
            caster.Features.SpellSlotsRemaining[level]--;
        }
    }

    /// <summary>The lowest slot level at or above the spell's own that still has a slot.</summary>
    private static int? HighestAvailableSlot(Combatant caster, int spellLevel)
    {
        for (var level = spellLevel; level <= 9; level++)
        {
            if (caster.Features.SpellSlotsRemaining.GetValueOrDefault(level) > 0)
            {
                return level;
            }
        }

        return null;
    }

    private void StartConcentrating(Combatant caster, SpellDefinition spell)
    {
        if (caster.Features.ConcentratingOn is { } existing)
        {
            Add(
                CombatStepKind.Spell,
                $"{caster.Name}'s Concentration on {existing} ends.",
                caster);
        }

        caster.Features.ConcentratingOn = spell.Name;
    }

    private void ResolveSpellAttack(
        Combatant caster,
        SpellDefinition spell,
        Combatant target,
        CombatantFeatures character)
    {
        // A spell attack uses the caster's own bonus rather than a weapon's, so it is
        // built here rather than read from the spell.
        var attack = new CombatAttack(
            spell.Name,
            spell.RangeFeet is null ? AttackKind.Melee : AttackKind.Ranged,
            character.SpellAttackBonus,
            spell.RangeFeet is null ? Battlefield.FeetPerSquare : null,
            spell.RangeFeet,
            spell.RangeFeet,
            spell.Damage);

        ResolveAttack(caster, attack, target, isOpportunityAttack: false);
    }

    private void ResolveSpellSave(
        Combatant caster,
        SpellDefinition spell,
        SaveEffect save,
        GridPosition point,
        Combatant? target,
        CombatantFeatures character)
    {
        var difficultyClass = save.DifficultyClass ?? character.SpellSaveDifficultyClass;

        // Spells pass no riders yet: executing the conditions a spell's save imposes is
        // its own piece of work, and landing it as a side effect of sharing this loop
        // would be a rules change nothing decided.
        ResolveSaveEffect(caster, spell.Name, save, difficultyClass, point, target, CombatStepKind.Spell, []);
    }

    private IReadOnlyList<Combatant> CreaturesIn(IReadOnlyList<GridPosition> squares)
    {
        var covered = squares.ToHashSet();

        return _combatants.Where(combatant => combatant.IsActive && covered.Contains(combatant.Position)).ToArray();
    }

    /// <summary>
    /// A creature that takes damage while concentrating must make a Constitution saving
    /// throw against DC 10 or half the damage, whichever is higher, or lose the spell.
    /// </summary>
    internal void CheckConcentration(Combatant combatant, int damageTaken)
    {
        if (combatant.Features.ConcentratingOn is not { } spellName || damageTaken <= 0)
        {
            return;
        }

        if (!combatant.CanAct)
        {
            combatant.Features.ConcentratingOn = null;
            Add(CombatStepKind.Spell, $"{combatant.Name} loses Concentration on {spellName}.", combatant);
            return;
        }

        var difficultyClass = SpellcastingRules.ConcentrationDifficultyClass(damageTaken);
        var roll = D20Test.Roll(_random, combatant.Stats.SaveBonusFor(Ability.Constitution));

        if (roll.Total >= difficultyClass)
        {
            Add(
                CombatStepKind.Spell,
                $"{combatant.Name} holds Concentration on {spellName}: {roll} vs DC {difficultyClass}.",
                combatant);
            return;
        }

        combatant.Features.ConcentratingOn = null;

        Add(
            CombatStepKind.Spell,
            $"{combatant.Name} loses Concentration on {spellName}: {roll} vs DC {difficultyClass}.",
            combatant);
    }
}
