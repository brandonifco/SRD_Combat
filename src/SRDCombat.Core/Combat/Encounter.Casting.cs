using SRDCombat.Core.Characters;
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
/// Deliberately not implemented: spells whose effect is neither damage, healing nor a
/// condition, and Cylinder areas. Each is refused with a named code so a client can say
/// why. (This paragraph once listed upcasting too, and stayed stale for the whole slice
/// that implemented it: scaling has applied at casting time since the upcasting slice —
/// automatically when the small slots run dry, and by choice through
/// <c>CastSpell</c>'s <c>slotLevel</c> since the Phase 7 polish.)
/// </para>
/// </remarks>
public sealed partial class Encounter
{
    /// <summary>Casts a spell at a creature.</summary>
    /// <param name="spellId">The spell to cast.</param>
    /// <param name="target">The creature it is aimed at.</param>
    /// <param name="slotLevel">
    /// The slot level to spend, for a deliberate upcast. Null — the default, and the
    /// only thing the policy ever passes — means the lowest slot that will do.
    /// </param>
    public ActionRefusal? CastSpell(string spellId, Combatant target, int? slotLevel = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        return CastSpell(spellId, target.Position, target, slotLevel);
    }

    /// <summary>Casts a spell at a point, for an area effect.</summary>
    public ActionRefusal? CastSpell(
        string spellId,
        GridPosition point,
        Combatant? target = null,
        int? slotLevel = null)
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

        if (CheckCastingCost(caster, spell, slotLevel) is { } refusal)
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

        // A point-aimed cast answers to the same printed range as a targeted one. This
        // check went unwritten for as long as only the policy cast spells — it always
        // named a creature — and a client aiming at bare ground must not slip past the
        // rule the creature-aimed path enforces. Gated on the spell having an area:
        // anything else aimed at a point is missing its creature, and the shape
        // resolution's needs-target refusal is the answer that teaches that rule.
        if (target is null && !spell.IsSelfRanged && spell.Save?.Area is not null
            && spell.TargetRangeFeet is { } pointRange
            && caster.Position.DistanceFeetTo(point) > pointRange)
        {
            return new ActionRefusal(
                "spell.out_of_range",
                $"That square is beyond {spell.Name}'s {pointRange} ft. range.");
        }

        // "Choose a Humanoid that you can see within range": the printed target-type
        // gate, refused before anything is spent. A point-aimed cast has no creature
        // to check; the spells that print the gate all name a single target.
        if (spell.TargetCreatureType is { } requiredType
            && target is not null
            && target.Stats.Type != requiredType)
        {
            return new ActionRefusal(
                "spell.wrong_target_type",
                $"{spell.Name} targets a {requiredType}; {target.Name} is not one.");
        }

        var resolution = ResolveSpellShape(spell, target);

        if (resolution is not null)
        {
            return resolution;
        }

        // "Can't be targeted directly." Applies wherever the spell aims at the creature
        // itself — an attack, a single-target save, a heal. A spell aimed at a point is
        // not targeting anyone: its area decides who is caught, and the Total Cover
        // exclusion inside AreaTargeting is the rule that decides it.
        if (target is not null
            && !spell.IsSelfRanged
            && (spell.IsSpellAttack || spell.Heal is not null || spell.Revival is not null
                || spell.Save is { Area: null })
            && CoverRules.Between(Battlefield, caster.Position, target.Position, _combatants) == CoverDegree.Total)
        {
            return new ActionRefusal(
                "spell.total_cover",
                $"{target.Name} has Total Cover from {caster.Name} and can't be targeted directly.");
        }

