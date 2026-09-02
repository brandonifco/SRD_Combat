using Godot;

namespace SRDCombat.Viewer;

/// <summary>
/// This client's own dialect is <c>--name=value</c>, one shell word, and that is the
/// only form recognised here — the console client's separate <c>--name value</c> form
/// (<c>SRDCombat.Console.ConsoleArguments</c>, #489) is a different binary's convention
/// and is deliberately not accepted. <c>null</c>
/// therefore means two different things a caller must not conflate: the flag was never
/// passed, or it was passed bare (<c>--name</c>, or the space form, which Godot hands
/// through as an unrelated second argument no different from a bare flag followed by
/// something else). <see cref="HasArgument"/> answers "was it passed at all"; a caller
/// that must refuse a present-but-valueless flag rather than silently treating it as
/// absent needs both (#470, M2).
/// </summary>
/// <remarks>
/// Moved verbatim off <c>FightScreen</c> by #327's S7 — <c>FightScreen</c> keeps thin
/// forwarders of the same names so no other call site changes.
/// </remarks>
internal static class ClientArguments
{
    internal static string? ArgumentValue(string name)
    {
        foreach (var argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith($"--{name}=", StringComparison.Ordinal))
            {
                return argument[(name.Length + 3)..];
            }
        }

        return null;
    }

    /// <summary>Whether <c>--name</c> appears at all, bare or with a value.</summary>
    internal static bool HasArgument(string name) =>
        OS.GetCmdlineUserArgs().Contains($"--{name}") || ArgumentValue(name) is not null;
}
