namespace SRDCombat.Core.Rules;

/// <summary>A species trait the engine executes.</summary>
/// <remarks>
/// Empty today. See <see cref="SpeciesTraitRegistry"/>'s remarks for why, and add a
/// member here only alongside the code that makes it do something.
/// </remarks>
public enum SpeciesTrait
{
}

/// <summary>
/// Which printed species trait names the engine really executes, and what each one does.
/// </summary>
/// <remarks>
/// <para>
/// A curated allowlist, exactly like <see cref="ClassFeatureRegistry"/> and
/// <see cref="MonsterTraitRegistry"/>: a printed name maps to a <see cref="SpeciesTrait"/>
/// only when the engine actually does the thing. <b>Add a name here only alongside the
/// code that implements it.</b>
/// </para>
/// <para>
/// Every one of the 33 printed trait instances across the nine species — 28 distinct
/// names, Darkvision through Breath Weapon — is currently absent from this map. A
/// species today contributes only its size and speed; every trait is text the player
/// reads with no effect on a fight. That gap is the point of issue #291: the character
/// sheet and both creation flows must say so, rather than let a player reasonably
/// believe Darkvision or a Dragonborn's Breath Weapon does something because its full
/// rules text is printed at the point of choice.
/// </para>
/// <para>
/// This registry is the other half of that fix — the same shape as
/// <see cref="ClassFeatureRegistry"/>, kept separate because species traits are a
/// different content model (<c>TraitEntry</c>, not a class feature). It starts empty on
/// purpose: nothing is inferred from a trait's <c>EntryMechanics</c> classification
/// (which records what the extractor could parse out of the printed text) into a claim
/// that the engine executes it. The two questions are independent, exactly as they are
/// for a monster's <c>MonsterEntry</c> — see <see cref="MonsterTraitRegistry"/>'s
/// remarks.
/// </para>
/// <para>
/// <b>Registration keys on the printed name alone, so a name may only be registered
/// once every one of its printed instances carries identical text</b> (#346). The same
/// printed name recurs across species with genuinely different rules — Darkvision is
/// 60 feet for five species and the Dwarf's own trait is 120 feet, six printed
/// instances, two texts. Because <see cref="Resolve"/> and <see cref="Implements"/>
/// look up the bare name, registering "Darkvision" for the 60-ft reading would also
/// resolve the Dwarf's 120-ft printing to it — silently executing the wrong range with
/// no residue and no failing test, the same shape as the Goblin Warrior's advantage
/// clause (see this project's top-level "The rule this project runs on"). A trip-wire
/// in <c>SRDCombat.Content.Tests.OriginContentTests</c> walks every real species trait
/// and fails the moment a name whose printed texts differ is registered without every
/// variant sharing one text. If a future trait genuinely needs per-variant behaviour
/// under one printed name, the fix is on this API — key on the trait's text (or its
/// owning species), not the bare name — never suppressing or narrowing that test.
/// </para>
/// </remarks>
public static class SpeciesTraitRegistry
{
    private static readonly Dictionary<string, SpeciesTrait> ByPrintedName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The implemented trait for a printed name, or null when not implemented.</summary>
    public static SpeciesTrait? Resolve(string printedName)
    {
        ArgumentNullException.ThrowIfNull(printedName);

        return ByPrintedName.TryGetValue(printedName, out var trait) ? trait : null;
    }

    /// <summary>Whether a printed trait name is one the engine executes.</summary>
    public static bool Implements(string printedName)
    {
        ArgumentNullException.ThrowIfNull(printedName);

        return ByPrintedName.ContainsKey(printedName);
    }

    /// <summary>Every printed name this engine implements, for reporting coverage.</summary>
    public static IReadOnlyCollection<string> ImplementedNames => ByPrintedName.Keys;
}
