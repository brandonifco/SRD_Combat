using Godot;

namespace SRDCombat.Viewer;

/// <summary>
/// The token art: animated pixel-art sprites for the combatants the free Craftpix packs
/// cover, loaded at runtime from <c>assets/sprites/</c> under the project directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>The directory is deliberately not in the repository.</b> Craftpix's free license
/// permits using the art in a game but not redistributing the source assets, and this
/// is a public repo — the same line the SRD PDF sits behind. A machine without the
/// directory gets an empty library, every lookup returns null, and the screens draw the
/// circle-and-letter tokens they always drew. Nothing here is load-bearing.
/// </para>
/// <para>
/// <b>The two maps are curated, and absence is honest.</b> A monster gets art only when
/// the pack genuinely depicts it — a Goblin Warrior is a goblin, a Skeleton a skeleton —
/// and a name with no plausible match stays a circle rather than wearing the wrong
/// body. The dragons are the stated example: the packs hold two colours, so the Red and
/// White Dragon Wyrmlings get them and the other eight wyrmlings stay circles, because
/// a red sprite labelled "Green Dragon Wyrmling" would be the display lying about the
/// one thing a player can check against the log. Party art maps from the class name
/// (<c>CombatantFeatures.ClassName</c>, the road TurnBanner already uses).
/// </para>
/// </remarks>
public sealed class SpriteLibrary
{
    /// <summary>One animation strip: a horizontal sheet of square frames.</summary>
    /// <param name="Texture">The whole sheet.</param>
    /// <param name="FrameCount">How many frames the sheet holds (width over height).</param>
    /// <param name="FrameSize">The square frame edge in pixels — the sheet's height.</param>
    /// <param name="Bounds">
    /// The union of every frame's opaque pixels, in frame-local coordinates. The frames
    /// are mostly empty air — a 128-pixel frame holds a figure of perhaps half that — so
    /// the screen scales and anchors by what is actually drawn, and the union rather
    /// than a per-frame box keeps the feet planted while the frames cycle.
    /// </param>
    public sealed record Strip(Texture2D Texture, int FrameCount, int FrameSize, Rect2I Bounds);

    /// <summary>The animations one combatant's art carries. Any of them may be missing.</summary>
    /// <remarks>
    /// The Priest packs genuinely have no Dead or Hurt sheet — the screen substitutes a
    /// rotated idle frame for a fallen priest rather than a circle, so a downed Cleric
    /// still reads as a body on the ground.
    /// </remarks>
    public sealed record CharacterArt(Strip? Idle, Strip? Walk, Strip? Dead);

    /// <summary>Party art by class name — the SRD prints twelve, the packs cover them all.</summary>
    private static readonly Dictionary<string, string> ByClassName = new(StringComparer.Ordinal)
    {
        ["Fighter"] = "Knight_1",
        ["Paladin"] = "Knight_2",
        ["Barbarian"] = "Gladiator_1",
        ["Monk"] = "Gladiator_2",
        ["Rogue"] = "Elf_1",
        ["Bard"] = "Elf_2",
        ["Ranger"] = "Elf_3",
        ["Cleric"] = "Priests_1",
        ["Druid"] = "Priests_3",
        ["Wizard"] = "Wanderer Magican",
        ["Sorcerer"] = "Fire Wizard",
        ["Warlock"] = "Lightning Mage",
    };

    /// <summary>
    /// Monster art by exact stat-block name. Exact on purpose — the PlausibleFoes lesson:
    /// a substring test would dress creatures in bodies that are not theirs.
    /// </summary>
    private static readonly Dictionary<string, string> ByMonsterName = new(StringComparer.Ordinal)
    {
        ["Goblin Minion"] = "Goblin_1",
        ["Goblin Warrior"] = "Goblin_2",
        ["Goblin Boss"] = "Goblin_3",
        ["Skeleton"] = "Skeleton_Warrior",
        ["Zombie"] = "Zombie Man",
        ["Ogre Zombie"] = "Wild Zombie",
        ["Gladiator"] = "Gladiator_3",
        ["Knight"] = "Knight_3",
        ["Mage"] = "Lightning Mage",
        ["Archmage"] = "Lightning Mage",
        ["Priest"] = "Priests_2",
        ["Priest Acolyte"] = "Priests_3",
        ["Cultist"] = "Priests_3",
        ["Scout"] = "Elf_2",
        ["Red Dragon Wyrmling"] = "Dragon_1",
        ["White Dragon Wyrmling"] = "Dragon_2",
    };

