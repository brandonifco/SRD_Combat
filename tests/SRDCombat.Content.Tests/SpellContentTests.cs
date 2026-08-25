using SRDCombat.Content.Validation;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Content.Tests;

/// <summary>
/// Covers the extracted Spell Descriptions against what the SRD prints.
/// </summary>
public class SpellContentTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void ASpellWhoseClassListWrapsIsStillExtracted()
    {
        // Cure Wounds prints five classes, which overflow the column: "Level 1
        // Abjuration (Bard, Cleric, Druid, Paladin," then "Ranger)". The type grammar is
        // anchored on its closing bracket, so every wrapped spell went undetected — 38
        // of them, silently, because a spell that is never detected raises no diagnostic.
        var cure = Content.Spells.Single(spell => spell.Name == "Cure Wounds");

        Assert.Equal(1, cure.Level);
        Assert.Equal(MagicSchool.Abjuration, cure.School);
        Assert.Equal(
            new[] { "Bard", "Cleric", "Druid", "Paladin", "Ranger" },
            cure.Classes);
    }

    [Fact]
    public void GuidingBoltCarriesItsAdvantageRiderAndNothingElseDoes()
    {
        // "and the next attack roll made against it before the end of your next turn
        // has Advantage" — structured at extraction like Sacred Flame's cover clause,
        // and printed exactly once in the book, so the flag's census is an exact count
        // rather than a floor: the sentence occurring anywhere new should be a
        // deliberate discovery, not a silent match.
        var lit = Content.Spells
            .Where(spell => spell.GrantsAdvantageAgainstTargetOnHit)
            .ToArray();

        var bolt = Assert.Single(lit);
        Assert.Equal("Guiding Bolt", bolt.Name);
        Assert.True(bolt.IsSpellAttack);
    }

    [Fact]
    public void TheOneSmallCapsHeadingIsRepaired()
    {
        // Acid Splash alone is set in GillSans-SemiBold-SC700, and small caps reach the
        // text layer as "Ac i d Sp lASh". It is repaired from a curated map keyed on that
        // exact text, so a better reader would stop matching rather than be overridden.
        var splash = Content.Spells.Single(spell => spell.Name == "Acid Splash");

        Assert.Equal(0, splash.Level);
        Assert.Equal(new[] { "Sorcerer", "Wizard" }, splash.Classes);
    }

    [Fact]
    public void EverySpellAClassListNamesExists()
    {
        // The cross-reference that would have caught the missing spells on day one: a
        // class's printed spell list names Cure Wounds, so an extraction without it
        // disagrees with itself.
        var known = Content.Spells.Select(spell => spell.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in new[] { "Cure Wounds", "Detect Magic", "Hold Person", "Aid", "Healing Word" })
        {
            Assert.Contains(name, known);
        }
    }

    [Fact]
    public void TheWholeSpellListIsExtracted()
    {
        // Exact, not a floor. This read ">= 300" for months and was satisfied by exactly
        // the broken number while 39 spells were missing — a floor is the right shape
        // for something that should grow as the engine models more, and the wrong shape
        // for a count fixed by the source document.
        Assert.Equal(SpellValidator.ExpectedSpellCount, Content.Spells.Count);

        // Levels 0-9 are all represented, which is a cheap check that no band was missed.
        Assert.Equal(
            Enumerable.Range(0, 10),
            Content.Spells.Select(spell => spell.Level).Distinct().Order());

        Assert.All(Content.Spells, spell => Assert.NotEmpty(spell.Classes));
    }

    [Fact]
    public void ACantripIsLevelZero()
    {
        var fireBolt = Content.SpellsById["spell.fire-bolt"];

        Assert.True(fireBolt.IsCantrip);
        Assert.Equal(0, fireBolt.Level);
        Assert.Equal(MagicSchool.Evocation, fireBolt.School);
        Assert.Equal(["Sorcerer", "Wizard"], fireBolt.Classes);
    }

    [Fact]
    public void ALevelledSpellMatchesItsPrintedHeader()
    {
        // "Level 3 Evocation (Sorcerer, Wizard) / Casting Time: Action / Range: 150 feet
        //  / Components: V, S, M (a ball of bat guano and sulfur) / Duration: Instantaneous"
        var fireball = Content.SpellsById["spell.fireball"];

        Assert.Equal(3, fireball.Level);
        Assert.Equal(MagicSchool.Evocation, fireball.School);
        Assert.Equal(SpellCastingTime.Action, fireball.CastingTime);
        Assert.Equal(150, fireball.RangeFeet);
        Assert.False(fireball.RequiresConcentration);

        Assert.True(fireball.Components.HasFlag(SpellComponents.Verbal));
        Assert.True(fireball.Components.HasFlag(SpellComponents.Somatic));
        Assert.True(fireball.Components.HasFlag(SpellComponents.Material));
        Assert.NotNull(fireball.MaterialComponent);
    }

    [Fact]
    public void SavingThrowSpellsAreStructured()
    {
        // The spell grammar differs from the stat block one in substance: no printed DC,
        // because it comes from the caster, and no precomputed average.
        var fireball = Content.SpellsById["spell.fireball"];
        var save = Assert.IsType<SaveEffect>(fireball.Save);

        Assert.Equal(Ability.Dexterity, save.Ability);
        Assert.Null(save.DifficultyClass);
        Assert.Equal(SaveSuccessOutcome.HalfDamage, save.SuccessOutcome);

        var area = Assert.IsType<EffectArea>(save.Area);
        Assert.Equal(AreaShape.Sphere, area.Shape);
        Assert.Equal(20, area.SizeFeet);

        Assert.Equal("8d6", save.FailureDamage[0].Amount.ToString());
        Assert.Equal(DamageType.Fire, save.FailureDamage[0].Type);
    }

    [Fact]
    public void AConeSpellReadsItsArea()
    {
        var save = Assert.IsType<SaveEffect>(Content.SpellsById["spell.burning-hands"].Save);
        var area = Assert.IsType<EffectArea>(save.Area);

        Assert.Equal(AreaShape.Cone, area.Shape);
        Assert.Equal(15, area.SizeFeet);
    }

    [Fact]
    public void AttackSpellsAreDistinguishedFromSaveSpells()
    {
        var fireBolt = Content.SpellsById["spell.fire-bolt"];

        Assert.True(fireBolt.IsSpellAttack);
        Assert.Null(fireBolt.Save);
        Assert.Equal(EntryMechanics.Attack, fireBolt.Mechanics);

        // The damage is captured for attack spells too, not just save spells.
        Assert.Equal("1d10", Assert.Single(fireBolt.Damage).Amount.ToString());
        Assert.Equal(DamageType.Fire, fireBolt.Damage[0].Type);

        Assert.False(Content.SpellsById["spell.fireball"].IsSpellAttack);
    }

    [Fact]
    public void ConcentrationIsReadFromTheDuration()
    {
        // Over a hundred spells need Concentration; it is stated only in the duration.
        Assert.True(Content.Spells.Count(spell => spell.RequiresConcentration) > 100);

        Assert.All(
            Content.Spells.Where(spell => spell.RequiresConcentration),
            spell => Assert.Contains("Concentration", spell.DurationText, StringComparison.OrdinalIgnoreCase));

        Assert.All(
            Content.Spells.Where(spell => !spell.RequiresConcentration),
            spell => Assert.DoesNotContain("Concentration", spell.DurationText, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CastingTimesAreClassifiedForTheActionEconomy()
    {
        // Most spells cost an Action; a handful are Bonus Actions or Reactions, and the
        // rest take a minute or more and cannot be cast in a fight at all.
        var usable = Content.Spells.Count(spell => spell.IsUsableInCombat);

        Assert.True(usable > 200, $"Only {usable} spells are castable in combat.");
        Assert.Contains(Content.Spells, spell => spell.CastingTime == SpellCastingTime.BonusAction);
        Assert.Contains(Content.Spells, spell => spell.CastingTime == SpellCastingTime.Reaction);
        Assert.Contains(Content.Spells, spell => !spell.IsUsableInCombat);
    }

    [Fact]
    public void RangeIsReadAsFeetOnlyWhereItIsADistance()
    {
        // "Self" and "Touch" have no numeric range, and "Self (15-foot Cone)" must not
        // report 15 feet — that is the area's size, not how far it reaches.
        Assert.Null(Content.SpellsById["spell.burning-hands"].RangeFeet);
        Assert.Equal(120, Content.SpellsById["spell.fire-bolt"].RangeFeet);

        Assert.All(
            Content.Spells.Where(spell => spell.RangeText.StartsWith("Self", StringComparison.Ordinal)),
            spell => Assert.Null(spell.RangeFeet));
    }

    [Fact]
    public void UpcastingIsKeptAsTextRatherThanStructured()
    {
        // Upcasting is not implemented, and structuring it would imply it is.
        var fireball = Content.SpellsById["spell.fireball"];

        Assert.NotNull(fireball.ScalingText);
        Assert.Contains("Higher-Level Spell Slot", fireball.ScalingText, StringComparison.Ordinal);

        // The scaling clause is held apart from the spell's own description, so its dice
        // do not leak into the spell's damage.
        Assert.DoesNotContain("Higher-Level", fireball.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySpellIsClassified()
    {
        Assert.All(Content.Spells, spell => Assert.True(Enum.IsDefined(spell.Mechanics)));

        Assert.All(
            Content.Spells.Where(spell => spell.Mechanics == EntryMechanics.Unmodelled),
            spell => Assert.NotEmpty(spell.UnclassifiedClauses));
    }

    [Fact]
    public void AStructuredSpellCarriesNoClauseListAndMakesNoCompletenessClaim()
    {
        // The invariant the field actually holds, both directions: clauses exactly when
        // the classifier reached no classification at all. Pinned because the tempting
        // misreading is the other one — that an empty list means the printed spell is
        // fully expressed. It does not, and there is no longer a property that says so
        // (#292): the four spells below classify structurally and are wrong in print,
        // each for a clause the engine never runs, and PreparableSpells refuses all four.
        Assert.All(
            Content.Spells.Where(spell => spell.Mechanics != EntryMechanics.Unmodelled),
            spell => Assert.Empty(spell.UnclassifiedClauses));

        var counterexamples = new[]
        {
            // The Restrained rider is extraction-refused and the only damage the shape
            // kept is the 2d4 Fire of burning webs, so the structured form is a one-shot
            // 20-foot Cube of fire — not the printed spell in any part.
            "spell.web",

            // "A creature must also make this save when the Sphere moves into its space
            // ..." — the fog persists and moves; the casting is one save and done.
            "spell.cloudkill",

            // Delayed damage at the end of the target's next turn, and damage on a miss.
            "spell.acid-arrow",

            // "one of the following effects of your choice" — an effect that is a menu,
            // of which the shape kept only the fourth bullet's 1d8.
            "spell.bestow-curse",
        };

        foreach (var id in counterexamples)
        {
            var spell = Content.SpellsById[id];

            Assert.NotEqual(EntryMechanics.Unmodelled, spell.Mechanics);
            Assert.Empty(spell.UnclassifiedClauses);
        }
    }

    [Fact]
    public void EveryClassSpellListNamesAKnownClass()
    {
        var known = Content.Classes.Select(definition => definition.Name).ToHashSet(StringComparer.Ordinal);

        var unknown = Content.Spells
            .SelectMany(spell => spell.Classes)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !known.Contains(name))
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void ASpellMissingItsComponentsIsAWarningRatherThanAnError()
    {
        // Six spells sit at the foot of a column and have their Components and Duration
        // lines missing from the PDF's text layer entirely — a defect in the source, not
        // in the parser, confirmed with two independent extractors. Inventing plausible
        // values would be worse than shipping the gap visibly.
        var result = SpellValidator.Validate(Content.Spells);

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, issue => issue.Code == "spell.components.none");
    }

    [Fact]
    public void WaterBreathingIsNotGradedNarrativeByNameCollisionWithABestiaryTrait()
    {
        // Water Breathing used to land on EntryMechanics.Narrative only because it
        // shares its exact printed name with the bestiary's Amphibious/Water
        // Breathing/Illumination inert list — a list curated about stat block and
        // species/class trait text, never read against this spell's own prose (#349).
        // It now grades Unmodelled like every spell the grammar does not structure —
        // Light, Alarm and Comprehend Languages among 184 others — with both its
        // sentences counted rather than silently waved through by an accidental match.
        var waterBreathing = Content.Spells.Single(spell => spell.Name == "Water Breathing");

        Assert.Equal(EntryMechanics.Unmodelled, waterBreathing.Mechanics);
        Assert.Equal(2, waterBreathing.UnclassifiedClauses.Count);

        // The fix is general, not a one-spell patch: no spell may reach Narrative by
        // consulting a list that was never curated about spell prose. If this fails,
        // either KnownInertEntries grew a name a spell also carries, or SpellParser
        // stopped passing consultInertList: false.
        Assert.DoesNotContain(Content.Spells, spell => spell.Mechanics == EntryMechanics.Narrative);
    }

    [Fact]
    public void SacredFlameDeniesItsTargetCover()
    {
        // "The target gains no benefit from Half Cover or Three-Quarters Cover for this
        // save." Left as prose, this sentence would have quietly weakened the spell
        // below its printed self the day cover landed — the Cleric's own cantrip, cast
        // every fight.
        var flame = Content.Spells.Single(spell => spell.Name == "Sacred Flame");

        Assert.NotNull(flame.Save);
        Assert.True(flame.Save!.CoverIgnored);

        // And exactly one spell prints that sentence, so nothing else was swept in.
        Assert.Single(Content.Spells, spell => spell.Save?.CoverIgnored == true);
    }

    [Fact]
    public void SpiritGuardiansDealsOnePrintedDamageSetWithTheAlternativeCounted()
    {
        // #375: "takes 3d8 Radiant damage (if you are good or neutral) or 3d8 Necrotic
        // damage (if you are evil)" is one roll with an alignment-gated type, not two
        // rolls of two types. The old extraction summed both branches into a 6d8
        // double print; this pins the printed 3d8 in both serialized sites, with the
        // alternative carried on EvilCasterDamageType rather than dropped.
        var spiritGuardians = Content.Spells.Single(spell => spell.Id == "spell.spirit-guardians");

        Assert.Equal(DamageType.Necrotic, spiritGuardians.EvilCasterDamageType);

        var expected = new[] { ("3d8", DamageType.Radiant) };
        AssertComponents(expected, spiritGuardians.Damage);
        AssertComponents(expected, spiritGuardians.Save?.FailureDamage ?? []);

        // Exactly one spell in the whole corpus carries this field — the validator's
        // own check, re-asserted here against the real committed content rather than a
        // hand-built list.
        Assert.Single(Content.Spells, spell => spell.EvilCasterDamageType is not null);
    }

    public static IEnumerable<object[]> MultiComponentSpellIds() => new[]
    {
        "spell.spirit-guardians",
        "spell.flame-strike",
        "spell.ice-storm",
        "spell.meteor-swarm",
        "spell.acid-arrow",
        "spell.ice-knife",
        "spell.tsunami",
        "spell.vitriolic-sphere",
        "spell.wall-of-ice",
        "spell.wall-of-thorns",
        "spell.weird",
        "spell.storm-of-vengeance",
        "spell.fire-shield",
        "spell.prismatic-spray",
        "spell.prismatic-wall",
    }.Select(id => new object[] { id });

    /// <summary>
    /// Census regression for every multi-component spell in the corpus (#375): the
    /// fourteen spells that genuinely print more than one damage component — three
    /// simultaneous "and"s the fix must be structurally unable to touch (Flame Strike,
    /// Ice Storm, Meteor Swarm), eight flattened multi-event "and"s that are a
    /// separate, already-stated limitation (Acid Arrow, Ice Knife, Tsunami, Vitriolic
    /// Sphere, Wall of Ice, Wall of Thorns, Weird, Storm of Vengeance), and three
    /// unrelated or-shapes out of scope for this fix (Fire Shield, Prismatic Spray,
    /// Prismatic Wall) — plus Spirit Guardians itself, now down to one component. Exact
    /// dice, type and count, not floors: any of the fourteen moving is the fix leaking
    /// past its scope.
    /// </summary>
    [Theory]
    [MemberData(nameof(MultiComponentSpellIds))]
    public void MultiComponentSpellDamageIsUnchangedExceptSpiritGuardians(string id)
    {
        var spell = Content.Spells.Single(candidate => candidate.Id == id);

        var expected = ExpectedMultiComponentDamage[id];
        AssertComponents(expected.Damage, spell.Damage);
        AssertComponents(expected.FailureDamage, spell.Save?.FailureDamage ?? []);
    }

    private static readonly IReadOnlyDictionary<string, (
        (string Dice, DamageType Type)[] Damage,
        (string Dice, DamageType Type)[] FailureDamage)> ExpectedMultiComponentDamage =
        new Dictionary<string, ((string, DamageType)[], (string, DamageType)[])>(StringComparer.Ordinal)
        {
            ["spell.spirit-guardians"] = (
                [("3d8", DamageType.Radiant)],
                [("3d8", DamageType.Radiant)]),
            ["spell.flame-strike"] = (
                [("5d6", DamageType.Fire), ("5d6", DamageType.Radiant)],
                [("5d6", DamageType.Fire), ("5d6", DamageType.Radiant)]),
            ["spell.ice-storm"] = (
                [("2d10", DamageType.Bludgeoning), ("4d6", DamageType.Cold)],
                [("2d10", DamageType.Bludgeoning), ("4d6", DamageType.Cold)]),
            ["spell.meteor-swarm"] = (
                [("20d6", DamageType.Fire), ("20d6", DamageType.Bludgeoning)],
                [("20d6", DamageType.Fire), ("20d6", DamageType.Bludgeoning)]),
            ["spell.acid-arrow"] = (
                [("4d4", DamageType.Acid), ("2d4", DamageType.Acid)],
                []),
            ["spell.ice-knife"] = (
                [("1d10", DamageType.Piercing), ("2d6", DamageType.Cold)],
                [("1d10", DamageType.Piercing), ("2d6", DamageType.Cold)]),
            ["spell.tsunami"] = (
                [("6d10", DamageType.Bludgeoning), ("5d10", DamageType.Bludgeoning)],
                [("6d10", DamageType.Bludgeoning), ("5d10", DamageType.Bludgeoning)]),
            ["spell.vitriolic-sphere"] = (
                [("10d4", DamageType.Acid), ("5d4", DamageType.Acid)],
                [("10d4", DamageType.Acid), ("5d4", DamageType.Acid)]),
            ["spell.wall-of-ice"] = (
                [("10d6", DamageType.Cold), ("5d6", DamageType.Cold)],
                [("10d6", DamageType.Cold), ("5d6", DamageType.Cold)]),
            ["spell.wall-of-thorns"] = (
                [("7d8", DamageType.Piercing), ("7d8", DamageType.Slashing)],
                [("7d8", DamageType.Piercing), ("7d8", DamageType.Slashing)]),
            ["spell.weird"] = (
                [("10d10", DamageType.Psychic), ("5d10", DamageType.Psychic)],
                [("10d10", DamageType.Psychic), ("5d10", DamageType.Psychic)]),
            ["spell.storm-of-vengeance"] = (
                [
                    ("2d6", DamageType.Thunder),
                    ("4d6", DamageType.Acid),
                    ("10d6", DamageType.Lightning),
                    ("2d6", DamageType.Bludgeoning),
                    ("1d6", DamageType.Cold),
                ],
                [
                    ("2d6", DamageType.Thunder),
                    ("4d6", DamageType.Acid),
                    ("10d6", DamageType.Lightning),
                    ("2d6", DamageType.Bludgeoning),
                    ("1d6", DamageType.Cold),
                ]),
            ["spell.fire-shield"] = (
                [("2d8", DamageType.Fire), ("2d8", DamageType.Cold)],
                []),
            ["spell.prismatic-spray"] = (
                [
                    ("12d6", DamageType.Fire),
                    ("12d6", DamageType.Acid),
                    ("12d6", DamageType.Lightning),
                    ("12d6", DamageType.Poison),
                    ("12d6", DamageType.Cold),
                ],
                [
                    ("12d6", DamageType.Fire),
                    ("12d6", DamageType.Acid),
                    ("12d6", DamageType.Lightning),
                    ("12d6", DamageType.Poison),
                    ("12d6", DamageType.Cold),
                ]),
            ["spell.prismatic-wall"] = (
                [
                    ("12d6", DamageType.Fire),
                    ("12d6", DamageType.Acid),
                    ("12d6", DamageType.Lightning),
                    ("12d6", DamageType.Poison),
                    ("12d6", DamageType.Cold),
                ],
                [
                    ("12d6", DamageType.Fire),
                    ("12d6", DamageType.Acid),
                    ("12d6", DamageType.Lightning),
                    ("12d6", DamageType.Poison),
                    ("12d6", DamageType.Cold),
                ]),
        };

    private static void AssertComponents(
        IReadOnlyList<(string Dice, DamageType Type)> expected,
        IReadOnlyList<AttackDamage> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Dice, actual[index].Amount.ToString());
            Assert.Equal(expected[index].Type, actual[index].Type);
        }
    }
}
