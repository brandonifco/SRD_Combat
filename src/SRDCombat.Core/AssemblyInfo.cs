using System.Runtime.CompilerServices;

// TurnResources' mutators are internal (#321) — action-economy state changes only
// through Encounter, never directly from a client. This opens exactly the seam the
// test project needs to set up scenarios, nothing wider.
[assembly: InternalsVisibleTo("SRDCombat.Core.Tests")]