        // Revivify's own gates, all before anything is spent. "Has died within the last
        // minute": a minute is ten rounds — the same reading ConditionDuration.ForMinutes
        // states — measured against the round the encounter stamped on the death; a
        // death this fight never saw reads as too long ago, because refusing a revival
        // the rules might allow is recoverable and reviving one they forbid is not. The
        // room check is the occupancy lesson pointed forward: the dead do not occupy
        // their square, so somebody may be standing on the corpse, and reviving into an
        // occupied square is the two-creatures-one-square state this engine only
        // grudgingly survives.
        if (spell.Revival is not null && target is not null)
        {
            if (!target.IsDead)
            {
                return new ActionRefusal(
                    "spell.target_not_dead",
                    $"{target.Name} is not dead; {spell.Name} revives the dead.");
            }

            if (target.DiedInRound is not { } died || Round - died > RevivalWindowRounds)
            {
                return new ActionRefusal(
                    "spell.dead_too_long",
                    $"{target.Name} has been dead longer than the minute {spell.Name} allows.");
            }

            if (_combatants.Any(other => other != target && !other.IsDead && other.Position == target.Position))
            {
                return new ActionRefusal(
                    "spell.no_room_to_stand",
                    $"Someone is standing where {target.Name} fell.");
            }
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

        var slotUsed = SpendCastingCost(caster, spell, slotLevel);

        // The narration names the slot actually spent, which is not always the spell's
        // own level: when the level 1 slots are dry, Cure Wounds burns a level 2 slot —
        // and as of upcasting, gets something for it.
        Add(
            CombatStepKind.SpellCast,
            $"{caster.Name} casts {spell.Name}" +
            (spell.IsCantrip ? " (cantrip)." : $" (level {slotUsed} slot)."),
            caster);

        if (spell.RequiresConcentration)
        {
            StartConcentrating(caster, spell);
        }

        // Scaling grows the definition itself, so the resolvers never know it happened:
        // an upcast Cure Wounds simply arrives with more dice.
        var scaled = ApplyScaling(spell, slotUsed, character.Level);

        if (scaled.Revival is { } revival && target is not null)
        {
            target.ReturnToLife(revival.HitPoints);

            Add(
                CombatStepKind.Spell,
                $"{target.Name} revives with {revival.HitPoints} hit point" +
                $"{(revival.HitPoints == 1 ? string.Empty : "s")} — back from the dead.",
                caster,
                target);
        }
        else if (scaled.Heal is { } heal && target is not null)
        {
            ResolveHeal(caster, scaled, heal, target, character, slotUsed ?? scaled.Level);
        }
        else if (scaled.IsSpellAttack && target is not null)
        {
            ResolveSpellAttack(caster, scaled, target, character);
        }
        else if (scaled.Save is { } save)
        {
            ResolveSpellSave(caster, scaled, save, point, target, character);
        }

        CheckForCompletion();
        return null;
    }

    /// <summary>How long after death Revivify still works: "within the last minute", ten rounds.</summary>
    private const int RevivalWindowRounds = 10;

    /// <summary>Whether the spell has a shape this engine can resolve at all.</summary>
    private static ActionRefusal? ResolveSpellShape(SpellDefinition spell, Combatant? target)
    {
        if (spell.Heal is not null || spell.Revival is not null)
        {
            return target is null
                ? new ActionRefusal("spell.needs_target", $"{spell.Name} needs a creature to target.")
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
    /// The dead stay dead <em>here</em>. The SRD's healing spells restore hit points,
    /// and none of them says anything about a creature that has died; raising the dead
    /// was its own piece of work, and became one — <see cref="SpellRevival"/> and the
    /// revival branch above this method are Revivify's (#119).
    /// </para>
    /// </remarks>
    private void ResolveHeal(
        Combatant caster,
        SpellDefinition spell,
        SpellHeal heal,
        Combatant target,
        CombatantFeatures character,
        int slotLevel)
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

        // Disciple of Life: "When a spell you cast with a spell slot restores Hit
        // Points ... additional Hit Points equal 2 plus the spell slot's level." The
        // slot's, not the spell's — an upcast Cure Wounds feeds it too. A cantrip is
        // cast without a slot and gets nothing.
        if (!spell.IsCantrip && caster.Stats.Has(ClassFeature.DiscipleOfLife))
        {
            modifier += 2 + slotLevel;
        }

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

    private static ActionRefusal? CheckCastingCost(Combatant caster, SpellDefinition spell, int? slotLevel)
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
            return slotLevel is null
                ? null
                : new ActionRefusal(
                    "spell.cantrip_needs_no_slot",
                    $"{spell.Name} is a cantrip and is cast without a slot.");
        }

        // A chosen slot answers to the printed rule before anything is spent: "you must
        // use a spell slot of the spell's level or higher".
        if (slotLevel is { } chosen)
        {
            if (chosen < spell.Level)
            {
                return new ActionRefusal(
                    "spell.slot_below_spell",
                    $"{spell.Name} is level {spell.Level}; a level {chosen} slot cannot hold it.");
            }

            return caster.Features.SpellSlotsRemaining.GetValueOrDefault(chosen) > 0
                ? null
                : new ActionRefusal(
                    "spell.no_slot",
                    $"{caster.Name} has no level {chosen} spell slot left.");
        }

        return HighestAvailableSlot(caster, spell.Level) is null
            ? new ActionRefusal(
                "spell.no_slot",
                $"{caster.Name} has no level {spell.Level} or higher spell slot left.")
            : null;
    }

