using SRDCombat.Game;
using SRDCombat.Core.Characters;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;

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
            "trade" => Trade(active, words),
            "trip" => encounter.CunningStrike(CunningStrikeEffect.Trip),
            "spark" => Spark(active, words),
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
        // player would make by hand every time; naming one overrides it. The choice is
        // shared with the Godot client so the two cannot drift apart on it.
        var name = words.Length > 2
            ? string.Join(' ', words[2..])
            : AttackChoice.BestFor(active, target, encounter.Combatants)?.Name;

        return name is null
            ? new ActionRefusal("client.no_attack", $"{active.Name} has no attack that reaches.")
            : encounter.Attack(name, target);
    }

    private ActionRefusal? Cast(Combatant active, string[] words)
    {
        if (words.Length < 3)
        {
            return new ActionRefusal("client.usage", "cast <spell name> <target letter | x,y> [slot level]");
        }

        // A trailing number is a deliberate upcast — "cast cure wounds b 3" burns the
        // level 3 slot — and the engine rules on whether the slot is legal.
        int? slot = null;

        if (words.Length > 3 && int.TryParse(words[^1], out var chosen))
        {
            slot = chosen;
            words = words[..^1];
        }

        var wanted = string.Join(' ', words[1..^1]);

        var spell = active.Stats.Character?.Spells.FirstOrDefault(candidate =>
            candidate.Name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));

        if (spell is null)
        {
            return new ActionRefusal("client.no_spell", $"{active.Name} knows no spell called '{wanted}'.");
        }

        // An area spell may be aimed at a bare square — "cast fireball 4,2" — and the
        // engine's point overload decides whether the spell supports it. Everything
        // else still names a creature by its letter.
        if (SquareArgument(words[^1]) is { } point)
        {
            return encounter.CastSpell(spell.Id, point, target: null, slot);
        }

        return Find(words[^1]) is not { } target
            ? new ActionRefusal("client.no_target", $"Nobody here is called '{words[^1]}'.")
            : encounter.CastSpell(spell.Id, target, slot);
    }

    /// <summary>Reads "x,y" as a grid square; null when the token is not one.</summary>
    private static GridPosition? SquareArgument(string token)
    {
        var parts = token.Split(',');

        return parts.Length == 2
            && int.TryParse(parts[0], out var x)
            && int.TryParse(parts[1], out var y)
                ? new GridPosition(x, y)
                : null;
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
        if (words.Length < 2)
        {
            return active.Inventory.Weakest is { } own
                ? encounter.DrinkPotion(own)
                : new ActionRefusal("client.no_potion", $"{active.Name} carries no potions.");
        }

        if (Find(words[1]) is not { } target)
        {
            return new ActionRefusal("client.no_target", $"Nobody here is called '{words[1]}'.");
        }

        // The target's own flask first, the actor's pack second — the order the engine
        // spends them in, so a character carrying none can still get a casualty up
        // with the casualty's own potion.
        return (target.Inventory.Weakest ?? active.Inventory.Weakest) is { } potency
            ? encounter.DrinkPotion(potency, target)
            : new ActionRefusal(
                "client.no_potion",
                $"Neither {active.Name} nor {target.Name} carries a potion.");
    }

    /// <summary>
    /// Hands one potion from the giver's own pack to somebody adjacent.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Drink"/>'s administer path, the direction is always giver to
    /// target: <c>Encounter.TradeItem</c> never falls back to the recipient's own pack, so
    /// this does not either — there is no "whoever has one" reading for a trade, only
    /// "what the actor is handing over". Everything past parsing — adjacency, allegiance,
    /// death, the once-per-turn cost, ownership — is the engine's to rule on.
    /// </remarks>
    private ActionRefusal? Trade(Combatant active, string[] words)
    {
        if (words.Length < 2)
        {
            return new ActionRefusal(
                "client.usage",
                "trade <target letter> [standard|greater|superior|supreme]");
        }

        if (Find(words[1]) is not { } target)
        {
            return new ActionRefusal("client.no_target", $"Nobody here is called '{words[1]}'.");
        }

        HealingPotion potency;

        if (words.Length > 2)
        {
            if (!TryParsePotency(words[2], out potency))
            {
                return new ActionRefusal(
                    "client.usage",
                    "trade <target letter> [standard|greater|superior|supreme]");
            }
        }
        else if (active.Inventory.Weakest is { } weakest)
        {
            potency = weakest;
        }
        else
        {
            return new ActionRefusal("client.no_potion", $"{active.Name} carries no potions.");
        }

        return encounter.TradeItem(new CombatTradeItem.Potion(potency), target);
    }

    /// <summary>Reads one of the four printed potencies by name.</summary>
    private static bool TryParsePotency(string word, out HealingPotion potency)
    {
        switch (word.ToLowerInvariant())
        {
            case "standard": potency = HealingPotion.Standard; return true;
            case "greater": potency = HealingPotion.Greater; return true;
            case "superior": potency = HealingPotion.Superior; return true;
            case "supreme": potency = HealingPotion.Supreme; return true;
            default: potency = default; return false;
        }
    }

    /// <summary>
    /// Divine Spark: heal an ally or harm an enemy.
    /// </summary>
    /// <remarks>
    /// The mode defaults from the target's side — an ally is healed, an enemy harmed
    /// with Radiant — because that is the choice a player would make every time; a
    /// trailing word overrides it, so healing an enemy or burning Necrotic is still a
    /// command away. The engine rules on everything else.
    /// </remarks>
    private ActionRefusal? Spark(Combatant active, string[] words)
    {
        if (words.Length < 2)
        {
            return new ActionRefusal("client.usage", "spark <letter> [heal|necrotic|radiant]");
        }

        if (Find(words[1]) is not { } target)
        {
            return new ActionRefusal("client.no_target", $"Nobody here is called '{words[1]}'.");
        }

        var mode = words.Length > 2 ? words[2].ToLowerInvariant() : null;

        return mode switch
        {
            "heal" => encounter.DivineSpark(target, DivineSparkUse.Heal),
            "necrotic" => encounter.DivineSpark(target, DivineSparkUse.Harm, DamageType.Necrotic),
            "radiant" => encounter.DivineSpark(target, DivineSparkUse.Harm, DamageType.Radiant),
            null => target.SideId == active.SideId
                ? encounter.DivineSpark(target, DivineSparkUse.Heal)
                : encounter.DivineSpark(target, DivineSparkUse.Harm),
            _ => new ActionRefusal("client.usage", "spark <letter> [heal|necrotic|radiant]"),
        };
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
        Display.Say("cast <spell> <x,y>      aim an area spell at a bare square");
        Display.Say("cast <spell> <at> <n>   burn a level n slot on purpose - an upcast");
        Display.Say("use <entry> <letter>    use a stat block entry by name");
        Display.Say("dodge / dash / disengage / stand / escape");
        Display.Say("rage / reckless / secondwind / surge / aim / trip");
        Display.Say("spark <letter> [heal|necrotic|radiant]  Divine Spark; allies healed, enemies harmed");
        Display.Say("cunning <dash|disengage>");
        Display.Say("drink [letter]          drink a potion, or give one to somebody adjacent");
        Display.Say("trade <letter> [standard|greater|superior|supreme]");
        Display.Say("                        give a potion to somebody adjacent — free once per turn");
        Display.Say("look                    redraw the grid");
        Display.Say("end                     end your turn");
        Display.Say("quit                    leave the fight");
        return null;
    }
}
