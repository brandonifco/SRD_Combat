using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Game;

/// <summary>
/// What a scenario looks like against a particular build's content: what refuses, and
/// what is merely worth saying.
/// </summary>
/// <remarks>
/// The two lists are different in kind and that is the point. An <b>error</b> is a
/// scenario this build cannot run — a monster id the bestiary does not have, a draft the
/// resolver refuses. A <b>notice</b> is something a human should read and nothing should
/// act on: today the only one is a content-fingerprint mismatch, which is provenance
/// rather than identity (see <see cref="BattleScenario.ContentVersion"/>).
/// </remarks>
/// <param name="Errors">Reasons this build cannot run the scenario.</param>
/// <param name="Notices">Things worth saying that refuse nothing.</param>
public sealed record ScenarioCheck(IReadOnlyList<string> Errors, IReadOnlyList<string> Notices)
{
    /// <summary>Whether this build can run the scenario.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Where a scenario meets the content that gives its ids meaning — the
/// <see cref="GauntletRun.Resume"/> half of the split <see cref="ScenarioFile"/>'s
/// remarks describe.
/// </summary>
/// <remarks>
/// This resolves what a scenario's ids <em>name</em> — the party and the explicit cast —
/// and nothing about a fight. Rolling a board, spending a budget and assembling a
/// <c>Fight</c> is <see cref="ScenarioRunner"/>'s (S2, #474); what lives here is the part
/// that is about the <em>scenario</em>: turning authored ids into resolved members and
/// monster definitions, and answering whether this build can run the thing at all.
/// </remarks>
public static class ScenarioContent
{
    /// <summary>What a scenario's refusals call the thing that named a missing id.</summary>
    private const string Subject = "the scenario";

    /// <summary>
    /// What a scenario's refusals suggest doing about it. Not a save's remedy: a save is
    /// a run in progress that the writing build can still play, while a scenario is a
    /// question, and the answer to a question about content this build does not have is
    /// to ask it of the build that does — or to re-author it.
    /// </summary>
    private const string Remedy =
        "Re-author the scenario against this build's content, or run it on the build it was written for.";

    /// <summary>
    /// Checks a scenario against the content that gives its ids meaning: every id it
    /// names resolves, the party it describes resolves, and a fingerprint that disagrees
    /// is said out loud without refusing anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every id, not merely the ones a code path happens to reach.</b> A draft's
    /// species, class and background go through <see cref="ContentDrift.Require"/> inside
    /// <see cref="PregeneratedParty.Resolve"/>, and its weapons, armour and magic items
    /// are refused by <c>CharacterResolver</c> — but only along the branches that
    /// particular draft takes. This walks the ids first and reports every miss, so a
    /// scenario naming three vanished weapons is told about three of them, and only then
    /// attempts the resolution that catches everything else.
    /// </para>
    /// <para>
    /// The id sweep formats <see cref="ContentDrift.MissingMessage"/> rather than calling
    /// <see cref="ContentDrift.Require"/> in a loop and catching: same text,
    /// single-sourced, without using exceptions to walk a list. The resolution attempt
    /// below it does catch, because refusing is <c>CharacterResolver</c>'s published
    /// voice and the point here is to report it rather than die on the first bad file in
    /// a directory.
    /// </para>
    /// </remarks>
    public static ScenarioCheck CheckAgainst(BattleScenario scenario, SrdContent content)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(content);

        var errors = new List<string>();
        var notices = new List<string>();

        if (scenario.ContentVersion is { } version
            && !string.Equals(version, content.ContentFingerprint, StringComparison.Ordinal))
        {
            notices.Add(
                $"authored against different content (scenario {ContentDrift.Truncate(version)}, " +
                $"loaded {ContentDrift.Truncate(content.ContentFingerprint)}). " +
                "A scenario is a question asked of the current build, so this is provenance rather than a " +
                "refusal — every id it names is checked individually.");
        }

