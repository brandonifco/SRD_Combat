using SRDCombat.Core.Dice;
namespace SRDCombat.Core.Definitions;

/// <summary>The SRD's eight schools of magic.</summary>
public enum MagicSchool
{
    Abjuration,
    Conjuration,
    Divination,
    Enchantment,
    Evocation,
    Illusion,
    Necromancy,
    Transmutation,
}

/// <summary>What a spell costs to cast, in the action economy.</summary>
public enum SpellCastingTime
{
    Action,
    BonusAction,
    Reaction,

    /// <summary>A minute or longer — unusable in a fight, but real.</summary>
    Extended,
}

/// <summary>Which components a spell needs.</summary>
[Flags]
public enum SpellComponents
{
    None = 0,
    Verbal = 1 << 0,
    Somatic = 1 << 1,
    Material = 1 << 2,
}

/// <summary>A spell from the SRD's Spell Descriptions.</summary>
public sealed record SpellDefinition
{
    /// <summary>Stable slug — <c>spell.acid-arrow</c>.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Spell level. Zero for a cantrip.</summary>
    public required int Level { get; init; }

    public required MagicSchool School { get; init; }

    /// <summary>True for a cantrip, which is level 0 and costs no slot.</summary>
    public bool IsCantrip => Level == 0;

    /// <summary>The classes whose spell list this appears on, as printed.</summary>
    public required IReadOnlyList<string> Classes { get; init; }

    public required SpellCastingTime CastingTime { get; init; }

    /// <summary>The casting time exactly as printed, since the enum flattens "1 minute" and "1 hour".</summary>
    public required string CastingTimeText { get; init; }

    /// <summary>True when the spell can also be cast as a Ritual.</summary>
    public bool IsRitual { get; init; }

    /// <summary>The range as printed — "60 feet", "Self", "Touch", "Self (15-foot Cone)".</summary>
    public required string RangeText { get; init; }

    /// <summary>The range in feet where the text gives one. Null for Self, Touch, Sight and Unlimited.</summary>
    public int? RangeFeet { get; init; }

    /// <summary>
    /// The creature type the spell's printed targeting names — Hold Person's "Choose a
    /// Humanoid". Null for the overwhelming majority, which target any creature.
    /// Structured at extraction from the exact printed sentence, and refused at casting
    /// with <c>spell.wrong_target_type</c> when the target is anything else.
    /// </summary>
    public CreatureType? TargetCreatureType { get; init; }

    /// <summary>
    /// The distance a target may be at, reading the two printed bands that carry no
    /// number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>"Touch" and "Self" both parse to no distance at all</b>, and a null range means
    /// "no limit" everywhere it is checked — so before this existed, Inflict Wounds, a
    /// Touch spell, could be cast across the room. Touch is read as 5 feet, the reach
    /// every other touch-shaped rule in this engine uses.
    /// </para>
    /// <para>
    /// <b>Self stays null on purpose.</b> A Self spell is not aimed at anybody: its area
    /// is centred on the caster and covers what it covers, so there is no target distance
    /// to check. <see cref="IsSelfRanged"/> is how a caller tells that apart from
    /// genuinely unlimited.
    /// </para>
    /// </remarks>
    public int? TargetRangeFeet => RangeFeet
        ?? (RangeText.StartsWith("Touch", StringComparison.OrdinalIgnoreCase) ? 5 : null);

    /// <summary>True when the spell is cast on the caster rather than aimed at a target.</summary>
    public bool IsSelfRanged =>
        RangeText.StartsWith("Self", StringComparison.OrdinalIgnoreCase);

    public required SpellComponents Components { get; init; }

    /// <summary>The material component in parentheses, when there is one.</summary>
    public string? MaterialComponent { get; init; }

    /// <summary>The duration as printed — "Instantaneous", "Concentration, up to 1 hour".</summary>
    public required string DurationText { get; init; }

    /// <summary>True when the spell requires Concentration.</summary>
    public bool RequiresConcentration { get; init; }

    /// <summary>The spell's full description.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// How the spell resolves, classified by the same rule as every other piece of rules
    /// text: a spell the model cannot express is Unmodelled and counted, never silent.
    /// </summary>
    public required EntryMechanics Mechanics { get; init; }

    /// <summary>The saving-throw effect, when the spell resolves through one.</summary>
    public SaveEffect? Save { get; init; }

    /// <summary>
    /// The spell's damage dice. Populated for attack spells and for damaging spells
    /// generally; a save spell's damage is also on <see cref="Save"/>.
    /// </summary>
    public IReadOnlyList<AttackDamage> Damage { get; init; } = [];

    /// <summary>Conditions the spell imposes.</summary>
    public IReadOnlyList<AppliedCondition> AppliedConditions { get; init; } = [];

    /// <summary>
    /// True when the spell resolves with a spell attack roll rather than a saving throw.
    /// </summary>
    /// <remarks>
    /// Detected from the printed phrase rather than by the attack grammar the bestiary
    /// uses: a spell says "make a ranged spell attack", with the bonus coming from the
    /// caster rather than from the spell, so there is no <c>Attack Roll: +N</c> to parse.
    /// </remarks>
    public bool IsSpellAttack { get; init; }

