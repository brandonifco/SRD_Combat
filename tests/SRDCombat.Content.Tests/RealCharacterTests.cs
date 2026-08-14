using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;

namespace SRDCombat.Content.Tests;

/// <summary>
/// Builds real characters from the real extracted SRD content and puts them in a real
/// fight. This is what joins the three phases together: extraction, character
/// resolution, and the combat engine.
/// </summary>
public class RealCharacterTests
{
    private static readonly SrdContent Content = ContentLoader.Load(RepositoryPaths.SrdContentDirectory);

    [Fact]
    public void ALevelOneFighterResolvesToThePrintedNumbers()
    {
        var sheet = Build("class.fighter", "species.dwarf", "background.soldier", level: 1);

        // Dwarf: Medium, 30 ft. Fighter: d10 hit die, Strength and Constitution saves.
        Assert.Equal("Fighter", sheet.ClassName);
        Assert.Equal(30, sheet.SpeedFeet);
        Assert.Equal(2, sheet.ProficiencyBonus);

        // Soldier offers Strength, Dexterity and Constitution; this draft takes +2 on the
        // first and +1 on the second, so Strength 15 -> 17 and Dexterity 13 -> 14.
        Assert.Equal(17, sheet.AbilityScores[Ability.Strength]);
        Assert.Equal(14, sheet.AbilityScores[Ability.Dexterity]);

        // d10 maximum plus a +2 Constitution modifier.
        Assert.Equal(12, sheet.MaximumHitPoints);

        // Chain Mail is a flat 16, and a Shield takes it to 18.
        Assert.Equal(18, sheet.ArmorClass);

        // Proficient Strength save: +3 modifier, +2 proficiency.
        Assert.Equal(5, sheet.SavingThrows[Ability.Strength]);

        // Dexterity is not a Fighter save, so it is the bare +2 modifier.
        Assert.Equal(2, sheet.SavingThrows[Ability.Dexterity]);
    }

    [Fact]
    public void AFighterGainsExtraAttackAtLevelFive()
    {
        Assert.Equal(1, Build("class.fighter", "species.dwarf", "background.soldier", 1).AttacksPerAction);

        var level5 = Build("class.fighter", "species.dwarf", "background.soldier", 5);

        Assert.True(level5.Has(ClassFeature.ExtraAttack));
        Assert.Equal(2, level5.AttacksPerAction);
        Assert.Equal(3, level5.ProficiencyBonus);
    }

    [Fact]
    public void ABarbarianGetsRageAndUnarmoredDefenseFromRealContent()
    {
        var barbarian = Build("class.barbarian", "species.goliath", "background.soldier", 1, armorId: null);

        Assert.True(barbarian.Has(ClassFeature.Rage));
        Assert.True(barbarian.Has(ClassFeature.UnarmoredDefenseBarbarian));

        // 10 + Dex 2 + Con 2, from the feature rather than from armour.
        Assert.Equal(14, barbarian.ArmorClass);
        Assert.Contains("Unarmored Defense", barbarian.ArmorClassSource, StringComparison.Ordinal);

        // Goliath is the one species that walks 35 feet.
        Assert.Equal(35, barbarian.SpeedFeet);
    }

    [Fact]
    public void ALevelFiveBarbarianSensesDangerAndMovesFaster()
    {
        var barbarian = Build("class.barbarian", "species.goliath", "background.soldier", 5, armorId: null);

        Assert.True(barbarian.Has(ClassFeature.DangerSense));
        Assert.True(barbarian.Has(ClassFeature.FastMovement));

        // Goliath 35, +10 from Fast Movement out of Heavy armour.
        Assert.Equal(45, barbarian.SpeedFeet);
    }

    [Fact]
    public void ARogueGetsSneakAttackAndCunningAction()
    {
        var rogue = Build("class.rogue", "species.halfling", "background.criminal", 5, armorId: null);

        Assert.True(rogue.Has(ClassFeature.SneakAttack));
        Assert.True(rogue.Has(ClassFeature.CunningAction));
        Assert.True(rogue.Has(ClassFeature.UncannyDodge));
        Assert.True(rogue.Has(ClassFeature.SteadyAim));

        // The Rogue's Sneak Attack dice come straight off the class table.
        Assert.Equal("3d6", Content.ClassesById["class.rogue"].AtLevel(5)!.Resources["Sneak Attack"]);
    }

