using Godot;
using SRDCombat.Core.Combat;
using SRDCombat.Content;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Dice;
using SRDCombat.Core.Rules;
using SRDCombat.Game;

using SRDCombat.Viewer.Ui;

namespace SRDCombat.Viewer;

/// <summary>Fight-to-fight progression: interludes, starting the next fight, reporting a finished one, and the save it writes on the way.</summary>
public partial class PlayMode : FightScreen
{
    /// <summary>
    /// Sets up the between-fights screen: what the last fight left behind, then what the
    /// run says about the next one — the rest taken, who returned, how everybody stands.
    /// </summary>
    /// <remarks>
    /// The wording deliberately matches the console client's, because both are only
    /// repeating what <see cref="GauntletRun"/> reports. The save was already written
    /// when the fight completed; the rest applied here lives in the run's memory and is
    /// reapplied on resume, exactly as the console's loop ordering has it.
    /// </remarks>
    private void EnterInterlude(IEnumerable<string> after)
    {
        _interlude.Clear();
        _interlude.AddRange(after);
        _phase = Phase.Interlude;

        if (_run is not { } run)
        {
            _phase = Phase.RunOver;
            QueueRedraw();
            return;
        }

        if (run.Outcome == RunOutcome.Survived)
        {
            _interlude.Add(string.Empty);
            _interlude.Add($"The gauntlet is beaten — {run.Ladder.Count} fights cleared.");
            AddFallen(run);
            _phase = Phase.RunOver;
        }
        else if (run.Outcome == RunOutcome.Defeated)
        {
            _interlude.Add(string.Empty);
            _interlude.Add($"The run ends after {run.Cleared} fight(s).");

            if (run.Cleared > 0)
            {
                _interlude.Add("Launch with --continue to retry from after the last cleared fight.");
            }

            AddFallen(run);
            _phase = Phase.RunOver;
        }
        else if (run.Next is { } step)
        {
            // The one reseed point: everything from here through the fight this
            // interlude sets up and the loot it pays out draws from this one segment
            // — see RunDice's remarks for why a fight always plays the same dice
            // regardless of how an earlier attempt at it went.
            _dice = new SeededRandomSource(RunDice.SeedFor(run.Seed, run.Cleared));

            var returnsBefore = run.Returns.Count;
            var rest = run.PrepareForNext(_dice);

            _interlude.Add(string.Empty);
            _interlude.Add(
                $"Fight {run.Cleared + 1} of {run.Ladder.Count} — " +
                $"{step.Difficulty.ToString().ToLowerInvariant()} difficulty.");

            if (rest is { } taken)
            {
                _interlude.Add($"The party takes a {taken} Rest.");
            }

            // The merchant reaches the party at each Long Rest, exactly as the
            // console's shop does. The button is the door; nothing is automatic.
            _shopAvailable = rest == RestKind.Long;
            _focus.PopToRoot();

            foreach (var returned in run.Returns.Skip(returnsBefore))
            {
                _interlude.Add(returned + ".");
            }

            _interlude.Add(string.Empty);

            foreach (var (member, state) in run.Party.Zip(run.States))
            {
                _interlude.Add(state.IsDead
                    ? $"{member.Draft.Name} — dead"
                    : $"{member.Draft.Name} — level {state.Level}, " +
                      $"{state.CurrentHitPoints}/{member.Sheet.MaximumHitPoints} hp, " +
                      $"{state.HitDiceRemaining} hit {(state.HitDiceRemaining == 1 ? "die" : "dice")}, " +
                      $"{state.ExperiencePoints} xp");
            }
        }

        QueueRedraw();
    }

    private void AddFallen(GauntletRun run)
    {
        var fallen = run.Fallen.ToArray();

        if (fallen.Length > 0)
        {
            _interlude.Add("Fallen: " + string.Join(", ", fallen) + ".");
        }
        else if (run.Casualties.Count > 0)
        {
            _interlude.Add($"Everyone made it, though {run.Casualties.Count} went down along the way.");
        }
    }

