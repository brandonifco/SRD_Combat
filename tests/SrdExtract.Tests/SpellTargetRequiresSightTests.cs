using SrdExtract.Parsing;

namespace SrdExtract.Tests;

/// <summary>
/// <see cref="SpellEffectParser.ParseTargetRequiresSight"/> — the spell half of #691's
/// "you can see" targeting claim, structured onto <c>SpellDefinition.TargetRequiresSight</c>
/// and, when the spell resolves through a save, folded onto <c>SaveEffect.TargetRequiresSight</c>
/// too (see <c>SpellParser.Parse</c>'s own wiring). Fixtures below are verbatim spell text
/// from <c>data/srd/spells.json</c> (page numbers as printed).
/// </summary>
public sealed class SpellTargetRequiresSightTests
{
    // ── The four executing spells that print the clause (#691's own evidence) ──────

    [Fact]
    public void HealingWordsSingularCreatureOfYourChoiceClaims()
    {
        // p.139, verbatim. A Heal, not a save — the whole reason this field lives on
        // SpellDefinition rather than only on SaveEffect.
        Assert.True(SpellEffectParser.ParseTargetRequiresSight(
            "A creature of your choice that you can see within range regains Hit Points " +
            "equal to 2d4 plus your spellcasting ability modifier."));
    }

    [Fact]
    public void HoldPersonsChooseAHumanoidClaims()
    {
        // p.141, verbatim.
        Assert.True(SpellEffectParser.ParseTargetRequiresSight(
            "Choose a Humanoid that you can see within range. The target must succeed on a " +
            "Wisdom saving throw or have the Paralyzed condition for the duration. At the end " +
            "of each of its turns, the target repeats the save, ending the spell on itself on a " +
            "success."));
    }

    [Fact]
    public void MindSpikesOneCreatureYouCanSeeClaims()
    {
        // p.149, verbatim.
        Assert.True(SpellEffectParser.ParseTargetRequiresSight(
            "You drive a spike of psionic energy into the mind of one creature you can see " +
            "within range. The target makes a Wisdom saving throw, taking 3d8 Psychic damage " +
            "on a failed save or half as much damage on a successful one."));
    }

    [Fact]
    public void SacredFlamesACreatureThatYouCanSeeClaims()
    {
        // p.159, verbatim.
        Assert.True(SpellEffectParser.ParseTargetRequiresSight(
            "Flame-like radiance descends on a creature that you can see within range. The " +
            "target must succeed on a Dexterity saving throw or take 1d8 Radiant damage. The " +
            "target gains no benefit from Half Cover or Three-Quarters Cover for this save."));
    }

    // ── Real-content negative: a spell that never prints the clause ────────────────

    [Fact]
    public void FireballsPointAimedTargetingDoesNotClaim()
    {
        // p.131, verbatim. "a point you choose within range" — a point, not a
        // creature, and the corpus never even prints "can see" here at all.
        Assert.False(SpellEffectParser.ParseTargetRequiresSight(
            "A bright streak flashes from you to a point you choose within range and then " +
            "blossoms with a low roar into a fiery explosion. Each creature in a " +
            "20-foot-radius Sphere centered on that point makes a Dexterity saving throw, " +
            "taking 8d6 Fire damage on a failed save or half as much damage on a successful " +
            "one."));
    }

    // ── Misattribution trip-wires (#407) ────────────────────────────────────────────

    [Fact]
    public void ViciousMockerysDisjunctiveCanSeeOrHearDoesNotClaim()
    {
        // p.171, verbatim. "one creature you can see or hear within range" — the
        // printed gate is disjunctive, so a target who can only be heard is still a
        // legal one. VisionRules.CanSee answers the sight half alone; claiming this
        // clause would refuse a target the print still allows, the exact
        // misattribution #691's own reasoning warns against.
        Assert.False(SpellEffectParser.ParseTargetRequiresSight(
            "You unleash a string of insults laced with subtle enchantments at one creature " +
            "you can see or hear within range. The target must succeed on a Wisdom saving " +
            "throw or take 1d6 Psychic damage and have Disadvantage on the next attack roll " +
            "it makes before the end of its next turn."));
    }

    [Fact]
    public void BanesPluralCreaturesOfYourChoiceDoesNotClaim()
    {
        // p.112, verbatim. "Up to three creatures of your choice that you can see" — a
        // plural, chosen-set target, not the singular-creature reading this field
        // states. The determiner (a/one/the) sitting directly against the noun is
        // what this pattern requires, and "three" is neither.
        Assert.False(SpellEffectParser.ParseTargetRequiresSight(
            "Up to three creatures of your choice that you can see within range must each " +
            "make a Charisma saving throw. Whenever a target that fails this save makes an " +
            "attack roll or a saving throw before the spell ends, the target must subtract " +
            "1d4 from the attack roll or save."));
    }
}
