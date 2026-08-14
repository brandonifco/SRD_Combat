using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// A draft's chosen spells: the plan reading, the printed columns, and the refusals.
/// </summary>
public class SpellPreparationTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void TheClericsMenuIsTheVerifiedEight()
    {
        // The honest menu character creation can offer a Cleric, pinned by name so a
        // change to what is verified is a visible test change rather than a silently
        // different menu. Revivify joined with #119; Hold Person when repeat saves and
        // Concentration-tied conditions landed. CLAUDE.md carries this list in prose;
        // this is the assertion behind it.
        var menu = PreparableSpells.For(Content, "class.cleric")
            .Select(spell => spell.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Cure Wounds", "Guiding Bolt", "Healing Word", "Hold Person", "Inflict Wounds",
                "Revivify", "Sacred Flame", "Spirit Guardians",
            ],
            menu);
    }

    [Fact]
    public void RevivifyCarriesItsStructuredShape()
    {
        var revivify = Content.SpellsById["spell.revivify"];

        Assert.NotNull(revivify.Revival);
        Assert.Equal(1, revivify.Revival!.HitPoints);
        Assert.True(SpellcastingRules.HasExecutableEffect(revivify));

        // Exactly one spell structures the shape — Raise Dead's ten days must not have
        // borrowed the one-minute window.
        Assert.Single(Content.Spells, spell => spell.Revival is not null);
    }

    [Fact]
    public void EveryRegisteredSpellIsCoherent()
    {
        // The registry is curated by hand, so assert the mechanical facts curation
        // could get wrong: every entry exists, sits on its class's printed list, has a
        // castable level at this game's cap, and passes the shape predicate — the
        // registry is a subset of the shape-executable, never an override of it.
        foreach (var classId in new[] { "class.cleric", "class.wizard", "class.ranger" })
        {
            var @class = Content.ClassesById[classId];
            var highestSlot = @class.Levels.Single(row => row.Level == 5).SpellSlots.Keys.Max();

            foreach (var spell in PreparableSpells.For(Content, classId))
            {
                Assert.Contains(@class.Name, spell.Classes);
                Assert.True(SpellcastingRules.HasExecutableEffect(spell), $"{spell.Name} fails the shape test.");
                Assert.True(
                    spell.Level <= highestSlot,
                    $"{spell.Name} (level {spell.Level}) is never castable by a level 5 {@class.Name}.");
            }
        }
    }

    [Fact]
    public void ANonCasterHasAnEmptyMenu() =>
        Assert.Empty(PreparableSpells.For(Content, "class.fighter"));

    [Fact]
    public void TheWizardsMenuIsTheVerifiedTen()
    {
        // Pinned by name like the Cleric's: Ray of Sickness and Shatter joined when
        // their one blocking clause each got a printed-sentence wire, Hold Person with
        // repeat saves, and a silently different menu should be a test change, not a
        // surprise. Hold Monster stays off deliberately: it is a level 5 spell, and a
        // level 5 Wizard's slots stop at level 3 — beyond this game's tier, the same
        // line the loot table draws at Very Rare.
        var menu = PreparableSpells.For(Content, "class.wizard")
            .Select(spell => spell.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Acid Splash", "Burning Hands", "Fire Bolt", "Fireball", "Hold Person",
                "Lightning Bolt", "Mind Spike", "Poison Spray", "Ray of Sickness", "Shatter",
            ],
            menu);
    }

    [Fact]
    public void TheWidenedSpellsCarryTheirWiresInContent()
    {
        // The two clauses that were each spell's whole blocker, now structured — and
        // exactly one spell carries each, so nothing else was swept in.
        var sickness = Content.SpellsById["spell.ray-of-sickness"];
        var rider = Assert.Single(sickness.AppliedConditions);
        Assert.Equal(ConditionType.Poisoned, rider.Condition);
        Assert.True(ConditionRules.CanBeImposed(rider));

        Assert.True(Content.SpellsById["spell.shatter"].Save!.ConstructsSaveAtDisadvantage);

        Assert.Single(
            Content.Spells,
            spell => spell.IsSpellAttack && spell.AppliedConditions.Any(ConditionRules.CanBeImposed));
        Assert.Single(Content.Spells, spell => spell.Save?.ConstructsSaveAtDisadvantage == true);
    }

    [Fact]
    public void PreparesInChosenOrderUpToThePrintedColumns()
    {
        var draft = Cleric() with
        {
            ChosenSpellIds =
            [
                "spell.sacred-flame",
                "spell.cure-wounds",
                "spell.healing-word",
                "spell.guiding-bolt",
                "spell.inflict-wounds",
                "spell.spirit-guardians",
            ],
        };

        // Level 1: Spirit Guardians (level 3) has no slot yet and is the plan, not an
        // error; the four level 1 spells fit the printed Prepared Spells column.
        var atOne = SpellPreparation.Prepare(Content, draft, level: 1);
        Assert.DoesNotContain(atOne, spell => spell.Name == "Spirit Guardians");
        Assert.Contains(atOne, spell => spell.Name == "Sacred Flame");
        Assert.Contains(atOne, spell => spell.Name == "Inflict Wounds");

        // Level 5: the level 3 slot exists and the column has room for everything.
        var atFive = SpellPreparation.Prepare(Content, draft, level: 5);
        Assert.Contains(atFive, spell => spell.Name == "Spirit Guardians");
        Assert.Equal(6, atFive.Count);
    }

    [Fact]
    public void TheColumnsCapThePlanInChosenOrder()
    {
        // Six level 1 choices against level 1's printed Prepared Spells column: the
        // first entries win, because the list is ordered the way the player ordered it.
        var draft = Cleric() with
        {
            ChosenSpellIds =
            [
                "spell.cure-wounds",
                "spell.healing-word",
                "spell.guiding-bolt",
                "spell.inflict-wounds",
                "spell.sacred-flame",
            ],
        };

        var allowance = Content.ClassesById["class.cleric"].Levels
            .Single(row => row.Level == 1)
            .ResourceCount("Prepared Spells");

        var prepared = SpellPreparation.Prepare(Content, draft, level: 1)
            .Where(spell => !spell.IsCantrip)
            .Select(spell => spell.Name)
            .ToArray();

        Assert.Equal(allowance, prepared.Length);
        Assert.Equal("Cure Wounds", prepared[0]);
    }

    [Fact]
    public void ANonCasterChoosingSpellsIsRefused()
    {
        var draft = Cleric() with
        {
            ClassId = "class.fighter",
            ChosenSpellIds = ["spell.cure-wounds"],
        };

        var refusal = Assert.Throws<ArgumentException>(() => SpellPreparation.Prepare(Content, draft, level: 1));
        Assert.Contains("no spell slots", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASpellOffTheClassListIsRefused()
    {
        var draft = Cleric() with { ChosenSpellIds = ["spell.fire-bolt"] };

        var refusal = Assert.Throws<ArgumentException>(() => SpellPreparation.Prepare(Content, draft, level: 1));
        Assert.Contains("not on the Cleric spell list", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnverifiedSpellIsRefused()
    {
        // Bane forces a save and does something the engine cannot express; preparing it
        // would put a spell on the sheet that every cast refuses. Bestow Curse is the
        // sharper case — its extracted shape looks castable and its printed effect is
        // mostly beyond the engine — and the registry refuses both the same way.
        foreach (var id in new[] { "spell.bane", "spell.bestow-curse" })
        {
            var draft = Cleric() with { ChosenSpellIds = [id] };

            var refusal = Assert.Throws<ArgumentException>(
                () => SpellPreparation.Prepare(Content, draft, level: 1));
            Assert.Contains("not verified", refusal.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnUnknownSpellIsRefused()
    {
        var draft = Cleric() with { ChosenSpellIds = ["spell.does-not-exist"] };

        Assert.Throws<ArgumentException>(() => SpellPreparation.Prepare(Content, draft, level: 1));
    }

    [Fact]
    public void AChoosingDraftResolvesToACombatantCarryingItsChoices()
    {
        var draft = Cleric() with
        {
            ChosenSpellIds = ["spell.sacred-flame", "spell.healing-word"],
        };

        var member = PregeneratedParty.Resolve(Content, draft, level: 1);
        var spells = member.Combatant.Stats.Character!.Spells.Select(spell => spell.Name).ToArray();

        Assert.Equal(["Sacred Flame", "Healing Word"], spells);
    }

    [Fact]
    public void ADraftThatChoseNoSpellsPreparesNone()
    {
        // This asserted the opposite until a played run read the Cast menu: a draft
        // with no chosen spells used to fall back to a curated per-class loadout that
        // skipped the printed columns, which is how a level 1 Cleric ended up holding
        // three spells it had no slot for. There is one preparation path now, and a
        // caster who chose nothing prepares nothing.
        var member = PregeneratedParty.Resolve(Content, Cleric(), level: 1);

        Assert.Empty(member.Combatant.Stats.Character!.Spells);
    }

    [Fact]
    public void ARunStartsFromCreatedDrafts()
    {
        // The overload the creation flow calls: four drafts in, a run whose party is
        // those characters, everything downstream indifferent to where a draft came
        // from.
        var drafts = Enumerable.Range(1, 4)
            .Select(index => Cleric() with { Name = $"Made {index}" })
            .ToArray();

        var run = GauntletRun.Start(Content, drafts);

        Assert.Equal(4, run.Party.Count);
        Assert.Equal("Made 1", run.Party[0].Draft.Name);
        Assert.NotNull(run.Next);
    }

    /// <summary>The pregen Cleric's shape, rebuilt here because the original is private.</summary>
    private static CharacterDraft Cleric() => new()
    {
        Name = "Testa",
        SpeciesId = "species.human",
        ClassId = "class.cleric",
        BackgroundId = "background.acolyte",
        Level = 1,
        BaseAbilityScores = new Dictionary<Ability, int>
        {
            [Ability.Strength] = 13,
            [Ability.Dexterity] = 10,
            [Ability.Constitution] = 14,
            [Ability.Intelligence] = 12,
            [Ability.Wisdom] = 15,
            [Ability.Charisma] = 8,
        },
        PrimaryIncrease = Ability.Wisdom,
        SecondaryIncrease = Ability.Intelligence,
        ChosenSkills = ["Insight", "Religion"],
        WeaponIds = ["weapon.mace"],
        ArmorId = "armor.chain-shirt",
        HasShield = true,
    };
}
