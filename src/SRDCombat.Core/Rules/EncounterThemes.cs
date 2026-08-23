using SRDCombat.Core.Definitions;

namespace SRDCombat.Core.Rules;

/// <summary>The company a creature keeps: which warband a random fight is built around.</summary>
public enum EncounterTheme
{
    /// <summary>Goblins, hobgoblins, bugbears, kobolds, and the beasts they keep.</summary>
    GoblinoidWarband,

    /// <summary>Gnolls, grimlocks, sahuagin, berserkers — tribal raiders and their beasts.</summary>
    SavageHunters,

    /// <summary>The walking dead and what haunts their crypts.</summary>
    Undead,

    /// <summary>Bandits, pirates, spies — the lawless side of the human world.</summary>
    Outlaws,

    /// <summary>Guards, soldiers, knights — the lawful side of it.</summary>
    Soldiery,

    /// <summary>Cultists and the fiends, elementals and guardians they traffic with.</summary>
    CultAndFiends,

    /// <summary>Dragon wyrmlings and the kobolds that serve them.</summary>
    Draconic,

    /// <summary>Wolves and the wild chases — packs that hunt together.</summary>
    WildPack,

    /// <summary>Giant vermin, swarms, oozes and fungi — what infests a dungeon.</summary>
    DungeonVermin,

    /// <summary>Animated objects, awakened plants and arcane guardians.</summary>
    ArcaneAndAnimated,
}

