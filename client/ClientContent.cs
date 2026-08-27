using Godot;
using SRDCombat.Content;

namespace SRDCombat.Viewer;

/// <summary>
/// Finds and loads the extracted SRD content, the way the console client finds it.
/// Moved off <c>FightScreen</c> by #327's S7 so a screen that is not a
/// <c>FightScreen</c> — <c>CreateMode</c> — can load content without inheriting a
/// board-drawing base class for it.
/// </summary>
internal static class ClientContent
{
    internal static SrdContent Load() => ContentLoader.Load(ContentDirectory());

    /// <summary>
    /// Walks up for <c>data/srd</c>, exactly as the console client does, so the viewer
    /// runs from wherever Godot was launched.
    /// </summary>
    private static string ContentDirectory()
    {
        var directory = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "data", "srd");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("No data/srd found above the project directory.");
    }
}
