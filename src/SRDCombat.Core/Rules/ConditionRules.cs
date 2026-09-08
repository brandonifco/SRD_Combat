using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;

namespace SRDCombat.Core.Rules;

/// <summary>
/// Which conditions the engine really executes, and whether a rider printed on an attack
/// may be imposed.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Executable"/> is a curated allowlist, exactly like the extractor's inert
/// list and <c>ClassFeatureRegistry</c>: a condition belongs on it only when the engine
/// actually does what the condition says. Adding a name here without the code behind it
/// would put a condition on a creature that changes nothing — the quietest possible
/// failure, and the one this project is built to avoid.
/// </para>
/// <para>
/// Thirteen conditions are on it today.
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Prone</b> — <c>AttackRules</c> gives Advantage within 5 feet and Disadvantage
/// beyond, <c>Encounter.Move</c> refuses to move a Prone creature, and
/// <c>Encounter.StandUp</c> ends it for half the creature's Speed.
/// </item>
/// <item>
/// <b>Grappled</b> — Speed 0, and Disadvantage on attack rolls <em>against any target
/// other than the grappler</em>, which is why the condition remembers who imposed it.
/// <c>Encounter.Escape</c> is the Strength (Athletics) or Dexterity (Acrobatics) check
/// against the printed escape DC, and the grapple also ends on its own when the grappler
/// is Incapacitated or dead, or when the two are further apart than the grapple's range.
/// </item>
/// <item>
/// <b>Restrained</b> — Speed 0, Advantage on attack rolls against it, Disadvantage on its
/// own, and Disadvantage on its Dexterity saving throws. Implemented alongside Grappled
/// because the two share the immobility. The riders that reach it hang off "until the
/// grapple ends" — a duration that is no expiry at all but a tie to the sibling grapple,
/// imposed only while the same creature's grapple holds the target and swept away with
/// it by <c>Encounter.EndGrapple</c>. (The web-shaped Restrained riders — the Ettercap,
/// the Giant Spider — stay refused: "until the web is destroyed" needs an object with
/// hit points.)
/// </item>
/// <item>
/// <b>Poisoned</b> — Disadvantage on the creature's attack rolls, in
/// <c>AttackRules.ResolveRollMode</c>, and Disadvantage on its ability checks, in
/// <see cref="AbilityCheckMode"/>. The "revisit the moment an in-combat ability check
/// exists" note this entry used to carry is discharged: <c>Encounter.Escape</c> is that
/// check, and <see cref="AbilityCheckMode"/> is the one place its mode and Frightened's
/// are decided together, so the two conditions cannot disagree about what hampers a
/// roll.
/// </item>
/// <item>
/// <b>Incapacitated</b> — <c>Combatant.CanAct</c> is false, so the creature takes no
/// actions, and a Dodge it had running stops helping.
/// </item>
/// <item>
/// <b>Unconscious</b> — brings Incapacitated and Prone with it, auto-fails Strength and
/// Dexterity saving throws, and any hit from within 5 feet is a Critical Hit.
/// </item>
/// <item>
/// <b>Blinded</b> — Advantage on attack rolls against it, Disadvantage on its own, in
/// <c>AttackRules</c>; both printed unconditionally on the condition and untouched by
/// #672. Its "automatically fail any ability check that requires sight" stays complete
/// by vacancy even after #673 added a second in-combat ability check: Hide's Dexterity
/// (Stealth) check does not require sight either — hiding is done by feel and
/// stillness, not by watching anything, so a Blinded creature hides exactly as well as
/// a sighted one — and <see cref="AbilityCheckMode"/> does not gate on Blinded for
/// either check. (An earlier version of this note predicted Hide's check would be the
/// one to close the vacancy; the designer's own #673 reading corrected that once the
/// print was actually read.)
/// </item>
/// <item>
/// <b>Charmed</b> — cannot attack the charmer or target it with a damaging effect. The
/// printed clause heading is "Can't Harm the Charmer", so "damaging" is read as
/// qualifying both "abilities" and "magical effects": a non-damaging effect aimed at the
/// charmer is allowed. Attacks are refused outright, Opportunity Attacks included; the
/// charmer's "Advantage on any ability check to interact with you socially" has nothing
/// to apply to in a fight. Enforced in <c>Encounter</c>'s attack, entry and casting
/// paths, off the condition's <c>SourceId</c>.
/// </item>
/// <item>
/// <b>Frightened</b> — Disadvantage on attack rolls and ability checks "while the source
/// of fear is within line of sight" (#672: <see cref="FrightenedSourceInSight"/>, built
/// on <see cref="VisionRules.CanSee(Combat.Battlefield, Combat.Combatant,
/// Combat.Combatant)"/>), and no willing movement closer to the source, unconditionally
/// — print carries no sight qualifier on "Can't Approach", so that clause did not move.
/// A dead source is still in sight: the predicate qualifies the <em>viewer</em>
/// (Frightened's bearer), not the target, and print does not end Frightened on the
/// source's death, so a corpse on the field is seen like any other occupied square. A
/// null or unresolvable <c>SourceId</c>, and a caller offering no battlefield, both read
/// as in sight — the hampering default, and the direction the engine took wholesale
/// before this predicate existed. "Closer" is judged at the destination:
/// <c>Encounter.Move</c> refuses a destination nearer the source than the square the
/// creature stands in, and does not judge the path between them. A second, separate
/// consumer of the same predicate landed with #681: some printings of Frightened (the
/// Nalfeshnee's Horror Nimbus) end the condition outright — rather than merely
/// hampering it — at the end of the bearer's own turn if the source is out of sight,
/// via <see cref="Combat.ActiveCondition.EndsWhenBearerCannotSeeSource"/> and the same
/// <see cref="SourceInSight"/> the Disadvantage gate is built on.
/// </item>
/// <item>
/// <b>Paralyzed</b> — brings Incapacitated, Speed 0, auto-fails Strength and Dexterity
/// saving throws, Advantage on attack rolls against it, and any hit from within 5 feet
/// is a Critical Hit — the same clause Unconscious prints.
/// </item>
/// <item>
/// <b>Stunned</b> — brings Incapacitated, auto-fails Strength and Dexterity saving
/// throws, and Advantage on attack rolls against it. Note what it does not print: no
/// Speed 0 and no automatic Critical Hits — memory adds both, the glossary has neither.
/// </item>
/// <item>
/// <b>Petrified</b> — brings Incapacitated, Speed 0, Advantage on attack rolls against
/// it, auto-fails Strength and Dexterity saving throws (all printed page 186, and note
/// no Critical Hit clause — Paralyzed and Unconscious have one, stone does not),
/// <b>Resistance to all damage</b> in <c>DamageRules.Apply</c> under the printed order
/// and no-stacking rules (page 17), and Immunity to the Poisoned condition as a gate in
/// <c>Combatant.AddCondition</c>. The "Turned to Inanimate Substance" clause — weight,
/// aging, the stone itself — is read as narratively inert in a fight. It carries no
/// printed end, so it lasts <c>BeyondTheFight</c>: the encounter's end is the rescue,
/// which is the same reading every outlasting duration already gets.
/// </item>
/// <item>
/// <b>Invisible</b> (#673) — Concealed and Attacks Affected, both gated through
/// <c>VisionRules.CanSee</c>'s Invisible-defeat clause (Blindsight/Truesight in range)
/// rather than a fresh reading of their own: <c>AttackCircumstances.AttackerIsUnseenByTarget</c>
/// / <c>TargetIsUnseenByAttacker</c> give the Attacks Affected pair (folded together
/// with p.14's Unseen Attackers and Targets, which has nothing else left to gate once
/// Invisible exists), and <c>Encounter.Hide</c>'s <c>target.unseen</c> refusal is
/// Concealed's one hard-coded consumer (Divine Spark), reached wherever a definition
/// says its target must be seen. The Surprise clause is #675's. Every printed exception
/// — "if a creature can somehow see you" — is the same Blindsight/Truesight clause in
/// every case, never a special case per consumer. Hide (#673) is this condition's one
/// producer today: <c>ActiveCondition.FindDifficultyClass</c> marks the instance Hide
/// created, and only that instance ends on Hide's four printed triggers — a spell's
/// Invisible (Invisibility, Greater Invisibility) carries none and ends however that
/// spell says.
/// </item>
/// </list>
/// <para>
/// Everything else is deliberately absent, and the absence is the point. Deafened needs
/// a hearing model that does not exist, and nothing here executes it; the rider is
/// reported as not modelled rather than imposed as scenery.
/// </para>
/// </remarks>
public static class ConditionRules
{
    private static readonly HashSet<ConditionType> Executable =
    [
        ConditionType.Blinded,
        ConditionType.Charmed,
        ConditionType.Frightened,
        ConditionType.Grappled,
        ConditionType.Incapacitated,
        ConditionType.Invisible,
        ConditionType.Paralyzed,
        ConditionType.Petrified,
        ConditionType.Poisoned,
        ConditionType.Prone,
        ConditionType.Restrained,
        ConditionType.Stunned,
        ConditionType.Unconscious,
    ];