/// <summary>
/// Which creatures fight side by side: the curated map that keeps a random encounter's
/// composition coherent.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fifth curated list, and the complaint that demanded it</b> (2026-08-20): a
/// fight had fielded an Ape, a Giant Eagle and a Scout as one group. The budget prices
/// creatures singly and drew them independently, so budget-compatible strangers ended up
/// in a squad. Every creature in the traditional roster now carries the themes it fights
/// under, and <c>EncounterBuilder</c> fills a fight with companions of its first pick.
/// </para>
/// <para>
/// <b>A bridge creature carries several themes and that is the design.</b> A Worg is a
/// goblin mount and a pack hunter, so a Worg drawn first can bring goblins or wolves —
/// and a goblin and a dire wolf who share nothing directly still stand together in the
/// Worg's fight, because companionship is judged against the fight's <em>anchor</em>,
/// not pairwise. One anchor keeps the rule cheap and the results readable.
/// </para>
/// <para>
/// <b>An empty theme set is a loner, and explicit on purpose.</b> An Owlbear fights
/// alone or beside other Owlbears (same-name companionship is always granted); a missing
/// entry is an error the validator catches, not a loner. The distinction is the
/// project's oldest rule wearing a new coat: nothing may hold unclassified silently.
/// </para>
/// <para>
/// <b>This governs only the random draw</b>, like every list beside it: an authored
/// encounter hands <c>EncounterBuilder</c> whatever sequence it likes.
/// </para>
/// </remarks>
public static class EncounterThemes
{
    private static readonly IReadOnlyDictionary<string, EncounterTheme[]> ByName =
        new Dictionary<string, EncounterTheme[]>(StringComparer.Ordinal)
        {
            // CR 0
            ["Awakened Shrub"] = [EncounterTheme.ArcaneAndAnimated],
            ["Giant Fire Beetle"] = [EncounterTheme.DungeonVermin],
            ["Lemure"] = [EncounterTheme.CultAndFiends],

            // CR 1/8
            ["Bandit"] = [EncounterTheme.Outlaws],
            ["Blood Hawk"] = [EncounterTheme.WildPack, EncounterTheme.Outlaws],
            ["Cultist"] = [EncounterTheme.CultAndFiends],
            ["Flying Snake"] = [EncounterTheme.DungeonVermin, EncounterTheme.CultAndFiends],
            ["Giant Rat"] = [EncounterTheme.DungeonVermin],
            ["Giant Weasel"] = [EncounterTheme.WildPack, EncounterTheme.DungeonVermin],
            ["Goblin Minion"] = [EncounterTheme.GoblinoidWarband],
            ["Guard"] = [EncounterTheme.Soldiery],
            ["Kobold Warrior"] = [EncounterTheme.GoblinoidWarband, EncounterTheme.Draconic],
            ["Warrior Infantry"] = [EncounterTheme.Soldiery],

            // CR 1/4
            ["Animated Flying Sword"] = [EncounterTheme.ArcaneAndAnimated],
            ["Axe Beak"] = [EncounterTheme.WildPack],
            ["Blink Dog"] = [EncounterTheme.WildPack],
            ["Giant Badger"] = [EncounterTheme.DungeonVermin, EncounterTheme.WildPack],
            ["Giant Bat"] = [EncounterTheme.DungeonVermin, EncounterTheme.Undead],
            ["Giant Centipede"] = [EncounterTheme.DungeonVermin],
            ["Giant Lizard"] = [EncounterTheme.DungeonVermin, EncounterTheme.GoblinoidWarband],
            ["Giant Venomous Snake"] = [EncounterTheme.DungeonVermin, EncounterTheme.CultAndFiends],
            ["Giant Wolf Spider"] = [EncounterTheme.DungeonVermin, EncounterTheme.GoblinoidWarband],
            ["Goblin Warrior"] = [EncounterTheme.GoblinoidWarband],
            ["Grimlock"] = [EncounterTheme.SavageHunters],
            ["Skeleton"] = [EncounterTheme.Undead],
            ["Steam Mephit"] = [EncounterTheme.CultAndFiends],
            ["Swarm of Bats"] = [EncounterTheme.DungeonVermin, EncounterTheme.Undead],
            ["Swarm of Rats"] = [EncounterTheme.DungeonVermin],
            ["Violet Fungus"] = [EncounterTheme.DungeonVermin],
            ["Wolf"] = [EncounterTheme.WildPack, EncounterTheme.GoblinoidWarband],
            ["Zombie"] = [EncounterTheme.Undead],

            // CR 1/2
            ["Giant Wasp"] = [EncounterTheme.DungeonVermin],
            ["Gnoll Warrior"] = [EncounterTheme.SavageHunters],
            ["Hobgoblin Warrior"] = [EncounterTheme.GoblinoidWarband, EncounterTheme.Soldiery],
            ["Magma Mephit"] = [EncounterTheme.CultAndFiends],
            ["Sahuagin Warrior"] = [EncounterTheme.SavageHunters],
            ["Scout"] = [EncounterTheme.Outlaws, EncounterTheme.Soldiery],
            ["Swarm of Insects"] = [EncounterTheme.DungeonVermin],
            ["Tough"] = [EncounterTheme.Outlaws],
            ["Troll Limb"] = [EncounterTheme.DungeonVermin],
            ["Worg"] = [EncounterTheme.GoblinoidWarband, EncounterTheme.WildPack],

            // CR 1
            ["Animated Armor"] = [EncounterTheme.ArcaneAndAnimated],
            ["Bugbear Warrior"] = [EncounterTheme.GoblinoidWarband],
            ["Dire Wolf"] = [EncounterTheme.WildPack],
            ["Giant Hyena"] = [EncounterTheme.SavageHunters, EncounterTheme.WildPack],
            ["Giant Vulture"] = [EncounterTheme.SavageHunters],
            ["Goblin Boss"] = [EncounterTheme.GoblinoidWarband],
            ["Hippogriff"] = [],
            ["Sphinx of Wonder"] = [EncounterTheme.ArcaneAndAnimated],
            ["Spy"] = [EncounterTheme.Outlaws, EncounterTheme.Soldiery],

            // CR 2
            ["Ankheg"] = [],
            ["Awakened Tree"] = [EncounterTheme.ArcaneAndAnimated],
            ["Azer Sentinel"] = [EncounterTheme.CultAndFiends],
            ["Bandit Captain"] = [EncounterTheme.Outlaws],
            ["Berserker"] = [EncounterTheme.Outlaws, EncounterTheme.SavageHunters],
            ["Black Dragon Wyrmling"] = [EncounterTheme.Draconic],
            ["Centaur Trooper"] = [EncounterTheme.Soldiery],
            ["Gargoyle"] = [EncounterTheme.CultAndFiends, EncounterTheme.ArcaneAndAnimated],
            ["Ghast"] = [EncounterTheme.Undead],
            ["Green Dragon Wyrmling"] = [EncounterTheme.Draconic],
            ["Ochre Jelly"] = [EncounterTheme.DungeonVermin],
            ["Ogre"] = [EncounterTheme.GoblinoidWarband, EncounterTheme.SavageHunters],
            ["Ogre Zombie"] = [EncounterTheme.Undead],
            ["Swarm of Venomous Snakes"] = [EncounterTheme.DungeonVermin, EncounterTheme.CultAndFiends],
            ["White Dragon Wyrmling"] = [EncounterTheme.Draconic],
            ["Will-o'-Wisp"] = [EncounterTheme.Undead],

            // CR 3
            ["Basilisk"] = [],
            ["Blue Dragon Wyrmling"] = [EncounterTheme.Draconic],
            ["Bugbear Stalker"] = [EncounterTheme.GoblinoidWarband],
            ["Hell Hound"] = [EncounterTheme.CultAndFiends],
            ["Hobgoblin Captain"] = [EncounterTheme.GoblinoidWarband, EncounterTheme.Soldiery],
            ["Knight"] = [EncounterTheme.Soldiery],
            ["Manticore"] = [],
            ["Owlbear"] = [],
            ["Swarm of Crawling Claws"] = [EncounterTheme.Undead],
            ["Warrior Veteran"] = [EncounterTheme.Soldiery],
            ["Winter Wolf"] = [EncounterTheme.WildPack],

            // CR 4
            ["Ettin"] = [EncounterTheme.GoblinoidWarband, EncounterTheme.SavageHunters],
            ["Guard Captain"] = [EncounterTheme.Soldiery],
            ["Red Dragon Wyrmling"] = [EncounterTheme.Draconic],
        };

