namespace SRDCombat.Viewer.Tests;

/// <summary>One shipped sheet's pinned geometry — see <see cref="SpriteGeometryTests"/>.</summary>
internal readonly record struct SpriteGeometryEntry(string RelativePath, int Width, int Height, int FrameCount);

/// <summary>
/// Reads and writes the committed manifest of every shipped sheet's canvas dimensions
/// (#467). Same shape and same discipline as the frozen transcript
/// (<c>FrozenTranscriptTests</c>/<c>TranscriptWriter</c>): a plain text fixture, one
/// line per sheet, regenerated only deliberately and reviewed as a diff — a manifest
/// that silently rewrote itself on every run would gate nothing.
/// </summary>
internal static class SpriteGeometryManifest
{
    public const string FixtureName = "sprite-geometry-manifest.tsv";

    private const char Separator = '\t';

    /// <summary>
    /// Every <b>tracked</b> <c>.png</c> under <c>client/assets/sprites</c> right now,
    /// measured from its header bytes (<see cref="PngGeometry"/>) — the tree's present
    /// truth, not the committed one. <see cref="SpriteGeometryTests"/> compares the two.
    /// </summary>
    /// <remarks>
    /// <b>The source of truth is git, not the filesystem, and that is not a stylistic
    /// choice</b> (#522). This gate first shipped reading the directory, which is wrong
    /// twice over. <c>.gitignore</c> ignores <c>client/assets/sprites/*</c> and whitelists
    /// the shipped folders back in, because Brandon keeps purchased Craftpix sheets in
    /// that same tree deliberately un-committed — so the working tree legitimately holds
    /// hundreds of PNGs that must never enter this manifest. A CI runner checks out only
    /// tracked files and saw none of them, so the gate passed there and failed on his
    /// machine with 354 phantom sheets: <b>green where merges are gated, red where a
    /// human works</b>, which is how a suite teaches people to ignore it.
    /// <para>
    /// The manifest is about what the repository <i>ships</i>. `git ls-files` is the
    /// direct expression of that; <c>Directory.EnumerateFiles</c> answers a different
    /// question that merely looked the same. Do not "simplify" this back.
    /// </para>
    /// </remarks>
    public static List<SpriteGeometryEntry> MeasureShippedTree()
    {
        var root = ViewerRepositoryPaths.SpritesDirectory;

        return TrackedSheets(root)
            .Select(path =>
            {
                var (width, height) = PngGeometry.Read(path);
                var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
                return new SpriteGeometryEntry(relativePath, width, height, FrameCountFor(width, height));
            })
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The <c>.png</c> files under <paramref name="root"/> that git has in its index —
    /// absolute paths. Untracked art in the same tree is invisible here by design; see
    /// <see cref="MeasureShippedTree"/>'s remarks for why that is the whole point.
    /// </summary>
    private static IEnumerable<string> TrackedSheets(string root)
    {
        var listing = new System.Diagnostics.ProcessStartInfo("git", "ls-files -z -- *.png")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var git = System.Diagnostics.Process.Start(listing)
            ?? throw new InvalidOperationException("could not run git to list tracked sprite sheets");

        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();

        if (git.ExitCode != 0)
        {
            // Never fall back to enumerating the directory: that is the bug this method
            // exists to fix, and a silent fallback would restore it on exactly the
            // machines where git is unavailable rather than reporting the problem.
            throw new InvalidOperationException(
                $"git ls-files failed in {root} (exit {git.ExitCode}): {git.StandardError.ReadToEnd()}");
        }

        return output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(relative => Path.Combine(root, relative));
    }

    /// <summary>
    /// <c>SpriteLibrary.LoadStrip</c>'s own rule, applied to the file as shipped rather
    /// than as loaded: a sheet narrower than it is tall is one drawing, padded to a
    /// single square frame at load time rather than rejected, so it always measures as
    /// one frame; anything else is however many <c>height</c>-wide frames its width
    /// holds.
    /// </summary>
    private static int FrameCountFor(int width, int height) => height <= 0 ? 0 : Math.Max(width, height) / height;

    public static List<SpriteGeometryEntry> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var entries = new List<SpriteGeometryEntry>();

        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split(Separator);

            if (fields.Length != 4)
            {
                throw new InvalidDataException(
                    $"Malformed line in the sprite geometry manifest (expected 4 tab-separated fields, got {fields.Length}): '{line}'");
            }

            entries.Add(new SpriteGeometryEntry(fields[0], int.Parse(fields[1]), int.Parse(fields[2]), int.Parse(fields[3])));
        }

        return entries;
    }

    public static void Write(string path, IReadOnlyList<SpriteGeometryEntry> entries)
    {
        var lines = new List<string>
        {
            "# Generated by SpriteGeometryManifestWriter (#467) — do not hand-edit.",
            "# RelativePath\tWidth\tHeight\tFrameCount",
        };

        lines.AddRange(entries
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .Select(entry => $"{entry.RelativePath}{Separator}{entry.Width}{Separator}{entry.Height}{Separator}{entry.FrameCount}"));

        File.WriteAllLines(path, lines);
    }
}