    /// <summary>
    /// "and the next attack roll made against it before the end of your next turn has
    /// Advantage" — Guiding Bolt, exactly once in the book (verified corpus-wide).
    /// Structured at extraction like Sacred Flame's cover clause, because leaving the
    /// sentence as prose shipped the spell as 4d6 and a light that did nothing.
    /// </summary>
    /// <remarks>
    /// The roll is anyone's — the printed sentence names the roll made against the
    /// target, not who makes it — and the expiry runs on the caster's turn-stamped
    /// clock, the same shape Vex uses (#153).
    /// </remarks>
    public bool GrantsAdvantageAgainstTargetOnHit { get; init; }

    /// <summary>
    /// The sentences of a spell the classifier could not classify at all — non-empty
    /// exactly when <see cref="Mechanics"/> is <see cref="EntryMechanics.Unmodelled"/>,
    /// and empty otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An empty list here is not a claim that the spell is fully modelled, and this
    /// type deliberately offers no such claim.</b> There was an <c>IsFullyModelled</c>
    /// beside it, mirroring <see cref="MonsterEntry.IsFullyModelled"/>. It answered
    /// <c>true</c> for all 154 structured spells — Web, Cloudkill, Acid Arrow, Bestow
    /// Curse and Chill Touch included — and it was removed rather than populated (#292).
    /// The reasoning, written down because the next reader will be tempted to put it
    /// back:
    /// </para>
    /// <para>
    /// <b>The stat-block accounting works because a stat block entry is all mechanics in
    /// a printed grammar</b> — <c>Attack Roll:</c>, <c>Hit:</c>, <c>Failure:</c> — so a
    /// leftover sentence is a real rule that got dropped, and there are few of them. A
    /// spell description is prose, and most of it is flavour. Running the same
    /// sentence-by-sentence accounting over the 154 structured spells, with the most
    /// generous possible reading of "accounted for" (credit any sentence naming a save, a
    /// spell attack, damage dice, healing, an area or a condition) leaves 72% of
    /// sentences unaccounted and exactly 7 spells fully modelled. Thirteen of the
    /// seventeen spells hand-verified against print for the casting menu come out
    /// <em>false</em>, on sentences like "A bright streak flashes from you to a point you
    /// choose within range"; and two of the seven that come out true are false positives
    /// — Chill Touch, whose "it can't regain Hit Points until the end of your next turn"
    /// shares a sentence with its damage, and Circle of Death, which nobody has read
    /// against print. <b>The derived signal would be anti-correlated with the truth, and
    /// its false positives are produced by the very failure this project's rule exists to
    /// prevent</b>: a clause hiding inside a sentence that parsed.
    /// </para>
    /// <para>
    /// Separating flavour from mechanics is what would fix that, and that is exactly the
    /// "does this look mechanical?" keyword filter this project removed rather than
    /// tuned — see the remarks on <c>EntryMechanicsParser.MechanicalSentences</c>. A
    /// keyword list always has false negatives, and a false negative loses a rule.
    /// </para>
    /// <para>
    /// <b>So the honest signal for a spell lives where it is genuinely derived: by
    /// hand.</b> <c>SRDCombat.Game.PreparableSpells</c> is the authority on which spells
    /// execute faithfully; it states its bar and names every spell it refuses together
    /// with the clause that refused it. <see cref="Rules.SpellcastingRules"/>'s
    /// <c>HasExecutableEffect</c> is the weaker, shape-only question — would casting do
    /// <em>something</em> — and is never a faithfulness claim. Should the spell grammar
    /// ever grow real partial accounting, this list is where the remainder goes; until
    /// then it is named for what it actually holds.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> UnclassifiedClauses { get; init; } = [];

    /// <summary>
    /// The "Using a Higher-Level Spell Slot" or "Cantrip Upgrade" clause, as printed.
    /// Kept as text: upcasting is not implemented, and structuring it would imply it is.
    /// </summary>
    public string? ScalingText { get; init; }

    /// <summary>
    /// The dice one extra slot level buys — "The healing increases by 2d8 for each
    /// spell slot level above 1" — structured only when the sentence has exactly that
    /// shape, the printed "above N" is the spell's own level, and the die matches the
    /// base effect's. Everything else stays on <see cref="ScalingText"/> as text.
    /// </summary>
    public DiceExpression? UpcastDicePerSlotLevel { get; init; }

    /// <summary>
    /// A cantrip's per-step growth — "increases by 1d8 when you reach levels 5, 11, and
    /// 17", which is the one shape every printed Cantrip Upgrade uses.
    /// </summary>
    public DiceExpression? CantripUpgradeDice { get; init; }

    public required int SourcePage { get; init; }

    /// <summary>
    /// The hit points this spell restores, when it heals. Null for every other spell.
    /// </summary>
    public SpellHeal? Heal { get; init; }

    /// <summary>
    /// The revival this spell performs, when it returns the dead to life — Revivify.
    /// Null for every other spell.
    /// </summary>
    public SpellRevival? Revival { get; init; }

    /// <summary>True when the spell can be cast during a fight at all.</summary>
    public bool IsUsableInCombat => CastingTime != SpellCastingTime.Extended;
}
