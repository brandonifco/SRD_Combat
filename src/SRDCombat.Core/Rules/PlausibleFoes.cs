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
/// Two things are excluded here, for two unrelated reasons: creatures that are
/// <b>property</b> rather than enemies, and creatures with nowhere to fight — a Killer
/// Whale on dry land, which <see cref="IsAquatic"/> covers.
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
/// <para>
/// <b>The second exclusion is a rule rather than a list</b>, and the difference is the
/// point: an animal is equipment because the book prices it, which only a list can say,
/// while a creature is aquatic because of numbers the stat block already carries. See
/// <see cref="IsAquatic"/> — a derived rule needs no maintenance and covers creatures
/// nobody has looked at yet.
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

    /// <summary>
    /// The land speed at or below which the SRD is saying a creature does not get around
    /// on foot at all.
    /// </summary>
    /// <remarks>
    /// One square a turn. The book uses it for everything whose real movement is
    /// something else — fish, bats, ghosts, a flying sword — and the gap below the next
    /// speed up is wide: **no creature in the bestiary walks between 5 and 10 feet.**
    /// </remarks>
    public const int TokenLandSpeedFeet = 5;

    /// <summary>
    /// Whether this creature's only real mobility is swimming, so a dry-land fight would
    /// have it flopping one square a turn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Derived from the stat block, and checked against all 330 before being trusted.</b>
    /// It catches exactly nine — Octopus, Piranha, Seahorse, Giant Seahorse, Reef Shark,
    /// Swarm of Piranhas, Hunter Shark, Killer Whale, Giant Shark — with no false
    /// positive and no false negative anywhere in the book.
    /// </para>
    /// <para>
    /// <b>All three clauses earn their place, and the middle one is why the obvious
    /// version is wrong.</b> A bare "walks 5 feet or less" also catches the Bat, the Owl,
    /// the Animated Flying Sword, the Swarm of Bats, the Will-o'-Wisp, the Ghost, the
    /// Wraith and both Fungi — every one a perfectly good land encounter, and several of
    /// them staples. A token land speed says only "not on foot"; what makes a creature
    /// aquatic is that <em>swimming is the only thing it has instead</em>.
    /// </para>
    /// <para>
    /// <b>The boundary is the book's own, not a tuned threshold.</b> The nearest creatures
    /// on the other side walk 10 feet — the Merfolk Skirmisher, the Merrow, the Aboleth
    /// and the Giant Octopus — and all four are meant to be met ashore. The Giant Octopus
    /// is the closest call in the bestiary and the SRD gave it twice the Octopus's land
    /// speed on purpose; following that is following print rather than arbitrating.
    /// </para>
    /// <para>
    /// This is an exclusion for want of anywhere to put them, not a claim that they are
    /// bad monsters. A battlefield with water would make every one of them playable, and
    /// this rule is what such a change would delete.
    /// </para>
    /// </remarks>
    public static bool IsAquatic(MonsterDefinition monster)
    {
        ArgumentNullException.ThrowIfNull(monster);

        if (monster.Speeds.GetValueOrDefault(MovementMode.Walk) > TokenLandSpeedFeet)
        {
            return false;
        }

        return monster.Speeds.ContainsKey(MovementMode.Swim)
            && !monster.Speeds.Any(speed => speed.Key is not (MovementMode.Walk or MovementMode.Swim));
    }

    /// <summary>Whether this creature may be drawn at random as an enemy.</summary>
    public static bool Admits(MonsterDefinition monster)
    {
        ArgumentNullException.ThrowIfNull(monster);

        return !Excluded.Contains(monster.Name) && !IsAquatic(monster);
    }
}