    /// <summary>
    /// Grows a spell's dice for the slot it was actually cast from, and for a cantrip's
    /// level steps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Using a Higher-Level Spell Slot. The healing increases by 2d8 for each spell
    /// slot level above 1." The extractor structures that sentence only when the extra
    /// die matches the base effect's, so growing the expression's count is exactly the
    /// printed arithmetic. A cantrip instead steps on the <em>caster's</em> level — 5,
    /// 11 and 17, the thresholds every printed Cantrip Upgrade uses.
    /// </para>
    /// <para>
    /// The definition is grown rather than the rolls patched, so every resolver —
    /// attack, save, heal — sees an ordinary spell that happens to carry more dice, and
    /// a Critical Hit doubles the upcast dice with the rest, as it should.
    /// </para>
    /// </remarks>
    private static SpellDefinition ApplyScaling(SpellDefinition spell, int? slotUsed, int casterLevel)
    {
        if (spell.IsCantrip && spell.CantripUpgradeDice is { } perStep)
        {
            var steps = casterLevel >= 17 ? 3 : casterLevel >= 11 ? 2 : casterLevel >= 5 ? 1 : 0;

            return steps > 0 ? Grown(spell, perStep.Count * steps) : spell;
        }

        if (spell.UpcastDicePerSlotLevel is { } perLevel && slotUsed is { } slot && slot > spell.Level)
        {
            return Grown(spell, perLevel.Count * (slot - spell.Level));
        }

        return spell;
    }

    /// <summary>The spell with its base effect's expression grown by extra dice of its own kind.</summary>
    /// <remarks>
    /// A save spell carries its damage in <em>both</em> <c>Damage</c> and
    /// <c>Save.FailureDamage</c>, and the save resolver reads the second — so every
    /// place the dice appear is grown, not just the first one found. Growing only
    /// <c>Damage</c> was this method's first bug, caught by a cantrip that refused to
    /// get bigger.
    /// </remarks>
    private static SpellDefinition Grown(SpellDefinition spell, int extraDice)
    {
        if (spell.Heal is { } heal)
        {
            spell = spell with { Heal = heal with { Dice = heal.Dice with { Count = heal.Dice.Count + extraDice } } };
        }

        if (spell.Damage.Count > 0)
        {
            var first = spell.Damage[0];
            var grown = first.Amount with { Count = first.Amount.Count + extraDice };

            spell = spell with
            {
                Damage = [first with { Amount = grown, PrintedAverage = grown.Average }, .. spell.Damage.Skip(1)],
            };
        }

        if (spell.Save is { } save && save.FailureDamage.Count > 0)
        {
            var first = save.FailureDamage[0];
            var grown = first.Amount with { Count = first.Amount.Count + extraDice };

            spell = spell with
            {
                Save = save with
                {
                    FailureDamage = [first with { Amount = grown, PrintedAverage = grown.Average }, .. save.FailureDamage.Skip(1)],
                },
            };
        }

        return spell;
    }

