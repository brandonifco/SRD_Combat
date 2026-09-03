using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>What a pending action is asking the player to point at.</summary>
public enum TargetKind
{
    /// <summary>An enemy, for a named attack.</summary>
    Attack,

    /// <summary>Whatever a spell may be aimed at — which side depends on the spell.</summary>
    Spell,

    /// <summary>An ally within reach who could drink a potion.</summary>
    Potion,

    /// <summary>An ally within reach who could receive a traded item.</summary>
    Trade,

    /// <summary>An ally, for Divine Spark's healing half.</summary>
    SparkHeal,

    /// <summary>An enemy, for Divine Spark's harming half.</summary>
    SparkHarm,
}

/// <summary>
/// Who an armed action could sensibly be pointed at, nearest first.
/// </summary>
/// <remarks>
/// <para>
/// <b>A convenience, not a rule</b> — the same standing as <see cref="AttackChoice"/>.
/// The engine still decides every one of these: it refuses an attack out of range with
/// <c>attack.out_of_range</c>, a spell beyond its range with its own code, a potion to
/// somebody too far off. What this does is spare the player pointing at things that were
/// never going to work, and give the keyboard somewhere sensible to start.
/// </para>
/// <para>
/// <b>Every predicate below reads the engine's own numbers rather than restating a
/// rule.</b> An attack's reach is <c>CombatAttack.CanReach</c> — the very method
/// <c>Encounter.Attack</c> refuses on — and a spell's is
/// <c>SpellDefinition.TargetRangeFeet</c>, which already reads Touch as five feet and
/// leaves Self null. Where a judgement is unavoidable it is stated rather than hidden,
/// and it is always the *generous* one, because a candidate the engine then refuses
/// costs a player a refusal message, while a candidate wrongly omitted costs them a move
/// they were entitled to and never saw offered.
/// </para>
/// <para>
/// It lives here rather than in a client for the reason <c>TurnBanner</c> does: two
/// clients deciding separately what may be pointed at would be two places to drift.
/// </para>
/// </remarks>
public static class TargetChoice
{
    /// <summary>
    /// The creatures this armed action could be pointed at, ordered nearest first.
    /// </summary>
    /// <param name="encounter">The fight.</param>
    /// <param name="actor">Whoever is acting.</param>
    /// <param name="kind">What is armed.</param>
    /// <param name="attack">The named attack, for <see cref="TargetKind.Attack"/>.</param>
    /// <param name="spell">The chosen spell, for <see cref="TargetKind.Spell"/>.</param>
    public static IReadOnlyList<Combatant> For(
        Encounter encounter,
        Combatant actor,
        TargetKind kind,
        CombatAttack? attack = null,
        SpellDefinition? spell = null)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(actor);

        return encounter.Combatants
            .Where(candidate => IsCandidate(encounter, actor, candidate, kind, attack, spell))
            .OrderBy(candidate => actor.DistanceFeetTo(candidate))
            // Ties break on identifier so the order is the same every time the same
            // action is armed — cycling that reshuffled itself would be unusable.
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The next target after <paramref name="current"/>, wrapping round; the first when
    /// nothing is selected yet.
    /// </summary>
    public static Combatant? Next(IReadOnlyList<Combatant> targets, string? current)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Count == 0)
        {
            return null;
        }

        var index = current is null
            ? -1
            : IndexOf(targets, current);

        return targets[(index + 1) % targets.Count];
    }

    private static int IndexOf(IReadOnlyList<Combatant> targets, string id)
    {
        for (var index = 0; index < targets.Count; index++)
        {
            if (string.Equals(targets[index].Id, id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsCandidate(
        Encounter encounter,
        Combatant actor,
        Combatant candidate,
        TargetKind kind,
        CombatAttack? attack,
        SpellDefinition? spell)
    {
        // The dead are nobody's target: an attack on a corpse is refused, and a potion
        // poured into one is the exact waste the engine guards against.
        if (candidate.IsDead)
        {
            return false;
        }

        var distance = actor.DistanceFeetTo(candidate);
        var enemy = candidate.SideId != actor.SideId;

        return kind switch
        {
            TargetKind.Attack => enemy && (attack is null || attack.CanReach(distance)),

            TargetKind.SparkHarm => enemy && distance <= DivineSparkRangeFeet,

            // The healing half is for allies, and the actor may spark themselves.
            TargetKind.SparkHeal => !enemy && distance <= DivineSparkRangeFeet,

            // Administering needs reach; drinking your own needs nothing. Either way the
            // flask must exist — the drinker's own first, the actor's pack second, which
            // is the order the engine spends them in.
            TargetKind.Potion =>
                !enemy
                && (ReferenceEquals(candidate, actor) || distance <= PotionRules.ReachFeet)
                && (candidate.Inventory.TotalPotions > 0 || actor.Inventory.TotalPotions > 0),

            // Trading needs only reach and a living ally other than the actor — what the
            // recipient already carries plays no part, unlike Potion's candidate list,
            // because the point is redistributing what the actor already has rather than
            // finding a flask that exists somewhere between the two packs.
            TargetKind.Trade =>
                !enemy && !ReferenceEquals(candidate, actor) && distance <= PotionRules.ReachFeet,

            // A spell's own printed range, and its own shape deciding which side it
            // wants. Self-range spells have no target but the caster.
            TargetKind.Spell => spell is null
                || (spell.IsSelfRanged
                    ? ReferenceEquals(candidate, actor)
                    : (spell.TargetRangeFeet is not { } range || distance <= range)
                        && WantsSide(spell, enemy)),

            _ => false,
        };
    }

    /// <summary>
    /// Whether a spell wants this side, read from the spell's own shape.
    /// </summary>
    /// <remarks>
    /// <b>Found by casting Guiding Bolt.</b> Range alone made every ally a candidate and
    /// the caster the nearest of them — distance zero — so arming a spell attack left the
    /// cursor exactly where it started and looked like no default at all. A spell's
    /// mechanics say who it is for without any guessing: one that resolves through an
    /// attack roll or a saving throw is aimed at an enemy, and one that restores hit
    /// points at a friend. Anything else stays generous and offers both, since an
    /// unrecognised shape is a reason to offer more rather than less.
    /// <para>
    /// This narrows the *ring* — where the cursor starts and what Tab walks — and narrows
    /// nothing else. A click still reaches anyone on the board, and the engine still
    /// rules on it; a Fireball dropped on your own front line remains a decision the
    /// player is allowed to make.
    /// </para>
    /// </remarks>
    private static bool WantsSide(SpellDefinition spell, bool enemy) => spell.Mechanics switch
    {
        EntryMechanics.Attack or EntryMechanics.SavingThrow => enemy,
        EntryMechanics.Healing => !enemy,
        _ => true,
    };

    /// <summary>Divine Spark's printed range, restated here only to filter candidates.</summary>
    private const int DivineSparkRangeFeet = 30;
}
