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

        if (target is not null && spell.RangeFeet is { } range
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

        if (spell.IsSpellAttack && target is not null)
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

        return save.Area is null && target is null
            ? new ActionRefusal("spell.needs_target", $"{spell.Name} needs a creature to target.")
            : null;
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

        var affected = save.Area is { } area
            ? CreaturesIn(AreaTargeting.Cover(area, caster.Position, point, Battlefield))
            : target is null ? [] : [target];

        if (save.Area is { } shape)
        {
            Add(
                CombatStepKind.Spell,
                $"{spell.Name} fills a {shape.SizeFeet}-foot {shape.Shape}, catching " +
                $"{affected.Count} creature(s).",
                caster);
        }

        foreach (var victim in affected)
        {
            var roll = D20Test.Roll(_random, victim.Stats.SaveBonusFor(save.Ability));
            var succeeded = roll.Total >= difficultyClass;

            Add(
                CombatStepKind.Spell,
                $"{victim.Name} makes a {save.Ability} saving throw: {roll} vs DC {difficultyClass} — " +
                (succeeded ? "success." : "failure."),
                caster,
                victim);

            if (succeeded && save.SuccessOutcome == SaveSuccessOutcome.NoEffect)
            {
                continue;
            }

            foreach (var component in save.FailureDamage)
            {
                var rolled = DiceRoller.Roll(_random, component.Amount);

                // A successful save against a damaging spell halves it.
                var halvings = succeeded && save.SuccessOutcome == SaveSuccessOutcome.HalfDamage ? 1 : 0;
                var rageHalving = RageResists(victim, component.Type) ? 1 : 0;

                var applied = DamageRules.Apply(
                    victim,
                    rolled.Total,
                    component.Type,
                    fromCriticalHit: false,
                    halvings + rageHalving);

                Add(
                    CombatStepKind.Damage,
                    $"{victim.Name} takes {applied.Effective} {component.Type} damage" +
                    (halvings > 0 ? " (halved by a successful save)" : string.Empty) +
                    $" [{rolled}] — {(victim.IsDead ? "dead" : $"{victim.CurrentHitPoints}/{victim.Stats.MaximumHitPoints} hit points")}.",
                    caster,
                    victim);

                CheckConcentration(victim, applied.Effective);

                if (applied.Died)
                {
                    Add(CombatStepKind.Died, $"{victim.Name} is dead.", victim);
                    break;
                }

                if (applied.Downed)
                {
                    Add(CombatStepKind.Downed, $"{victim.Name} drops to 0 hit points and falls Unconscious.", victim);
                    break;
                }
            }
        }
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