    /// <summary>
    /// Conditions that set a creature's Speed to 0. Paralyzed and Unconscious print the
    /// same clause, and are listed for fidelity even though their Incapacitated already
    /// stops the creature acting before Speed is consulted.
    /// </summary>
    private static readonly ConditionType[] SpeedZero =
    [
        ConditionType.Grappled,
        ConditionType.Restrained,
        ConditionType.Paralyzed,
        ConditionType.Petrified,
        ConditionType.Unconscious,
    ];

    /// <summary>
    /// Conditions printing "You automatically fail Strength and Dexterity saving throws"
    /// — Paralyzed, Stunned and Unconscious carry the clause word for word.
    /// </summary>
    private static readonly ConditionType[] AutoFailsStrengthAndDexteritySaves =
    [
        ConditionType.Paralyzed,
        ConditionType.Petrified,
        ConditionType.Stunned,
        ConditionType.Unconscious,
    ];

    /// <summary>True when the engine gives this condition its rules effects.</summary>
    public static bool IsExecutable(ConditionType condition) => Executable.Contains(condition);

    /// <summary>
    /// True when the creature's Speed is 0 and cannot increase.
    /// </summary>
    /// <remarks>
    /// Checked at the point of moving rather than baked into the turn's movement
    /// allowance, because a creature can be grappled part-way through its own turn and
    /// the SRD's "your Speed is 0 and can't increase" takes effect at once.
    /// </remarks>
    public static bool IsImmobile(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        return SpeedZero.Any(combatant.HasCondition);
    }