    private readonly Dictionary<string, CharacterArt> _bySheetFolder;

    private SpriteLibrary(Dictionary<string, CharacterArt> bySheetFolder) =>
        _bySheetFolder = bySheetFolder;

    /// <summary>Whether any art loaded at all — false means every token is a circle.</summary>
    public bool IsEmpty => _bySheetFolder.Count == 0;

    /// <summary>
    /// The art for one combatant, or null when none applies — the caller's cue to draw
    /// the circle token instead.
    /// </summary>
    public CharacterArt? ForToken(bool isParty, string? className, string name)
    {
        var map = isParty ? ByClassName : ByMonsterName;
        var key = isParty ? className : name;

        return key is not null
            && map.TryGetValue(key, out var folder)
            && _bySheetFolder.TryGetValue(folder, out var art)
            ? art
            : null;
    }

    /// <summary>
    /// Loads whatever art is present. A missing directory, folder or sheet is skipped
    /// silently — absence is the supported state, not an error.
    /// </summary>
    public static SpriteLibrary Load()
    {
        var root = Path.Combine(ProjectSettings.GlobalizePath("res://"), "assets", "sprites");
        var loaded = new Dictionary<string, CharacterArt>(StringComparer.Ordinal);

        if (!Directory.Exists(root))
        {
            return new SpriteLibrary(loaded);
        }

        foreach (var folder in ByClassName.Values.Concat(ByMonsterName.Values).Distinct())
        {
            var directory = Path.Combine(root, folder);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            var art = new CharacterArt(
                LoadStrip(Path.Combine(directory, "Idle.png")),
                LoadStrip(Path.Combine(directory, "Walk.png")),
                LoadStrip(Path.Combine(directory, "Dead.png")));

            // Idle is the one animation a token cannot do without: it is the frame a
            // standing combatant shows every moment it is not walking.
            if (art.Idle is not null)
            {
                loaded[folder] = art;
            }
        }

        return new SpriteLibrary(loaded);
    }

    private static Strip? LoadStrip(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var image = new Image();

        if (image.Load(path) != Error.Ok)
        {
            return null;
        }

        image.Convert(Image.Format.Rgba8);

        var frameSize = image.GetHeight();

        if (frameSize == 0 || image.GetWidth() < frameSize)
        {
            return null;
        }

        var frameCount = image.GetWidth() / frameSize;

        return new Strip(
            ImageTexture.CreateFromImage(image),
            frameCount,
            frameSize,
            OpaqueBounds(image, frameCount, frameSize));
    }

    /// <summary>
    /// The union of every frame's opaque pixels, folded into one frame's coordinates.
    /// </summary>
    private static Rect2I OpaqueBounds(Image image, int frameCount, int frameSize)
    {
        var data = image.GetData();
        var width = image.GetWidth();
        int minX = frameSize, minY = frameSize, maxX = -1, maxY = -1;

        for (var y = 0; y < frameSize; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (data[(((y * width) + x) * 4) + 3] < 32)
                {
                    continue;
                }

                var frameX = x % frameSize;
                minX = Math.Min(minX, frameX);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, frameX);
                maxY = Math.Max(maxY, y);
            }
        }

        // A fully transparent sheet would be a broken export; treat the whole frame as
        // the figure rather than dividing by an empty box downstream.
        return maxX < minX
            ? new Rect2I(0, 0, frameSize, frameSize)
            : new Rect2I(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
    }
}
