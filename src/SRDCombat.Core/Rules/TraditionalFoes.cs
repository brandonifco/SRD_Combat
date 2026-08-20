using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>
/// Which creatures belong in a fantasy gauntlet at all: the genre cut that keeps the
/// random draw to traditional D&amp;D monsters and enemies.
/// </summary>
/// <remarks>
/// <para>
/// <b>A fourth axis, judged rather than derived, and asked for from play.</b>
/// <see cref="MonsterPool"/> grades coverage, <see cref="EncounterBudget"/> prices the
/// fight, and <see cref="PlausibleFoes"/> removes property and the stranded — and a
/// fight could still open with an Ape beside a Lion, because none of those axes has an
/// opinion about genre. The first answer was <c>EncounterBuilder.ClassicMonsterWeight</c>,
/// a 3× preference that made animals rarer; on 2026-08-20 Brandon directed the full cut:
/// the random draw fields classic monsters and enemies only, and the safari is out.
/// </para>
/// <para>
/// <b>The rule that generates the list:</b> a real-world animal — mundane or prehistoric
/// — is excluded <em>unless</em> the book made it fantastic (a Giant or Swarm variant, a
/// Blood Hawk, a Flying Snake, a Worg), and the bystander NPCs and noble mounts go with
/// them. What survives is goblinoids, undead, dragons, fiends, constructs, monstrosities,
/// human adversaries, the wolf pack and the giant dungeon vermin — 81 of the tier's 117.
/// </para>
/// <para>
/// <b>Names are matched exactly</b>, the <see cref="PlausibleFoes"/> lesson: excluding
/// the Rat must not exclude the Giant Rat, and a substring test would. A content test
/// pins every name here against the corpus so a renamed stat block cannot escape the cut.
/// </para>
/// <para>
/// <b>The cost is a thin floor, accepted deliberately.</b> CR 0 keeps three creatures
/// (Awakened Shrub, Giant Fire Beetle, Lemure), which is why the earlier slice shipped a
/// weight rather than a filter. Brandon chose the filter with open eyes: low-CR
/// "expansion" fill-ins are planned to repopulate the band, and until they land the
/// builder's own fallbacks cover the rare slot that only a 10 XP creature could fill.
/// </para>
/// <para>
/// <b>This governs only the random draw</b>, like every list beside it: an authored
/// encounter that wants a pack of lions passes <c>traditionalFoesOnly: false</c> or
/// hands <c>EncounterBuilder</c> its own sequence.
/// </para>
/// </remarks>
public static class TraditionalFoes
{
    /// <summary>
    /// Real-world animals printed for the world's fauna rather than for a fantasy
    /// gauntlet — the safari, the farmyard's wild cousins, and the zoo.
    /// </summary>
    public static IReadOnlyList<string> MundaneAnimals { get; } =
    [
        "Ape",
        "Baboon",
        "Badger",
        "Bat",
        "Black Bear",
        "Brown Bear",
        "Crab",
        "Deer",
        "Eagle",
        "Frog",
        "Hawk",
        "Hippopotamus",
        "Hyena",
        "Jackal",
        "Lion",
        "Lizard",
        "Owl",
        "Panther",
        "Polar Bear",
        "Rat",
        "Raven",
        "Scorpion",
        "Spider",
        "Tiger",
        "Venomous Snake",
        "Vulture",
        "Weasel",
    ];

    /// <summary>The prehistoric menagerie — dinosaurs and megafauna, not dungeon stock.</summary>
    public static IReadOnlyList<string> PrehistoricBeasts { get; } =
    [
        "Ankylosaurus",
        "Archelon",
        "Plesiosaurus",
        "Pteranodon",
        "Saber-Toothed Tiger",
    ];

    /// <summary>
    /// People the book prints as the world's population rather than as its villains, and
    /// the noble mounts a party allies with rather than fights.
    /// </summary>
    public static IReadOnlyList<string> BystandersAndMounts { get; } =
    [
        "Commoner",
        "Giant Eagle",
        "Noble",
        "Pegasus",
    ];

    private static readonly HashSet<string> Excluded = new(
        MundaneAnimals.Concat(PrehistoricBeasts).Concat(BystandersAndMounts),
        StringComparer.Ordinal);

    /// <summary>Every name this rule excludes, for a validator to check against the content.</summary>
    public static IReadOnlyCollection<string> ExcludedNames => Excluded;

    /// <summary>Whether this creature belongs in the traditional random draw.</summary>
    public static bool Admits(MonsterDefinition monster)
    {
        ArgumentNullException.ThrowIfNull(monster);

        return !Excluded.Contains(monster.Name);
    }
}