    /// <summary>The condition holding this creature still, for narrating a refusal.</summary>
    public static ConditionType? ImmobilisedBy(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        return SpeedZero.Cast<ConditionType?>().FirstOrDefault(condition => combatant.HasCondition(condition!.Value));
    }

    /// <summary>
    /// Whether a Dodging creature keeps Dodge's benefits — Disadvantage on attack rolls
    /// against it, Advantage on its Dexterity saving throws.
    /// </summary>
    /// <remarks>
    /// The printed exception is one sentence covering both halves: "You lose these
    /// benefits if you have the Incapacitated condition or if your Speed is 0" (glossary,
    /// Dodge). Speed 0 is read as the condition-borne kind (<see cref="IsImmobile"/> —
    /// Grappled, Restrained, Paralyzed, Unconscious), not a turn-scoped forfeit like
    /// Steady Aim's: Dodge's benefits run from the dodger's turn to the start of its
    /// next, which is exactly when a forfeit that ends "at the end of the current turn"
    /// has already expired. Both call sites — the attack roll and the saving throw —
    /// go through here so the exception cannot be half-applied.
    /// </remarks>
    public static bool RetainsDodgeBenefits(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        return !combatant.HasCondition(ConditionType.Incapacitated) && !IsImmobile(combatant);
    }

    /// <summary>
    /// Whether a Frightened bearer's source of fear is within its line of sight — the
    /// gate on both of Frightened's Disadvantage clauses (#672). Callers only reach this
    /// once they already know the bearer has Frightened; it answers "is the source in
    /// sight", not "is the bearer Frightened". A thin wrapper over
    /// <see cref="SourceInSight"/>, reading Frightened's own recorded <c>SourceId</c>.
    /// </summary>
    public static bool FrightenedSourceInSight(
        Combatant bearer,
        Battlefield? battlefield,
        IReadOnlyCollection<Combatant>? combatants)
    {
        ArgumentNullException.ThrowIfNull(bearer);

        return SourceInSight(
            bearer,
            bearer.ConditionState(ConditionType.Frightened)?.SourceId,
            battlefield,
            combatants);
    }

    /// <summary>
    /// Whether <paramref name="sourceId"/> is within <paramref name="bearer"/>'s line of
    /// sight — the shared predicate behind <see cref="FrightenedSourceInSight"/> (a
    /// hampering gate) and <see cref="Combat.ActiveCondition.EndsWhenBearerCannotSeeSource"/>
    /// (an ending gate, #681), so the two consumers of "within line of sight" can never
    /// disagree about what the phrase means.
    /// </summary>
    /// <remarks>
    /// Three cases all read as in sight, the hampering default, because each is a way
    /// of not knowing otherwise rather than a way of knowing the source is out of sight:
    /// a null <paramref name="sourceId"/>; a <paramref name="sourceId"/> that does not
    /// resolve to anyone in <paramref name="combatants"/> (left the field, or never
    /// matched); and a caller offering no <paramref name="battlefield"/> or no
    /// <paramref name="combatants"/> at all — the two-creature unit tests, which read
    /// exactly as they did before this predicate existed. A dead source still resolves
    /// and is still seen: <see cref="VisionRules.CanSee(Combat.Battlefield,
    /// Combat.Combatant, Combat.Combatant)"/> qualifies the viewer (the bearer), not the
    /// target, and neither Frightened's Disadvantage clause nor the Nalfeshnee's ending
    /// clause ends on the source's death.
    /// </remarks>
    public static bool SourceInSight(
        Combatant bearer,
        string? sourceId,
        Battlefield? battlefield,
        IReadOnlyCollection<Combatant>? combatants)
    {
        ArgumentNullException.ThrowIfNull(bearer);

        if (sourceId is null || battlefield is null || combatants is null)
        {
            return true;
        }

        var source = combatants.FirstOrDefault(
            candidate => string.Equals(candidate.Id, sourceId, StringComparison.Ordinal));

        return source is null || VisionRules.CanSee(battlefield, bearer, source);
    }

