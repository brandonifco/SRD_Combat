using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>
/// Which creatures may turn up as a randomly drawn enemy.
/// </summary>
/// <remarks>
/// <para>
/// <b>A third axis, and neither of the other two owns it.</b> <see cref="MonsterPool"/>
/// grades mechanical coverage — whether the engine executes a creature's turn in full —
/// and <see cref="EncounterBudget"/> prices the fight. A Camel passes both: its stat
/// block is simple and entirely modelled, and 25 XP is 25 XP. It is still absurd as a
/// foe, because <b>the SRD prints it so a party can own one</b>, not because it ambushes
/// anybody. Coverage is not difficulty, and coverage is not appropriateness either.
/// </para>
/// <para>
/// <b>Most of this is derived rather than judged.</b> The Equipment chapter prices eight
/// animals in its <i>Mounts and Other Animals</i> table (printed page 100) — with a
/// carrying capacity and a cost in gold — and says of them that a mount's "primary
/// purpose is to carry gear that would otherwise slow you down". That is the SRD itself
/// declaring which creatures are equipment, so <see cref="PricedAsEquipment"/> is a
/// transcription of a printed table rather than an opinion about animals.
/// </para>
/// <para>
/// <b>Two names are a judgement, and here is the reading.</b> A Cat and a Goat are not on
/// that table, are not priced, and are still household animals the book prints because
/// the world contains them — livestock is not made for fighting and is not aggressive
/// under normal circumstances. Nothing else is excluded on temperament: a <b>weak wild
/// animal is a poor fight, not an absurd one</b>, and the budget already answers poor
/// fights by buying more of them. So the Rat, the Raven and the Deer stay in, and this
/// list does not become a bestiary-wide model of which animals are cross.
/// </para>
/// <para>
/// <b>Two entries were considered and deliberately left excluded.</b> An Elephant is a
/// genuine threat in the wild and a Mastiff is a war dog that fights beside its handler —
/// but both are priced in that table, and the line is drawn there rather than re-argued
/// per animal. <b>Excluding them costs nothing</b>, because this governs only the random
/// draw: <c>EncounterBuilder</c> takes any sequence it is handed, so a stampeding
/// elephant or a bandit's hounds can be authored deliberately, which is where a creature
/// like that belongs anyway rather than turning up on a die roll.
/// </para>
/// <para>
/// <b>Names are matched exactly.</b> A Giant Goat is a wild mountain creature with a
/// charging Ram attack and belongs in the pool; a substring test would take it out with
/// the farm animal. This is the same trap as <c>GillSans</c> versus
/// <c>GillSans-SemiBold</c>, one layer up.
/// </para>
/// </remarks>
public static class PlausibleFoes
{
    /// <summary>
    /// The <i>Mounts and Other Animals</i> table, printed page 100, by stat block name.
    /// </summary>
    /// <remarks>
    /// The table prints "Horse, Draft" and "Horse, Riding" where the bestiary prints
    /// "Draft Horse" and "Riding Horse"; these are the stat block's names, because that
    /// is what a candidate carries.
    /// </remarks>
    public static IReadOnlyList<string> PricedAsEquipment { get; } =
    [
        "Camel",
        "Draft Horse",
        "Elephant",
        "Mastiff",
        "Mule",
        "Pony",
        "Riding Horse",
        "Warhorse",
    ];

    /// <summary>Household animals the book prints for the world rather than for a fight.</summary>
    public static IReadOnlyList<string> DomesticAnimals { get; } =
    [
        "Cat",
        "Goat",
    ];

    private static readonly HashSet<string> Excluded =
        new(PricedAsEquipment.Concat(DomesticAnimals), StringComparer.Ordinal);

    /// <summary>Every name this rule excludes, for a validator to check against the content.</summary>
    public static IReadOnlyCollection<string> ExcludedNames => Excluded;

    /// <summary>Whether this creature may be drawn at random as an enemy.</summary>
    public static bool Admits(MonsterDefinition monster)
    {
        ArgumentNullException.ThrowIfNull(monster);

        return !Excluded.Contains(monster.Name);
    }
}
