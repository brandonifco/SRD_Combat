using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Core.Tests.Combat;

/// <summary>
/// The Search action (#674): the Wisdom (Perception) check against a hidden creature's
/// recorded find-DC, its <c>search.not_hidden</c> and <c>action.spent</c> refusals,
/// Tactical Mind, and the settled reading that passive Perception never auto-finds.
/// </summary>
/// <remarks>
/// See <c>Encounter.Hiding.cs</c>'s <c>Search</c> remarks for the reading this pins:
/// Search takes an explicit target (the same shape #673 already gave Attack against an
/// Invisible creature), and only a spent Search action — never a standing passive
/// Perception score — can end a Hide-conferred Invisible.
/// </remarks>
public class SearchActionTests
{
    [Fact]
    public void ASearcherWhoMeetsTheFindDC_RevealsTheHiddenCreature()
    {
        var (encounter, searcher, hidden) = SearcherAndHidden(findDifficultyClass: 15, new ScriptedRandomSource(20, 1, 15));

        Assert.Null(encounter.Search(hidden));

        Assert.False(hidden.HasCondition(ConditionType.Invisible));
        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("searcher", StringComparison.Ordinal)
                && step.Narration.Contains("finds", StringComparison.Ordinal)
                && step.Narration.Contains("no longer hidden", StringComparison.Ordinal));
    }

    [Fact]
    public void ASearcherWhoFallsShort_DoesNotRevealTheHiddenCreature()
    {
        var (encounter, searcher, hidden) = SearcherAndHidden(findDifficultyClass: 15, new ScriptedRandomSource(20, 1, 5));

        Assert.Null(encounter.Search(hidden));

        Assert.True(hidden.HasCondition(ConditionType.Invisible));
        Assert.False(searcher.Turn.HasAction, "Search spends the action whether or not it succeeds.");
        Assert.Contains(
            encounter.Log,
            step => step.ActorId == "searcher" && step.Narration.Contains("not found", StringComparison.Ordinal));
    }

    [Fact]
    public void ATargetThatIsNotHidden_RefusesSearchNotHidden()
    {
        var searcher = CombatTestData.Combatant("searcher", stats: CombatTestData.Stats(initiativeBonus: 10));
        var notHidden = CombatTestData.Combatant("bystander", sideId: CombatTestData.Monsters, x: 1);
        var encounter = Encounter.Start(new Battlefield(8, 8), [searcher, notHidden], new ScriptedRandomSource(20, 1));

        var refusal = encounter.Search(notHidden);

        Assert.Equal("search.not_hidden", refusal?.Code);
        Assert.True(searcher.Turn.HasAction, "A refused Search must not spend the action.");
    }

    [Fact]
    public void ASpellsInvisibleTarget_IsAlsoRefusedSearchNotHidden()
    {
        // A spell's Invisible carries no find-DC — RevealHidden already refuses to touch
        // it (HideActionTests.RevealHidden_NeverTouchesASpellsInvisible), and Search must
        // agree before spending anything, not merely fail silently at the reveal.
        var searcher = CombatTestData.Combatant("searcher", stats: CombatTestData.Stats(initiativeBonus: 10));
        var target = CombatTestData.Combatant("target", sideId: CombatTestData.Monsters, x: 1);
        target.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: "invisibility-spell"));
        var encounter = Encounter.Start(new Battlefield(8, 8), [searcher, target], new ScriptedRandomSource(20, 1));

        var refusal = encounter.Search(target);

        Assert.Equal("search.not_hidden", refusal?.Code);
        Assert.True(target.HasCondition(ConditionType.Invisible));
    }

    [Fact]
    public void AnAllyCannotSearchAHiddenAlly_RefusesSearchNotEnemy()
    {
        // Print's third ending trigger is "an enemy finds you" (p.183), not any
        // creature — Codex's P1 finding on this slice's first draft. A same-side
        // Search must leave the ally hidden rather than silently ending Hide.
        var searcher = CombatTestData.Combatant("ally", stats: CombatTestData.Stats(initiativeBonus: 10));
        var hidden = CombatTestData.Combatant("hidden-ally", x: 1);
        hidden.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: hidden.Id, FindDifficultyClass: 5));
        var encounter = Encounter.Start(new Battlefield(8, 8), [searcher, hidden], new ScriptedRandomSource(20, 1));

        var refusal = encounter.Search(hidden);

        Assert.Equal("search.not_enemy", refusal?.Code);
        Assert.True(hidden.HasCondition(ConditionType.Invisible));
        Assert.True(searcher.Turn.HasAction, "A refused Search must not spend the action.");
    }

    [Fact]
    public void AHiderCannotSearchItself_RefusesSearchNotEnemy()
    {
        var hider = CombatTestData.Combatant("hider", stats: CombatTestData.Stats(initiativeBonus: 10));
        hider.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: hider.Id, FindDifficultyClass: 5));
        var encounter = Encounter.Start(new Battlefield(8, 8), [hider], new ScriptedRandomSource(20));

        var refusal = encounter.Search(hider);

        Assert.Equal("search.not_enemy", refusal?.Code);
        Assert.True(hider.HasCondition(ConditionType.Invisible));
    }

    [Fact]
    public void HidesRecordedFindDCIsWhatSearchReadsAgainst_NotTheStaticDC15()
    {
        // Codex's P2 finding: every other test's hidden creature carries the Hide DC
        // (15) as its find-DC too, so a regression that hardcoded 15 into Search would
        // still pass them. This test drives Hide itself with a Stealth bonus of +9 and
        // a roll of 10, recording a find-DC of 19 — deliberately not 15 — then proves
        // Search reads exactly that recorded total: one short fails, exactly meeting it
        // succeeds.
        var (fallShort, fallShortHider, _, fallShortDC) = HiddenBehindWallWithRecordedFindDC(
            new ScriptedRandomSource(20, 1, 10, 18));
        Assert.Equal(19, fallShortDC);

        Assert.Null(fallShort.Search(fallShortHider));

        Assert.True(fallShortHider.HasCondition(ConditionType.Invisible));

        var (metExactly, metExactlyHider, _, metExactlyDC) = HiddenBehindWallWithRecordedFindDC(
            new ScriptedRandomSource(20, 1, 10, 19));
        Assert.Equal(19, metExactlyDC);

        Assert.Null(metExactly.Search(metExactlyHider));

        Assert.False(metExactlyHider.HasCondition(ConditionType.Invisible));
    }

    [Fact]
    public void SearchIsRefusedWhenTheActionIsAlreadySpent()
    {
        var (encounter, searcher, hidden) = SearcherAndHidden(findDifficultyClass: 15, new ScriptedRandomSource(20, 1));
        Assert.Null(encounter.Dodge());

        var refusal = encounter.Search(hidden);

        Assert.Equal("action.spent", refusal?.Code);
        Assert.True(hidden.HasCondition(ConditionType.Invisible));
    }

    [Fact]
    public void PassivePerceptionDoesNotAutoFind_TurnsPassingWithoutASearchLeaveTheHiderHidden()
    {
        // The settled #673/#674 reading: only an active Search action can end a
        // Hide-conferred Invisible. A bystander with an enormous standing Perception
        // score, sharing a turn with the hider and never spending an action to Search,
        // must never see the hidden creature revealed by that score alone.
        var hiderStats = CombatTestData.Stats(initiativeBonus: 20);
        var hider = new Combatant("hider", "hider", CombatTestData.Heroes, hiderStats, new GridPosition(0, 0));
        hider.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: "hider", FindDifficultyClass: 5));

        var watcherStats = CombatTestData.Stats(initiativeBonus: -10) with
        {
            SkillBonuses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Perception"] = 30 },
        };
        var watcher = CombatTestData.Combatant("watcher", sideId: CombatTestData.Monsters, stats: watcherStats, x: 1);

        var encounter = Encounter.Start(new Battlefield(8, 8), [hider, watcher], new ScriptedRandomSource(20, 1));

        // Nothing consumes the watcher's Perception bonus unless Search is actually
        // taken — ending both combatants' turns without calling Search is the whole
        // pin: the DC-5 find is trivially beatable, and yet nothing here beats it.
        encounter.EndTurn();
        encounter.EndTurn();

        Assert.True(hider.HasCondition(ConditionType.Invisible));
        Assert.DoesNotContain(encounter.Log, step => step.Narration.Contains("no longer hidden", StringComparison.Ordinal));
    }

    [Fact]
    public void TacticalMindTurnsAFailedSearchIntoASuccess()
    {
        // Two combatants consume two initiative rolls (20, 20) before the Perception
        // check: 5 + 2 (the fighter's explicit Perception bonus) = 7 against DC 15,
        // failing; Tactical Mind's 1d10 rolls 9, taking the total to 16 — a success, and
        // the hidden target must actually be revealed, not merely have the check pass.
        var fighter = TacticalMindSearcher();
        var hidden = CombatTestData.Combatant("hidden", sideId: CombatTestData.Monsters, x: 1);
        hidden.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: hidden.Id, FindDifficultyClass: 15));
        var encounter = Encounter.Start(new Battlefield(8, 8), [fighter, hidden], new ScriptedRandomSource(20, 20, 5, 9));

        Assert.Null(encounter.Search(hidden));

        Assert.False(hidden.HasCondition(ConditionType.Invisible));
        Assert.Equal(0, fighter.Features.SecondWindRemaining);
        Assert.Contains(encounter.Log, step => step.Narration.Contains("Tactical Mind", StringComparison.Ordinal));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// A searcher at (0,0) and one already-hidden enemy at (1,0), carrying a Hide-conferred
    /// Invisible with the given find-DC. The scripted random is consumed as: initiative
    /// (two rolls), then the Search action's Wisdom (Perception) roll.
    /// </summary>
    private static (Encounter Encounter, Combatant Searcher, Combatant Hidden) SearcherAndHidden(
        int findDifficultyClass, IRandomSource random)
    {
        var searcher = CombatTestData.Combatant("searcher", stats: CombatTestData.Stats(initiativeBonus: 10));
        var hidden = CombatTestData.Combatant("hidden", sideId: CombatTestData.Monsters, x: 1);
        hidden.AddCondition(new ActiveCondition(ConditionType.Invisible, SourceId: hidden.Id, FindDifficultyClass: findDifficultyClass));

        var encounter = Encounter.Start(new Battlefield(8, 8), [searcher, hidden], random);

        return (encounter, searcher, hidden);
    }

    /// <summary>
    /// A hider (Stealth bonus +9) behind a wall from one enemy, at (0,0) and (4,0) on a
    /// wall-blocked field, who actually takes Hide with the given roll — so the returned
    /// <c>RecordedFindDC</c> is whatever Hide itself computed (roll + 9), never a
    /// hardcoded constant. Consumes: initiative (two rolls), Hide's Stealth check (one
    /// roll, Normal mode), then ends the hider's turn so the enemy is active and a
    /// caller's next scripted die is free for its own <see cref="Search"/> roll.
    /// </summary>
    private static (Encounter Encounter, Combatant Hider, Combatant Enemy, int RecordedFindDC)
        HiddenBehindWallWithRecordedFindDC(IRandomSource random)
    {
        var field = new Battlefield(9, 5, blocked: [new GridPosition(2, 0)]);

        var hiderStats = CombatTestData.Stats(initiativeBonus: 10) with
        {
            SkillBonuses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Stealth"] = 9 },
        };
        var hider = new Combatant("hider", "hider", CombatTestData.Heroes, hiderStats, new GridPosition(0, 0));
        var enemy = CombatTestData.Combatant(
            "enemy", sideId: CombatTestData.Monsters, stats: CombatTestData.Stats(initiativeBonus: -10), x: 4, y: 0);

        var encounter = Encounter.Start(field, [hider, enemy], random);

        Assert.Null(encounter.Hide());
        var recordedFindDC = hider.ConditionState(ConditionType.Invisible)!.FindDifficultyClass!.Value;

        encounter.EndTurn();

        return (encounter, hider, enemy, recordedFindDC);
    }

    private static Combatant TacticalMindSearcher()
    {
        var stats = CombatTestData.Stats(initiativeBonus: 10) with
        {
            SkillBonuses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Perception"] = 2 },
            Character = new CombatantFeatures(
                [ClassFeature.TacticalMind, ClassFeature.SecondWind],
                AttacksPerAction: 1,
                SneakAttackDamage: null,
                RageDamageBonus: 0,
                RageUses: 0,
                SecondWindUses: 1,
                ActionSurgeUses: 0,
                Level: 5),
        };

        return new Combatant("fighter", "fighter", CombatTestData.Heroes, stats, new GridPosition(0, 0));
    }
}