    private void StartNextFight()
    {
        if (_run is not { } run || run.Next is null)
        {
            return;
        }

        var fight = run.BeginNext(_dice);

        _fight = fight;
        _encounter = fight.Encounter;
        _labels = Labels.For(_encounter.Combatants);
        AdoptBattlefield(_encounter);
        // The objective rides the subtitle when the rung is not a plain deathmatch: it is
        // the one line already on screen for the whole fight, and a goal shown once before
        // the first turn is a goal forgotten by round three. The wording comes from the
        // encounter so the two clients cannot word it differently.
        _subtitle = $"fight {run.Cleared + 1} of {run.Ladder.Count} — seed {_seed} — "
            + (_encounter.Objective.Kind == ObjectiveKind.Defeat
                ? $"the party against {RosterOf(fight)}"
                : _encounter.ObjectiveDescription);
        _phase = Phase.Fighting;
        _fightEndHandled = false;
        _buttonsFor = null;

        // A fresh fight is a fresh log: nothing has been scanned for acts, and any
        // walk or swing the last fight left half-played dies with it.
        _walkStepsSeen = 0;
        ClearActs();

        RefreshAfterAction(null);
    }

    /// <summary>
    /// Reports a finished fight to the run and saves. The save happens only after a
    /// cleared fight, never after the defeat itself — the file keeps the last state
    /// worth returning to, which is what makes reloading a retry.
    /// </summary>
    private void HandleFightEnd()
    {
        if (_fightEndHandled)
        {
            return;
        }

        _fightEndHandled = true;

        if (_run is not { } run || _fight is not { } fight || _encounter is not { } encounter)
        {
            QueueRedraw();
            return;
        }

        // Hold on the outcome first, for the probe exactly as for a person: the
        // Outcome card is one of the focuses #327 moves, so it earns the same real
        // input path everything else in the probe takes. This used to short-circuit
        // straight to CompleteAndReport under --probe, from before the probe could
        // press a key at all (#499) — RunProbe now dismisses the card itself.
        _focus.Push(new PlayFocus.Outcome());
        QueueRedraw();
    }

    /// <summary>Finishes the fight the card was announcing: rewards, save, interlude.</summary>
    private void CompleteAndReport()
    {
        _focus.PopToRoot();

        if (_run is not { } run || _fight is not { } fight || _encounter is not { } encounter)
        {
            return;
        }

        var levelUpsBefore = run.LevelUps.Count;
        var lootBefore = run.LootFound.Count;
        var magicItemFindersBefore = run.MagicItemFinders.Count;

        run.CompleteFight(fight, _dice);

        var after = new List<string>
        {
            encounter.WinningSide == PregeneratedParty.SideId
                ? $"Fight {run.Cleared} cleared!"
                : "The party falls.",
        };

        after.AddRange(run.LevelUps.Skip(levelUpsBefore).Select(line => line + "!"));
        after.AddRange(run.LootFound.Skip(lootBefore).Select(line => line + "!"));

        // Right where the award lands, not one screen later (#534): a permanent
        // item's resolved effect is stated here, next to the "finds ..." line that
        // named it, rather than only surfacing the next time this character's panel
        // draws.
        foreach (var finder in run.MagicItemFinders.Skip(magicItemFindersBefore))
        {
            var sheet = run.Party[finder].Sheet;
            var announcement = MagicItemReadout.Announce(
                run.Party[finder].Draft.Name,
                sheet.MagicItemNames,
                sheet.SpellAttackItemBonus,
                sheet.IgnoresHalfCoverOnSpellAttacks);

            if (announcement.Length > 0)
            {
                after.Add(announcement);
            }
        }

        if (run.Outcome != RunOutcome.Defeated)
        {
            if (_isNewRun)
            {
                SaveFile.BeginNewRun(_savePath, RunSave.ToJson(run));
                _isNewRun = false;
            }
            else
            {
                SaveFile.ContinueWrite(_savePath, RunSave.ToJson(run));
            }
            after.Add($"Saved to {_savePath}.");
        }

        EnterInterlude(after);
    }
}
