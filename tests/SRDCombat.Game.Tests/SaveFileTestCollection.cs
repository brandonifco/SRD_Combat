namespace SRDCombat.Game.Tests;

/// <summary>
/// The SaveFile fault seam is process-wide while it throws immediately after a real
/// filesystem operation. Every fixture that calls SaveFile is kept here so xUnit cannot
/// deliver an injected crash to an unrelated SaveFile call.
/// </summary>
[CollectionDefinition("SaveFile filesystem fault injection", DisableParallelization = true)]
public sealed class SaveFileTestCollection;
