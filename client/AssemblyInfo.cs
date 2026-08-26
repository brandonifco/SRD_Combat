using System.Runtime.CompilerServices;

// The client's test project (#190). The client's public surface is what Godot calls —
// scene entry points and node methods — so the parts worth pinning by test are the
// rules underneath them, which are private by right and stay that way. `internal` plus
// this line is the smallest opening that makes them reachable: nothing new is published
// to the game, and the test project is named rather than the world.
[assembly: InternalsVisibleTo("SRDCombat.Viewer.Tests")]
