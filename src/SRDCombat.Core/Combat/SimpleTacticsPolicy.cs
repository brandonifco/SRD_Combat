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

        var target = NearestEnemy(encounter, actor);

        if (target is null)
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

        // Attack from where we stand if anything reaches.
        if (TryAttack(encounter, actor, target))
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

        MoveTowards(encounter, actor, target);

        // The move may have provoked an Opportunity Attack that dropped us, or ended the
        // fight outright, so re-check before swinging.
        if (encounter.IsComplete || !actor.CanAct)
        {
            encounter.EndTurn();
            return;
        }

        var closest = NearestEnemy(encounter, actor);

        if (closest is not null && TryAttack(encounter, actor, closest))
        {
            SpendRemainingAttacks(encounter, actor);
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
               && NearestEnemy(encounter, actor) is { } next
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

        // Healing Word is a Bonus Action, so getting someone up can cost nothing but the
        // slot; Cure Wounds is an Action and is only worth it if nobody can be reached
        // the cheap way.
        else if (actor.Turn.HasBonusAction && TryHealFallenAlly(encounter, actor, SpellCastingTime.BonusAction))
        {
            // Healed with the Bonus Action; the Action is still free to fight with.
        }
        else if (actor.Turn.HasAction && TryHealFallenAlly(encounter, actor, SpellCastingTime.Action))
        {
            return;
        }

        if (actor.Turn.HasBonusAction && ShouldRage(encounter, actor))
        {
            encounter.Rage();
        }

        if (actor.Turn.HasBonusAction && IsBadlyHurt(actor))
        {
            encounter.SecondWind();
        }

        // Last, because Second Wind is free and a potion is gone for the rest of the
        // run: drink only when badly hurt and nothing cheaper was available.
        if (actor.Turn.HasBonusAction
            && IsBadlyHurt(actor)
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
        if (actor.Inventory.Weakest is not { } potency)
        {
            return false;
        }

        var fallen = encounter.Combatants
            .Where(other => other.SideId == actor.SideId
                && !ReferenceEquals(other, actor)
                && !other.IsDead
                && other.CurrentHitPoints == 0)
            .OrderBy(other => actor.Position.DistanceFeetTo(other.Position))
            .FirstOrDefault();

        return fallen is not null && encounter.DrinkPotion(potency, fallen) is null;
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

    /// <summary>True when a character has lost more than half its hit points.</summary>
    private static bool IsBadlyHurt(Combatant actor) =>
        actor.CurrentHitPoints * 2 <= actor.Stats.MaximumHitPoints;

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
            .Where(spell => spell.Damage.Count > 0 || spell.Save?.FailureDamage.Count > 0)
            .Where(spell => spell.CastingTime == SpellCastingTime.Action)
            .Where(spell => spell.IsCantrip || !slotsAreSpokenFor)
            .Where(spell => spell.IsSelfRanged || spell.TargetRangeFeet is null || spell.TargetRangeFeet >= distance)
            .Select(spell => (Spell: spell, Value: SpellValue(encounter, actor, target, spell)))
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
            && (other.CurrentHitPoints == 0 || IsBadlyHurt(other)));

    /// <summary>Whether this caster has anything to spend a slot healing with.</summary>
    private static bool CanHeal(CombatantFeatures character) =>
        character.Spells.Any(spell => spell.Heal is not null && !spell.IsCantrip);

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

    /// <summary>Attacks with the hardest-hitting attack that can reach the target.</summary>
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
            .OrderByDescending(candidate => candidate.Damage.Sum(damage => damage.Amount.Average))
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
    /// firing position. Two consequences are deliberate. A square that does not close
    /// the distance is still taken when it turns a blocked attack into a possible one —
    /// the sidestep that clears a wall, without which an archer stood forever behind
    /// the one square it could not shoot past. And among squares it can attack from,
    /// the actor prefers the one where the enemy side's return lines suffer the most
    /// cover, shelter ranking above closeness because once a square delivers the attack,
    /// closing further buys a ranged creature nothing.
    /// </remarks>
    private static void MoveTowards(Encounter encounter, Combatant actor, Combatant target)
    {
        var reach = actor.Stats.Attacks.Count > 0
            ? actor.Stats.Attacks.Max(attack => attack.MaximumRangeFeet)
            : MovementRules.MeleeReachFeet(actor);

        var field = encounter.Battlefield;
        var enemies = encounter.Combatants
            .Where(enemy => enemy.SideId != actor.SideId && enemy.IsActive)
            .ToArray();

        var currentDistance = actor.Position.DistanceFeetTo(target.Position);
        var canAttackFromHere = currentDistance <= reach
            && CoverRules.Between(field, actor.Position, target.Position) != CoverDegree.Total;

        var candidates = field.AllSquares()
            .Where(square => MovementRules.FindPath(
                field,
                actor,
                square,
                actor.Turn.MovementFeet,
                encounter.Combatants) is not null)
            .Select(square => new
            {
                Square = square,
                Distance = square.DistanceFeetTo(target.Position),
                CanAttackFrom = square.DistanceFeetTo(target.Position) <= reach
                    && CoverRules.Between(field, square, target.Position) != CoverDegree.Total,
                Shelter = enemies.Sum(enemy => ShelterValue(
                    CoverRules.Between(field, enemy.Position, square))),
            })
            .Where(option =>
                option.Distance < currentDistance
                || (option.CanAttackFrom && !canAttackFromHere))
            .OrderByDescending(option => option.CanAttackFrom)
            .ThenByDescending(option => option.Shelter)
            .ThenBy(option => option.Distance)
            .ThenBy(option => option.Square.X)
            .ThenBy(option => option.Square.Y)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        encounter.Move(candidates[0].Square);
    }

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
