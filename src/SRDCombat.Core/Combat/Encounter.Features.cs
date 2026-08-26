using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>What a Rogue may do with Cunning Action.</summary>
public enum CunningActionKind
{
    Dash,
    Disengage,
}

/// <summary>
/// The class feature actions a character can take on its turn.
/// </summary>
/// <remarks>
/// Kept in their own file because they are a distinct concern from the universal
/// actions every combatant has — and because the list will grow as more features become
/// implemented, while Move/Attack/Dodge will not.
/// </remarks>
public sealed partial class Encounter
{
    /// <summary>
    /// Barbarian Rage: a Bonus Action granting resistance to physical damage and bonus
    /// damage on Strength melee attacks.
    /// </summary>
    public ActionRefusal? Rage()
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.Stats.Has(ClassFeature.Rage))
        {
            return new ActionRefusal("feature.absent", $"{combatant.Name} does not have Rage.");
        }

        if (!combatant.Turn.HasBonusAction)
        {
            return new ActionRefusal("bonus_action.spent", $"{combatant.Name} has used its Bonus Action.");
        }

        // "Take a Bonus Action to extend your Rage" — the third printed extension, and
        // the only one available to a Barbarian with nothing in reach. It spends the
        // Bonus Action and no Rage use, so raging again is never the waste the old
        // already_raging refusal implied.
        if (combatant.Features.IsRaging)
        {
            combatant.Turn.SpendBonusAction();
            combatant.Features.SustainedRageThisTurn = true;

            Add(
                CombatStepKind.Feature,
                $"{combatant.Name} takes a Bonus Action to extend the Rage.",
                combatant);

            return null;
        }

        if (combatant.Features.RagesRemaining <= 0)
        {
            return new ActionRefusal("feature.rage.exhausted", $"{combatant.Name} has no Rages left.");
        }

        combatant.Turn.SpendBonusAction();
        combatant.Features.RagesRemaining--;
        combatant.Features.IsRaging = true;
        combatant.Features.RageBeganOnTurn = combatant.TurnsBegun;

        Add(
            CombatStepKind.Feature,
            $"{combatant.Name} flies into a Rage! Resistance to Bludgeoning, Piercing and Slashing damage, " +
            $"and +{combatant.Stats.Character!.RageDamageBonus} damage on Strength melee attacks " +
            $"({combatant.Features.RagesRemaining} Rage(s) left).",
            combatant);

        return null;
    }

    /// <summary>Fighter Second Wind: a Bonus Action to regain 1d10 + level hit points.</summary>
    public ActionRefusal? SecondWind()
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.Stats.Has(ClassFeature.SecondWind))
        {
            return new ActionRefusal("feature.absent", $"{combatant.Name} does not have Second Wind.");
        }

        if (combatant.Features.SecondWindRemaining <= 0)
        {
            return new ActionRefusal("feature.second_wind.exhausted", $"{combatant.Name} has no uses left.");
        }

        if (!combatant.Turn.HasBonusAction)
        {
            return new ActionRefusal("bonus_action.spent", $"{combatant.Name} has used its Bonus Action.");
        }

        combatant.Turn.SpendBonusAction();
        combatant.Features.SecondWindRemaining--;

        var roll = DiceRoller.Roll(_random, new DiceExpression(1, 10, combatant.Stats.Character!.Level));
        var healed = DamageRules.Heal(combatant, roll.Total);

        Add(
            CombatStepKind.Feature,
            $"{combatant.Name} uses Second Wind: {roll} — regains {healed} hit points " +
            $"({combatant.CurrentHitPoints}/{combatant.Stats.MaximumHitPoints}).",
            combatant);

        return null;
    }

    /// <summary>
    /// Rogue Steady Aim: a Bonus Action for Advantage on the next attack roll this
    /// turn, at the cost of all movement.
    /// </summary>
    /// <remarks>
    /// "You can use this bonus action only if you haven't moved during this turn, and
    /// after you use [it], your Speed is 0 until the end of the current turn." Spending
    /// movement is what the engine reads as having moved — standing up included — and
    /// forfeited movement stays 0 through a later Dash.
    /// </remarks>
    public ActionRefusal? SteadyAim()
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.Stats.Has(ClassFeature.SteadyAim))
        {
            return new ActionRefusal("feature.absent", $"{combatant.Name} does not have Steady Aim.");
        }

        if (combatant.Turn.HasMoved)
        {
            return new ActionRefusal(
                "feature.steady_aim.moved",
                $"{combatant.Name} has already moved this turn.");
        }

        if (!combatant.Turn.HasBonusAction)
        {
            return new ActionRefusal("bonus_action.spent", $"{combatant.Name} has used its Bonus Action.");
        }

        combatant.Turn.SpendBonusAction();
        combatant.Turn.ForfeitMovement();
        combatant.Features.SteadyAimedThisTurn = true;

        Add(
            CombatStepKind.Feature,
            $"{combatant.Name} takes Steady Aim: Advantage on its next attack, and its Speed is 0 this turn.",
            combatant);

        return null;
    }

    /// <summary>Fighter Action Surge: one extra action on this turn.</summary>
    public ActionRefusal? ActionSurge()
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.Stats.Has(ClassFeature.ActionSurge))
        {
            return new ActionRefusal("feature.absent", $"{combatant.Name} does not have Action Surge.");
        }

        if (combatant.Features.ActionSurgeRemaining <= 0)
        {
            return new ActionRefusal("feature.action_surge.exhausted", $"{combatant.Name} has no uses left.");
        }

        if (combatant.Turn.HasAction)
        {
            return new ActionRefusal(
                "feature.action_surge.action_available",
                $"{combatant.Name} still has an action; Action Surge would be wasted.");
        }

        combatant.Features.ActionSurgeRemaining--;
        combatant.Turn.RestoreAction();

        Add(CombatStepKind.Feature, $"{combatant.Name} uses Action Surge and can act again.", combatant);
        return null;
    }

    /// <summary>
    /// Barbarian Reckless Attack: Advantage on Strength attacks this turn, at the cost
    /// of Advantage to everyone attacking you until your next turn.
    /// </summary>
    public ActionRefusal? RecklessAttack()
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.Stats.Has(ClassFeature.RecklessAttack))
        {
            return new ActionRefusal("feature.absent", $"{combatant.Name} does not have Reckless Attack.");
        }

        if (combatant.Features.IsRecklessThisTurn)
        {
            return new ActionRefusal("feature.reckless.already", $"{combatant.Name} is already attacking recklessly.");
        }

        combatant.Features.IsRecklessThisTurn = true;

        Add(
            CombatStepKind.Feature,
            $"{combatant.Name} attacks recklessly — Advantage on Strength attacks, and Advantage to attackers.",
            combatant);

        return null;
    }

    /// <summary>
    /// Rogue Cunning Strike: declares an effect to add to this turn's Sneak Attack,
    /// paid for with Sneak Attack dice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declared rather than chosen at the moment of the hit, because the SRD pays for it
    /// in dice removed <em>before</em> rolling — "You remove the die before rolling" — so
    /// the choice has to exist by the time damage is rolled. It costs nothing to declare
    /// and is only spent if a Sneak Attack actually lands, which matches "when you deal
    /// Sneak Attack damage, you can add one of the following".
    /// </para>
    /// <para>
    /// Only Trip is executed; <see cref="CunningStrikeEffect"/> records why each of the
    /// others is not. Declaring it costs no action of any kind — it is a rider on the
    /// attack, not an action.
    /// </para>
    /// </remarks>
    public ActionRefusal? CunningStrike(CunningStrikeEffect effect)
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.Stats.Has(ClassFeature.CunningStrike))
        {
            return new ActionRefusal("feature.absent", $"{combatant.Name} does not have Cunning Strike.");
        }

        if (effect == CunningStrikeEffect.None)
        {
            combatant.Features.CunningStrike = CunningStrikeEffect.None;
            return null;
        }

        if (combatant.Features.SneakAttackUsedThisTurn)
        {
            return new ActionRefusal(
                "feature.cunning_strike.sneak_attack_spent",
                $"{combatant.Name} has already dealt Sneak Attack damage this turn.");
        }

        // One die is forgone, so there has to be more than one to forgo it from — a
        // level 5 Rogue's 3d6 can spend one and still deal 2d6.
        if (SneakAttackDice(combatant) <= CunningStrikeCostDice)
        {
            return new ActionRefusal(
                "feature.cunning_strike.too_few_dice",
                $"{combatant.Name} has too few Sneak Attack dice to pay for {effect}.");
        }

        combatant.Features.CunningStrike = effect;

        Add(
            CombatStepKind.Feature,
            $"{combatant.Name} prepares a Cunning Strike ({effect}), forgoing " +
            $"{CunningStrikeCostDice}d6 of Sneak Attack damage.",
            combatant);

        return null;
    }

    /// <summary>Every printed Cunning Strike effect this engine executes costs one die.</summary>
    private const int CunningStrikeCostDice = 1;

    /// <summary>How many Sneak Attack dice this combatant rolls.</summary>
    private static int SneakAttackDice(Combatant combatant) =>
        combatant.Stats.Character?.SneakAttackDamage?.Count ?? 0;

    /// <summary>
    /// The Sneak Attack damage actually rolled, with any declared Cunning Strike's cost
    /// already removed.
    /// </summary>
    /// <remarks>
    /// "You remove the die before rolling" — the cost comes off the dice, not off the
    /// total, so a spent die never contributes and never gets doubled by a Critical Hit.
    /// </remarks>
    private static DiceExpression SneakAttackDamageAfterCunningStrike(Combatant attacker)
    {
        var damage = attacker.Stats.Character!.SneakAttackDamage!;

        return attacker.Features.CunningStrike == CunningStrikeEffect.None
            ? damage
            : damage with { Count = Math.Max(1, damage.Count - CunningStrikeCostDice) };
    }

    /// <summary>
    /// Resolves a declared Cunning Strike effect, immediately after the attack's damage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "If a Cunning Strike effect requires a saving throw, the DC equals 8 plus your
    /// Dexterity modifier and Proficiency Bonus." Trip is gated on size — "<b>If the
    /// target is Large or smaller</b>, it must succeed on a Dexterity saving throw" —
    /// which is the same gate a monster's printed rider uses, so the rider goes through
    /// the same <c>AppliedCondition</c> machinery rather than a second path that could
    /// disagree with it.
    /// </para>
    /// <para>
    /// The gate is read <em>before</em> the save, because the sentence puts it there: a
    /// Huge target does not roll and fail, it is never asked. Rolling first and filtering
    /// after would give the same visible outcome and consume a die that the rules never
    /// call for — which the scripted-dice tests treat as the bug it is.
    /// </para>
    /// </remarks>
    private void ResolveCunningStrike(Combatant attacker, Combatant target)
    {
        if (attacker.Features.CunningStrike != CunningStrikeEffect.Trip)
        {
            return;
        }

        attacker.Features.CunningStrike = CunningStrikeEffect.None;

        var rider = new AppliedCondition(ConditionType.Prone, MaximumTargetSize: CreatureSize.Large);

        if (!target.IsActive || !rider.AllowsTargetSize(target.Stats.Size))
        {
            return;
        }

        var difficultyClass = 8
            + attacker.Stats.ModifierFor(Ability.Dexterity)
            + attacker.Stats.ProficiencyBonus;

        var roll = D20Test.Roll(_random, target.Stats.SaveBonusFor(Ability.Dexterity));
        var succeeded = roll.Total >= difficultyClass;

        Add(
            CombatStepKind.Feature,
            $"{target.Name} makes a Dexterity saving throw against {attacker.Name}'s Trip: " +
            $"{roll} vs DC {difficultyClass} — {(succeeded ? "stays up." : "goes down.")}",
            attacker,
            target);

        if (!succeeded)
        {
            ImposeConditions(attacker, [rider], target, grappleRangeFeet: null);
        }
    }

    /// <summary>How far Divine Spark reaches: "another creature you can see within 30 feet".</summary>
    private const int DivineSparkRangeFeet = 30;

    /// <summary>
    /// Cleric Channel Divinity — Divine Spark: a Magic action pointing the Holy Symbol
    /// at another creature within 30 feet, rolling 1d8 + Wisdom, and either restoring
    /// that many hit points or forcing a Constitution saving throw for that much
    /// Necrotic or Radiant damage, half (round down) on a success.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The readings, in printed order: a Magic action spends the Action; "another
    /// creature" refuses the Cleric itself; "that you can see" is the Total Cover
    /// refusal, sight being unmodelled — the same reading every targeted spell makes;
    /// the DC "equals the spell save DC from this class's Spellcasting feature", which
    /// a resolved Cleric already carries, and a Channel Divinity bearer without
    /// resolved spellcasting falls back to the same Wisdom-based arithmetic that DC is
    /// made of. The dice step at Cleric levels 7, 13 and 18 is written from the printed
    /// thresholds even though this game ends at level 5, so the arithmetic cannot
    /// quietly stay wrong if the cap ever moves.
    /// </para>
    /// <para>
    /// Two deliberate boundaries. Divine Spark "channel[s] divine energy ... to fuel
    /// magical effects", so Magic Resistance applies although it is not a spell — the
    /// one non-spell path that passes <c>magicalEffect: true</c> into the save loop.
    /// And Disciple of Life does not feed it: that feature reads "a spell you cast with
    /// a spell slot", and this is neither. Every refusal fires before the use is spent,
    /// the potion precedent — a use burned pointing at a corpse cannot be given back.
    /// </para>
    /// </remarks>
    public ActionRefusal? DivineSpark(
        Combatant target,
        DivineSparkUse use,
        DamageType damageType = DamageType.Radiant)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.Stats.Has(ClassFeature.ChannelDivinity))
        {
            return new ActionRefusal("feature.absent", $"{combatant.Name} does not have Channel Divinity.");
        }

        if (combatant.Features.ChannelDivinityRemaining <= 0)
        {
            return new ActionRefusal(
                "feature.channel_divinity.exhausted",
                $"{combatant.Name} has no Channel Divinity uses left.");
        }

        if (!combatant.Turn.HasAction)
        {
            return new ActionRefusal("action.spent", $"{combatant.Name} has already used its action.");
        }

        if (ReferenceEquals(target, combatant))
        {
            return new ActionRefusal(
                "feature.divine_spark.self",
                "Divine Spark points at another creature, not the Cleric itself.");
        }

        if (target.IsDead)
        {
            return new ActionRefusal(
                "feature.divine_spark.dead",
                $"{target.Name} is dead; Divine Spark restores the living or harms them.");
        }

        if (combatant.DistanceFeetTo(target) > DivineSparkRangeFeet)
        {
            return new ActionRefusal(
                "feature.divine_spark.out_of_range",
                $"{target.Name} is beyond Divine Spark's {DivineSparkRangeFeet} ft. range.");
        }

        if (CoverRules.AgainstSpace(Battlefield, combatant.Space, target.Space, _combatants) == CoverDegree.Total)
        {
            return new ActionRefusal(
                "feature.total_cover",
                $"{target.Name} has Total Cover from {combatant.Name} and can't be targeted.");
        }

        if (use == DivineSparkUse.Harm
            && damageType is not (DamageType.Necrotic or DamageType.Radiant))
        {
            return new ActionRefusal(
                "feature.divine_spark.damage_type",
                $"Divine Spark deals Necrotic or Radiant damage, not {damageType}.");
        }

        // "Can't Harm the Charmer": a damaging magical effect aimed at the charmer,
        // refused before anything is spent, exactly as a damaging spell is.
        if (use == DivineSparkUse.Harm && CharmedBy(combatant, target))
        {
            return new ActionRefusal(
                "feature.charmed",
                $"{combatant.Name} is Charmed by {target.Name} and cannot harm them.");
        }

        combatant.Turn.SpendAction();
        combatant.Features.ChannelDivinityRemaining--;

        Add(
            CombatStepKind.Feature,
            $"{combatant.Name} uses Channel Divinity — Divine Spark " +
            $"({combatant.Features.ChannelDivinityRemaining} use(s) left).",
            combatant);

        var level = combatant.Stats.Character?.Level ?? 1;
        var diceCount = level >= 18 ? 4 : level >= 13 ? 3 : level >= 7 ? 2 : 1;
        var wisdom = combatant.Stats.ModifierFor(Ability.Wisdom);
        var dice = new DiceExpression(diceCount, 8, wisdom);

        if (use == DivineSparkUse.Heal)
        {
            var rolled = DiceRoller.Roll(_random, dice);
            var wasDown = target.CurrentHitPoints == 0;
            var restored = DamageRules.Heal(target, rolled.Total);

            Add(
                CombatStepKind.Feature,
                $"{target.Name} regains {restored} hit points [{rolled}] — {DescribeHealth(target)}." +
                (wasDown && target.CurrentHitPoints > 0
                    ? $" {target.Name} is back on their feet."
                    : string.Empty),
                combatant,
                target);

            return null;
        }

        var difficultyClass = combatant.Stats.Character is { SpellSaveDifficultyClass: > 0 } caster
            ? caster.SpellSaveDifficultyClass
            : SpellcastingRules.SaveDifficultyClass(combatant.Stats.ProficiencyBonus, wisdom);

        ResolveSaveEffect(
            combatant,
            "Divine Spark",
            new SaveEffect(
                Ability.Constitution,
                difficultyClass,
                Area: null,
                [new AttackDamage(dice, damageType, dice.Average)],
                SaveSuccessOutcome.HalfDamage,
                AppliedConditions: []),
            difficultyClass,
            target.Position,
            target,
            CombatStepKind.Feature,
            riders: [],
            magicalEffect: true);

        CheckForCompletion();
        return null;
    }

    /// <summary>Rogue Cunning Action: Dash or Disengage as a Bonus Action.</summary>
    public ActionRefusal? CunningAction(CunningActionKind kind)
    {
        if (ActiveCombatant is not { } combatant)
        {
            return new ActionRefusal("encounter.complete", "The encounter is over.");
        }

        if (!combatant.Stats.Has(ClassFeature.CunningAction))
        {
            return new ActionRefusal("feature.absent", $"{combatant.Name} does not have Cunning Action.");
        }

        if (!combatant.Turn.HasBonusAction)
        {
            return new ActionRefusal("bonus_action.spent", $"{combatant.Name} has used its Bonus Action.");
        }

        combatant.Turn.SpendBonusAction();

        if (kind == CunningActionKind.Dash)
        {
            combatant.Turn.AddMovement(combatant.Stats.SpeedFeet);
            Add(
                CombatStepKind.Feature,
                $"{combatant.Name} uses Cunning Action to Dash, gaining {combatant.Stats.SpeedFeet} ft.",
                combatant);
        }
        else
        {
            combatant.Turn.Disengage();
            Add(CombatStepKind.Feature, $"{combatant.Name} uses Cunning Action to Disengage.", combatant);
        }

        return null;
    }

    /// <summary>
    /// Whether a Rogue's Sneak Attack applies to this attack.
    /// </summary>
    /// <remarks>
    /// The SRD's conditions: once per turn, with a Finesse or Ranged weapon, and either
    /// the attack has Advantage, or an ally of the target is within 5 feet of it and the
    /// attack does not have Disadvantage.
    /// </remarks>
    internal bool SneakAttackApplies(Combatant attacker, CombatAttack attack, Combatant target, AttackRoll roll)
    {
        if (!attacker.Stats.Has(ClassFeature.SneakAttack)
            || attacker.Features.SneakAttackUsedThisTurn
            || attacker.Stats.Character?.SneakAttackDamage is null)
        {
            return false;
        }

        // Every weapon a Rogue is proficient with is Finesse or Ranged, but the feature
        // is conditioned on the weapon rather than the class, so it is checked.
        var qualifies = attack.Kind == AttackKind.Ranged || attack.ReachFeet is not null;

        if (!qualifies)
        {
            return false;
        }

        if (roll.Roll.Mode == RollMode.Advantage)
        {
            return true;
        }

        if (roll.Roll.Mode == RollMode.Disadvantage)
        {
            return false;
        }

        return _combatants.Any(ally =>
            ally.Id != attacker.Id
            && ally.SideId == attacker.SideId
            && ally.IsActive
            && ally.DistanceFeetTo(target) <= Battlefield.FeetPerSquare);
    }

    /// <summary>
    /// Rogue Uncanny Dodge: a Reaction to halve one attack's damage.
    /// </summary>
    /// <remarks>
    /// Taken automatically rather than offered as a choice. There is no interesting
    /// decision in it at this scope — a Rogue with a spare Reaction always wants to halve
    /// incoming damage — and making the engine ask would complicate every attack for no
    /// gameplay gain. Revisit if a competing Reaction is ever implemented.
    /// </remarks>
    private bool TryUncannyDodge(Combatant target)
    {
        if (!target.Stats.Has(ClassFeature.UncannyDodge)
            || !target.Turn.HasReaction
            || !target.CanAct)
        {
            return false;
        }

        target.Turn.SpendReaction();

        Add(
            CombatStepKind.Feature,
            $"{target.Name} uses Uncanny Dodge, halving the damage.",
            target);

        return true;
    }

    /// <summary>Rage grants Resistance to Bludgeoning, Piercing and Slashing damage.</summary>
    private static bool RageResists(Combatant target, DamageType type) =>
        target.Features.IsRaging
        && type is DamageType.Bludgeoning or DamageType.Piercing or DamageType.Slashing;

    /// <summary>
    /// Ends a Rage that the printed Duration clause no longer keeps alive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "The Rage lasts until the end of your next turn, and it ends early if you don
    /// Heavy armor or have the Incapacitated condition. If your Rage is still active
    /// on your next turn, you can extend the Rage for another round by doing one of
    /// the following: Make an attack roll against an enemy. Force an enemy to make a
    /// saving throw. Take a Bonus Action to extend your Rage."
    /// </para>
    /// <para>
    /// Two readings that the first played run corrected, both of which had been
    /// quietly costing the Barbarian its whole feature: the turn a Rage is
    /// <em>entered</em> on never has to extend it, because the printed duration
    /// already reaches the end of the next turn; and a <b>missed</b> attack roll
    /// extends it, because the option is the roll rather than the hit. Donning Heavy
    /// armor cannot happen inside a fight, so only the Incapacitated half of the
    /// early-end clause is checked, at this boundary rather than the instant the
    /// condition lands — a stated approximation, and the only one here.
    /// </para>
    /// </remarks>
    private void EndRageIfUnsustained(Combatant combatant)
    {
        if (!combatant.Features.IsRaging)
        {
            return;
        }

        var incapacitated = combatant.HasCondition(ConditionType.Incapacitated);

        // The entering turn is already paid for by the printed duration.
        if (!incapacitated
            && (combatant.TurnsBegun <= combatant.Features.RageBeganOnTurn
                || combatant.Features.SustainedRageThisTurn))
        {
            return;
        }

        combatant.Features.IsRaging = false;
        Add(CombatStepKind.Feature, $"{combatant.Name}'s Rage ends.", combatant);
    }
}