    [Fact]
    public void ACastersSheetReportsSlotsAndSaysWhatIsNotImplemented()
    {
        var cleric = Build("class.cleric", "species.human", "background.acolyte", 5);

        Assert.Equal(new Dictionary<int, int> { [1] = 4, [2] = 3, [3] = 2 }, cleric.SpellSlots);

        // The honest half: the sheet states outright that its casting does nothing yet,
        // rather than presenting a Cleric that silently cannot cast.
        Assert.Contains("Spellcasting", cleric.UnimplementedFeatures);

        // Channel Divinity executes (Divine Spark), so the name is claimed — while the
        // features hanging off its unimplemented half stay reported: Sear Undead rides
        // Turn Undead, and Turn Undead is refused for its unmodelled early outs.
        Assert.True(cleric.Has(ClassFeature.ChannelDivinity));
        Assert.DoesNotContain("Channel Divinity", cleric.UnimplementedFeatures);
        Assert.Contains("Sear Undead", cleric.UnimplementedFeatures);
    }

    [Fact]
    public void ARealPartyFightsRealMonstersToAConclusion()
    {
        var party = new[]
        {
            Combatant("Ferrin", Build("class.fighter", "species.dwarf", "background.soldier", 3), 0, 3),
            Combatant("Bruk", Build("class.barbarian", "species.goliath", "background.soldier", 3, armorId: null), 0, 4),
        };

        var goblins = new[]
        {
            Spawn("goblin-1", "monster.goblin-warrior", 10, 3),
            Spawn("goblin-2", "monster.goblin-warrior", 10, 4),
            Spawn("goblin-3", "monster.goblin-warrior", 11, 3),
        };

        var encounter = Encounter.Start(
            new Battlefield(14, 8),
            party.Concat(goblins),
            new SeededRandomSource(4242));

        SimpleTacticsPolicy.RunToCompletion(encounter);

        Assert.True(encounter.IsComplete);
        Assert.NotNull(encounter.WinningSide);
        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.Damage);
    }

    [Fact]
    public void CharactersFallUnconsciousWhereMonstersDie()
    {
        // The difference that matters most when a character enters combat.
        var sheet = Build("class.wizard", "species.gnome", "background.sage", 1, armorId: null);
        var wizard = Combatant("Wizard", sheet, 0, 0);

        Assert.False(wizard.Stats.DiesAtZeroHitPoints);

        SRDCombat.Core.Rules.DamageRules.Apply(wizard, sheet.MaximumHitPoints, DamageType.Slashing);

        Assert.False(wizard.IsDead);
        Assert.True(wizard.IsDying);
        Assert.True(wizard.HasCondition(ConditionType.Unconscious));
    }

    [Fact]
    public void ARealWizardCastsRealSpellsAtRealMonsters()
    {
        var sheet = Build("class.wizard", "species.gnome", "background.sage", 5, armorId: null);

        var spells = new[]
        {
            Content.SpellsById["spell.fire-bolt"],
            Content.SpellsById["spell.fireball"],
        };

        var wizard = Combatant("Wizard", sheet, 0, 4, spells);

        var goblins = Enumerable.Range(0, 3)
            .Select(index => Spawn($"goblin-{index}", "monster.goblin-warrior", 8 + index, 4))
            .ToArray();

        var encounter = Encounter.Start(
            new Battlefield(16, 10),
            goblins.Prepend(wizard),
            new SeededRandomSource(99));

        // Initiative is rolled, so wait for the wizard's turn rather than assuming it
        // acts first.
        while (encounter.ActiveCombatant?.Id != wizard.Id && !encounter.IsComplete)
        {
            SimpleTacticsPolicy.TakeTurn(encounter);
        }

        // A Fireball centred on the goblins catches all three. Aimed at where they
        // actually stand rather than where they spawned, because the goblins may have
        // taken turns before the wizard's — and the policy moves them with a mind of
        // its own (since #108 they fan out rather than queueing behind each other).
        // The 20-foot radius reaches every goblin from their midpoint by construction.
        var midpoint = new GridPosition(
            (int)Math.Round(goblins.Average(goblin => goblin.Position.X)),
            (int)Math.Round(goblins.Average(goblin => goblin.Position.Y)));

        Assert.Null(encounter.CastSpell("spell.fireball", midpoint));

        Assert.Contains(encounter.Log, step => step.Kind == CombatStepKind.Spell);
        Assert.All(goblins, goblin =>
            Assert.True(goblin.CurrentHitPoints < goblin.Stats.MaximumHitPoints || goblin.IsDead));

        Assert.Contains(
            encounter.Log,
            step => step.Narration.Contains("Fireball fills a 20-foot Sphere", StringComparison.Ordinal));

        // A level 3 slot was spent; the wizard has 4/3/2 at level 5.
        Assert.Equal(1, wizard.Features.SpellSlotsRemaining[3]);
    }

    [Fact]
    public void ARealWizardsSaveDifficultyClassMatchesTheRules()
    {
        var sheet = Build("class.wizard", "species.gnome", "background.sage", 5, armorId: null);
        var wizard = Combatant("Wizard", sheet, 0, 0, [Content.SpellsById["spell.fire-bolt"]]);

        // 8 + proficiency + Intelligence modifier.
        var expected = 8 + sheet.ProficiencyBonus + sheet.Modifier(Ability.Intelligence);

        Assert.Equal(expected, wizard.Stats.Character!.SpellSaveDifficultyClass);
        Assert.Equal(
            sheet.ProficiencyBonus + sheet.Modifier(Ability.Intelligence),
            wizard.Stats.Character.SpellAttackBonus);
    }

    private static CharacterSheet Build(
        string classId,
        string speciesId,
        string backgroundId,
        int level,
        string? armorId = "armor.chain-mail")
    {
        var background = Content.BackgroundsById[backgroundId];

        var draft = new CharacterDraft
        {
            Name = "Test",
            SpeciesId = speciesId,
            ClassId = classId,
            BackgroundId = backgroundId,
            Level = level,
            BaseAbilityScores = new Dictionary<Ability, int>
            {
                [Ability.Strength] = 15,
                [Ability.Dexterity] = 13,
                [Ability.Constitution] = 14,
                [Ability.Intelligence] = 12,
                [Ability.Wisdom] = 10,
                [Ability.Charisma] = 8,
            },
            // Take the +2/+1 on whichever abilities this background actually offers.
            PrimaryIncrease = background.AbilityScores[0],
            SecondaryIncrease = background.AbilityScores[1],
            WeaponIds = ["weapon.longsword"],
            ArmorId = armorId,
            HasShield = armorId is not null,
        };

        return CharacterResolver.Resolve(
            draft,
            new CharacterBuildContent(
                Content.SpeciesById[speciesId],
                Content.ClassesById[classId],
                background,
                Content.WeaponsById,
                Content.ArmorById));
    }

    private static Combatant Combatant(
        string name,
        CharacterSheet sheet,
        int x,
        int y,
        IReadOnlyList<SpellDefinition>? spells = null)
    {
        var classLevel = Content.ClassesById["class." + sheet.ClassName.ToLowerInvariant()].AtLevel(sheet.Level)!;

        var sneakAttack = classLevel.Resources.TryGetValue("Sneak Attack", out var dice)
            && DiceExpression.TryParse(dice, out var parsed)
                ? parsed
                : null;

        return new Combatant(
            name,
            name,
            "the party",
            CombatantStats.FromCharacter(
                sheet,
                sneakAttack,
                classLevel.ResourceCount("Rage Damage") ?? 0,
                classLevel.ResourceCount("Rages") ?? 0,
                classLevel.ResourceCount("Second Wind") ?? 0,
                actionSurgeUses: 0,
                spells,
                spells is null
                    ? null
                    : SpellcastingRules.AbilityFor("class." + sheet.ClassName.ToLowerInvariant())),
            new GridPosition(x, y));
    }

    private static Combatant Spawn(string id, string monsterId, int x, int y)
    {
        var monster = Content.MonstersById[monsterId];

        return new Combatant(id, monster.Name, "the goblins", CombatantStats.FromMonster(monster), new GridPosition(x, y));
    }
}