        foreach (var entry in scenario.Enemies.Roster ?? [])
        {
            RequireId(content.MonstersById, entry.MonsterId, "monster", scenario.Name, errors);
        }

        foreach (var member in scenario.Party.Members ?? [])
        {
            CheckMemberIds(member.Draft, content, errors);
        }

        try
        {
            _ = ResolveParty(scenario, content);
        }
        catch (Exception failure) when (
            failure is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            // The three types the resolution path refuses with: InvalidDataException from
            // ContentDrift.Require, ArgumentException from CharacterResolver's own
            // validation, InvalidOperationException from a class table with no row at the
            // level asked for.
            errors.Add($"the party does not resolve: {failure.Message}");
        }

        return new ScenarioCheck(errors, notices);
    }

    /// <summary>
    /// Turns a scenario's authored party into resolved members, seated in a column the
    /// way <see cref="PregeneratedParty.Build"/> seats its four.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The preset resolves through <see cref="PregeneratedParty.Build"/>, never
    /// through a stored copy of its drafts</b>, which is the whole reason the preset
    /// exists: a scenario that froze a copy of the pregenerated four would silently stop
    /// tracking a change to them, and the library would drift one file at a time with
    /// nothing failing. A scenario using the preset stores no drafts at all — read the
    /// JSON and there is a level and nothing else.
    /// </para>
    /// <para>
    /// Refuses rather than returns problems, in <c>CharacterResolver</c>'s and
    /// <see cref="ContentDrift"/>'s voice. <see cref="CheckAgainst"/> is the method for a
    /// caller that wants a list; this is the method for a caller that has already
    /// checked.
    /// </para>
    /// <para>
    /// <b>A member's <see cref="ScenarioStartingState"/> is applied here, once the sheet
    /// it must be validated against exists</b> (S8, #480). A null
    /// <see cref="ScenarioMember.StartingState"/> never calls
    /// <see cref="PregeneratedParty.CarryingOver"/> at all, which is what makes "absent
    /// means full strength" byte-identical to the fight this method built before this
    /// field existed, rather than merely equivalent to it (acceptance criterion 1). A
    /// member whose state marks it dead is <b>excluded from the fielded fight</b> — the
    /// way <see cref="Gauntlet.BeginNext"/>'s own <c>survivors</c> filter excludes a
    /// run's dead — reusing the same construction mechanism every other member goes
    /// through rather than skipping it. <see cref="PregeneratedParty.Resolve"/> still
    /// builds this member a combatant, exactly as it does for a member that stays, and
    /// <see cref="ValidateStartingState"/> still checks every one of its fields against
    /// that resolved sheet — an out-of-range value beside <c>IsDead: true</c> is refused
    /// by name exactly as it would be for a living member (criterion 2 makes no
    /// exception for the dead). Only the fielded party itself excludes the member
    /// afterwards, so no combatant from this member ever takes a spawn square or a turn.
    /// Every other value is checked against this member's own resolved
    /// <see cref="PartyMember.Sheet"/> and <see cref="Combatant.Stats"/> and refused by
    /// name, never clamped (criterion 2); see <see cref="ValidateStartingState"/>.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PartyMember> ResolveParty(BattleScenario scenario, SrdContent content)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(content);

        if (scenario.Party.PregeneratedLevel is { } level)
        {
            return PregeneratedParty.Build(content, level);
        }

        var members = scenario.Party.Members
            ?? throw new InvalidDataException(
                "the scenario names neither a pregenerated level nor members; "
                + "ScenarioFile.FromJson refuses this, so it was built in memory rather than loaded.");

        var living = new List<PartyMember>();