    /// <summary>Every name the map covers, for the validator.</summary>
    public static IReadOnlyCollection<string> MappedNames => ByName.Keys.ToArray();

    /// <summary>
    /// The themes a creature fights under. Empty for a mapped loner; null when the
    /// creature is not in the map at all, which the caller treats as unconstrained —
    /// an authored pool of unmapped creatures builds exactly as it always did.
    /// </summary>
    public static IReadOnlyList<EncounterTheme>? ThemesOf(MonsterDefinition monster)
    {
        ArgumentNullException.ThrowIfNull(monster);

        return ByName.TryGetValue(monster.Name, out var themes) ? themes : null;
    }

    /// <summary>
    /// Whether a candidate may stand in a fight anchored by the given creature: the same
    /// stat block always may, a mapped pair needs a shared theme, and an unmapped
    /// creature on either side leaves the fight unconstrained.
    /// </summary>
    public static bool Companions(MonsterDefinition anchor, MonsterDefinition candidate)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(candidate);

        if (string.Equals(anchor.Name, candidate.Name, StringComparison.Ordinal))
        {
            return true;
        }

        var anchorThemes = ThemesOf(anchor);
        var candidateThemes = ThemesOf(candidate);

        if (anchorThemes is null || candidateThemes is null)
        {
            return true;
        }

        return anchorThemes.Intersect(candidateThemes).Any();
    }

    /// <summary>Whether this creature has any company at all — false for a mapped loner.</summary>
    public static bool KeepsCompany(MonsterDefinition monster) =>
        ThemesOf(monster) is not { Count: 0 };
}
