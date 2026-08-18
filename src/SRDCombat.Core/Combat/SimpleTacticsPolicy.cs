using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>
/// Takes a combatant's whole turn: close with the nearest enemy and hit it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately unsophisticated. Real monster tactics — focus fire, using the action
/// economy properly, positioning, retreating — are their own phase of work. What this
/// exists for is to drive a fight from start to finish without a client, which is what
/// makes an end-to-end engine test possible at all.
/// </para>
/// <para>
/// It is nonetheless fully deterministic: every tie is broken by an explicit ordering
/// rather than by enumeration order, so the same seed always produces the same fight.
/// Without that the frozen transcripts would be flaky.
/// </para>
/// </remarks>
public static class SimpleTacticsPolicy
{
    /// <summary>Plays out the active combatant's turn and ends it.</summary>
    public static void TakeTurn(Encounter encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);

        if (encounter.IsComplete || encounter.ActiveCombatant is not { } actor)
        {
            return;
        }

        if (!actor.CanAct)
        {
            encounter.EndTurn();
            return;
        }

        // Escaping comes first and costs the whole action, which is a real choice being
        // made crudely: a grappled creature could instead hit its grappler at no penalty.
        // Getting free is the better default, and without it a grapple would never end,
        // since nothing else in this policy can lift one.
        if (actor.HasCondition(ConditionType.Grappled))
        {
            encounter.Escape();
        }

        if (actor.HasCondition(ConditionType.Prone) && !ConditionRules.IsImmobile(actor))
        {
            encounter.StandUp();
        }

        // Class features and spells, before anything is spent on attacking. Gated on the
        // combatant being a character, which is also the line between the two kinds of
        // judgement this policy makes — see UseCharacterFeatures.
        UseCharacterFeatures(encounter, actor);

        // The side's shared judgement first: characters converge on the party
        // doctrine's focus target where they can (#123), and monsters hunt what
        // their own doctrine says they are (#127) — a pack flanks, a tactical mind
        // converges, a beast stays greedy.
        var target = ChooseTarget(encounter, actor);

        if (target is null)
        {
            encounter.EndTurn();
            return;
        }

        // Before anything is spent: a legal but penalized shot — the target behind an
        // ally or a low obstacle — is worth a step sideways first, when a strictly
        // cheaper firing square is in walking distance. Attacks and spells both fire
        // from wherever this leaves us.
        ImproveFiringPosition(encounter, actor, target);

        if (encounter.IsComplete || !actor.CanAct)
        {
            encounter.EndTurn();
            return;
        }

        // Spells are weighed against the swing they would replace, rather than being a
        // last resort. The old rule — cast only when the weapon cannot reach — made two
        // whole categories unreachable by construction (#85): a Touch spell fails
        // wherever the weapon already failed, and a self-centred Emanation is worth most
        // in exactly the melee the old rule cast from.
        if (TryCastDamagingSpell(encounter, actor, target))
        {
            SpendRemainingAttacks(encounter, actor);
            encounter.EndTurn();
            return;
        }

        // Attack from where we stand if anything reaches — unless standing still is
        // costing us the better weapon.
        //
        // "Anything reaches" was the whole test for as long as a melee character owned
        // only melee attacks: nothing reached, so it moved. Equip the printed starting
        // kits and it inverts. A Fighter's Javelin reaches 120 feet, so *something*
        // always reaches from the spawn square, and the front line spent whole fights
        // lobbing its weakest attack at long range instead of walking in behind a
        // Greataxe. Measured on the pregens: full clears fell from 38 of 120 to 2.
        //
        // Falling through to the walk loses nothing when closing turns out to be
        // impossible: the code below re-targets and attacks from wherever the move
        // ended, which is the same throw from a nearer square.
        if (!WouldRatherClose(actor, target) && TryAttack(encounter, actor, target))
        {
            SpendRemainingAttacks(encounter, actor);
            encounter.EndTurn();
            return;
        }

        // "Should I use it now?" — the branch that stops a monster always attacking. If
        // the Attack action reached nothing, a limited-use entry that does reach — the
        // Ape's Rock at 25 feet — is used instead of closing empty-handed.
        if (TryUseLimitedEntry(encounter, actor, target))
        {
            encounter.EndTurn();
            return;
        }

        var moved = MoveTowards(encounter, actor, target);

        // The move may have provoked an Opportunity Attack that dropped us, or ended the
        // fight outright, so re-check before swinging.
        if (encounter.IsComplete || !actor.CanAct)
        {
            encounter.EndTurn();
            return;
        }

        var closest = ChooseTarget(encounter, actor);

        if (closest is not null && TryAttack(encounter, actor, closest))
        {
            SpendRemainingAttacks(encounter, actor);
        }
        else if (!moved)
        {
            // Stuck: nothing active in reach and nowhere better to stand. A downed
            // enemy is the one target the ordinary flow cannot see, and ignoring it
            // has produced a literal stalemate — see FinishTheDowned.
            FinishTheDowned(encounter, actor);
        }