        foreach (var (member, index) in members.Select((member, index) => (member, index)))
        {
            var resolved = PregeneratedParty.Resolve(content, member.Draft, member.Level, x: 0, y: index);

            if (member.StartingState is not { } state)
            {
                living.Add(resolved);
                continue;
            }

            // Validated exactly like a living member's — an out-of-range value beside
            // IsDead: true is still an authored value nobody checked, and criterion 2
            // promises every value is validated, not every value on a member who
            // survives. Only the exclusion itself distinguishes a dead member: the
            // CombatantCarryOver this produces is thrown away rather than carried,
            // because a dead member's resolved values (hit points, resources) are never
            // fielded — there is no combatant a dead member's carry-over could apply to.
            var carryOver = ValidateStartingState(resolved, state);

            if (state.IsDead)
            {
                continue;
            }

            living.Add(resolved.CarryingOver(carryOver));
        }

        return living;
    }

    /// <summary>
    /// Turns a member's authored <see cref="ScenarioStartingState"/> into the
    /// <see cref="CombatantCarryOver"/> <see cref="PregeneratedParty.CarryingOver"/>
    /// takes, refusing by name against <paramref name="member"/>'s own resolved sheet
    /// wherever the author asked for more than the character has (#480 criterion 2).
    /// </summary>
    /// <remarks>
    /// <b>A dead member reaches here too</b> — <see cref="ResolveParty"/> calls this for
    /// every member that carries a <see cref="ScenarioStartingState"/>, dead or not, and
    /// excludes the dead only afterwards, by discarding the <see cref="CombatantCarryOver"/>
    /// this returns rather than by skipping the call. An out-of-range hit-point or
    /// resource count beside <c>IsDead: true</c> is refused by name exactly as it would be
    /// for a member who stays — criterion 2 checks every value, and a member marked dead
    /// is not an exemption from that. Zero hit points and not dead is legal here exactly
    /// as it is on <c>CharacterState</c>: downed-and-stable, not a range violation.
    /// </remarks>
    private static CombatantCarryOver ValidateStartingState(PartyMember member, ScenarioStartingState state)
    {
        var owner = member.Draft.Name;
        var sheet = member.Sheet;
        var character = member.Combatant.Stats.Character;

        if (state.CurrentHitPoints is { } hitPoints && (hitPoints < 0 || hitPoints > sheet.MaximumHitPoints))
        {
            throw new InvalidDataException(
                $"{owner}: starting hit points {hitPoints} is out of range (0-{sheet.MaximumHitPoints}).");
        }

        if (state.HitDiceRemaining is { } hitDice && (hitDice < 0 || hitDice > sheet.Level))
        {
            throw new InvalidDataException(
                $"{owner}: starting hit dice remaining {hitDice} is out of range (0-{sheet.Level}).");
        }

        RequireResourceInRange(owner, "rages remaining", state.RagesRemaining, character?.RageUses ?? 0);
        RequireResourceInRange(
            owner, "Second Wind uses remaining", state.SecondWindRemaining, character?.SecondWindUses ?? 0);
        RequireResourceInRange(
            owner, "Action Surge uses remaining", state.ActionSurgeRemaining, character?.ActionSurgeUses ?? 0);
        RequireResourceInRange(
            owner,
            "Channel Divinity uses remaining",
            state.ChannelDivinityRemaining,
            character?.ChannelDivinityUses ?? 0);

        if (state.SpellSlotsRemaining is { } slots)
        {
            foreach (var (slotLevel, count) in slots)
            {
                if (!sheet.SpellSlots.TryGetValue(slotLevel, out var maximum))
                {
                    throw new InvalidDataException(
                        $"{owner}: starting spell slots name level {slotLevel}, which this character has none of.");
                }

                if (count < 0 || count > maximum)
                {
                    throw new InvalidDataException(
                        $"{owner}: starting level {slotLevel} spell slots {count} is out of range (0-{maximum}).");
                }
            }
        }

        if (state.Potions is { } potions)
        {
            foreach (var (potency, count) in potions)
            {
                // The dictionary key is a bare enum value read off JSON as a number, so an
                // undefined potency (a typo, or a value written by a build with more
                // potencies than this one) is not caught by System.Text.Json the way an
                // unknown property name is — PotionRules.Healing(potency) is the first
                // place that would notice, and only once the potion is drunk mid-fight.
                // Refused by name here instead, at load, the same as every other value
                // this method checks against the resolved sheet.
                if (!Enum.IsDefined(potency))
                {
                    throw new InvalidDataException(
                        $"{owner}: starting potions name potency '{potency}', which is not a Potion of Healing "
                        + "this build defines.");
                }

                if (count < 0)
                {
                    throw new InvalidDataException($"{owner}: starting potions carried cannot be negative.");
                }
            }
        }

        return new CombatantCarryOver(
            state.CurrentHitPoints ?? sheet.MaximumHitPoints,
            state.RagesRemaining,
            state.SecondWindRemaining,
            state.ActionSurgeRemaining,
            state.SpellSlotsRemaining,
            state.Potions,
            state.ChannelDivinityRemaining);
    }

    /// <summary>One resource's remaining count, checked against the class table's own allowance.</summary>
    private static void RequireResourceInRange(string owner, string label, int? value, int maximum)
    {
        if (value is { } count && (count < 0 || count > maximum))
        {
            throw new InvalidDataException($"{owner}: starting {label} {count} is out of range (0-{maximum}).");
        }
    }

    /// <summary>
    /// Turns a scenario's explicit cast into monster definitions, in the order it named
    /// them and one per head.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Order is preserved and counts are expanded, because both are the fight.</b> The
    /// cast's order decides which creature takes which spawn square and which index its
    /// combatant id carries, so the entries are walked in sequence rather than grouped;
    /// <see cref="RosterParser.ToRoster"/> is the other half of that promise, folding only
    /// adjacent equal ids so the two are exactly reversible.
    /// </para>
    /// <para>
    /// The sibling of <see cref="ResolveParty"/>, and refusing in the same voice for the
    /// same reason: <see cref="CheckAgainst"/> is the method for a caller that wants the
    /// list of problems, and this is the method for a caller that has already checked.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<MonsterDefinition> ResolveRoster(BattleScenario scenario, SrdContent content)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(content);

        var roster = scenario.Enemies.Roster
            ?? throw new InvalidDataException(
                "the scenario names no roster; ScenarioFile.FromJson refuses a scenario that names "
                + "neither a roster nor a budget, so this one was built in memory rather than loaded.");

        return
        [
            .. roster.SelectMany(entry => Enumerable.Repeat(
                ContentDrift.Require(
                    content.MonstersById, entry.MonsterId, "monster", scenario.Name, Subject, Remedy),
                entry.Count)),
        ];
    }

    private static void CheckMemberIds(CharacterDraft draft, SrdContent content, List<string> errors)
    {
        var owner = draft.Name;

        RequireId(content.SpeciesById, draft.SpeciesId, "species", owner, errors);
        RequireId(content.ClassesById, draft.ClassId, "class", owner, errors);
        RequireId(content.BackgroundsById, draft.BackgroundId, "background", owner, errors);

        foreach (var weaponId in draft.WeaponIds)
        {
            RequireId(content.WeaponsById, weaponId, "weapon", owner, errors);
        }

        foreach (var masteryId in draft.WeaponMasteryIds)
        {
            RequireId(content.WeaponsById, masteryId, "mastered weapon", owner, errors);
        }

        if (draft.ArmorId is { } armorId)
        {
            RequireId(content.ArmorById, armorId, "armor", owner, errors);
        }

        foreach (var item in draft.MagicItems)
        {
            RequireId(content.MagicItemsById, item.ItemId, "magic item", owner, errors);
        }

        foreach (var spellId in draft.ChosenSpellIds)
        {
            RequireId(content.SpellsById, spellId, "spell", owner, errors);
        }
    }

    private static void RequireId<TValue>(
        IReadOnlyDictionary<string, TValue> byId,
        string id,
        string kind,
        string owner,
        List<string> errors)
    {
        if (!byId.ContainsKey(id))
        {
            errors.Add(ContentDrift.MissingMessage(id, kind, owner, Subject, Remedy));
        }
    }
}
