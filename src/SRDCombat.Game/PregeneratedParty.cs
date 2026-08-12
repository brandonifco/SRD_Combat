using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game;

/// <summary>A party member: the draft that made them, the sheet it resolved to, and their combatant.</summary>
/// <param name="Draft">What was chosen. Levelling re-resolves this rather than mutating the sheet.</param>
/// <param name="Sheet">Every number, derived.</param>
/// <param name="Combatant">The fighting piece.</param>
public sealed record PartyMember(CharacterDraft Draft, CharacterSheet Sheet, Combatant Combatant)
{
    /// <summary>
    /// The same character as a fresh combatant at a given square.
    /// </summary>
    /// <remarks>
    /// A combatant's position is the engine's to change once a fight is running, so
    /// setting up a fight makes a new one rather than moving an existing piece. It starts
    /// at full hit points with every resource unspent — correct for a standalone fight,
    /// and the thing the gauntlet will have to revisit when it decides what carries
    /// between them.
    /// </remarks>
    public PartyMember AtPosition(GridPosition position) => this with
    {
        Combatant = new Combatant(
            Combatant.Id,
            Combatant.Name,
            Combatant.SideId,
            Combatant.Stats,
            position,
            CarriedOver),
    };

    /// <summary>
    /// The same character carrying wounds and spent resources in from an earlier fight.
    /// </summary>
    /// <remarks>
    /// Held rather than applied immediately because placement makes the combatant, and a
    /// combatant's starting state has to be set when it is constructed. Set it here and
    /// <see cref="AtPosition"/> honours it.
    /// </remarks>
    public PartyMember CarryingOver(CombatantCarryOver carriedOver) => this with { CarriedOver = carriedOver };

    /// <summary>What this member brings in from an earlier fight. Null means full strength.</summary>
    public CombatantCarryOver? CarriedOver { get; init; }
}

/// <summary>
/// Four hand-authored characters to play with, until building your own party exists.
/// </summary>
/// <remarks>
/// <para>
/// One per mechanical shape the engine handles, chosen so that sitting down to a fight
/// exercises the breadth that has been built rather than four variations on "hit it with
/// a sword": a Fighter (Second Wind, Action Surge, a Fighting Style, Tactical Mind), a
/// Rogue (Sneak Attack, Cunning Action, Steady Aim, Expertise, Cunning Strike), a Cleric
/// (spell slots, an attack spell and a save spell, Concentration) and a Barbarian (Rage,
/// Reckless Attack, Danger Sense, Fast Movement).
/// </para>
/// <para>
/// <b>Drafts, not stored sheets.</b> Every number comes from <see cref="CharacterResolver"/>,
/// so these cannot drift from the rules that make them, and levelling a party is
/// re-resolving each draft at the new level rather than editing anything.
/// </para>
/// </remarks>
public static class PregeneratedParty
{
    /// <summary>The side identifier the party fights under.</summary>
    public const string SideId = "party";

    /// <summary>Builds the party at a given level, placed in a column at <paramref name="x"/>.</summary>
    public static IReadOnlyList<PartyMember> Build(SrdContent content, int level = 1, int x = 0)
    {
        ArgumentNullException.ThrowIfNull(content);

        return
        [
            Member(content, Fighter(level), level, x, 0),
            Member(content, Barbarian(level), level, x, 1),
            Member(content, Rogue(level), level, x, 2),
            Member(content, Cleric(level), level, x, 3),
        ];
    }

    /// <summary>
    /// Resolves one character from a draft at a level.
    /// </summary>
    /// <remarks>
    /// Public because levelling is re-resolving a draft, and characters level
    /// individually once a death makes the party diverge.
    /// </remarks>
    public static PartyMember Resolve(SrdContent content, CharacterDraft draft, int level, int x = 0, int y = 0)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(draft);

