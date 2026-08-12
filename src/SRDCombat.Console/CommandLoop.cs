using SRDCombat.Game;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Console;

/// <summary>
/// Reads commands and drives one fight to its end.
/// </summary>
/// <remarks>
/// <para>
/// The party is played; every other side is taken by <see cref="SimpleTacticsPolicy"/>.
/// The loop only ever calls the engine's public actions and prints what comes back —
/// <b>a refusal is shown with its code</b> rather than swallowed, because a refusal is
/// the engine explaining a rule and hiding it would make the client the second place
/// rules live.
/// </para>
/// <para>
/// Targets are named by their initial, matching the grid, so "a g" reads as "attack the
/// goblin" and needs no lookup table on screen.
/// </para>
/// </remarks>
internal sealed class CommandLoop(Encounter encounter, string partySideId)
{
    private int _printed;

    /// <summary>Plays until one side is finished, or the player quits.</summary>
    /// <returns>True if the fight was played to its end.</returns>
    public bool Run()
    {
        Display.DrawFrame(encounter, _printed);
        _printed = encounter.Log.Count;

        while (!encounter.IsComplete)
        {
            if (encounter.ActiveCombatant is not { } active)
            {
                break;
            }

            if (active.SideId != partySideId)
            {
                SimpleTacticsPolicy.TakeTurn(encounter);
                Flush();
                continue;
            }

            if (!active.CanAct)
            {
                Display.Warn($"{active.Name} cannot act.");
                encounter.EndTurn();
                Flush();
                continue;
            }

            Display.DrawTurnHeader(active);

            if (!ReadAndRunTurn(active))
            {
                return false;
            }
        }

        Flush();
        return true;
    }

    /// <summary>Reads commands until the turn ends. False means the player quit.</summary>
    private bool ReadAndRunTurn(Combatant active)
    {
        while (encounter.ActiveCombatant == active && !encounter.IsComplete)
        {
            System.Console.Write($"{active.Name}> ");
            var line = System.Console.ReadLine();

            if (line is null or "quit" or "q")
            {
                return false;
            }

            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 0)
            {
                continue;
            }

            Execute(active, words);
            Flush();

            if (encounter.ActiveCombatant == active && !encounter.IsComplete && active.CanAct)
            {
                Display.DrawTurnHeader(active);
            }
        }

