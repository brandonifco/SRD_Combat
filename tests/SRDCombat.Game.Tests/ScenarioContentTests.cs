using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

namespace SRDCombat.Game.Tests;

/// <summary>
/// Where a scenario meets the content that gives its ids meaning: everything missing is
/// named, the party resolves through the rules that make it, and a fingerprint that
/// disagrees is said out loud without refusing anything.
/// </summary>
public class ScenarioContentTests
{
    private static readonly SrdContent Content = TestContent.Srd;

    private static CharacterDraft Fighter { get; } = PregeneratedParty.Build(Content)[0].Draft;

    private static CharacterDraft Barbarian { get; } = PregeneratedParty.Build(Content)[1].Draft;

    private static CharacterDraft Cleric { get; } = PregeneratedParty.Build(Content)[3].Draft;

    [Fact]
    public void AScenarioNamingOnlyContentThisBuildHasChecksClean()
    {
        var check = ScenarioContent.CheckAgainst(Roster("monster.ogre"), Content);

        Assert.Empty(check.Errors);
        Assert.Empty(check.Notices);
        Assert.True(check.IsValid);
    }

    [Fact]
    public void AMissingMonsterIsRefusedByName()
    {
        var error = Assert.Single(
            ScenarioContent.CheckAgainst(Roster("monster.tarrasque-of-mars"), Content).Errors);

        Assert.Contains("monster 'monster.tarrasque-of-mars'", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scenario's refusal is not a save's. Both go through <c>ContentDrift</c> so the
    /// wording is single-sourced, but a scenario must not tell the reader to start a new
    /// run — there is no run.
    /// </summary>
    [Fact]
    public void AScenarioRefusalNamesTheScenarioAndOffersAScenariosRemedy()
    {
        var error = Assert.Single(ScenarioContent.CheckAgainst(Roster("monster.nothing"), Content).Errors);

        Assert.StartsWith("the scenario names", error, StringComparison.Ordinal);
        Assert.DoesNotContain("start a new run", error, StringComparison.Ordinal);
        Assert.Contains("Re-author the scenario", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every miss, not the first one. A scenario naming three vanished things is told
    /// about three of them — the whole reason the id sweep formats
    /// <c>ContentDrift.MissingMessage</c> rather than calling <c>Require</c> in a loop.
    /// </summary>
    [Fact]
    public void EveryMissingIdIsReportedNotJustTheFirst()
    {
        var scenario = Roster("monster.gone-one", "monster.gone-two") with
        {
            Party = new ScenarioParty
            {
                Members =
                [
                    new ScenarioMember
                    {
                        Level = 3,
                        Draft = Fighter with
                        {
                            SpeciesId = "species.gone",
                            WeaponIds = ["weapon.gone", "weapon.club"],
                            ArmorId = "armor.gone",
                        },
                    },
                ],
            },
        };

        var errors = ScenarioContent.CheckAgainst(scenario, Content).Errors;

        Assert.Contains(errors, error => error.Contains("monster 'monster.gone-one'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("monster 'monster.gone-two'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("species 'species.gone'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("weapon 'weapon.gone'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("armor 'armor.gone'", StringComparison.Ordinal));
    }

    /// <summary>
    /// A draft the resolver refuses for reasons that are not a missing id — here, Expertise
    /// in a skill the character has no proficiency in — is reported rather than thrown, so
    /// a library check names the broken file instead of dying on it.
    /// </summary>
    [Fact]
    public void APartyThatDoesNotResolveIsReportedRatherThanThrown()
    {
        var scenario = Roster("monster.ogre") with
        {
            Party = new ScenarioParty
            {
                Members =
                [
                    new ScenarioMember { Level = 3, Draft = Fighter with { ExpertiseSkills = ["Arcana"] } },
                ],
            },
        };

        Assert.Contains(
            "the party does not resolve",
            Assert.Single(ScenarioContent.CheckAgainst(scenario, Content).Errors),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same reading <see cref="GauntletRun.Resume"/> reaches since #355: a scenario
    /// is a question asked of the current build, and refusing the whole library after
    /// every extractor regeneration would make the surface useless inside a week. The
    /// per-id checks are what actually refuse.
    /// </summary>
    [Fact]
    public void AContentVersionMismatchIsANoticeAndRefusesNothing()
    {
        var check = ScenarioContent.CheckAgainst(
            Roster("monster.ogre") with { ContentVersion = "DEADBEEFDEADBEEF" },
            Content);

        Assert.Empty(check.Errors);
        Assert.True(check.IsValid);
        Assert.Contains("DEADBEEFDEAD", Assert.Single(check.Notices), StringComparison.Ordinal);
    }

    [Fact]
    public void AMatchingContentVersionSaysNothing() =>
        Assert.Empty(
            ScenarioContent.CheckAgainst(
                Roster("monster.ogre") with { ContentVersion = Content.ContentFingerprint },
                Content).Notices);

    /// <summary>
    /// Criterion 7: the preset resolves through <see cref="PregeneratedParty.Build"/> at
    /// the scenario's level, so a change to the pregens applies to every scenario using
    /// it. A library that froze copies of their drafts would drift one file at a time
    /// with nothing failing.
    /// </summary>
    [Fact]
    public void ThePregeneratedPresetResolvesThroughPregeneratedPartyRatherThanAStoredCopy()
    {
        var resolved = ScenarioContent.ResolveParty(
            Roster("monster.ogre") with { Party = new ScenarioParty { PregeneratedLevel = 4 } },
            Content);

        var built = PregeneratedParty.Build(Content, level: 4);

        Assert.Equal(
            built.Select(member => (member.Draft.Name, member.Sheet.Level, member.Sheet.MaximumHitPoints)),
            resolved.Select(member => (member.Draft.Name, member.Sheet.Level, member.Sheet.MaximumHitPoints)));
    }

    /// <summary>
    /// A member is resolved at the level the <em>scenario</em> names, not the one the
    /// draft carries — the same rule <see cref="PregeneratedParty.Resolve"/> follows,
    /// because levelling in this game is re-resolving a draft.
    /// </summary>
    [Fact]
    public void AMemberResolvesAtTheLevelTheScenarioNamesRatherThanTheDraftsOwn()
    {
        var scenario = Roster("monster.ogre") with
        {
            Party = new ScenarioParty
            {
                Members = [new ScenarioMember { Level = 5, Draft = Fighter with { Level = 1 } }],
            },
        };

        Assert.Equal(5, Assert.Single(ScenarioContent.ResolveParty(scenario, Content)).Sheet.Level);
    }

    // ---- S8 (#480): party starting state — wounds, spent resources, the dead.

    /// <summary>
    /// Acceptance criterion 1: absent means full strength, byte-identical to the fight
    /// this method built before <see cref="ScenarioMember.StartingState"/> existed. A
    /// null <c>StartingState</c> must never call <see cref="PregeneratedParty.CarryingOver"/>
    /// at all — calling it with an all-null <see cref="ScenarioStartingState"/> would
    /// still produce a full-strength combatant, but it would not be the same code path,
    /// and this is the test that tells the two apart.
    /// </summary>
    [Fact]
    public void AMemberWithNoStartingStateIsNeverCarriedOver()
    {
        var scenario = SoloMember(new ScenarioMember { Level = 3, Draft = Fighter });

        var resolved = Assert.Single(ScenarioContent.ResolveParty(scenario, Content));

        Assert.Null(resolved.CarriedOver);
        Assert.Equal(resolved.Sheet.MaximumHitPoints, resolved.Combatant.CurrentHitPoints);
    }

    /// <summary>
    /// Criterion 3: the state rides <see cref="PregeneratedParty.CarryingOver"/>, and the
    /// combatant that actually reflects it is only built once, by
    /// <see cref="PregeneratedParty.AtPosition"/> — the same path <see cref="ScenarioRunner"/>
    /// already calls through <c>EncounterFactory</c>. This is the end-to-end proof: every
    /// resource <c>ScenarioContent.ValidateStartingState</c> forwards into the
    /// <see cref="CombatantCarryOver"/> it returns shows up on the combatant the fight
    /// actually fields, not merely on the intermediate <see cref="PartyMember"/> or on
    /// the validated <see cref="ScenarioStartingState"/> itself.
    /// </summary>
    /// <remarks>
    /// A Codex review of this PR caught that the original shape of this test asserted
    /// only hit points and one spell-slot count — a stub that nulled
    /// <c>ValidateStartingState</c>'s forwarding of rages, Second Wind, Action Surge,
    /// Channel Divinity or potions (<c>ScenarioContent.cs</c>'s <c>ValidateStartingState</c>
    /// return statement) would have gone undetected. No single pregenerated class has all
    /// four resources, so this fields three members — a Fighter (Second Wind, Action
    /// Surge, potions), a Barbarian (Rages) and a Cleric (Channel Divinity, plus the
    /// original hit-point and spell-slot proof) — to reach every forwarded field at once.
    /// </remarks>
    [Fact]
    public void StartingStateReachesTheCombatantTheFightActuallyFields()
    {
        var resolvedFighter = Assert.Single(
            ScenarioContent.ResolveParty(SoloMember(new ScenarioMember { Level = 3, Draft = Fighter }), Content));
        var maximumSecondWind = resolvedFighter.Combatant.Stats.Character!.SecondWindUses;
        var maximumActionSurge = resolvedFighter.Combatant.Stats.Character!.ActionSurgeUses;

        var resolvedBarbarian = Assert.Single(
            ScenarioContent.ResolveParty(SoloMember(new ScenarioMember { Level = 3, Draft = Barbarian }), Content));
        var maximumRages = resolvedBarbarian.Combatant.Stats.Character!.RageUses;

        var resolvedCleric = Assert.Single(
            ScenarioContent.ResolveParty(SoloMember(new ScenarioMember { Level = 3, Draft = Cleric }), Content));
        var maximumChannelDivinity = resolvedCleric.Combatant.Stats.Character!.ChannelDivinityUses;
        var wounded = resolvedCleric.Sheet.MaximumHitPoints - 5;
        var slotLevel = resolvedCleric.Sheet.SpellSlots.Keys.Min();
        var remainingSlots = resolvedCleric.Sheet.SpellSlots[slotLevel] - 1;

        var scenario = Roster("monster.ogre") with
        {
            Party = new ScenarioParty
            {
                Members =
                [
                    new ScenarioMember
                    {
                        Level = 3,
                        Draft = Fighter,
                        StartingState = new ScenarioStartingState
                        {
                            SecondWindRemaining = maximumSecondWind - 1,
                            ActionSurgeRemaining = maximumActionSurge - 1,
                            Potions = new Dictionary<HealingPotion, int> { [HealingPotion.Standard] = 2 },
                        },
                    },
                    new ScenarioMember
                    {
                        Level = 3,
                        Draft = Barbarian,
                        StartingState = new ScenarioStartingState { RagesRemaining = maximumRages - 1 },
                    },
                    new ScenarioMember
                    {
                        Level = 3,
                        Draft = Cleric,
                        StartingState = new ScenarioStartingState
                        {
                            CurrentHitPoints = wounded,
                            ChannelDivinityRemaining = maximumChannelDivinity - 1,
                            SpellSlotsRemaining = new Dictionary<int, int> { [slotLevel] = remainingSlots },
                        },
                    },
                ],
            },
        };

        var fight = ScenarioRunner.Build(Content, scenario, seed: 1);

        var fighter = fight.Party.Single(member => member.Draft.Name == Fighter.Name).Combatant;
        var barbarian = fight.Party.Single(member => member.Draft.Name == Barbarian.Name).Combatant;
        var cleric = fight.Party.Single(member => member.Draft.Name == Cleric.Name).Combatant;

        Assert.Equal(maximumSecondWind - 1, fighter.Features.SecondWindRemaining);
        Assert.Equal(maximumActionSurge - 1, fighter.Features.ActionSurgeRemaining);
        Assert.Equal(2, fighter.Inventory.CountOf(HealingPotion.Standard));

        Assert.Equal(maximumRages - 1, barbarian.Features.RagesRemaining);

        Assert.Equal(wounded, cleric.CurrentHitPoints);
        Assert.Equal(remainingSlots, cleric.Features.SpellSlotsRemaining[slotLevel]);
        Assert.Equal(maximumChannelDivinity - 1, cleric.Features.ChannelDivinityRemaining);
    }

    /// <summary>Criterion 2: hit points above the sheet's maximum are refused, naming the member.</summary>
    [Fact]
    public void StartingHitPointsAboveTheSheetsMaximumIsRefused()
    {
        var maximum = Assert.Single(
            ScenarioContent.ResolveParty(SoloMember(new ScenarioMember { Level = 3, Draft = Fighter }), Content))
            .Sheet.MaximumHitPoints;

        var scenario = SoloMember(new ScenarioMember
        {
            Level = 3,
            Draft = Fighter,
            StartingState = new ScenarioStartingState { CurrentHitPoints = maximum + 1 },
        });

        var failure = Assert.Throws<InvalidDataException>(() => ScenarioContent.ResolveParty(scenario, Content));

        Assert.Contains(Fighter.Name, failure.Message, StringComparison.Ordinal);
        Assert.Contains("hit points", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Criterion 2: hit dice above the character's level are refused, naming the member.</summary>
    [Fact]
    public void StartingHitDiceAboveTheCharactersLevelIsRefused()
    {
        var scenario = SoloMember(new ScenarioMember
        {
            Level = 3,
            Draft = Fighter,
            StartingState = new ScenarioStartingState { HitDiceRemaining = 4 },
        });

        var failure = Assert.Throws<InvalidDataException>(() => ScenarioContent.ResolveParty(scenario, Content));

        Assert.Contains(Fighter.Name, failure.Message, StringComparison.Ordinal);
        Assert.Contains("hit dice", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Criterion 2: a resource count above the class table's own allowance is refused —
    /// here, a Barbarian's Rages, read off the resolved combatant rather than hardcoded,
    /// so the test does not silently stop meaning anything if the table's numbers move.
    /// </summary>
    [Fact]
    public void AResourceCountAboveTheClassTablesAllowanceIsRefused()
    {
        var resolved = Assert.Single(
            ScenarioContent.ResolveParty(SoloMember(new ScenarioMember { Level = 3, Draft = Barbarian }), Content));

        var maximumRages = resolved.Combatant.Stats.Character!.RageUses;

        var scenario = SoloMember(new ScenarioMember
        {
            Level = 3,
            Draft = Barbarian,
            StartingState = new ScenarioStartingState { RagesRemaining = maximumRages + 1 },
        });

        var failure = Assert.Throws<InvalidDataException>(() => ScenarioContent.ResolveParty(scenario, Content));

        Assert.Contains(Barbarian.Name, failure.Message, StringComparison.Ordinal);
        Assert.Contains("rages", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Criterion 2: a spell slot level the character does not have is refused.</summary>
    [Fact]
    public void ASpellSlotLevelTheCharacterDoesNotHaveIsRefused()
    {
        var resolved = Assert.Single(
            ScenarioContent.ResolveParty(SoloMember(new ScenarioMember { Level = 1, Draft = Cleric }), Content));

        var unheldLevel = Enumerable.Range(1, 9).First(level => !resolved.Sheet.SpellSlots.ContainsKey(level));

        var scenario = SoloMember(new ScenarioMember
        {
            Level = 1,
            Draft = Cleric,
            StartingState = new ScenarioStartingState
            {
                SpellSlotsRemaining = new Dictionary<int, int> { [unheldLevel] = 1 },
            },
        });

        var failure = Assert.Throws<InvalidDataException>(() => ScenarioContent.ResolveParty(scenario, Content));

        Assert.Contains(Cleric.Name, failure.Message, StringComparison.Ordinal);
        Assert.Contains($"level {unheldLevel}", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Criterion 2: more slots at a level than the sheet grants is refused.</summary>
    [Fact]
    public void MoreSlotsThanTheSheetGrantsIsRefused()
    {
        var resolved = Assert.Single(
            ScenarioContent.ResolveParty(SoloMember(new ScenarioMember { Level = 1, Draft = Cleric }), Content));

        var heldLevel = resolved.Sheet.SpellSlots.Keys.Min();
        var maximum = resolved.Sheet.SpellSlots[heldLevel];

        var scenario = SoloMember(new ScenarioMember
        {
            Level = 1,
            Draft = Cleric,
            StartingState = new ScenarioStartingState
            {
                SpellSlotsRemaining = new Dictionary<int, int> { [heldLevel] = maximum + 1 },
            },
        });

        var failure = Assert.Throws<InvalidDataException>(() => ScenarioContent.ResolveParty(scenario, Content));

        Assert.Contains(Cleric.Name, failure.Message, StringComparison.Ordinal);
        Assert.Contains($"level {heldLevel} spell slots {maximum + 1}", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A negative potion count is refused, naming the member.</summary>
    [Fact]
    public void ANegativePotionCountIsRefused()
    {
        var scenario = SoloMember(new ScenarioMember
        {
            Level = 3,
            Draft = Fighter,
            StartingState = new ScenarioStartingState
            {
                Potions = new Dictionary<HealingPotion, int> { [HealingPotion.Standard] = -1 },
            },
        });

        var failure = Assert.Throws<InvalidDataException>(() => ScenarioContent.ResolveParty(scenario, Content));

        Assert.Contains(Fighter.Name, failure.Message, StringComparison.Ordinal);
        Assert.Contains("potions", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Codex review of this PR caught that only the potion <em>counts</em> were
    /// validated — the dictionary's <c>HealingPotion</c> keys were never checked against
    /// the enum's own defined members, so an undefined potency (cast from an integer JSON
    /// wrote, or authored before this build's potency table shrank) passed load clean and
    /// only failed later, mid-fight, when <c>PotionRules.Healing</c> was asked to price a
    /// potency it had never heard of. Criterion 2 promises every value is refused by name
    /// at load; this pins that an undefined potency is one of those values.
    /// </summary>
    [Fact]
    public void AnUndefinedPotionPotencyIsRefused()
    {
        var scenario = SoloMember(new ScenarioMember
        {
            Level = 3,
            Draft = Fighter,
            StartingState = new ScenarioStartingState
            {
                Potions = new Dictionary<HealingPotion, int> { [(HealingPotion)99] = 1 },
            },
        });

        var failure = Assert.Throws<InvalidDataException>(() => ScenarioContent.ResolveParty(scenario, Content));

        Assert.Contains(Fighter.Name, failure.Message, StringComparison.Ordinal);
        Assert.Contains("potions", failure.Message, StringComparison.Ordinal);
        Assert.Contains("99", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Criterion 5, the subtle one: zero hit points and not dead is legal, and means
    /// downed-and-stable — the same state a gauntlet character carries into the next
    /// fight after surviving a knockout, per <c>CharacterState</c>'s own doc comment
    /// ("Zero and not dead means downed and stable") and <c>RunState.AfterFight</c>'s
    /// ("A character who went down but survived is Stable at 0"). Read off the actual
    /// combatant the fight fields, not merely the resolved <see cref="PartyMember"/>,
    /// because that is where <c>Combatant</c>'s own zero-hit-point handling
    /// (Unconscious, stable, not dead) runs — a Codex review of this PR caught that the
    /// original assertions here (0 hit points, not dead, Unconscious) all held even when
    /// the combatant was left <em>dying</em> rather than stable, because nothing checked
    /// <see cref="Combatant.IsStable"/> or <see cref="Combatant.IsDying"/> directly.
    /// </summary>
    [Fact]
    public void ZeroHitPointsAndNotDeadIsLegalAndMeansDownedAndStable()
    {
        var scenario = SoloMember(new ScenarioMember
        {
            Level = 3,
            Draft = Fighter,
            StartingState = new ScenarioStartingState { CurrentHitPoints = 0 },
        });

        var fight = ScenarioRunner.Build(Content, scenario, seed: 1);
        var combatant = Assert.Single(fight.Party).Combatant;

        Assert.Equal(0, combatant.CurrentHitPoints);
        Assert.False(combatant.IsDead);
        Assert.True(combatant.HasCondition(ConditionType.Unconscious));
        Assert.True(combatant.IsStable);
        Assert.False(combatant.IsDying);
    }

    /// <summary>
    /// The scripted-dice complement to the assertion above, reaching for the actual
    /// mechanism rather than the derived <see cref="Combatant.IsStable"/> flag alone.
    /// <see cref="Encounter.Start"/> (which <see cref="ScenarioRunner.Build"/> reaches
    /// through <c>EncounterFactory</c>) rolls initiative on the seed and immediately
    /// begins Round 1's first turn — the exact code path that gates a Death Saving Throw
    /// on <see cref="DeathSaveRules.MustRoll"/> — before this test ever calls anything
    /// else. Seeds are searched, not guessed, for one that seats the downed member first
    /// in turn order, so the assertion is not vacuously true because the fixture's own
    /// turn was never reached.
    /// </summary>
    [Fact]
    public void AZeroHitPointStartingMemberRollsNoDeathSaveOnItsTurn()
    {
        for (var seed = 1; seed <= 50; seed++)
        {
            var scenario = SoloMember(new ScenarioMember
            {
                Level = 3,
                Draft = Fighter,
                StartingState = new ScenarioStartingState { CurrentHitPoints = 0 },
            });

            var fight = ScenarioRunner.Build(Content, scenario, seed);
            var combatant = Assert.Single(fight.Party).Combatant;

            if (fight.Encounter.TurnOrder.First().Id != combatant.Id)
            {
                // This seed's initiative roll put the Ogre first, so the downed
                // member's own start-of-turn processing — the code this test exists to
                // pin — has not run yet. Try another seed rather than asserting nothing.
                continue;
            }

            // The member went first, so Encounter.Start already ran its full
            // start-of-turn handling for it, on real scripted dice (this seed). Had
            // IsStable come back false, that handling would have rolled a Death Save
            // right there: the log would carry a DeathSave step, and the roll's own
            // side effects (a natural 1's second failure, a natural 20's heal to 1 hit
            // point) would have moved DeathSaveFailures or CurrentHitPoints off zero.
            Assert.DoesNotContain(fight.Encounter.Log, step => step.Kind == CombatStepKind.DeathSave);
            Assert.Equal(0, combatant.DeathSaveFailures);
            Assert.Equal(0, combatant.DeathSaveSuccesses);
            Assert.Equal(0, combatant.CurrentHitPoints);
            return;
        }

        Assert.Fail(
            "No seed from 1 to 50 seated the downed member first in turn order; widen the search range.");
    }

    /// <summary>
    /// Criterion 4: a member marked dead is excluded from the fight the way a run's own
    /// dead are (<see cref="Gauntlet.BeginNext"/>'s <c>survivors</c> filter) — no
    /// combatant is built for it at all.
    /// </summary>
    [Fact]
    public void AMemberMarkedDeadIsExcludedFromTheResolvedParty()
    {
        var scenario = Roster("monster.ogre") with
        {
            Party = new ScenarioParty
            {
                Members =
                [
                    new ScenarioMember { Level = 3, Draft = Fighter },
                    new ScenarioMember
                    {
                        Level = 3,
                        Draft = Barbarian,
                        StartingState = new ScenarioStartingState { IsDead = true },
                    },
                ],
            },
        };

        var resolved = ScenarioContent.ResolveParty(scenario, Content);

        Assert.Equal([Fighter.Name], resolved.Select(member => member.Draft.Name));
    }

    /// <summary>
    /// A dead member's other fields are validated exactly like a living member's —
    /// <see cref="ScenarioContent.ResolveParty"/> excludes a dead member from the fielded
    /// fight, never from the check. A Codex review of this PR caught the previous shape
    /// of this test entrenching the opposite reading: it asserted an illegal hit-point
    /// value beside <c>IsDead = true</c> passed clean, when criterion 2 ("every value is
    /// validated") makes no exception for the dead. Only <em>where the member ends up</em>
    /// (excluded from the returned list) is what <see cref="ScenarioStartingState.IsDead"/>
    /// decides.
    /// </summary>
    [Fact]
    public void ADeadMembersOtherFieldsAreValidatedLikeALivingMembersBeforeExclusion()
    {
        var scenario = SoloMember(new ScenarioMember
        {
            Level = 3,
            Draft = Fighter,
            StartingState = new ScenarioStartingState { IsDead = true, CurrentHitPoints = 999 },
        });

        var failure = Assert.Throws<InvalidDataException>(() => ScenarioContent.ResolveParty(scenario, Content));

        Assert.Contains(Fighter.Name, failure.Message, StringComparison.Ordinal);
        Assert.Contains("999", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The complement: a dead member whose other fields are all legal is excluded from
    /// the fielded fight cleanly, the same as before this PR's exclusion-order fix —
    /// exclusion still works, it is just no longer a way to smuggle an unchecked value
    /// past validation.
    /// </summary>
    [Fact]
    public void ADeadMemberWithLegalFieldsIsStillExcludedFromTheResolvedParty()
    {
        var scenario = SoloMember(new ScenarioMember
        {
            Level = 3,
            Draft = Fighter,
            StartingState = new ScenarioStartingState { IsDead = true, CurrentHitPoints = 1 },
        });

        Assert.Empty(ScenarioContent.ResolveParty(scenario, Content));
    }

    private static BattleScenario SoloMember(ScenarioMember member) => new()
    {
        FormatVersion = ScenarioFile.CurrentFormatVersion,
        Name = "starting-state-check",
        Notes = "A fixture for S8 starting-state checks.",
        Party = new ScenarioParty { Members = [member] },
        Enemies = new ScenarioEnemies
        {
            Roster = [new ScenarioRosterEntry { MonsterId = "monster.ogre", Count = 1 }],
        },
    };

    private static BattleScenario Roster(params string[] monsterIds) => new()
    {
        FormatVersion = ScenarioFile.CurrentFormatVersion,
        Name = "check",
        Notes = "A fixture for the content checks.",
        Party = new ScenarioParty { PregeneratedLevel = 3 },
        Enemies = new ScenarioEnemies
        {
            Roster = [.. monsterIds.Select(id => new ScenarioRosterEntry { MonsterId = id, Count = 1 })],
        },
    };
}
