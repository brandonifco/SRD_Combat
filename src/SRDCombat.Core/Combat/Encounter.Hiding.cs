using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Combat;

/// <summary>
/// The Hide action, its shared prerequisite and roll (also reached through Cunning
/// Action, in <c>Encounter.Features.cs</c>); the reveal that ends it; and the Search
/// action (#674) that is the counterplay — the one caller of <see cref="RevealHidden"/>
/// this slice adds.
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
/// one; "an enemy finds you" is <see cref="Search"/>'s successful Wisdom (Perception)
/// check, which calls <see cref="RevealHidden"/> below; "you make an attack roll" is
/// hooked in <c>Encounter.ResolveAttack</c>, after the roll resolves rather than
/// before — p.14: "you give away your location when the attack hits or misses" — so a
/// first swing of a pair keeps its Advantage and a second rolls normally; "you cast a
/// spell with a Verbal component" is hooked in <c>Encounter.CastSpell</c>, gated on
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
    /// it — Hide's third printed trigger, "an enemy finds you" (p.183). Called by
    /// <see cref="Search"/> on a successful Wisdom (Perception) check. Returns false,
    /// narrating nothing, when <paramref name="target"/> was not (Hide-)hidden to begin
    /// with.
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
    /// The Search action, aimed at one creature suspected to be hidden: a Wisdom
    /// (Perception) check against that creature's recorded
    /// <see cref="ActiveCondition.FindDifficultyClass"/>, revealing it on success.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>SRD 5.2.1 p.187, Search [Action]:</b> "When you take the Search action, you
    /// make a Wisdom check to discern something that isn't obvious. The Search table
    /// suggests which skills are applicable" — the table's row for "Concealed creature
    /// or object" names Perception, and Hide (p.183) gives the found creature's DC as
    /// "the DC for a creature to find you with a Wisdom (Perception) check."
    /// </para>
    /// <para>
    /// <b>Search takes an explicit target, the same reading #673 already settled for
    /// Attack.</b> Print's Search is a general "look for something"; this engine's
    /// actions are all targeted (<see cref="Attack"/>, <c>CastSpell</c>,
    /// <c>DivineSpark</c>), and #673's own reading of "Attacks Affected" already treats
    /// an attack aimed at an Invisible creature as legal — "the attacker is read as
    /// knowing where the target is and pays Disadvantage." Search follows the same
    /// shape: <paramref name="target"/> names who the searcher is trying to find, and
    /// the roll (not knowledge of the target's square) decides whether that guess pays
    /// off. A target the caller cannot actually see is a client/AI honesty question
    /// (#673-AI, #314), not an engine refusal — the engine's job, as with Attack, is the
    /// check and its refusals, not omniscience policing.
    /// </para>
    /// <para>
    /// <b>Passive Perception does NOT auto-find — the settled #673 reading.</b> Print
    /// gives the find to "a Wisdom (Perception) check", and Passive Perception (p.22) is
    /// the score used "when you're not actively looking for something"; in a fight the
    /// searcher <em>is</em> looking, and the active look is this action. So a hidden
    /// creature never loses Invisible from a bystander's standing Perception score, no
    /// matter how high — only a spent Search action can find it. This action computes no
    /// passive score and nothing else in the engine reads one against
    /// <see cref="ActiveCondition.FindDifficultyClass"/>.
    /// </para>
    /// <para>
    /// <b>Only an enemy may Search someone hidden</b> — print's third ending trigger is
    /// specifically "<em>an enemy</em> finds you" (p.183), not any creature, so an ally
    /// (or the hider itself) Searching does nothing to end Hide. Refused with
    /// <c>search.not_enemy</c>, checked before <c>search.not_hidden</c> so a same-side
    /// target gets the honest reason even while genuinely hidden.
    /// </para>
    /// <para>
    /// Refused with <c>search.not_hidden</c> when <paramref name="target"/> carries no
    /// Hide-conferred Invisible (never spent, already found, or a spell's Invisible,
    /// which carries no find-DC and is untouched here — same scope as
    /// <see cref="EndHiding"/>) — this is "there is no hidden creature to find" for a
    /// name search names. Poisoned and Frightened hamper the check through the same
    /// <see cref="ConditionRules.AbilityCheckMode"/> Hide and Escape use, and a failed
    /// check still offers Tactical Mind, the same as both.
    /// </para>
    /// </remarks>
    public ActionRefusal? Search(Combatant target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!TryGetActingCombatant(out var searcher, out var refusal))
        {
            return refusal;
        }

        if (searcher.SideId == target.SideId)
        {
            return new ActionRefusal(
                "search.not_enemy",
                $"{target.Name} is not an enemy of {searcher.Name}, and only an enemy finding you ends Hide.");
        }

        if (target.ConditionState(ConditionType.Invisible) is not { FindDifficultyClass: { } difficultyClass })
        {
            return new ActionRefusal(
                "search.not_hidden",
                $"{target.Name} is not hidden, so there is nothing to find.");
        }

        if (!searcher.Turn.HasAction)
        {
            return new ActionRefusal("action.spent", $"{searcher.Name} has already used its action.");
        }

        searcher.Turn.SpendAction();

        var perception = SkillRules.BonusFor(searcher, "Perception");
        var mode = ConditionRules.AbilityCheckMode(searcher, Battlefield, _combatants);
        var roll = D20Test.Roll(_random, perception, mode);
        var found = roll.Total >= difficultyClass;

        Add(
            CombatStepKind.Condition,
            $"{searcher.Name} searches for {target.Name}: {roll} vs DC {difficultyClass} — " +
            (found ? "found!" : "not found."),
            searcher,
            target);

        // Tactical Mind turns a failed ability check around — the same seam Escape's
        // grapple check and Hide's Stealth check use, and this is the third call site.
        if (!found)
        {
            found = TryTacticalMind(searcher, roll.Total, difficultyClass);
        }

        if (found)
        {
            RevealHidden(target, searcher);
        }

        return null;
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