        return true;
    }

    private void Execute(Combatant active, string[] words)
    {
        var refusal = words[0].ToLowerInvariant() switch
        {
            "move" or "m" => Move(words),
            "attack" or "a" => Attack(active, words),
            "cast" or "c" => Cast(active, words),
            "use" or "u" => UseEntry(active, words),
            "dodge" => encounter.Dodge(),
            "dash" => encounter.Dash(),
            "disengage" => encounter.Disengage(),
            "stand" => encounter.StandUp(),
            "escape" => encounter.Escape(),
            "rage" => encounter.Rage(),
            "reckless" => encounter.RecklessAttack(),
            "secondwind" or "sw" => encounter.SecondWind(),
            "surge" => encounter.ActionSurge(),
            "aim" => encounter.SteadyAim(),
            "cunning" => CunningAction(words),
            "drink" => Drink(active, words),
            "trip" => encounter.CunningStrike(CunningStrikeEffect.Trip),
            "end" or "e" => EndTurn(),
            "look" or "l" => Look(),
            "help" or "?" => Help(),
            _ => new ActionRefusal("client.unknown", $"'{words[0]}' is not a command. Try 'help'."),
        };

        if (refusal is not null)
        {
            Display.Warn($"{refusal.Message}  [{refusal.Code}]");
        }
    }

    private ActionRefusal? Move(string[] words)
    {
        if (words.Length < 3
            || !int.TryParse(words[1], out var x)
            || !int.TryParse(words[2], out var y))
        {
            return new ActionRefusal("client.usage", "move <x> <y>");
        }

        return encounter.Move(new GridPosition(x, y));
    }

    private ActionRefusal? Attack(Combatant active, string[] words)
    {
        if (words.Length < 2)
        {
            return new ActionRefusal("client.usage", "attack <target letter> [attack name]");
        }

        if (Find(words[1]) is not { } target)
        {
            return new ActionRefusal("client.no_target", $"Nobody here is called '{words[1]}'.");
        }

        // Default to the hardest-hitting attack that reaches, which is the choice a
        // player would make by hand every time; naming one overrides it.
        var name = words.Length > 2
            ? string.Join(' ', words[2..])
            : BestAttack(active, target)?.Name;

        return name is null
            ? new ActionRefusal("client.no_attack", $"{active.Name} has no attack that reaches.")
            : encounter.Attack(name, target);
    }

    private CombatAttack? BestAttack(Combatant active, Combatant target)
    {
        var distance = active.Position.DistanceFeetTo(target.Position);

        return active.Stats.Attacks
            .Where(attack => attack.CanReach(distance))
            .OrderByDescending(attack => attack.Damage.Sum(damage => damage.Amount.Average))
            .ThenBy(attack => attack.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private ActionRefusal? Cast(Combatant active, string[] words)
    {
        if (words.Length < 3)
        {
            return new ActionRefusal("client.usage", "cast <spell name> <target letter>");
        }

        if (Find(words[^1]) is not { } target)
        {
            return new ActionRefusal("client.no_target", $"Nobody here is called '{words[^1]}'.");
        }

        var wanted = string.Join(' ', words[1..^1]);

        var spell = active.Stats.Character?.Spells.FirstOrDefault(candidate =>
            candidate.Name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));

        return spell is null
            ? new ActionRefusal("client.no_spell", $"{active.Name} knows no spell called '{wanted}'.")
            : encounter.CastSpell(spell.Id, target);
    }

    private ActionRefusal? UseEntry(Combatant active, string[] words)
    {
        if (words.Length < 3)
        {
            return new ActionRefusal("client.usage", "use <entry name> <target letter>");
        }

        return Find(words[^1]) is not { } target
            ? new ActionRefusal("client.no_target", $"Nobody here is called '{words[^1]}'.")
            : encounter.UseEntry(string.Join(' ', words[1..^1]), target);
    }

    /// <summary>
    /// Drinks a potion, or administers one to somebody adjacent.
    /// </summary>
    /// <remarks>
    /// The potency is chosen rather than typed — the weakest carried, because spending a
    /// greater potion on a scratch wastes the difference and is the mistake a client can
    /// spare the player without deciding anything a rule cares about. Everything else,
    /// reach and the Bonus Action included, is the engine's to refuse.
    /// </remarks>
    private ActionRefusal? Drink(Combatant active, string[] words)
    {
        if (active.Inventory.Weakest is not { } potency)
        {
            return new ActionRefusal("client.no_potion", $"{active.Name} carries no potions.");
        }

        if (words.Length < 2)
        {
            return encounter.DrinkPotion(potency);
        }

        return Find(words[1]) is { } target
            ? encounter.DrinkPotion(potency, target)
            : new ActionRefusal("client.no_target", $"Nobody here is called '{words[1]}'.");
    }

    private ActionRefusal? CunningAction(string[] words)
    {
        if (words.Length < 2)
        {
            return new ActionRefusal("client.usage", "cunning <dash|disengage>");
        }

        return words[1].ToLowerInvariant() switch
        {
            "dash" => encounter.CunningAction(CunningActionKind.Dash),
            "disengage" => encounter.CunningAction(CunningActionKind.Disengage),
            _ => new ActionRefusal("client.usage", "cunning <dash|disengage>"),
        };
    }

    private ActionRefusal? EndTurn()
    {
        encounter.EndTurn();
        return null;
    }

    private ActionRefusal? Look()
    {
        Display.DrawGrid(encounter);
        Display.DrawRoster(encounter);
        return null;
    }

    /// <summary>Finds a combatant by its grid label, or by the start of its name.</summary>
    private Combatant? Find(string token) =>
        encounter.Combatants.FirstOrDefault(combatant =>
            !combatant.IsDead && Display.Labels.Matches(combatant, token));

    /// <summary>Prints whatever the engine has logged since the last time we looked.</summary>
    private void Flush()
    {
        Display.DrawLog(encounter, _printed);
        _printed = encounter.Log.Count;
    }

    private static ActionRefusal? Help()
    {
        Display.Say("move <x> <y>            walk, provoking Opportunity Attacks");
        Display.Say("attack <letter> [name]  attack; defaults to the best that reaches");
        Display.Say("cast <spell> <letter>   cast a spell at someone");
        Display.Say("use <entry> <letter>    use a stat block entry by name");
        Display.Say("dodge / dash / disengage / stand / escape");
        Display.Say("rage / reckless / secondwind / surge / aim / trip");
        Display.Say("cunning <dash|disengage>");
        Display.Say("drink [letter]          drink a potion, or give one to somebody adjacent");
        Display.Say("look                    redraw the grid");
        Display.Say("end                     end your turn");
        Display.Say("quit                    leave the fight");
        return null;
    }
}
