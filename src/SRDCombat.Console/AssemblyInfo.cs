using System.Runtime.CompilerServices;

// The console's own argument parsing (ConsoleArguments, #489) is internal — nothing else
// needs to call it — and this line is the smallest opening that makes it reachable from
// a plain xUnit test without publishing it any wider.
[assembly: InternalsVisibleTo("SRDCombat.Console.Tests")]
