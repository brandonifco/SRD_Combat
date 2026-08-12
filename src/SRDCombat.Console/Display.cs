using SRDCombat.Game;
using SRDCombat.Core.Combat;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Console;

/// <summary>
/// Draws the fight: the grid, the roster, and the combat log.
/// </summary>
/// <remarks>
/// <para>
/// <b>The log appends and never replaces.</b> That is a stated requirement rather than a
/// styling choice — the sibling project's combat log arrived late, replaced its contents,
/// and was immediately called messy, because a player who looks away misses the roll that
/// killed them. Every roll, save and damage number the engine produced is already in
/// <see cref="Encounter.Log"/>; this prints it and nothing more.
/// </para>
/// <para>
/// Nothing here recomputes a rule. Every number displayed is read off the engine, so the
/// screen cannot disagree with the fight.
/// </para>
/// </remarks>
internal static class Display
{
    /// <summary>The label assignment for the current fight. Set once when it starts.</summary>
    public static Labels Labels { get; set; } = Labels.For([]);

    /// <summary>How many log lines are shown after each command.</summary>
    private const int LogTail = 14;

    /// <summary>Draws everything: grid, roster, and whatever the last command produced.</summary>
    public static void DrawFrame(Encounter encounter, int fromLogIndex)
    {
        DrawGrid(encounter);
        DrawRoster(encounter);
        DrawLog(encounter, fromLogIndex);
    }

    /// <summary>
    /// The battlefield, one cell per square, with each combatant shown by its initial.
    /// </summary>
    /// <remarks>
    /// Row 0 is drawn at the top and y increases downward, which matches how the grid is
    /// indexed rather than how a map is conventionally drawn. Stating it beats leaving a
    /// player to work out why "north" moves them to a higher number.
    /// </remarks>
    public static void DrawGrid(Encounter encounter)
    {
        var field = encounter.Battlefield;
        var occupants = encounter.Combatants
            .Where(combatant => !combatant.IsDead)
            .ToLookup(combatant => combatant.Position);

        System.Console.WriteLine();
        System.Console.Write("    ");

        for (var x = 0; x < field.Width; x++)
        {
            System.Console.Write($"{x % 10} ");
        }

        System.Console.WriteLine();

        for (var y = 0; y < field.Height; y++)
        {
            System.Console.Write($"{y,2}  ");

            for (var x = 0; x < field.Width; x++)
            {
                var square = new GridPosition(x, y);
                var here = occupants[square].FirstOrDefault();

                if (here is null)
                {
                    System.Console.Write(field.IsPassable(square) ? ". " : "# ");
                    continue;
                }

                WriteColoured(
                    $"{Initial(here)} ",
                    here.SideId == PartySideId ? ConsoleColor.Cyan : ConsoleColor.Red);
            }

            System.Console.WriteLine();
        }
    }

    /// <summary>Everyone in the fight, in initiative order, with the active one marked.</summary>
    public static void DrawRoster(Encounter encounter)
    {
        System.Console.WriteLine();

        foreach (var combatant in encounter.Combatants.OrderByDescending(c => c.Initiative))
        {
            var active = combatant == encounter.ActiveCombatant ? ">" : " ";
            var health = combatant.IsDead
                ? "dead"
                : $"{combatant.CurrentHitPoints}/{combatant.Stats.MaximumHitPoints} hp";

            var conditions = combatant.Conditions.Count > 0
                ? "  [" + string.Join(", ", combatant.Conditions.OrderBy(c => c.ToString(), StringComparer.Ordinal)) + "]"
                : string.Empty;

            var line =
                $"{active} {Initial(combatant)}  {combatant.Name,-10} " +
                $"init {combatant.Initiative,3}  AC {combatant.Stats.ArmorClass,2}  {health,-12}" +
                $"({combatant.Position.X},{combatant.Position.Y}){conditions}";

            WriteColoured(
                line + Environment.NewLine,
                combatant.IsDead ? ConsoleColor.DarkGray
                    : combatant.SideId == PartySideId ? ConsoleColor.Cyan
                    : ConsoleColor.Red);
        }
    }

    /// <summary>Prints log entries from an index onward, so nothing is ever reprinted or lost.</summary>
    public static void DrawLog(Encounter encounter, int fromIndex)
    {
        var entries = encounter.Log.Skip(fromIndex).ToArray();

        if (entries.Length == 0)
        {
            return;
        }

        System.Console.WriteLine();

        foreach (var step in entries.TakeLast(LogTail))
        {
            WriteColoured("  " + step.Narration + Environment.NewLine, ColourFor(step.Kind));
        }
    }

    /// <summary>What the active combatant can do, listed so nothing has to be memorised.</summary>
    public static void DrawTurnHeader(Combatant combatant)
    {
        System.Console.WriteLine();
        WriteColoured($"── {combatant.Name}'s turn ──" + Environment.NewLine, ConsoleColor.Yellow);

        var parts = new List<string>
        {
            $"{combatant.Turn.MovementFeet} ft. movement",
            combatant.Turn.HasAction ? "action" : "action spent",
            combatant.Turn.HasBonusAction ? "bonus action" : "bonus action spent",
        };

        System.Console.WriteLine("  " + string.Join(" · ", parts));

        if (combatant.Stats.Attacks.Count > 0)
        {
            System.Console.WriteLine(
                "  attacks: " + string.Join(", ", combatant.Stats.Attacks.Select(attack => attack.Name)));
        }

        if (combatant.Stats.Character is { Spells.Count: > 0 } character)
        {
            System.Console.WriteLine("  spells: " + string.Join(", ", character.Spells.Select(spell => spell.Name)));
        }
    }

    /// <summary>The single letter a combatant is drawn as, unique per fight by construction.</summary>
    public static char Initial(Combatant combatant) => Labels.Of(combatant);

    public static void Warn(string message) =>
        WriteColoured("  " + message + Environment.NewLine, ConsoleColor.Magenta);

    public static void Say(string message) => System.Console.WriteLine("  " + message);

    /// <summary>The side identifier the party fights under, for colouring.</summary>
    public static string PartySideId { get; set; } = "party";

    private static ConsoleColor ColourFor(CombatStepKind kind) => kind switch
    {
        CombatStepKind.Damage => ConsoleColor.Red,
        CombatStepKind.Died or CombatStepKind.Downed => ConsoleColor.DarkRed,
        CombatStepKind.Feature or CombatStepKind.Spell => ConsoleColor.Green,
        CombatStepKind.Condition => ConsoleColor.Yellow,
        CombatStepKind.RoundStarted or CombatStepKind.EncounterStarted => ConsoleColor.White,
        CombatStepKind.TurnStarted or CombatStepKind.TurnEnded => ConsoleColor.DarkGray,
        _ => ConsoleColor.Gray,
    };

    private static void WriteColoured(string text, ConsoleColor colour)
    {
        var previous = System.Console.ForegroundColor;
        System.Console.ForegroundColor = colour;
        System.Console.Write(text);
        System.Console.ForegroundColor = previous;
    }
}