    private static int? SpendCastingCost(Combatant caster, SpellDefinition spell, int? slotLevel)
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
            return null;
        }

        // A chosen slot is spent exactly as chosen; CheckCastingCost already ruled it
        // legal, and the deliberate upcast buys the same printed dice the automatic
        // one does.
        if (slotLevel is { } picked)
        {
            caster.Features.SpellSlotsRemaining[picked]--;
            return picked;
        }

        // Spend the lowest slot that will do. A higher one is only burned when nothing
        // smaller is left — and since upcasting, it buys the printed extra dice.
        if (HighestAvailableSlot(caster, spell.Level) is { } level)
        {
            caster.Features.SpellSlotsRemaining[level]--;
            return level;
        }

        return null;
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
            SweepConcentrationConditions(caster);
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
        // built here rather than read from the spell. Its riders come across under the
        // same filter a monster's attack riders pass on the way to a CombatAttack —
        // only conditions the engine can impose to the letter — and ImposeRiders then
        // treats a spell's hit exactly like a bite's.
        var attack = new CombatAttack(
            spell.Name,
            spell.RangeFeet is null ? AttackKind.Melee : AttackKind.Ranged,
            character.SpellAttackBonus,
            spell.RangeFeet is null ? Battlefield.FeetPerSquare : null,
            spell.RangeFeet,
            spell.RangeFeet,
            spell.Damage,
            spell.AppliedConditions.Where(ConditionRules.CanBeImposed).ToArray())
        {
            GrantsAdvantageAgainstTargetOnHit = spell.GrantsAdvantageAgainstTargetOnHit,
        };

        ResolveAttack(caster, attack, target, isOpportunityAttack: false, isSpellAttack: true);
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

        // A spell's save imposes every rider the engine can execute, the same filter
        // an attack's riders pass — the "own piece of work" the earlier version of
        // this comment deferred. Hold Person's Paralyzed is the first customer: its
        // duration carries the Concentration tie and the repeated save, and the loop
        // below records both on the imposed condition.
        ResolveSaveEffect(
            caster,
            spell.Name,
            save,
            difficultyClass,
            point,
            target,
            CombatStepKind.Spell,
            save.AppliedConditions.Where(ConditionRules.CanBeImposed).ToArray());
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

        // The blow itself may have downed or killed the target — glossary p.186's
        // "Incapacitated or Dead" clause ends Concentration outright, with no save to
        // roll. This is the damage-caused half of that clause; the half that lands
        // with no damage at all (a save-imposed Paralyzed, an escalating gaze reaching
        // Petrified) goes through <see cref="BreakConcentrationOnIncapacitated"/>
        // instead, since nothing here ever runs for it.
        if (!combatant.CanAct)
        {
            EndConcentration(combatant, spellName);
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
        SweepConcentrationConditions(combatant);
    }

    /// <summary>
    /// Glossary p.186, Concentration: "Incapacitated or Dead. Your Concentration ends
    /// if you have the Incapacitated condition or you die." Unlike the damage-taken
    /// check above, this is not a saving throw — Incapacitated ends Concentration
    /// outright, the instant the condition lands, whichever printed condition brought
    /// it (Paralyzed, Stunned, Unconscious, Petrified, or Incapacitated imposed in its
    /// own right).
    /// </summary>
    /// <remarks>
    /// Called from every place a condition can land on a creature outside the damage
    /// path already covered by <see cref="CheckConcentration"/>: a save effect's or an
    /// attack's rider (<see cref="ImposeConditions"/>) and a repeat-save escalation
    /// (<see cref="Escalate"/>). Death and damage-caused Unconsciousness are covered by
    /// <see cref="CheckConcentration"/> instead — but only where its call is present, so
    /// every site that can drop a creature's hit points must call it too, not just the
    /// two the Multiattack and save-effect loops already did. Missed once already:
    /// Weapon Mastery's Graze and Cleave both deal their own damage and did not, so a
    /// concentrating creature Grazed or Cleaved to 0 kept concentrating until this was
    /// caught in review. This method is a no-op when the creature is not concentrating
    /// or can still act, so it is safe to call unconditionally after any condition
    /// lands.
    /// </remarks>
    internal void BreakConcentrationOnIncapacitated(Combatant combatant)
    {
        if (combatant.Features.ConcentratingOn is { } spellName && !combatant.CanAct)
        {
            EndConcentration(combatant, spellName);
        }
    }

    private void EndConcentration(Combatant combatant, string spellName)
    {
        combatant.Features.ConcentratingOn = null;
        Add(CombatStepKind.Spell, $"{combatant.Name} loses Concentration on {spellName}.", combatant);
        SweepConcentrationConditions(combatant);
    }
}