        return Member(content, draft with { Level = level }, level, x, y);
    }

    private static PartyMember Member(SrdContent content, CharacterDraft draft, int level, int x, int y)
    {
        var sheet = CharacterResolver.Resolve(
            draft,
            new CharacterBuildContent(
                content.SpeciesById[draft.SpeciesId],
                content.ClassesById[draft.ClassId],
                content.BackgroundsById[draft.BackgroundId],
                content.WeaponsById,
                content.ArmorById));

        var spells = SpellIdsFor(draft.ClassId)
            .Where(content.SpellsById.ContainsKey)
            .Select(id => content.SpellsById[id])
            .ToArray();

        var stats = StatsFor(content, sheet, draft.ClassId, level, spells);

        return new PartyMember(
            draft,
            sheet,
            new Combatant(draft.Name, draft.Name, SideId, stats, new GridPosition(x, y)));
    }

    /// <summary>
    /// Reads the per-level resource columns off the class table — Rages, Rage Damage,
    /// Second Wind, Sneak Attack — and hands them to the combatant.
    /// </summary>
    /// <remarks>
    /// These live in the class advancement table rather than on the sheet because they
    /// are per-level numbers the table prints, and <c>CombatantStats.FromCharacter</c>
    /// takes them as parameters for exactly that reason.
    /// </remarks>
    private static CombatantStats StatsFor(
        SrdContent content,
        CharacterSheet sheet,
        string classId,
        int level,
        IReadOnlyList<SpellDefinition> spells)
    {
        var row = content.ClassesById[classId].AtLevel(level)
            ?? throw new InvalidOperationException($"{classId} has no level {level} row.");

        var sneakAttack = row.Resources.TryGetValue("Sneak Attack", out var dice)
            && DiceExpression.TryParse(dice, out var parsed)
                ? parsed
                : null;

        return CombatantStats.FromCharacter(
            sheet,
            sneakAttack,
            row.ResourceCount("Rage Damage") ?? 0,
            row.ResourceCount("Rages") ?? 0,
            // The Fighter's table prints no Second Wind column; the feature grants two
            // uses at level 1, and the engine needs a number rather than a name.
            sheet.Has(ClassFeature.SecondWind) ? row.ResourceCount("Second Wind") ?? 2 : 0,
            sheet.Has(ClassFeature.ActionSurge) ? 1 : 0,
            spells.Count > 0 ? spells : null,
            spells.Count > 0 ? SpellcastingRules.AbilityFor(classId) : null);
    }

    /// <summary>
    /// The spells the pregenerated caster knows.
    /// </summary>
    /// <remarks>
    /// One of each shape the engine resolves, so playing exercises every casting path:
    /// Sacred Flame forces a save, Guiding Bolt rolls a spell attack, and Cure Wounds
    /// and Healing Word restore hit points. A spell the engine cannot resolve would be
    /// refused with a reason at the point of casting, which is honest but makes for a
    /// poor fight.
    /// </remarks>
    private static IReadOnlyList<string> SpellIdsFor(string classId) => classId switch
    {
        // Both healing spells, deliberately: Cure Wounds heals more, Healing Word is a
        // Bonus Action at 60 feet, and the choice between them — get someone up now, or
        // heal harder next turn — is the most interesting one a party has in a fight.
        "class.cleric" =>
        [
            "spell.sacred-flame",
            "spell.guiding-bolt",
            "spell.cure-wounds",
            "spell.healing-word",
        ],
        _ => [],
    };

    private static CharacterDraft Fighter(int level) => new()
    {
        Name = "Brenna",
        SpeciesId = "species.dwarf",
        ClassId = "class.fighter",
        BackgroundId = "background.soldier",
        Level = level,
        BaseAbilityScores = Scores(strength: 15, dexterity: 13, constitution: 14, intelligence: 10, wisdom: 12, charisma: 8),
        PrimaryIncrease = Ability.Strength,
        SecondaryIncrease = Ability.Constitution,
        ChosenSkills = ["Athletics", "Perception"],
        FightingStyle = FightingStyle.Defense,
        WeaponIds = ["weapon.longsword"],
        ArmorId = "armor.chain-mail",
        HasShield = true,
    };

    private static CharacterDraft Barbarian(int level) => new()
    {
        Name = "Korrin",
        SpeciesId = "species.human",
        ClassId = "class.barbarian",
        BackgroundId = "background.soldier",
        Level = level,
        BaseAbilityScores = Scores(strength: 15, dexterity: 14, constitution: 14, intelligence: 8, wisdom: 12, charisma: 10),
        PrimaryIncrease = Ability.Strength,
        SecondaryIncrease = Ability.Constitution,
        ChosenSkills = ["Athletics", "Survival"],
        WeaponIds = ["weapon.greataxe"],
        // No armour: Unarmored Defense is the Barbarian's own AC rule, and putting them
        // in Chain Mail would quietly switch it off.
        ArmorId = null,
    };

    private static CharacterDraft Rogue(int level) => new()
    {
        Name = "Sable",
        SpeciesId = "species.halfling",
        ClassId = "class.rogue",
        BackgroundId = "background.criminal",
        Level = level,
        BaseAbilityScores = Scores(strength: 10, dexterity: 15, constitution: 13, intelligence: 14, wisdom: 12, charisma: 8),
        PrimaryIncrease = Ability.Dexterity,
        SecondaryIncrease = Ability.Constitution,
        ChosenSkills = ["Stealth", "Acrobatics"],
        ExpertiseSkills = ["Stealth", "Acrobatics"],
        WeaponIds = ["weapon.shortsword", "weapon.shortbow"],
        ArmorId = "armor.leather-armor",
    };

    private static CharacterDraft Cleric(int level) => new()
    {
        Name = "Aldous",
        SpeciesId = "species.human",
        ClassId = "class.cleric",
        BackgroundId = "background.acolyte",
        Level = level,
        BaseAbilityScores = Scores(strength: 13, dexterity: 10, constitution: 14, intelligence: 12, wisdom: 15, charisma: 8),
        PrimaryIncrease = Ability.Wisdom,
        // The Acolyte offers Intelligence, Wisdom and Charisma — no Constitution, which
        // the resolver refuses outright rather than quietly dropping.
        SecondaryIncrease = Ability.Intelligence,
        ChosenSkills = ["Insight", "Religion"],
        WeaponIds = ["weapon.mace"],
        ArmorId = "armor.chain-shirt",
        HasShield = true,
    };

    private static Dictionary<Ability, int> Scores(
        int strength,
        int dexterity,
        int constitution,
        int intelligence,
        int wisdom,
        int charisma) =>
        new()
        {
            [Ability.Strength] = strength,
            [Ability.Dexterity] = dexterity,
            [Ability.Constitution] = constitution,
            [Ability.Intelligence] = intelligence,
            [Ability.Wisdom] = wisdom,
            [Ability.Charisma] = charisma,
        };
}
