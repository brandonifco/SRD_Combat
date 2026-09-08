using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>
/// The Hide action, its shared prerequisite and roll (also reached through Cunning
/// Action, in <c>Encounter.Features.cs</c>), and the reveal that ends it.
/// </summary>
/// <remarks>
/// <para>
/// <b>SRD 5.2.1 p.183, Hide [Action]:</b> "you must succeed on a DC 15 Dexterity
/// (Stealth) check while you're Heavily Obscured or behind Three-Quarters Cover or
/// Total Cover, and you must be out of any enemy's line of sight... On a successful
/// check, you have the Invisible condition while hidden. Make note of your check's
/// total, which is the DC for a creature to find you with a Wisdom (Perception) check.
/// You stop being hidden immediately after any of the following occurs: you make a
/// sound louder than a whisper, an enemy finds you, you make an attack roll, or you
/// cast a spell with a Verbal component."
/// </para>
/// <para>
/// <b>R1 (adopted, #673's designer reading): Hide's prerequisite collapses to "no
/// qualifying enemy viewer can see the hider" under <see cref="VisionRules"/>.</b> The
/// printed prerequisite is <c>Stealth ≥ 15</c> AND <c>(Heavily Obscured OR
/// Three-Quarters Cover OR Total Cover)</c> AND <c>out of every enemy's line of
/// sight</c> — and against the only sight predicate this engine owns
/// (<see cref="VisionRules"/>, an unblocked centre-to-centre line, #671), two of those
/// three disjuncts are inert:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Heavily Obscured is inert</b> because the engine has no light or obscurement
/// model at all — no square is ever Heavily Obscured, the same world-assumption
/// <c>VisionRules</c> and <c>PartyVision</c> already state (every battlefield is Bright
/// Light).
/// </item>
/// <item>
/// <b>Three-Quarters Cover is also inert, and this is the substantive reading.</b>
/// Three-Quarters here is two low obstacles crossed by a line that is <em>not</em>
/// blocked (<c>CoverRules</c>) — so an enemy with Three-Quarters cover on the hider
/// still has line of sight to them by the only definition this engine owns. "Behind
/// Three-Quarters Cover from E" and "out of E's line of sight" are therefore
/// contradictory here, and the conjunction reduces to the Total-Cover branch alone.
/// Print evidently intends a second sight tier ("they can see the arrow slit, not you")
/// this engine's model does not have; giving it one is R2, filed only if Brandon's feel
/// verdict asks for it — not this slice.
/// </item>
/// </list>
/// <para>
/// So Hide's whole prerequisite is: <b>no enemy viewer can see any square of the
/// hider's space</b> — <see cref="CheckHidePrerequisites"/>, which is
/// <see cref="VisionRules.CanSee(Battlefield, Combatant, Combatant)"/> asked of every
/// enemy toward the hider. Consequences worth stating alongside the reading: a Blinded
/// or Unconscious enemy does not deny the hide (its eyes are closed, and the predicate
/// already says so); a Stunned or Paralyzed enemy <em>does</em> deny it (eyes open — the
/// predicate qualifies viewers by sight, not by ability to act); creatures never block
/// sight (<c>CoverRules</c>: crowds are not walls, so you cannot hide behind an ally);
/// and "if you can see a creature, you can discern whether it can see you" is satisfied
/// by construction, because the predicate is symmetric.
/// </para>
/// <para>
/// <b>Hide while already hidden is refused</b>, <c>hide.already_hidden</c>: print is
/// silent, but "you try to hide yourself" has nothing left to try, and refusing keeps
/// the state one-shot rather than a DC-fishing loop. Refused whenever the hider already
/// carries <em>any</em> Invisible — from Hide or from a spell — rather than only a
/// Hide-conferred one: <c>Combatant.AddCondition</c>'s merge-on-reapply path does not
/// carry <see cref="ActiveCondition.FindDifficultyClass"/> across, so a second Hide
/// while a spell's Invisible is already running would silently drop the new find-DC
/// rather than layer it on. Refusing the attempt outright keeps that gap out of reach
/// rather than adding an untested merge path to close it.
/// </para>
/// <para>
/// <b>The four ending triggers, and where each one lives:</b> "a sound louder than a
/// whisper" is inert — nothing in this engine produces a sound — and is named rather
/// than silently missing, because print names it and no rule the engine executes makes
/// one; "an enemy finds you" is #674's Search, and <see cref="RevealHidden"/> is the
/// removal it will call; "you make an attack roll" is hooked in
/// <c>Encounter.ResolveAttack</c>, after the roll resolves rather than before — p.14:
/// "you give away your location when the attack hits or misses" — so a first swing of a
/// pair keeps its Advantage and a second rolls normally; "you cast a spell with a
/// Verbal component" is hooked in <c>Encounter.CastSpell</c>, gated on
/// <see cref="SpellComponents.Verbal"/>. Moving into the open does not end hidden — the
/// list above is exhaustive, and staying hidden while you move is the entire point of
/// the action.
/// </para>
/// </remarks>
public sealed partial class Encounter
{
    /// <summary>The Hide action's flat DC, printed on the action itself.</summary>
    private const int HideDifficultyClass = 15;