        encounter.EndTurn();
    }

    /// <summary>Runs the whole fight, stopping if it somehow fails to resolve.</summary>
    /// <param name="encounter">The fight to run.</param>
    /// <param name="roundLimit">
    /// A guard against a fight that cannot end — two creatures that can never reach or
    /// hurt each other would otherwise loop forever.
    /// </param>
    public static void RunToCompletion(Encounter encounter, int roundLimit = 50)
    {
        ArgumentNullException.ThrowIfNull(encounter);

        while (!encounter.IsComplete && encounter.Round <= roundLimit)
        {
            TakeTurn(encounter);
        }
    }

    /// <summary>
    /// Uses the rest of the swings an Attack action bought, from Extra Attack or a
    /// Multiattack. Retargets between swings, so a creature that kills its target does
    /// not waste the remainder on a corpse.
    /// </summary>
    private static void SpendRemainingAttacks(Encounter encounter, Combatant actor)
    {
        while (!encounter.IsComplete
               && actor.CanAct
               && actor.Features.AttacksRemainingThisAction > 0
               && ChooseTarget(encounter, actor) is { } next
               && TryAttack(encounter, actor, next))
        {
            // TryAttack consumes one swing per call.
        }
    }

    /// <summary>
    /// Uses the class features and spells a character has, before it commits to attacking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Gated on <c>Stats.Character</c>, which is what separates the two kinds of
    /// judgement this policy makes.</b> A monster's turn is nearly always "hit the
    /// nearest thing"; a character's turn has a resource question in front of it — heal,
    /// rage, or swing? Because only characters carry features, one policy can hold both
    /// without a monster ever reaching this code, and the frozen transcript's
    /// hand-authored combatants are untouched by it.
    /// </para>
    /// <para>
    /// The order below is the judgement, and it is deliberately shallow: <b>get a fallen
    /// ally up first</b>, because a character at 0 hit points contributes nothing and
    /// dies permanently, and every measurement of a run showed casualties compounding
    /// into a death spiral. Then Rage, which is a whole fight's worth of value for one
    /// Bonus Action; then Second Wind, which is the cheapest healing a Fighter has.
    /// </para>
    /// <para>
    /// Every branch attempts the engine's own action and gives up if it is refused, so
    /// the policy never needs a second copy of the rules about slots, uses and action
    /// economy. A refusal costs nothing and changes nothing.
    /// </para>
    /// </remarks>
    private static void UseCharacterFeatures(Encounter encounter, Combatant actor)
    {
        if (actor.Stats.Character is null)
        {
            return;
        }

        // A potion poured down a fallen ally's throat costs a Bonus Action and no slot,
        // so it is tried before the spell that would spend one. Reach is the price: it
        // only works on somebody adjacent, which a caster standing back cannot use.
        if (actor.Turn.HasBonusAction && TryAdministerPotion(encounter, actor))
        {
            // Back on their feet for the cost of a Bonus Action.
        }

        // A dead ally outranks a downed one: the downed are still rolling Death Saving
        // Throws and can be got up later, while the dead have a printed window that is
        // closing. The refusals decide what "can be revived" means — the policy just
        // asks, and a refused cast costs nothing.
        else if (actor.Turn.HasAction && TryReviveDeadAlly(encounter, actor))
        {
            return;
        }

        // Healing Word is a Bonus Action, so getting someone up can cost nothing but the
        // slot; Cure Wounds is an Action and is only worth it if nobody can be reached
        // the cheap way.
        else if (actor.Turn.HasBonusAction && TryHealFallenAlly(encounter, actor, SpellCastingTime.BonusAction))
        {
            // Healed with the Bonus Action; the Action is still free to fight with.
        }

        // Divine Spark before the Action-time slot heal: both spend the Action, and
        // the spark spends no slot — the cautious healer's arithmetic, one rung
        // cheaper. Only the heal half is ever chosen here; the harm half is a real
        // option a player has, but spending the party's cheapest revival resource on
        // damage is exactly the trade the measured slot-reserve findings warn against.
        else if (actor.Turn.HasAction && TryDivineSparkFallenAlly(encounter, actor))
        {
            return;
        }
        else if (actor.Turn.HasAction && TryHealFallenAlly(encounter, actor, SpellCastingTime.Action))
        {
            return;
        }

        if (actor.Turn.HasBonusAction && ShouldRage(encounter, actor))
        {
            encounter.Rage();
        }

        if (actor.Turn.HasBonusAction && IsBadlyHurt(encounter, actor))
        {
            encounter.SecondWind();
        }

        // Last, because Second Wind is free and a potion is gone for the rest of the
        // run: drink only when badly hurt and nothing cheaper was available.
        if (actor.Turn.HasBonusAction
            && IsBadlyHurt(encounter, actor)
            && actor.Inventory.Weakest is { } potency)
        {
            encounter.DrinkPotion(potency);
        }
    }

    /// <summary>
    /// Pours a potion down the throat of an adjacent ally who is down but not dead.
    /// </summary>
    /// <remarks>
    /// The nearest one, so a character standing between two casualties helps the one it
    /// is actually beside. Refusals are the engine's to give — reach, the Bonus Action,
    /// an empty pack — and a refusal costs nothing, so this never re-checks them.
    /// </remarks>
    private static bool TryAdministerPotion(Encounter encounter, Combatant actor)
    {
        var fallen = encounter.Combatants
            .Where(other => other.SideId == actor.SideId
                && !ReferenceEquals(other, actor)
                && !other.IsDead
                && other.CurrentHitPoints == 0)
            .OrderBy(other => actor.Position.DistanceFeetTo(other.Position))
            .ThenBy(other => other.Id, StringComparer.Ordinal);

        // The casualty's own flask counts, and is reached for first — a potion found by
        // somebody who later goes down used to be stuck with them, because this only
        // ever looked in the helper's pack.
        foreach (var ally in fallen)
        {
            var potency = ally.Inventory.Weakest ?? actor.Inventory.Weakest;

            if (potency is { } chosen && encounter.DrinkPotion(chosen, ally) is null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Spends a Channel Divinity use getting the nearest fallen ally within 30 feet
    /// back up, for no slot.
    /// </summary>
    /// <remarks>
    /// The nearest first, the potion's own tiebreak; the engine's refusals — range,
    /// Total Cover, an exhausted Channel Divinity — are the arbiter, and a refused
    /// attempt costs nothing, so the loop just tries the next casualty.
    /// </remarks>
    private static bool TryDivineSparkFallenAlly(Encounter encounter, Combatant actor)
    {
        if (!actor.Stats.Has(ClassFeature.ChannelDivinity)
            || actor.Features.ChannelDivinityRemaining <= 0)
        {
            return false;
        }

        var fallen = encounter.Combatants
            .Where(other => other.SideId == actor.SideId
                && other.Id != actor.Id
                && !other.IsDead
                && other.CurrentHitPoints == 0)
            .OrderBy(other => actor.Position.DistanceFeetTo(other.Position))
            .ThenBy(other => other.Id, StringComparer.Ordinal);

        return fallen.Any(ally => encounter.DivineSpark(ally, DivineSparkUse.Heal) is null);
    }

    /// <summary>
    /// Revives the freshest dead ally when the actor carries a revival spell — walking
    /// to the body first, because Revivify is Touch.
    /// </summary>
    /// <remarks>
    /// The engine's refusals are the arbiter of "can be revived": the policy only asks,
    /// and a refused cast costs nothing. The freshest corpse first because every window
    /// is closing and the newest death has the most left. When the body is beyond this
    /// turn's movement, the walk toward it still happens and the method reports false,
    /// so the rest of the turn is spent fighting from wherever the walk ended.
    /// </remarks>
    private static bool TryReviveDeadAlly(Encounter encounter, Combatant actor)
    {
        if (actor.Stats.Character is not { CanCast: true } character)
        {
            return false;
        }

        var revival = character.Spells.FirstOrDefault(spell =>
            spell.Revival is not null && spell.CastingTime == SpellCastingTime.Action);

        if (revival is null)
        {
            return false;
        }

        var corpses = encounter.Combatants
            .Where(other => other.SideId == actor.SideId
                && other.Id != actor.Id
                && other.IsDead
                && other.DiedInRound is not null)
            .OrderByDescending(other => other.DiedInRound)
            .ThenBy(other => other.Id, StringComparer.Ordinal)
            .ToArray();

        foreach (var corpse in corpses)
        {
            if (actor.Position.DistanceFeetTo(corpse.Position) > Battlefield.FeetPerSquare)
            {
                var beside = encounter.Battlefield.AllSquares()
                    // Beside, never on: the corpse does not block the square, and
                    // standing on it would be exactly the crowding the revival's own
                    // no-room refusal exists to prevent.
                    .Where(square => square != corpse.Position
                        && square.DistanceFeetTo(corpse.Position) <= Battlefield.FeetPerSquare)
                    .Select(square => (Square: square, Path: MovementRules.FindPath(
                        encounter.Battlefield,
                        actor,
                        square,
                        actor.Turn.MovementFeet,
                        encounter.Combatants)))
                    .Where(candidate => candidate.Path is not null)
                    // The healer's walk to the body respects the same arithmetic as
                    // every other walk: the least-provoking way in first.
                    .OrderBy(candidate => ProvokedDamageAlong(encounter, actor, candidate.Path!))
                    .ThenBy(candidate => candidate.Square.DistanceFeetTo(actor.Position))
                    .ThenBy(candidate => candidate.Square.X)
                    .ThenBy(candidate => candidate.Square.Y)
                    .Select(candidate => (GridPosition?)candidate.Square)
                    .FirstOrDefault();

                if (beside is { } destination)
                {
                    encounter.Move(destination);
                }
                else
                {
                    // Out of range this turn: commit the walk toward the freshest body
                    // and fight on from wherever it ends.
                    MoveTowards(encounter, actor, corpse);
                    return false;
                }
            }

            if (actor.Position.DistanceFeetTo(corpse.Position) <= Battlefield.FeetPerSquare
                && encounter.CastSpell(revival.Id, corpse) is null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether casting this spell would consume the caster's last slot at or above its
    /// revival spell's level — the slot a death would need.
    /// </summary>
    private static bool WouldSpendTheLastRevivalSlot(Combatant actor, SpellDefinition spell)
    {
        var revival = actor.Stats.Character!.Spells.FirstOrDefault(candidate => candidate.Revival is not null);

        if (revival is null)
        {
            return false;
        }

        // The slot the engine would pick: the lowest level at or above the spell's own
        // with a slot remaining — SpendCastingCost's rule, mirrored.
        int? picked = null;

        for (var level = spell.Level; level <= 9; level++)
        {
            if (actor.Features.SpellSlotsRemaining.GetValueOrDefault(level) > 0)
            {
                picked = level;
                break;
            }
        }

        if (picked is not { } chosen || chosen < revival.Level)
        {
            return false;
        }

        var remainingAtRevivalLevels = 0;

        for (var level = revival.Level; level <= 9; level++)
        {
            remainingAtRevivalLevels += actor.Features.SpellSlotsRemaining.GetValueOrDefault(level);
        }

        return remainingAtRevivalLevels <= 1;
    }

    /// <summary>
    /// Heals the ally most in need with a spell of the given casting time.
    /// </summary>
    /// <remarks>
    /// Only a character at 0 hit points is worth a slot here. Topping up a wounded ally
    /// is a judgement this placeholder has no way to make well — it cannot see what is
    /// coming — while getting somebody off the floor is unambiguous: they are otherwise
    /// out of the fight and rolling Death Saving Throws.
    /// </remarks>
    private static bool TryHealFallenAlly(Encounter encounter, Combatant actor, SpellCastingTime castingTime)
    {
        var healing = actor.Stats.Character!.Spells
            .Where(spell => spell.Heal is not null && spell.CastingTime == castingTime)
            // The cautious healer's newest clause: a heal must not burn the last slot
            // that could power a revival. The engine spends the lowest slot that fits,
            // so once the small slots are dry an upcast Cure Wounds quietly eats the
            // level 3 slot Revivify needs — which is exactly how the first measured
            // level-5 runs fired zero revivals across forty-five deaths: every slot
            // that could answer a death had already bought hit points.
            .Where(spell => !WouldSpendTheLastRevivalSlot(actor, spell))
            // The biggest heal first: a slot spent getting someone up should buy as much
            // margin as it can.
            .OrderByDescending(spell => spell.Heal!.Dice.Average)
            .ThenBy(spell => spell.Id, StringComparer.Ordinal)
            .ToArray();

        if (healing.Length == 0)
        {
            return false;
        }

        var fallen = encounter.Combatants
            .Where(other => other.SideId == actor.SideId
                && other.Id != actor.Id
                && !other.IsDead
                && other.CurrentHitPoints == 0)
            .OrderBy(other => other.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        if (fallen is null)
        {
            return false;
        }

        return healing.Any(spell => encounter.CastSpell(spell.Id, fallen) is null);
    }

    /// <summary>Whether a Barbarian should rage: an enemy is close enough to matter.</summary>
    /// <remarks>
    /// Rage lasts the fight and costs a Bonus Action, so the only real mistake is raging
    /// with nothing to hit. "Close enough" is a single move away, which is when the
    /// Barbarian is about to be in the fight whatever happens next.
    /// </remarks>
    private static bool ShouldRage(Encounter encounter, Combatant actor)
    {
        if (!actor.Stats.Has(ClassFeature.Rage) || actor.Features.IsRaging)
        {
            return false;
        }

        return encounter.EnemiesOf(actor).Any(enemy =>
            actor.Position.DistanceFeetTo(enemy.Position) <= actor.Stats.SpeedFeet);
    }

    /// <summary>
    /// True when a character is hurt enough to spend something on itself: half its hit
    /// points gone while somebody can still heal, a third gone once nobody can.
    /// </summary>
    /// <remarks>
    /// The party's shape raising the caution threshold (#126): a wound the Cleric can
    /// answer is cheaper than the same wound with the Cleric down or dry, so Second
    /// Wind and the potion in the pack both fire earlier when
    /// <see cref="PartyDoctrine.HasHealer"/> says the safety net is gone.
    /// </remarks>
    private static bool IsBadlyHurt(Encounter encounter, Combatant actor) =>
        PartyDoctrine.HasHealer(encounter, actor)
            ? actor.CurrentHitPoints * 2 <= actor.Stats.MaximumHitPoints
            : actor.CurrentHitPoints * 3 <= actor.Stats.MaximumHitPoints * 2;

    /// <summary>
    /// Casts the hardest-hitting damaging spell that reaches, for a caster whose weapon
    /// does not.
    /// </summary>
    /// <remarks>
    /// The same shape as <see cref="TryUseLimitedEntry"/> and reached at the same point,
    /// so a Cleric thirty feet from the fight throws Sacred Flame rather than walking.
    /// An area spell is skipped when its coverage would catch the caster's own side,
    /// exactly as a breath weapon is.
    /// </remarks>
    private static bool TryCastDamagingSpell(Encounter encounter, Combatant actor, Combatant target)
    {
        if (actor.Stats.Character is not { CanCast: true } character || !actor.Turn.HasAction)
        {
            return false;
        }

        var distance = actor.Position.DistanceFeetTo(target.Position);
        var swing = WeaponValue(actor, distance);

        // A healer's slots have another job. Burning them on damage while somebody is
        // down is how a party loses a run it was winning, and it is a measured effect
        // rather than a feeling: potions moved the median from 4 to 7.5 precisely
        // because getting people off the floor is worth more than extra damage.
        var slotsAreSpokenFor = NeedsHealing(encounter, actor) && CanHeal(character);

        var spells = character.Spells
            .Where(spell => spell.Damage.Count > 0
                || spell.Save?.FailureDamage.Count > 0
                || HardControlRiders(spell).Any())
            .Where(spell => spell.CastingTime == SpellCastingTime.Action)
            .Where(spell => spell.IsCantrip || !slotsAreSpokenFor)
            .Where(spell => !ShouldHoldTheArea(encounter, actor, target, spell))
            // Hold Person's printed gate: a spell that names its target's creature
            // type is never aimed at anything else — the engine would refuse, and the
            // policy should not spend its evaluation asking.
            .Where(spell => spell.TargetCreatureType is not { } required
                || target.Stats.Type == required)
            .Where(spell => spell.IsSelfRanged || spell.TargetRangeFeet is null || spell.TargetRangeFeet >= distance)
            .Select(spell => (Spell: spell,
                Value: SpellValue(encounter, actor, target, spell)
                    + ControlValue(actor, target, spell)))
            .Where(candidate => candidate.Value > 0)
            .Where(candidate => IsWorthCasting(candidate.Spell, candidate.Value, swing))
            .OrderByDescending(candidate => candidate.Value)
            // A cantrip first among equals: it costs nothing.
            .ThenBy(candidate => candidate.Spell.Level)
            .ThenBy(candidate => candidate.Spell.Id, StringComparer.Ordinal);

        return spells.Any(candidate => encounter.CastSpell(candidate.Spell.Id, target) is null);
    }

    /// <summary>
    /// Whether anybody on this creature's side needs the slots more than the enemy does.
    /// </summary>
    /// <remarks>
    /// <b>Badly hurt counts, not just down</b>, and that is measured rather than assumed:
    /// reserving slots only for a character already at 0 hit points clears a median of 5
    /// fights, while holding them from the moment anybody is badly hurt clears 6.5. The
    /// cautious healer wins, because a slot spent on damage is gone when the character
    /// who needed it drops.
    /// </remarks>
    private static bool NeedsHealing(Encounter encounter, Combatant actor) =>
        encounter.Combatants.Any(other => other.SideId == actor.SideId
            && !other.IsDead
            && (other.CurrentHitPoints == 0 || IsBadlyHurt(encounter, other)));

    /// <summary>Whether this caster has anything to spend a slot healing with.</summary>
    private static bool CanHeal(CombatantFeatures character) =>
        character.Spells.Any(spell => spell.Heal is not null && !spell.IsCantrip);

    /// <summary>
    /// How many rounds an area slot waits for a clump before patience expires and the
    /// value rule alone decides.
    /// </summary>
    private const int AreaPatienceRounds = 3;

    /// <summary>
    /// Whether a slotted area spell should be held back for a better clump: early in
    /// the fight, with several enemies still standing, an area that would catch only
    /// one of them is a fireball spent on a goblin.
    /// </summary>
    /// <remarks>
    /// The party's shape being patient (#126). The value rule alone cannot make this
    /// judgement — an AoE-rich caster's swing is weak, so a single-target area cast
    /// clears the 1.5× bar on round one and the slot is gone before the enemies ever
    /// close into the huddle the spell was prepared for. Three bounds keep the patience
    /// honest: a cantrip is never held (it costs nothing), the last enemy standing is
    /// as clumped as it will ever get, and after round three the clump that was coming
    /// has either come or never will.
    /// </remarks>
    private static bool ShouldHoldTheArea(
        Encounter encounter,
        Combatant actor,
        Combatant target,
        SpellDefinition spell)
    {
        if (spell.IsCantrip
            || spell.Save?.Area is not { } area
            || !AreaTargeting.CanResolve(area.Shape)
            || encounter.Round > AreaPatienceRounds)
        {
            return false;
        }

        if (encounter.EnemiesOf(actor).Count(enemy => enemy.IsActive) <= 1)
        {
            return false;
        }

        var covered = AreaTargeting.Cover(area, actor.Position, target.Position, encounter.Battlefield)
            .ToHashSet();

        var caught = encounter.Combatants.Count(combatant =>
            combatant.IsActive
            && combatant.SideId != actor.SideId
            && covered.Contains(combatant.Position));

        return caught < 2;
    }

    /// <summary>
    /// Whether a spell is worth casting instead of swinging.
    /// </summary>
    /// <remarks>
    /// A cantrip only has to be better, because it costs nothing. A slot has to be
    /// <em>clearly</em> better — the margin exists so a level 3 slot is not spent on
    /// something a mace would have finished, which is the mistake a fallback rule could
    /// never make and a value rule makes constantly.
    /// </remarks>
    private static bool IsWorthCasting(SpellDefinition spell, double spellValue, double weaponValue) =>
        spell.IsCantrip ? spellValue > weaponValue : spellValue > weaponValue * SlotMargin;

    /// <summary>How much clearer a slotted spell must be than a weapon swing.</summary>
    private const double SlotMargin = 1.5;

    /// <summary>
    /// How many rounds a landed hold is priced at. Two is a stated crude constant: the
    /// printed repeat save gives the victim a fresh roll every turn, so "for the
    /// duration" is worth a couple of rounds in practice, not the whole minute.
    /// </summary>
    private const double HeldRounds = 2.0;

    /// <summary>
    /// The riders that remove a whole action economy: what makes a control spell worth
    /// a slot at all. Paralyzed and Stunned both take the turn away entirely — lesser
    /// conditions are real but not priceable at this policy's altitude.
    /// </summary>
    private static IEnumerable<AppliedCondition> HardControlRiders(SpellDefinition spell) =>
        (spell.Save?.AppliedConditions ?? [])
            .Where(ConditionRules.CanBeImposed)
            .Where(rider => rider.Condition is ConditionType.Paralyzed or ConditionType.Stunned);

    /// <summary>
    /// What holding this target is worth: its threat per round, for the rounds the
    /// hold plausibly lasts, discounted by the chance the save succeeds outright.
    /// </summary>
    /// <remarks>
    /// The same altitude as every other number here — <c>ThreatPerRound</c> against
    /// numbers made the same way. A held creature also grants Advantage and close-in
    /// Critical Hits, which this deliberately does not price: the removed action
    /// economy is the measurable bulk, and the extras only sweeten a cast the bulk
    /// already justified.
    /// </remarks>
    private static double ControlValue(Combatant actor, Combatant target, SpellDefinition spell)
    {
        if (!HardControlRiders(spell).Any())
        {
            return 0;
        }

        var difficultyClass = spell.Save?.DifficultyClass
            ?? actor.Stats.Character?.SpellSaveDifficultyClass
            ?? 10;

        var failChance = Math.Clamp(
            (difficultyClass - 1 - target.Stats.SaveBonusFor(spell.Save!.Ability)) / 20.0,
            0.05,
            0.95);

        return PartyDoctrine.ThreatPerRound(target) * HeldRounds * failChance;
    }

    /// <summary>Expected damage from this creature's best reaching attack, for one action.</summary>
    private static double WeaponValue(Combatant actor, int distance)
    {
        var best = actor.Stats.Attacks
            .Where(attack => attack.CanReach(distance))
            .Where(attack => actor.Stats.AllowsInMultiattack(attack.Name))
            .Where(attack => actor.Uses.IsAvailable(attack.Name))
            .Select(attack => attack.Damage.Sum(damage => damage.Amount.Average))
            .DefaultIfEmpty(0)
            .Max();

        // An Attack action buys several swings, so the comparison is per action.
        return best * Math.Max(1, actor.Stats.AttacksPerAction);
    }

    /// <summary>
    /// What a spell is worth here: its damage against everything it would catch, less
    /// what it would do to this creature's own side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An area that catches a friend is a trade, not a veto.</b> The old rule refused
    /// any area covering an ally, which sounds prudent and made Spirit Guardians — a
    /// 15-foot Emanation centred on the caster, in a party that fights in a huddle —
    /// literally uncastable. Counting both sides lets the obvious good cast happen and
    /// still refuses the obvious bad one.
    /// </para>
    /// <para>
    /// Allies are weighed the same as enemies rather than more heavily. That is a
    /// deliberate simplification and it is the crude part of this judgement: a real
    /// player weighs the Cleric's own hit points differently from a goblin's.
    /// </para>
    /// </remarks>
    private static double SpellValue(
        Encounter encounter,
        Combatant actor,
        Combatant target,
        SpellDefinition spell)
    {
        var damage = spell.Damage.Sum(component => component.Amount.Average)
            + (spell.Save?.FailureDamage.Sum(component => component.Amount.Average) ?? 0);

        if (damage <= 0)
        {
            return 0;
        }

        if (spell.Save?.Area is not { } area)
        {
            return damage;
        }

        if (!AreaTargeting.CanResolve(area.Shape))
        {
            return 0;
        }

        var covered = AreaTargeting.Cover(area, actor.Position, target.Position, encounter.Battlefield)
            .ToHashSet();

        var enemies = encounter.Combatants.Count(c =>
            c.IsActive && c.SideId != actor.SideId && covered.Contains(c.Position));

        var friends = encounter.Combatants.Count(c =>
            c.IsActive && c.SideId == actor.SideId && covered.Contains(c.Position));

        return damage * (enemies - friends);
    }

    /// <summary>
    /// The last resort of a stuck turn: attack the nearest downed enemy in reach, the
    /// one target the ordinary flow cannot see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>EnemiesOf</c> hides the Unconscious, deliberately, so every targeting path
    /// ignores a character at 0 hit points — and that produced a fight that could not
    /// end: generated walls formed a pocket whose one doorway was plugged by an
    /// unconscious character (a body occupies its square since the 0-hit-point
    /// occupancy fix), a Giant Vulture stood beside the body for fifty rounds without
    /// swinging, and both sides idled to the round limit. The engine has always
    /// allowed the attack — "an Unconscious creature is a legal target, and hitting
    /// one is how death saves fail" — the policy just never asked.
    /// </para>
    /// <para>
    /// The gate is what keeps this from changing ordinary fights: it fires only when
    /// the turn produced nothing — no active enemy in reach after the move, and no
    /// move worth making — which in an open field essentially never happens. A
    /// monster mid-approach never diverts to stomp the fallen; only a creature with
    /// literally nothing else to do finishes what is at its feet, and a downed
    /// character in a doorway stops being a wall the fight cannot pass.
    /// </para>
    /// </remarks>
    private static void FinishTheDowned(Encounter encounter, Combatant actor)
    {
        if (!actor.Turn.HasAction && actor.Features.AttacksRemainingThisAction <= 0)
        {
            return;
        }

        var downed = encounter.Combatants
            .Where(other => other.SideId != actor.SideId
                && !other.IsDead
                && !other.IsActive)
            .OrderBy(other => actor.Position.DistanceFeetTo(other.Position))
            .ThenBy(other => other.Id, StringComparer.Ordinal)
            .ToArray();

        // Nearest first, and stop at the first swing that lands.
        foreach (var body in downed)
        {
            if (TryAttack(encounter, actor, body))
            {
                return;
            }
        }

        // None in reach: spend the stuck turn closing on the nearest instead, so the
        // swing comes next round rather than never. This is not a redundancy of the
        // approach that got us here — the ordinary walk steers by shelter as well as
        // distance, so a stuck creature can be standing one sheltered square off the
        // body it needs to clear.
        if (downed.Length > 0)
        {
            MoveTowards(encounter, actor, downed[0]);
        }
    }

    /// <summary>
    /// The Phase 6 split in one dispatch (#127): a character consults the party's
    /// doctrine, a monster its own, and both fall back to the greedy nearest-weakest
    /// choice their doctrine declines to override.
    /// </summary>
    private static Combatant? ChooseTarget(Encounter encounter, Combatant actor) =>
        actor.Stats.Character is not null
            ? PartyDoctrine.ChooseTarget(encounter, actor, NearestEnemy(encounter, actor))
            : MonsterDoctrine.ChooseTarget(encounter, actor, NearestEnemy(encounter, actor));

    /// <summary>
    /// Chooses what to attack: finish something off if anything is already in reach,
    /// otherwise close on the nearest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Focus fire is the single largest thing a policy can do</b>, and its absence was
    /// glaring in every fight played: four combatants would spread damage across five
    /// enemies, kill none of them, and take five creatures' worth of attacks back every
    /// round. Killing one removes its attacks permanently; wounding five removes nothing.
    /// </para>
    /// <para>
    /// The rule is deliberately narrow — <em>among the enemies this creature can already
    /// hit, take the weakest</em>. It does not chase a wounded enemy across the field,
    /// because walking past a healthy one to finish a distant cripple is how a real
    /// player gets flanked, and this policy has no way to judge that.
    /// </para>
    /// </remarks>
    private static Combatant? NearestEnemy(Encounter encounter, Combatant actor)
    {
        var enemies = encounter.EnemiesOf(actor).ToArray();

        var inReach = enemies
            .Where(enemy => actor.Stats.Attacks.Any(attack =>
                attack.CanReach(actor.Position.DistanceFeetTo(enemy.Position))))
            .ToArray();

        // Ties broken by identifier rather than enumeration order, so the same seed
        // always produces the same fight.
        return (inReach.Length > 0 ? inReach : enemies)
            .OrderBy(enemy => inReach.Length > 0 ? enemy.CurrentHitPoints : 0)
            .ThenBy(enemy => actor.Position.DistanceFeetTo(enemy.Position))
            .ThenBy(enemy => enemy.CurrentHitPoints)
            .ThenBy(enemy => enemy.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Whether the actor would do better walking in than swinging from here: it owns a
    /// harder-hitting attack than anything that reaches at this distance, and it still
    /// has the movement to go and use it.
    /// </summary>
    /// <remarks>
    /// Deliberately compares the same figure <see cref="TryAttack"/> sorts on —
    /// <see cref="ValueAt"/> — so the two agree about which attack is "better". The
    /// unreaching side is valued raw, because closing is exactly what removes the
    /// distance the discount prices. A tie keeps the creature where it stands, which is
    /// what leaves a genuine archer shooting: the Rogue's Shortsword and Shortbow
    /// average the same, so she never closes for the blade — while an archer beyond
    /// normal range does close, since her own bow is worth more from nearer in.
    /// </remarks>
    private static bool WouldRatherClose(Combatant actor, Combatant target)
    {
        if (actor.Turn.MovementFeet <= 0)
        {
            return false;
        }

        var distance = actor.Position.DistanceFeetTo(target.Position);

        var usable = actor.Stats.Attacks
            .Where(attack => actor.Stats.AllowsInMultiattack(attack.Name))
            .Where(attack => actor.Uses.IsAvailable(attack.Name))
            .ToArray();

        var hardest = usable
            .Select(attack => attack.Damage.Sum(damage => damage.Amount.Average))
            .DefaultIfEmpty(0)
            .Max();

        var reaching = usable
            .Where(attack => attack.CanReach(distance))
            .Select(attack => ValueAt(attack, distance))
            .DefaultIfEmpty(0)
            .Max();

        return hardest > reaching;
    }

    /// <summary>
    /// What an attack is worth at this distance: its average damage, halved when the
    /// roll would be at long range.
    /// </summary>
    /// <remarks>
    /// The halving is a stated crude constant, like the hold's two held rounds: long
    /// range imposes Disadvantage, rolling twice and taking the worse roughly squares
    /// a typical hit chance, and half is close enough for ranking attacks against each
    /// other. It is what stops a Barbarian lobbing a Handaxe 40 feet at Disadvantage
    /// when walking in behind the Greataxe pays better — and it is a preference, never
    /// a veto: with no movement left or nothing better owned, the long throw is still
    /// taken.
    /// </remarks>
    private static double ValueAt(CombatAttack attack, int distanceFeet)
    {
        var average = attack.Damage.Sum(damage => damage.Amount.Average);

        return attack.IsAtLongRange(distanceFeet) ? average / 2 : average;
    }

    /// <summary>
    /// Attacks with the most valuable attack that can reach the target — average
    /// damage, discounted at long range per <see cref="ValueAt"/>.
    /// </summary>
    private static bool TryAttack(Encounter encounter, Combatant actor, Combatant target)
    {
        if (!actor.Turn.HasAction && actor.Features.AttacksRemainingThisAction <= 0)
        {
            return false;
        }

        var distance = actor.Position.DistanceFeetTo(target.Position);

        var attack = actor.Stats.Attacks
            .Where(candidate => candidate.CanReach(distance))
            .Where(candidate => actor.Stats.AllowsInMultiattack(candidate.Name))
            // A spent "(Recharge 5-6)" attack would be refused, and the refusal would
            // abort the whole attack loop — filter it out so the next-best attack swings.
            .Where(candidate => actor.Uses.IsAvailable(candidate.Name))
            .OrderByDescending(candidate => ValueAt(candidate, distance))
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        return attack is not null && encounter.Attack(attack.Name, target) is null;
    }

    /// <summary>
    /// Uses the hardest-hitting limited-use entry that reaches the target — an attack
    /// like the Ape's Rock, or a saving-throw effect like a breath weapon — when the
    /// Attack action cannot reach anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only entries with a printed usage limit are considered, deliberately. The other
    /// entries locked out of a Multiattack are the lycanthropes' form-gated attacks —
    /// "Bite (Wolf or Hybrid Form Only)" — and the engine has no concept of form, so
    /// choosing one here would be this policy silently deciding what shape the creature
    /// fights in. A client may make that call through <c>UseEntry</c>; this policy does
    /// not.
    /// </para>
    /// <para>
    /// An area entry is skipped when its area would catch the user's own side — the one
    /// piece of judgement this placeholder allows itself, because a wolf breathing on its
    /// own pack reads as a bug in every transcript it appears in. The check still counts
    /// the user among its own side, which now costs nothing: the printed glossary excludes
    /// an Emanation's origin from its area (see <c>AreaTargeting</c>), so no shape this
    /// engine resolves covers its own user, and Emanation entries became choosable when
    /// that reading was verified.
    /// </para>
    /// </remarks>
    private static bool TryUseLimitedEntry(Encounter encounter, Combatant actor, Combatant target)
    {
        if (!actor.Turn.HasAction)
        {
            return false;
        }

        var distance = actor.Position.DistanceFeetTo(target.Position);

        var entry = actor.Stats.Entries
            .Where(candidate => candidate.Section == MonsterEntrySection.Action
                && actor.Uses.Tracks(candidate.Name)
                && actor.Uses.IsAvailable(candidate.Name))
            .Select(candidate => new
            {
                candidate.Name,
                Damage = candidate.Mechanics switch
                {
                    EntryMechanics.Attack => AttackFor(actor, candidate.Name) is { } attack
                        && attack.CanReach(distance)
                            ? attack.Damage.Sum(damage => damage.Amount.Average)
                            : (int?)null,
                    EntryMechanics.SavingThrow => SaveReaches(encounter, actor, target, candidate.Save, distance)
                        ? candidate.Save!.FailureDamage.Sum(damage => damage.Amount.Average)
                        : null,
                    _ => null,
                },
            })
            .Where(candidate => candidate.Damage is not null)
            .OrderByDescending(candidate => candidate.Damage)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        return entry is not null && encounter.UseEntry(entry.Name, target) is null;
    }

    private static CombatAttack? AttackFor(Combatant actor, string name) =>
        actor.Stats.Attacks.FirstOrDefault(attack =>
            string.Equals(attack.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a saving-throw entry can be aimed at the target from here without
    /// catching a friend.
    /// </summary>
    private static bool SaveReaches(
        Encounter encounter,
        Combatant actor,
        Combatant target,
        SaveEffect? save,
        int distance)
    {
        if (save is not { DifficultyClass: not null })
        {
            return false;
        }

        if (save.Area is not { } area)
        {
            // A single-target save entry models no range, so nothing gates the distance.
            return true;
        }

        if (!AreaTargeting.CanResolve(area.Shape))
        {
            return false;
        }

        // Cone, Line and Emanation extend from the user; a target beyond their size is
        // out of reach. Point-aimed shapes land wherever they are aimed.
        if (area.Shape is AreaShape.Cone or AreaShape.Line or AreaShape.Emanation
            && area.SizeFeet < distance)
        {
            return false;
        }

        var covered = AreaTargeting.Cover(area, actor.Position, target.Position, encounter.Battlefield)
            .ToHashSet();

        return !encounter.Combatants.Any(combatant =>
            combatant.IsActive
            && combatant.SideId == actor.SideId
            && covered.Contains(combatant.Position));
    }

    /// <summary>
    /// Moves as close to the target as this turn's movement allows, preferring a square
    /// the creature can actually attack from — and, among those, the best-sheltered one.
    /// </summary>
    /// <remarks>
    /// Cover changed what "can attack from" means: a square within reach whose line the
    /// target has Total Cover against delivers nothing, so it no longer counts as a
    /// firing position. Three preferences are deliberate, in this order. A square that
    /// does not close the distance is still taken when it turns a blocked attack into a
    /// possible one — the sidestep that clears a wall, without which an archer stood
    /// forever behind the one square it could not shoot past. A shooter avoids ending
    /// beside an enemy, because the printed Ranged Attacks in Close Combat rule would
    /// put its own roll at Disadvantage there — the engine enforces it, so the policy
    /// respects it. Among what remains, the clean shot comes first: creatures grant
    /// Half Cover too (#108), so the square where the target's cover is cheapest wins,
    /// which is what stops an archer shooting through the ally it spawned behind when a
    /// step sideways clears the line. Then shelter — the square where the enemy side's
    /// return lines suffer the most cover — ranking above closeness, because once a
    /// square delivers the attack, closing further buys a ranged creature nothing.
    /// </remarks>
    private static bool MoveTowards(Encounter encounter, Combatant actor, Combatant target)
    {
        var reach = ReachOf(actor);
        var others = OthersThan(encounter, actor);

        var currentDistance = actor.Position.DistanceFeetTo(target.Position);
        var canAttackFromHere = currentDistance <= reach
            && CoverRules.Between(encounter.Battlefield, actor.Position, target.Position, others)
                != CoverDegree.Total;

        var best = ScoreSquares(encounter, actor, target, others, reach)
            .Where(option =>
                option.Distance < currentDistance
                || (option.CanAttackFrom && !canAttackFromHere))
            .OrderByDescending(option => option.CanAttackFrom)
            .ThenBy(option => option.InCloseCombat)
            // The walk's own cost ranks above the shot's: an Opportunity Attack taken
            // is worth more than a +2 on the target's AC avoided.
            .ThenBy(option => option.ProvokedDamage)
            .ThenBy(option => option.OwnShotPenalty)
            .ThenByDescending(option => option.Shelter)
            .ThenBy(option => option.Distance)
            .ThenBy(option => option.Square.X)
            .ThenBy(option => option.Square.Y)
            .FirstOrDefault();

        // Whether the actor actually went anywhere is the caller's stalemate signal:
        // a turn that neither attacked nor moved is stuck, and a refused move is not
        // a move.
        return best is not null && encounter.Move(best.Square) is null;
    }

    /// <summary>
    /// Steps to a cheaper firing square before the attack is attempted, when the shot
    /// from here is legal but penalized.
    /// </summary>
    /// <remarks>
    /// Without this, cover the actor could shoot through was cover it always shot
    /// through: an attack through Half Cover succeeds, so the turn never reached the
    /// movement that would have cleared the line — the exact behaviour #108 was
    /// deferred over, an archer firing forever past the ally it spawned behind. Only a
    /// square that strictly improves the shot is worth the walk, so a penalized shot
    /// with nothing better stays where it is rather than drifting.
    /// </remarks>
    private static void ImproveFiringPosition(Encounter encounter, Combatant actor, Combatant target)
    {
        var reach = ReachOf(actor);
        var others = OthersThan(encounter, actor);

        var currentCover = CoverRules.Between(
            encounter.Battlefield, actor.Position, target.Position, others);
        var currentPenalty = CoverRules.Bonus(currentCover);

        // Out of reach or fully blocked is MoveTowards' problem; a clean shot needs no
        // improving.
        if (actor.Position.DistanceFeetTo(target.Position) > reach
            || currentCover == CoverDegree.Total
            || currentPenalty == 0)
        {
            return;
        }

        var best = ScoreSquares(encounter, actor, target, others, reach)
            .Where(option => option.CanAttackFrom && option.OwnShotPenalty < currentPenalty)
            // A sidestep is free by definition; a sidestep that eats an Opportunity
            // Attack is not a sidestep. The improvement this method buys is at most a
            // few points of AC, a provoked swing costs more, and the shot from here is
            // legal — so a provoking candidate is refused outright rather than merely
            // deprioritised. This is the veto for the watched bug: a caster walking
            // out of two enemies' reach to clear an ally's Half Cover.
            .Where(option => option.ProvokedDamage == 0)
            .OrderBy(option => option.InCloseCombat)
            .ThenBy(option => option.OwnShotPenalty)
            .ThenByDescending(option => option.Shelter)
            .ThenBy(option => option.Distance)
            .ThenBy(option => option.Square.X)
            .ThenBy(option => option.Square.Y)
            .FirstOrDefault();

        if (best is not null)
        {
            encounter.Move(best.Square);
        }
    }

    /// <summary>One reachable square, scored for choosing a firing position.</summary>
    private sealed record FiringOption(
        GridPosition Square,
        int Distance,
        bool CanAttackFrom,
        bool InCloseCombat,
        double ProvokedDamage,
        int OwnShotPenalty,
        int Shelter);

    /// <summary>Every square this turn's movement reaches, scored.</summary>
    private static IEnumerable<FiringOption> ScoreSquares(
        Encounter encounter,
        Combatant actor,
        Combatant target,
        IReadOnlyList<Combatant> others,
        int reach)
    {
        var field = encounter.Battlefield;
        var enemies = others
            .Where(enemy => enemy.SideId != actor.SideId && enemy.IsActive)
            .ToArray();

        // Only a shooter cares about standing beside an enemy — the printed Ranged
        // Attacks in Close Combat rule the engine enforces. A melee creature's firing
        // squares are all beside its target by definition, so the flag would tie
        // everywhere and decide nothing.
        var shoots = reach > Battlefield.FeetPerSquare;

        return field.AllSquares()
            .Select(square => (Square: square, Path: MovementRules.FindPath(
                field,
                actor,
                square,
                actor.Turn.MovementFeet,
                encounter.Combatants)))
            .Where(candidate => candidate.Path is not null)
            .Select(candidate =>
            {
                var (square, path) = candidate;
                var targetCover = CoverRules.Between(field, square, target.Position, others);

                return new FiringOption(
                    square,
                    square.DistanceFeetTo(target.Position),
                    square.DistanceFeetTo(target.Position) <= reach
                        && targetCover != CoverDegree.Total,
                    shoots && enemies.Any(enemy =>
                        square.DistanceFeetTo(enemy.Position) <= Battlefield.FeetPerSquare),
                    ProvokedDamageAlong(encounter, actor, path!),
                    CoverRules.Bonus(targetCover),
                    enemies.Sum(enemy => ShelterValue(
                        CoverRules.Between(field, enemy.Position, square, others))));
            });
    }

    /// <summary>
    /// The Opportunity-Attack damage a walk along this path can expect: each distinct
    /// enemy whose reach the path leaves, once — a Reaction is one per round — costed at
    /// its hardest melee attack's average.
    /// </summary>
    /// <remarks>
    /// Two stated simplifications. The path judged is the pathfinder's own cheapest one
    /// for the destination — an equally-cheap safer path to the same square would go
    /// unnoticed, and making <c>FindPath</c> itself provocation-aware is a further slice
    /// if the difference ever shows. And the cost is the raw damage average rather than
    /// hit-chance-weighted — the same simplification the attack chooser makes, and
    /// enough to order squares by. What it fixes is real and was watched happening: a
    /// caster walking out of two enemies' reach to improve a shot, eating both swings,
    /// because nothing in the scoring knew the swings existed.
    /// </remarks>
    private static double ProvokedDamageAlong(Encounter encounter, Combatant actor, MovementPath path)
    {
        var provoked = new HashSet<string>(StringComparer.Ordinal);
        var total = 0.0;
        var from = actor.Position;

        foreach (var step in path.Steps)
        {
            foreach (var enemy in MovementRules.FindOpportunityAttackers(actor, from, step, encounter.Combatants))
            {
                if (provoked.Add(enemy.Id))
                {
                    total += enemy.Stats.Attacks
                        .Where(attack => attack.Kind == AttackKind.Melee)
                        .Select(attack => attack.Damage.Sum(damage => damage.Amount.Average))
                        .DefaultIfEmpty(0)
                        .Max();
                }
            }

            from = step;
        }

        return total;
    }

    /// <summary>
    /// How far the actor can deliver an attack from, counting only attacks it could
    /// actually swing — TryAttack's own filter — so a creature whose thrown Rock is
    /// spent plans like the melee creature it now is rather than standing off at a
    /// range it can no longer use.
    /// </summary>
    private static int ReachOf(Combatant actor)
    {
        var usable = actor.Stats.Attacks
            .Where(attack => actor.Uses.IsAvailable(attack.Name))
            .Where(attack => actor.Stats.AllowsInMultiattack(attack.Name))
            .ToArray();

        // The reach of the attack this creature would actually *make*, not the longest
        // one it owns — ordered exactly as TryAttack orders them, hardest-hitting first,
        // so the walk plans to arrive where the swing it intends can land.
        //
        // Taking the maximum instead was a latent bug for as long as no melee character
        // carried a thrown weapon, and it surfaced the moment the pregens were equipped
        // from the printed starting kits (a Fighter's Javelins, a Barbarian's Handaxes).
        // A Javelin reaches 120 feet at long range and the sides start 30 apart, so
        // every front-liner counted itself "already in reach" from its spawn square,
        // never closed, and spent the fight lobbing 1d6+3 at Disadvantage instead of
        // walking in behind a Greataxe. Measured: full clears fell from 38 of 120 to 2.
        //
        // Ties break toward the longer reach, which keeps a genuine archer at range —
        // the Rogue's Shortsword and Shortbow average the same, and she should still
        // shoot.
        return usable.Length > 0
            ? usable
                .OrderByDescending(attack => attack.Damage.Sum(damage => damage.Amount.Average))
                .ThenByDescending(attack => attack.MaximumRangeFeet)
                .First()
                .MaximumRangeFeet
            : MovementRules.MeleeReachFeet(actor);
    }

    /// <summary>
    /// Everyone but the actor: it is evaluating squares it would occupy, and its own
    /// body at the square it is leaving must not shelter or obstruct the plan.
    /// </summary>
    private static IReadOnlyList<Combatant> OthersThan(Encounter encounter, Combatant actor) =>
        encounter.Combatants.Where(other => other.Id != actor.Id).ToArray();

    /// <summary>
    /// What standing behind this much cover is worth when choosing a square. The printed
    /// bonuses for the graded degrees, and better than both for Total, which cannot be
    /// shot through at all.
    /// </summary>
    private static int ShelterValue(CoverDegree degree) => degree switch
    {
        CoverDegree.Total => 7,
        _ => CoverRules.Bonus(degree),
    };
}
