using SRDCombat.Content;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>
/// The spells character creation may offer: a curated allowlist, per class, of spells
/// verified by hand against print to execute faithfully.
/// </summary>
/// <remarks>
/// <para>
/// This is the sixth curated allowlist, and it exists because the shape data cannot be
/// trusted for this decision. <see cref="SpellcastingRules.HasExecutableEffect"/> asks
/// whether casting would do <em>something</em>; it says yes to Bestow Curse, whose
/// extracted save-plus-damage shape is a sliver of a printed effect the engine cannot
/// express. A menu filtered on shape would offer spells that execute partially, which is
/// the Goblin-Warrior bug wearing a spell list. So, like every registry before it: <b>an
/// id appears here only after its printed text was read against what the engine does.</b>
/// </para>
/// <para>
/// <b>There is no derived second opinion to fall back on, and that too is a decision.</b>
/// The stat-block accounting that grades monsters was measured against spell prose and
/// found anti-correlated with the truth — it calls thirteen of the seventeen spells below
/// unmodelled on their flavour sentences, while passing Chill Touch, whose blocking
/// clause shares a sentence with its damage. So <c>SpellDefinition.IsFullyModelled</c>
/// was retired rather than populated (#292), and <b>this list is the authority</b>: the
/// numbers and the whole argument are on <c>SpellDefinition.UnclassifiedClauses</c>.
/// </para>
/// <para>
/// The bar, stated: every clause that affects a <em>creature in a fight</em> must
/// execute; a clause about things the battlefield does not model — flammable objects,
/// light, sound, hiding — is read as satisfied by construction, the same reasoning
/// Elven Chain's training override and (before #106) the Wand's cover clause used.
/// Excluded by that bar, with the failing clause: Thunderwave (pushes 10 feet — forced
/// movement does not exist), Ray of Frost and Chill Touch (Speed and healing-denial
/// riders on a hit, neither being a condition the model holds), Scorching Ray (three
/// rays at chosen targets — one casting call, one target), Acid Arrow (delayed damage,
/// and damage on a miss), Chromatic Orb (a damage type chosen at casting, and a leap),
/// Flaming Sphere, Web, Vampiric Touch, Phantasmal Force, Bestow Curse, Ensnaring
/// Strike (persistent or triggered effects with no model). Every condition rider on the
/// launch lists' save spells is extraction-refused — repeat-save outs, "for the
/// duration", chooser's-choice conditions — so none of those spells can join until the
/// model grows the missing shape.
/// </para>
/// <para>
/// <b>Two entries joined when their one blocking clause got a printed-sentence wire</b>
/// (the widening pass): Shatter, whose "A Construct has Disadvantage on the save" rides
/// <c>SaveEffect.ConstructsSaveAtDisadvantage</c> and executes against the stats' own
/// creature type; and Ray of Sickness, whose "has the Poisoned condition until the end
/// of your next turn" is the bestiary's own attack-rider shape, parsed whole by the
/// spell grammar and imposed by the same path a bite's rider takes.
/// </para>
/// <para>
/// <b>One entry predates the bar and carries a stated gap rather than a silent one.</b>
/// Spirit Guardians' printed form is a persistent aura (halved Speed, saves each turn)
/// that the engine casts as a one-time Emanation; it is kept because removing it would
/// change the pregens, and the damage it does deal is real and correctly priced.
/// (Guiding Bolt carried this paragraph's other gap until #155: its "next attack roll
/// made against it ... has Advantage" rider now executes whole, structured at
/// extraction and spent by the next roll against the lit target on the caster's
/// turn-stamped clock.)
/// </para>
/// </remarks>
public static class PreparableSpells
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> IdsByClassId =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["class.cleric"] =
            [
                "spell.sacred-flame",
                "spell.guiding-bolt",
                "spell.cure-wounds",
                "spell.healing-word",
                "spell.inflict-wounds",
                "spell.hold-person",
                "spell.revivify",
                "spell.spirit-guardians",
            ],
            ["class.wizard"] =
            [
                "spell.acid-splash",
                "spell.fire-bolt",
                "spell.poison-spray",
                "spell.burning-hands",
                "spell.ray-of-sickness",
                "spell.mind-spike",
                "spell.hold-person",
                "spell.shatter",
                "spell.fireball",
                "spell.lightning-bolt",
            ],
            ["class.ranger"] =
            [
                "spell.cure-wounds",
            ],
        };

    /// <summary>
    /// The spells creation may offer this class, in menu order: cantrips first, then by
    /// level. Empty for a class with no verified spells — which is every non-caster,
    /// and honestly also a caster nothing has been verified for yet.
    /// </summary>
    public static IReadOnlyList<SpellDefinition> For(SrdContent content, string classId)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(classId);

        if (!IdsByClassId.TryGetValue(classId, out var ids))
        {
            return [];
        }

        return ids
            .Select(id => content.SpellsById[id])
            .OrderBy(spell => spell.Level)
            .ThenBy(spell => spell.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Whether this spell is verified to execute faithfully for this class.</summary>
    public static bool Allows(string classId, string spellId) =>
        IdsByClassId.TryGetValue(classId, out var ids)
        && ids.Contains(spellId, StringComparer.OrdinalIgnoreCase);
}