    /// <summary>The Hide action.</summary>
    public ActionRefusal? Hide()
    {
        if (!TryGetActingCombatant(out var combatant, out var refusal))
        {
            return refusal;
        }

        if (!combatant.Turn.HasAction)
        {
            return new ActionRefusal("action.spent", $"{combatant.Name} has already used its action.");
        }

        if (CheckHidePrerequisites(combatant) is { } prerequisiteRefusal)
        {
            return prerequisiteRefusal;
        }

        combatant.Turn.SpendAction();
        PerformHide(combatant);
        return null;
    }

    /// <summary>
    /// Hide's own two refusals — <c>hide.already_hidden</c> and <c>hide.in_sight</c> —
    /// checked and returned without spending anything, so both the Action and Cunning
    /// Action forms (<c>Encounter.CunningAction</c>) read the same reading and the same
    /// wording. See this file's own remarks for the R1 collapse this enforces.
    /// </summary>
    private ActionRefusal? CheckHidePrerequisites(Combatant hider)
    {
        if (hider.HasCondition(ConditionType.Invisible))
        {
            return new ActionRefusal("hide.already_hidden", $"{hider.Name} is already hidden.");
        }

        // Every enemy viewer, not just active ones: a Stunned or Paralyzed enemy's eyes
        // are open even though it cannot act, and R1 qualifies viewers by sight, not by
        // ability to act. VisionRules.CanSee already disqualifies a dead, Unconscious or
        // Blinded viewer (Blindsight in range excepted) on its own.
        var seenBy = _combatants.FirstOrDefault(enemy =>
            enemy.SideId != hider.SideId && VisionRules.CanSee(Battlefield, enemy, hider));

        return seenBy is not null
            ? new ActionRefusal(
                "hide.in_sight",
                $"{hider.Name} is in {seenBy.Name}'s sight and cannot hide.")
            : null;
    }

    /// <summary>
    /// Rolls Hide's Dexterity (Stealth) check and applies the Invisible condition on
    /// success. Shared by the Action and Cunning Action forms once each has spent its
    /// own resource and passed <see cref="CheckHidePrerequisites"/>.
    /// </summary>
    private void PerformHide(Combatant hider)
    {
        var stealth = SkillRules.BonusFor(hider, "Stealth");

        // Poisoned and Frightened through the shared ability-check seam (#672), plus
        // worn armour's own Stealth-specific Disadvantage (p.92) — a Stealth-only
        // clause, so it is combined here at the call site rather than folded into
        // AbilityCheckMode itself (#672's addendum).
        var mode = D20Test.Combine(
            ConditionRules.AbilityCheckMode(hider, Battlefield, _combatants),
            hasAdvantage: false,
            hasDisadvantage: hider.Stats.StealthDisadvantageFromArmor);

        var roll = D20Test.Roll(_random, stealth, mode);
        var hidden = roll.Total >= HideDifficultyClass;
        var total = roll.Total;

        Add(
            CombatStepKind.Condition,
            $"{hider.Name} tries to hide: {roll} vs DC {HideDifficultyClass} — " +
            (hidden ? $"hidden (find DC {total})." : "seen."),
            hider);

        // Tactical Mind turns a failed ability check around — the same seam Escape's
        // grapple check uses, and this is the second call site the feature's own
        // remarks ask for.
        if (!hidden && TryTacticalMind(hider, roll.Total, HideDifficultyClass, out var boostedTotal))
        {
            hidden = true;
            total = boostedTotal;
        }

        if (hidden)
        {
            hider.AddCondition(new ActiveCondition(
                ConditionType.Invisible,
                SourceId: hider.Id,
                FindDifficultyClass: total));
        }
    }

    /// <summary>
    /// Ends a hider's own Hide-conferred Invisible instance — the one carrying a
    /// <see cref="ActiveCondition.FindDifficultyClass"/> — regardless of which of the
    /// four printed triggers fired. A no-op for a creature that is not (Hide-)hidden,
    /// and never touches a spell's Invisible, which carries no find-DC and ends however
    /// that spell says.
    /// </summary>
    private bool EndHiding(Combatant hider)
    {
        if (hider.ConditionState(ConditionType.Invisible) is not { FindDifficultyClass: not null })
        {
            return false;
        }

        return hider.RemoveCondition(ConditionType.Invisible);
    }

    /// <summary>
    /// Ends a hidden creature's Invisible instance because <paramref name="by"/> found
    /// it — Hide's third printed trigger, "an enemy finds you" (p.183). No caller ships
    /// in this slice; #674's Search action is the first. Returns false, narrating
    /// nothing, when <paramref name="target"/> was not (Hide-)hidden to begin with.
    /// </summary>
    public bool RevealHidden(Combatant target, Combatant by)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(by);

        if (!EndHiding(target))
        {
            return false;
        }

        Add(CombatStepKind.Condition, $"{by.Name} finds {target.Name} — no longer hidden.", target, by);
        return true;
    }

    /// <summary>
    /// Ends the attacking hider's own Invisible instance after an attack roll resolves —
    /// Hide's fourth printed trigger, "you make an attack roll" — narrated only when it
    /// actually fires. Called from <c>ResolveAttack</c> for every attack roll: the
    /// Attack action, each swing of Extra Attack or Multiattack, Opportunity Attacks,
    /// and spell attacks all funnel through there.
    /// </summary>
    private void RevealAfterOwnAttackRoll(Combatant attacker)
    {
        if (EndHiding(attacker))
        {
            Add(CombatStepKind.Condition, $"{attacker.Name} is no longer hidden.", attacker);
        }
    }
}