    /// <summary>
    /// The roll mode for an in-combat ability check — the one seam Escape, Hide (#673)
    /// and Search (#674) all call, so none of them can decide what hampers a check
    /// differently from the others. Poisoned hampers unconditionally; Frightened hampers
    /// only while its source is within sight (<see cref="FrightenedSourceInSight"/>).
    /// Neither stacks with the other — Advantage and Disadvantage never do — so this
    /// only ever needs to know whether either applies, not how many do.
    /// </summary>
    public static RollMode AbilityCheckMode(
        Combatant checker,
        Battlefield? battlefield,
        IReadOnlyCollection<Combatant>? combatants)
    {
        ArgumentNullException.ThrowIfNull(checker);

        var hampered = checker.HasCondition(ConditionType.Poisoned)
            || (checker.HasCondition(ConditionType.Frightened)
                && FrightenedSourceInSight(checker, battlefield, combatants));

        return hampered ? RollMode.Disadvantage : RollMode.Normal;
    }

    /// <summary>
    /// The condition making this creature automatically fail a saving throw with this
    /// ability, or null when the save is rolled normally. No die is consumed by an
    /// automatic failure — the printed clause replaces the roll rather than penalising it.
    /// </summary>
    public static ConditionType? AutoFailingSaveCondition(Combatant combatant, Ability ability)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        if (ability is not (Ability.Strength or Ability.Dexterity))
        {
            return null;
        }

        return AutoFailsStrengthAndDexteritySaves
            .Cast<ConditionType?>()
            .FirstOrDefault(condition => combatant.HasCondition(condition!.Value));
    }

    /// <summary>
    /// Whether a rider could ever be imposed, ignoring who it is aimed at: the model
    /// expresses everything printed with it, and the engine executes the condition.
    /// </summary>
    public static bool CanBeImposed(AppliedCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        // An escalating rider is only as executable as the condition it deepens into:
        // a Restrained the engine could impose whose second tier it could not would
        // hold its victim while quietly forgetting the worse half of the sentence.
        return condition.IsFullyModelled
            && IsExecutable(condition.Condition)
            && (condition.EscalatesTo is not { } deeper || IsExecutable(deeper));
    }

    /// <summary>
    /// Turns a printed duration into a concrete expiry against the right creature's turn
    /// counter.
    /// </summary>
    /// <remarks>
    /// The <c>+ TurnsAhead</c> is the whole of "next" — and of "for 1 minute", which is
    /// the same clock set ten turns out. A rider applied during the devil's own turn and
    /// one applied during somebody else's — on an Opportunity Attack — both read "until
    /// the start of the devil's next turn" and mean different moments; counting from the
    /// owner's turn count at the moment of application gets both right without either
    /// case being special. A duration that outlasts any fight gets no expiry at all: the
    /// condition ends with the encounter, exactly like one printed with no duration. And
    /// "until the grapple ends" gets none either — <c>Encounter.EndGrapple</c> owns that
    /// end, keyed on the condition's source being the grappler.
    /// </remarks>
    public static ConditionExpiry? ExpiryFor(ConditionDuration? duration, Combatant source, Combatant bearer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bearer);

        if (duration is null || duration.OutlastsFight || duration.WhileGrappleHolds)
        {
            return null;
        }

        var owner = duration.Owner == ConditionDurationOwner.Bearer ? bearer : source;

        return new ConditionExpiry(owner.Id, duration.Clock, owner.TurnsBegun + duration.TurnsAhead);
    }

    /// <summary>
    /// Whether this rider may be imposed on this target — the same check plus the printed
    /// size gate.
    /// </summary>
    /// <remarks>
    /// Immunity is not tested here. <see cref="Combatant.AddCondition"/> owns that, and
    /// checking it twice would let the two answers drift apart.
    /// </remarks>
    public static bool CanImpose(AppliedCondition condition, Combatant target)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(target);

        return CanBeImposed(condition) && condition.AllowsTargetSize(target.Stats.Size);
    }
}
