using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>
/// Resolves a draft's chosen spells into the list a caster brings to a fight.
/// </summary>
/// <remarks>
/// <para>
/// The draft's list is the <em>plan</em> — the same reading as its Ability Score
/// Improvements — and resolving at a level prepares what the class table's printed
/// columns allow: cantrips against the Cantrips column, level 1+ spells against
/// Prepared Spells, each taken in the order they were chosen. A spell whose level has
/// no slots yet is skipped rather than refused: "must be of a level for which you have
/// spell slots" is a fact about today, and the plan is about later. "A level for which
/// you have spell slots" is read as any slot of the spell's level or higher, since a
/// higher slot casts a lower spell — at this game's levels the distinction never
/// actually bites, and the reading is stated so that stays visible.
/// </para>
/// <para>
/// What is refused, loudly and before anything resolves: a draft choosing spells for a
/// class that cannot cast, an unknown id, a spell missing from the class's printed
/// list, and a spell outside <see cref="PreparableSpells"/> — the curated set verified
/// by hand to execute faithfully. The registry rather than the shape predicate,
/// because a spell whose extracted shape is a sliver of its printed effect would
/// execute partially, and a hand-written draft must not be able to smuggle one past
/// the menu.
/// </para>
/// </remarks>
public static class SpellPreparation
{
    /// <summary>The spells the draft prepares at this level, in chosen order.</summary>
    public static IReadOnlyList<SpellDefinition> Prepare(
        SrdContent content,
        CharacterDraft draft,
        int level)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.ChosenSpellIds.Count == 0)
        {
            return [];
        }

        var @class = content.ClassesById[draft.ClassId];

        if (!@class.IsSpellcaster)
        {
            throw new ArgumentException(
                $"{@class.Name} has no spell slots at any level, so a draft choosing spells for one is wrong.",
                nameof(draft));
        }

        var row = @class.Levels.SingleOrDefault(candidate => candidate.Level == level)
            ?? throw new ArgumentException($"{@class.Name} has no level {level} row.", nameof(level));

        var chosen = draft.ChosenSpellIds
            .Select(id => Validated(content, @class, id))
            .ToArray();

        // The allowances come through CharacterCreation so Thaumaturge's extra cantrip
        // lands here too — one reading of the columns, not two that could disagree.
        var (cantripAllowance, preparedAllowance) = CharacterCreation.SpellAllowances(
            @class,
            level,
            draft.DivineOrder);
        var highestSlot = row.SpellSlots.Count > 0 ? row.SpellSlots.Keys.Max() : 0;

        var cantrips = chosen
            .Where(spell => spell.IsCantrip)
            .Take(cantripAllowance);

        var levelled = chosen
            .Where(spell => !spell.IsCantrip && spell.Level <= highestSlot)
            .Take(preparedAllowance);

        // Back to the order they were chosen in, so the draft's own sequence — which is
        // also what any client displays — survives the split.
        var prepared = cantrips.Concat(levelled).ToHashSet();

        return chosen.Where(prepared.Contains).Distinct().ToArray();
    }

    private static SpellDefinition Validated(SrdContent content, ClassDefinition @class, string id)
    {
        if (!content.SpellsById.TryGetValue(id, out var spell))
        {
            throw new ArgumentException($"Unknown spell '{id}'.");
        }

        if (!spell.Classes.Contains(@class.Name, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{spell.Name} is not on the {@class.Name} spell list.");
        }

        if (!PreparableSpells.Allows(@class.Id, spell.Id))
        {
            throw new ArgumentException(
                $"{spell.Name} is not verified to execute faithfully; preparing it would hold an " +
                "unimplemented rule silently. PreparableSpells is the curated list.");
        }

        return spell;
    }
}
