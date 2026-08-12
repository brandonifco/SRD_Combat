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

        // A caster whose weapon cannot reach still has something to do.
        if (TryCastDamagingSpell(encounter, actor, target))
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

        // Healing Word is a Bonus Action, so getting someone up can cost nothing but the
        // slot; Cure Wounds is an Action and is only worth it if nobody can be reached
        // the cheap way.
        if (actor.Turn.HasBonusAction && TryHealFallenAlly(encounter, actor, SpellCastingTime.BonusAction))
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

        var spells = character.Spells
            .Where(spell => spell.Damage.Count > 0)
            .Where(spell => spell.CastingTime == SpellCastingTime.Action)
            .Where(spell => spell.RangeFeet is null || spell.RangeFeet >= distance)
            .Where(spell => spell.Save is not { Area: not null } || SpellAreaIsSafe(encounter, actor, target, spell))
            .OrderByDescending(spell => spell.Damage.Sum(damage => damage.Amount.Average))
            .ThenBy(spell => spell.Id, StringComparer.Ordinal);

        return spells.Any(spell => encounter.CastSpell(spell.Id, target) is null);
    }

    /// <summary>Whether a spell's area can be aimed at the target without catching a friend.</summary>
    private static bool SpellAreaIsSafe(Encounter encounter, Combatant actor, Combatant target, SpellDefinition spell)
    {
        if (spell.Save?.Area is not { } area || !AreaTargeting.CanResolve(area.Shape))
        {
            return false;
        }

        var covered = AreaTargeting.Cover(area, actor.Position, target.Position, encounter.Battlefield).ToHashSet();

        return !encounter.Combatants.Any(combatant =>
            combatant.IsActive && combatant.SideId == actor.SideId && covered.Contains(combatant.Position));
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
    /// the creature can actually attack from.
    /// </summary>
    private static void MoveTowards(Encounter encounter, Combatant actor, Combatant target)
    {
        var reach = actor.Stats.Attacks.Count > 0
            ? actor.Stats.Attacks.Max(attack => attack.MaximumRangeFeet)
            : MovementRules.MeleeReachFeet(actor);

        var candidates = encounter.Battlefield.AllSquares()
            .Where(square => MovementRules.FindPath(
                encounter.Battlefield,
                actor,
                square,
                actor.Turn.MovementFeet,
                encounter.Combatants) is not null)
            .Select(square => new
            {
                Square = square,
                Distance = square.DistanceFeetTo(target.Position),
            })
            .Where(option => option.Distance < actor.Position.DistanceFeetTo(target.Position))
            .OrderBy(option => option.Distance > reach ? 1 : 0)
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
}
