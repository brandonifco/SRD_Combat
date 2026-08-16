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
    public sealed record Strip(Texture2D Texture, int FrameCount, int FrameSize);

    /// <summary>
    /// Where one character stands in its own canvas, and how big it is — measured once
    /// and shared by every strip, which is what keeps a figure the same size and in the
    /// same place whatever it is doing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The packs are canvas-aligned, and that is the whole basis of this.</b> Measured
    /// across every strip the game draws, the figure's lowest opaque pixel sits on the
    /// canvas's bottom edge — so the canvas carries the ground line, and a strip needs no
    /// bounds of its own. What differs between strips is *motion the artist drew*: the
    /// Knight's swing lunges some twenty pixels forward, the Elf's shifts the other way,
    /// a walk cycle strides and bobs. Measuring each strip separately and re-centring it —
    /// which is what this did at first — deletes exactly that motion and, worse, changes
    /// the figure's size between animations, because an extended sword makes the attack
    /// strip's box wider and scales the whole body down to fit it.
    /// </para>
    /// <para>
    /// So the metrics come from the *standing* strips only (Idle, and Walk for a creature
    /// whose idle is a crouch — the Wild Zombie kneels to feed and walks on all fours),
    /// and every strip is then drawn through the one transform.
    /// </para>
    /// </remarks>
    /// <param name="CentreX">
    /// The figure's own centre line in canvas pixels — measured, never assumed, because
    /// the packs do not agree on it (the Knight stands at 33 of its 128, the Goblin at 62).
    /// </param>
    /// <param name="GroundY">The canvas row the figure's feet rest on.</param>
    /// <param name="Stature">How tall the figure stands, in canvas pixels.</param>
    /// <param name="Breadth">How wide it stands — what decides whether it fits a square.</param>
    public sealed record Figure(float CentreX, float GroundY, int Stature, int Breadth);

    /// <summary>The animations one combatant's art carries. Any of them may be missing.</summary>
    /// <remarks>
    /// The Priest packs genuinely have no Dead or Hurt sheet — the screen substitutes a
    /// rotated idle frame for a fallen priest rather than a circle, so a downed Cleric
    /// still reads as a body on the ground, and simply skips the flinch. Attack is the
    /// first strip the pack offers under its three spellings (<c>Attack.png</c>,
    /// <c>Attack_1.png</c>, <c>Attack 1.png</c> — the packs disagree with each other, not
    /// with themselves); a combatant whose pack has none simply never swings on screen.
    /// </remarks>
    /// <param name="Repose">
    /// The frame of <paramref name="Dead"/> a body settles on — see
    /// <see cref="RestingFrame"/>. Zero when there is no death strip.
    /// </param>
    public sealed record CharacterArt(
        Strip? Idle,
        Strip? Walk,
        Strip? Dead,
        Strip? Attack,
        Strip? Hurt,
        Figure Figure,
        int Repose);

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
        // The three goblin packs are a small crouching one, a plain one and an armoured
        // one carrying a shield, so they sort onto the three printed goblins by build.
        // The Warrior takes a hand-drawn still instead: asked for directly, on the
        // stated preference that the drawing is liked better than the animation. The
        // Minion and Boss keep their packs because they are what tells the three goblins
        // apart on the board, not because a pack outranks a drawing.
        ["Goblin Minion"] = "Goblin_3",
        ["Goblin Warrior"] = "Goblin_Drawn",
        ["Goblin Boss"] = "Goblin_2",

        // Hand-drawn single frames rather than animated packs, added 2026-08-16 because
        // these four are among the creatures most often drawn and every one of them was
        // showing as a bare lettered circle beside a party in full animation. A still
        // token that looks like the thing it is beats a moving one that does not.
        ["Gnoll Warrior"] = "Gnoll_Warrior",
        ["Black Bear"] = "Black_Bear",
        ["Brown Bear"] = "Brown_Bear",
        ["Giant Wasp"] = "Giant_Wasp",
        ["Dire Wolf"] = "Dire_Wolf",
        ["Giant Eagle"] = "Giant_Eagle",
        ["Giant Hyena"] = "Giant_Hyena",
        ["Ape"] = "Ape",
        ["Hobgoblin Warrior"] = "Hobgoblin_Warrior",
        ["Tough"] = "Tough",
        ["Sahuagin Warrior"] = "Sahuagin_Warrior",
        ["Giant Wolf Spider"] = "Giant_Wolf_Spider",
        ["Berserker"] = "Berserker",
        ["Animated Armor"] = "Animated_Armor",

        // One drawing for both printed bugbears. A Bugbear Stalker is a bugbear, and the
        // alternative is leaving it a lettered circle beside a Warrior that has a body —
        // the initiative list and the token's own letter still tell them apart. This is
        // the opposite call to the goblins, where three distinct packs were worth keeping
        // because the builds are what distinguishes the three printed goblins on sight.
        ["Bugbear Warrior"] = "Bugbear",
        ["Bugbear Stalker"] = "Bugbear",
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
        ["Scout"] = "Scout_Human",
        ["Red Dragon Wyrmling"] = "Dragon_1",
        ["White Dragon Wyrmling"] = "Dragon_2",
    };

    private readonly Dictionary<string, CharacterArt> _bySheetFolder;

    private SpriteLibrary(
        Dictionary<string, CharacterArt> bySheetFolder,
        Strip? arrow,
        Strip? bolt,
        IReadOnlyList<GroundTheme> themes)
    {
        _bySheetFolder = bySheetFolder;
        Arrow = arrow;
        Bolt = bolt;
        Themes = themes;
    }

    /// <summary>
    /// One look for a battlefield: the ground under everything, and the two things that
    /// stand on it.
    /// </summary>
    /// <remarks>
    /// <b>Ground is a flat tile on purpose.</b> The pack's textured tiles are edge and
    /// cliff pieces — laid across a field they repeat visibly and turn a tactical board
    /// into wallpaper. A plain fill recedes, the obstacles carry the scene, and what a
    /// player needs to read at a glance stays readable. A theme is picked per fight, so
    /// one battle is woodland and the next is bare rock.
    /// </remarks>
    /// <param name="Name">The theme's name, for the record.</param>
    /// <param name="Ground">
    /// A strip of interchangeable 16-pixel tiles. Several rather than one, because a
    /// single tile laid over a whole field is still a lattice — just a finer one than
    /// grid lines were. The board picks per square.
    /// </param>
    /// <param name="Wall">What stands where the battlefield is impassable.</param>
    /// <param name="Low">What stands on a low obstacle — smaller, since it does not block.</param>
    public sealed record GroundTheme(string Name, Strip Ground, Texture2D? Wall, Texture2D? Low);

    /// <summary>The battlefield looks available, or empty when the art is absent.</summary>
    public IReadOnlyList<GroundTheme> Themes { get; }

    /// <summary>
    /// The arrow that crosses the board for a ranged weapon attack, drawn pointing right
    /// and rotated along its flight. Null when the art is absent.
    /// </summary>
    public Strip? Arrow { get; }

    /// <summary>The bolt a spell attack throws — a looping strip, rotated the same way.</summary>
    public Strip? Bolt { get; }

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
            return new SpriteLibrary(loaded, null, null, []);
        }

        // The two projectiles, in a folder of their own rather than borrowed from the
        // creature that happens to ship them: any archer fires the same arrow, and a
        // sheet keyed to a stat block would tie a Rogue's shortbow to the Skeleton
        // Archer's presence on disk. Both are optional, like every other sheet.
        var projectiles = Path.Combine(root, "Projectiles");
        var arrow = LoadStrip(Path.Combine(projectiles, "Arrow.png"));
        var bolt = LoadStrip(Path.Combine(projectiles, "Bolt.png"));

        // The battlefield's own art, themed. A theme needs its ground; the things that
        // stand on it are optional, and a theme missing them simply has bare ground.
        var terrain = Path.Combine(root, "Terrain");
        var tree = LoadTexture(Path.Combine(terrain, "Tree.png"));
        var rock = LoadTexture(Path.Combine(terrain, "Rock.png"));
        var bush = LoadTexture(Path.Combine(terrain, "Bush.png"));

        var themes = new List<GroundTheme>();

        foreach (var (name, wall) in new[] { ("Woodland", tree), ("Rocky", rock), ("Barren", rock) })
        {
            if (LoadStrip(Path.Combine(terrain, $"Ground_{name}.png")) is { } ground)
            {
                themes.Add(new GroundTheme(name, ground, wall, bush));
            }
        }

        foreach (var folder in ByClassName.Values.Concat(ByMonsterName.Values).Distinct())
        {
            var directory = Path.Combine(root, folder);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            var idle = LoadStrip(Path.Combine(directory, "Idle.png"));

            // Idle is the one animation a token cannot do without: it is the frame a
            // standing combatant shows every moment it is doing nothing else, and the
            // figure's measurements are taken from it.
            if (idle is null)
            {
                continue;
            }

            var walk = LoadStrip(Path.Combine(directory, "Walk.png"));
            var deadPath = Path.Combine(directory, "Dead.png");
            var dead = LoadStrip(deadPath);

            var figure = Measure(
                Path.Combine(directory, "Idle.png"),
                walk is null ? null : Path.Combine(directory, "Walk.png"),
                idle.FrameSize);

            loaded[folder] = new CharacterArt(
                idle,
                walk,
                dead,
                LoadStrip(Path.Combine(directory, "Attack.png"))
                    ?? LoadStrip(Path.Combine(directory, "Attack_1.png"))
                    ?? LoadStrip(Path.Combine(directory, "Attack 1.png")),
                LoadStrip(Path.Combine(directory, "Hurt.png")),
                figure,
                dead is null ? 0 : RestingFrame(deadPath, figure));
        }

        return new SpriteLibrary(loaded, arrow, bolt, themes);
    }

    /// <summary>One whole image as a texture, or null when it is not there.</summary>
    private static Texture2D? LoadTexture(string path) =>
        LoadImage(path) is { } image ? ImageTexture.CreateFromImage(image) : null;

    private static Strip? LoadStrip(string path)
    {
        if (LoadImage(path) is not { } image)
        {
            return null;
        }

        var frameSize = image.GetHeight();

        if (frameSize == 0)
        {
            return null;
        }

        // A sheet narrower than it is tall is not a strip at all — it is a single
        // standing figure, which is what hand-drawn art for one creature looks like
        // beside the packs' animated rows. It is padded out to a square frame rather
        // than rejected, so one drawing is a complete (if motionless) token: every
        // metric in this file is measured from a square frame, and every pose falls back
        // to Idle when its own strip is missing.
        //
        // Padded horizontally centred and *not* vertically, because the packs are
        // canvas-aligned with the figure's feet on the bottom edge and Figure's ground
        // line depends on it. Growing the canvas upward keeps the feet where they are.
        if (image.GetWidth() < frameSize)
        {
            var square = Image.CreateEmpty(frameSize, frameSize, false, image.GetFormat());
            square.Fill(new Color(0, 0, 0, 0));
            square.BlitRect(
                image,
                new Rect2I(0, 0, image.GetWidth(), image.GetHeight()),
                new Vector2I((frameSize - image.GetWidth()) / 2, 0));

            image = square;
        }

        return new Strip(
            ImageTexture.CreateFromImage(image),
            image.GetWidth() / frameSize,
            frameSize);
    }

    private static Image? LoadImage(string path)
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
        return image;
    }

    /// <summary>
    /// Measures where the figure stands and how big it is, from the strips in which it
    /// is standing up.
    /// </summary>
    /// <remarks>
    /// Stature is the taller of the two strips' typical frames rather than the idle's
    /// alone, which is what a crouching idle needs: the Wild Zombie kneels to feed, and
    /// measured on that pose it would be drawn at half the size it walks at. Each strip
    /// is summarised by its *median* frame so that one lunging or crouching frame cannot
    /// set the size of the whole character.
    /// </remarks>
    private static Figure Measure(string idlePath, string? walkPath, int frameSize)
    {
        var idle = FrameBoxes(idlePath);
        var walk = walkPath is null ? [] : FrameBoxes(walkPath);

        var standing = walk.Count > 0 && Median(walk, box => box.Size.Y) > Median(idle, box => box.Size.Y)
            ? walk
            : idle;

        // The ground line is the canvas's own, which is where every strip these packs
        // ship puts the feet; falling back to the measured bottom keeps a pack that
        // padded its canvas from floating above the floor.
        var ground = standing.Count == 0 ? frameSize : standing.Max(box => box.End.Y);

        return new Figure(
            idle.Count == 0 ? frameSize / 2f : Median(idle, box => box.Position.X + (box.Size.X / 2)),
            Math.Max(ground, frameSize * 0.75f),
            Math.Max(1, Median(standing, box => box.Size.Y)),
            Math.Max(1, Median(standing, box => box.Size.X)));
    }

    /// <summary>
    /// The frame of a death strip that a body should settle on and hold.
    /// </summary>
    /// <remarks>
    /// <b>Not the last one</b>, which is what this held at first and what made a killed
    /// goblin a red smear on the floor. Every pack's death animation ends by taking the
    /// body away — sinking it, or fading it out — so the final frame is a seven-pixel
    /// remnant rather than a corpse. The frame worth keeping is the most substantial one
    /// in which the creature is actually *down*: of the frames below half its standing
    /// height, the one with the most pixels still drawn. A pack whose body never drops
    /// that low (the Knights lie propped on an elbow) falls back to the fullest frame of
    /// the strip's second half, which skips the fade for the same reason.
    /// </remarks>
    private static int RestingFrame(string deadPath, Figure figure)
    {
        var frames = FrameWeights(deadPath);

        if (frames.Count == 0)
        {
            return 0;
        }

        var lying = frames
            .Where(frame => frame.Height * 2 < figure.Stature)
            .ToArray();

        var candidates = lying.Length > 0
            ? lying
            : [.. frames.Skip(frames.Count / 2)];

        return candidates.MaxBy(frame => frame.Pixels).Index;
    }

    /// <summary>Each frame's height and how much of it is actually drawn.</summary>
    private static List<(int Index, int Height, int Pixels)> FrameWeights(string path)
    {
        var frames = new List<(int, int, int)>();

        if (LoadImage(path) is not { } image)
        {
            return frames;
        }

        var data = image.GetData();
        var width = image.GetWidth();
        var frameSize = image.GetHeight();

        if (frameSize == 0 || width < frameSize)
        {
            return frames;
        }

        for (var frame = 0; frame < width / frameSize; frame++)
        {
            int top = frameSize, bottom = -1, pixels = 0;

            for (var y = 0; y < frameSize; y++)
            {
                for (var x = 0; x < frameSize; x++)
                {
                    // Weighed by opacity rather than counted: these strips fade a body
                    // out as well as flattening it, and a ghost is not a corpse either.
                    var alpha = data[(((y * width) + (frame * frameSize) + x) * 4) + 3];

                    if (alpha < 32)
                    {
                        continue;
                    }

                    pixels += alpha;
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }

            frames.Add((frame, bottom < top ? 0 : (bottom - top) + 1, pixels));
        }

        return frames;
    }

    private static int Median(IReadOnlyList<Rect2I> boxes, Func<Rect2I, int> of)
    {
        if (boxes.Count == 0)
        {
            return 1;
        }

        var sorted = boxes.Select(of).Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    /// <summary>Each frame's opaque box, in that frame's own coordinates.</summary>
    private static List<Rect2I> FrameBoxes(string path)
    {
        var boxes = new List<Rect2I>();

        if (LoadImage(path) is not { } image)
        {
            return boxes;
        }

        var data = image.GetData();
        var width = image.GetWidth();
        var frameSize = image.GetHeight();

        if (frameSize == 0 || width < frameSize)
        {
            return boxes;
        }

        for (var frame = 0; frame < width / frameSize; frame++)
        {
            int minX = frameSize, minY = frameSize, maxX = -1, maxY = -1;

            for (var y = 0; y < frameSize; y++)
            {
                for (var x = 0; x < frameSize; x++)
                {
                    if (data[(((y * width) + (frame * frameSize) + x) * 4) + 3] < 32)
                    {
                        continue;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            // An empty frame is a legitimate part of a strip (a flash of nothing between
            // poses); it contributes no measurement rather than a zero-sized figure.
            if (maxX >= minX)
            {
                boxes.Add(new Rect2I(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1));
            }
        }

        return boxes;
    }
}
