using Godot;
using SRDCombat.Core.Combat;

namespace SRDCombat.Viewer;

/// <summary>
/// The token art: sprites for the combatants, loaded at runtime from
/// <c>assets/sprites/</c> under the project directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>Almost everything shipped today is Brandon's own committed still</b> — one
/// hand-drawn frame per pose, run through the committed pipeline
/// (<c>tools/asset_pipeline/master_to_sprite.py</c>, #294) and palette-mapped, rather
/// than an animated pack. The loader itself is still pack-agnostic: it reads whatever
/// strips a mapped folder holds, multi-frame or single. That generality used to matter
/// because most of the roster wore the free Craftpix character packs; as of #295 no
/// entry in either map below points at one any more (the last, gitignored-only
/// mappings — the eight remaining party classes, and Goblin Boss/Gladiator/Knight/
/// Mage/Archmage/Priest/Priest Acolyte — were removed rather than left pointing at a
/// folder that can never ship, per #295's acceptance criteria). Craftpix's free
/// license permits using the art in a game but not redistributing the source assets,
/// so a folder like that exists only on a machine that downloaded the pack by hand and
/// would never appear in a distributed build regardless of what the maps below say.
/// The loader's fallback still holds for any future gap: a directory that isn't there
/// (Craftpix-sourced or otherwise) gets skipped silently, and the screens draw the
/// circle-and-letter tokens they always drew. Nothing here is load-bearing.
/// </para>
/// <para>
/// <b>The two maps are curated, and absence is honest.</b> A monster gets art only when
/// a real drawing depicts it, and a name with no drawing stays a circle rather than
/// wearing the wrong body. The dragons are the stated example: the old Craftpix packs
/// held only two colours, so a red sprite labelled "Green Dragon Wyrmling" would have
/// been the display lying about the one thing a player can check against the log —
/// which is why all five chromatic wyrmlings (Black, Blue, Green, Red, White) wear
/// Brandon's own stills instead, and the five metallic wyrmlings stay circles for want
/// of a drawing. Party art maps from the class name
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
    /// <param name="Cast">
    /// The casting pose, played on the engine's <c>SpellCast</c> step. Only the drawn
    /// sets carry one (the packs never did), and a combatant without it simply casts
    /// without a pose, exactly as everything did before the Cleric's arrived.
    /// </param>
    public sealed record CharacterArt(
        Strip? Idle,
        Strip? Walk,
        Strip? Dead,
        Strip? Attack,
        Strip? Hurt,
        Strip? Cast,
        Figure Figure,
        int Repose);

    /// <summary>
    /// Party art by class name. The SRD prints twelve; four ship drawn art today and
    /// the other eight are unmapped (see the removal note below), so those eight draw
    /// the circle-and-letter token until Brandon draws them.
    /// </summary>
    private static readonly Dictionary<string, string> ByClassName = new(StringComparer.Ordinal)
    {
        // Hand-drawn sets — single frames per pose (four, five with a Cast — the
        // Cleric is the first to carry one), produced from painted sources
        // through the measured pipeline the Barbarian repaint recorded (crop, box
        // downscale with an unsharp pass at twice target size, quantize to the master
        // palette at client/assets/palette/SRD_Combat.gpl with hard alpha). Each pose
        // is one frame, which the loader pads and plays across its duration. That
        // recipe is now a committed, deterministic script rather than a hand
        // process — tools/asset_pipeline/master_to_sprite.py (#294) — with a
        // per-image de-grain pass added on top; see its module docstring for why
        // it clusters per image rather than across a character's poses the way
        // PR #238's reverted pass did.
        ["Fighter"] = "Fighter_Drawn",
        ["Barbarian"] = "Barbarian_Drawn",
        ["Rogue"] = "Rogue_Drawn",
        ["Cleric"] = "Cleric_Drawn",

        // Paladin, Monk, Bard, Ranger, Druid, Wizard, Sorcerer and Warlock stood here
        // mapped to Craftpix folders (Knight_2, Gladiator_2, Elf_2, Elf_3, Priests_3,
        // "Wanderer Magican", "Fire Wizard", "Lightning Mage") until #295. Every one of
        // those folders is gitignored — Craftpix's free license permits using the art
        // in a game but forbids redistributing it, so the folder exists only on a
        // machine that downloaded the pack by hand (client/README.md's "Sprite art"
        // section). A mapping whose only target can never be committed is not a real
        // fallback: `Directory.Exists` already turns a missing folder into a circle at
        // runtime, but leaving the entry in source claims these eight classes have art
        // when no shipped build — the one a stranger downloads per the Definition of
        // Finished — ever will. Removed rather than left pointing at a folder that
        // will never ship, per #295's "re-point at shipped art or explicitly fall
        // back" acceptance criterion: no drawn replacement exists yet for any of the
        // eight, so explicit fallback (no entry, an honest circle) is what's left.
        // Add the entry back, pointing at a committed folder, the day one is drawn.
    };

    /// <summary>
    /// Monster art by exact stat-block name. Exact on purpose — the PlausibleFoes lesson:
    /// a substring test would dress creatures in bodies that are not theirs.
    /// </summary>
    private static readonly Dictionary<string, string> ByMonsterName = new(StringComparer.Ordinal)
    {
        // The three printed goblins told apart by build: the Minion and Warrior wear
        // Brandon's drawings. The Boss mapped to the Craftpix "Goblin_2" pack until
        // #295 removed it — that folder is gitignored (see the ByClassName removal
        // note above for why a Craftpix-only mapping doesn't survive #295) and no
        // drawing exists yet, so the Boss is an honest circle until one does.
        ["Goblin Minion"] = "Goblin_Minion",
        ["Goblin Warrior"] = "Goblin_Drawn",

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
        ["Awakened Tree"] = "Awakened_Tree",
        ["Bandit Captain"] = "Bandit_Captain",
        ["Black Dragon Wyrmling"] = "Black_Dragon_Wyrmling",
        ["Archelon"] = "Archelon",
        ["Azer Sentinel"] = "Azer_Sentinel",
        ["Ankheg"] = "Ankheg",
        ["Giant Vulture"] = "Giant_Vulture",

        // The 2026-08-20 batch: twelve more of Brandon's stills, drawn against the
        // traditional roster's optimal-dimensions spec, retiring their circle tokens.
        ["Axe Beak"] = "Axe_Beak",
        ["Ettin"] = "Ettin",
        ["Giant Bat"] = "Giant_Bat",
        ["Giant Lizard"] = "Giant_Lizard",
        ["Hippogriff"] = "Hippogriff",
        ["Manticore"] = "Manticore",
        ["Ochre Jelly"] = "Ochre_Jelly",
        ["Ogre"] = "Ogre",
        ["Owlbear"] = "Owlbear",
        ["Swarm of Crawling Claws"] = "Swarm_of_Crawling_Claws",
        ["Violet Fungus"] = "Violet_Fungus",
        ["Winter Wolf"] = "Winter_Wolf",

        // The 2026-08-21 batch: Brandon's stills for the two printed bugbears (each has
        // its own drawing now — the shared body that stood in for both is retired), the
        // five dragon wyrmlings (the Red and White drop the two Craftpix dragons, and
        // the other three colours shed circles a pack could only have lied about), the
        // four swarms, and the rest of the roster's bare circles.
        ["Bugbear Warrior"] = "Bugbear_Warrior",
        ["Bugbear Stalker"] = "Bugbear_Stalker",
        ["Blue Dragon Wyrmling"] = "Blue_Dragon_Wyrmling",
        ["Green Dragon Wyrmling"] = "Green_Dragon_Wyrmling",
        ["Centaur Trooper"] = "Centaur_Trooper",
        ["Gargoyle"] = "Gargoyle",
        ["Lemure"] = "Lemure",
        ["Wolf"] = "Wolf",
        ["Swarm of Bats"] = "Swarm_of_Bats",
        ["Swarm of Insects"] = "Swarm_of_Insects",
        ["Swarm of Rats"] = "Swarm_of_Rats",
        ["Swarm of Venomous Snakes"] = "Swarm_of_Venomous_Snakes",

        // The second 2026-08-21 batch: twenty-one more slots drawn, the classic-roster
        // core among them — and the Skeleton, Zombie, Ogre Zombie and Cultist move off
        // the packs that stood in for them onto Brandon's own stills.
        ["Animated Flying Sword"] = "Animated_Flying_Sword",
        ["Awakened Shrub"] = "Awakened_Shrub",
        ["Bandit"] = "Bandit",
        ["Ghast"] = "Ghast",
        ["Giant Centipede"] = "Giant_Centipede",
        ["Giant Fire Beetle"] = "Giant_Fire_Beetle",
        ["Giant Rat"] = "Giant_Rat",
        ["Guard"] = "Guard",
        ["Hobgoblin Captain"] = "Hobgoblin_Captain",
        ["Kobold Warrior"] = "Kobold_Warrior",
        ["Magma Mephit"] = "Magma_Mephit",
        ["Pirate"] = "Pirate",
        ["Steam Mephit"] = "Steam_Mephit",
        ["Troll Limb"] = "Troll_Limb",
        ["Warrior Veteran"] = "Warrior_Veteran",
        ["Worg"] = "Worg",
        ["Skeleton"] = "Skeleton",
        ["Zombie"] = "Zombie",
        ["Ogre Zombie"] = "Ogre_Zombie",
        // Gladiator, Knight, Mage, Archmage, Priest and Priest Acolyte mapped to
        // Craftpix folders (Gladiator_3, Knight_3, "Lightning Mage" twice over,
        // Priests_2, Priests_3) until #295 removed them for the same reason as the
        // ByClassName removals above: those folders are gitignored and will never
        // ship, and no drawing exists yet for any of the six, so they render as
        // circles now rather than silently depending on packs a stranger's build
        // will never have.
        ["Cultist"] = "Cultist",
        ["Scout"] = "Scout_Human",
        ["Red Dragon Wyrmling"] = "Red_Dragon_Wyrmling",
        ["White Dragon Wyrmling"] = "White_Dragon_Wyrmling",

        // #295: two more of Brandon's stills, run through the committed pipeline
        // (tools/asset_pipeline/master_to_sprite.py) at its default parameters —
        // both were 100% palette-conformant on the first pass, so neither needed a
        // SPRITE_TARGETS override.
        ["Guard Captain"] = "Guard_Captain",
        ["Warrior Infantry"] = "Warrior_Infantry",
    };

    private readonly Dictionary<string, CharacterArt> _bySheetFolder;
    private readonly Dictionary<string, Strip> _projectiles;

    private SpriteLibrary(
        Dictionary<string, CharacterArt> bySheetFolder,
        Dictionary<string, Strip> projectiles,
        IReadOnlyList<GroundTheme> themes)
    {
        _bySheetFolder = bySheetFolder;
        _projectiles = projectiles;
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
    /// A strip of interchangeable 48-pixel tiles, one per movement square. Several
    /// rather than one, because a single tile laid over a whole field is still a
    /// lattice — just a finer one than grid lines were. The board picks per square, and
    /// the pick is uniform over the strip, so a tile's frequency is set by how many
    /// times it appears there: bases repeat, accents appear once.
    /// </param>
    /// <param name="Wall">
    /// What stands where the battlefield is impassable — variants, one chosen per
    /// footprint by its anchor's hash, so twin pillars on one field need not be twins.
    /// Empty means no art and the flat colour.
    /// </param>
    /// <param name="Low">
    /// What stands on a low obstacle — smaller, since it does not block. Variants,
    /// chosen the same way; empty means none.
    /// </param>
    /// <param name="Difficult">
    /// What Difficult Terrain wears, one drawing per square, variants chosen by the
    /// square's own hash — brambles on the woodland, rubble and scrub when their art
    /// arrives. Empty means the theme has no difficult art yet and the board keeps its
    /// wash: the rule may never go invisible for want of a drawing.
    /// </param>
    public sealed record GroundTheme(
        string Name,
        Strip Ground,
        IReadOnlyList<Texture2D> Wall,
        IReadOnlyList<Texture2D> Low,
        IReadOnlyList<Texture2D> Difficult);

    /// <summary>The battlefield looks available, or empty when the art is absent.</summary>
    public IReadOnlyList<GroundTheme> Themes { get; }

    /// <summary>
    /// What a ranged attack flies, or null when nothing applies. Keyed by the attack's
    /// own name first — <c>Dart.png</c> for a Dart, <c>Handaxe.png</c> tumbling for a
    /// Handaxe, spaces as underscores, the exact-name convention monster art uses — and
    /// falling back to the two generics: <c>Arrow.png</c> for any weapon,
    /// <c>Spell.png</c> (then the arrow) for a spell. The spell generic was
    /// <c>Bolt.png</c> for a day, until Brandon's crossbow bolt needed the word: a
    /// "bolt" is ammunition first, and a file name a weapon could plausibly claim is
    /// no name for a fallback. A multi-frame strip loops in flight, which is what
    /// makes a thrown axe tumble.
    /// </summary>
    public Strip? ProjectileFor(RangedAttackKind kind, string? attackName)
    {
        if (kind == RangedAttackKind.None)
        {
            return null;
        }

        if (attackName is not null
            && _projectiles.TryGetValue(attackName.Replace(' ', '_'), out var named))
        {
            return named;
        }

        return kind == RangedAttackKind.Spell
            ? _projectiles.GetValueOrDefault("Spell") ?? _projectiles.GetValueOrDefault("Arrow")
            : _projectiles.GetValueOrDefault("Arrow");
    }

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
            return new SpriteLibrary(loaded, [], []);
        }

        // The projectiles, in a folder of their own rather than borrowed from the
        // creature that happens to ship them: any archer fires the same arrow, and a
        // sheet keyed to a stat block would tie a Rogue's shortbow to the Skeleton
        // Archer's presence on disk. Every PNG in the folder is loaded by its file
        // name, so per-weapon art is a dropped file and never a code change; all of it
        // is optional, like every other sheet.
        var projectiles = new Dictionary<string, Strip>(StringComparer.Ordinal);
        var projectilesFolder = Path.Combine(root, "Projectiles");

        if (Directory.Exists(projectilesFolder))
        {
            foreach (var sheet in Directory.EnumerateFiles(projectilesFolder, "*.png"))
            {
                if (LoadStrip(sheet) is { } strip)
                {
                    projectiles[Path.GetFileNameWithoutExtension(sheet)] = strip;
                }
            }
        }

        // The battlefield's own art, themed. A theme needs its ground; the things that
        // stand on it are optional, and a theme missing them simply has bare ground.
        // Scenery is per-theme — grey rock on the grey ground, red rock on the clay,
        // tree and brush on the grass — so a fight never mixes the families. The
        // Craftpix pack cuts (Tree/Rock/Bush) that once backed absent per-theme files
        // were deleted on 2026-08-20 once every theme carried Brandon's own drawings;
        // a theme without a drawing now falls back to the flat colours, the same
        // supported absence as everywhere else in this loader.
        var terrain = Path.Combine(root, "Terrain");

        var themes = new List<GroundTheme>();

        foreach (var name in new[] { "Woodland", "Rocky", "Barren" })
        {
            if (LoadStrip(Path.Combine(terrain, $"Ground_{name}.png")) is { } ground)
            {
                // Every slot is a variant list: the base name and its numbered
                // siblings (_2, _3, ... — the rocky rubble ships four). The _2
                // files had sat on disk unloaded since they were drawn — a variant
                // nobody loads is a variant nobody sees.
                Texture2D[] Variants(string slot)
                {
                    var own = new List<Texture2D>();

                    if (LoadTexture(Path.Combine(terrain, $"{slot}_{name}.png")) is { } first)
                    {
                        own.Add(first);
                    }

                    for (var variant = 2; variant <= 9; variant++)
                    {
                        if (LoadTexture(Path.Combine(terrain, $"{slot}_{name}_{variant}.png")) is { } next)
                        {
                            own.Add(next);
                        }
                    }

                    return [.. own];
                }

                themes.Add(new GroundTheme(
                    name,
                    ground,
                    Variants("Wall"),
                    Variants("Low"),
                    Variants("Difficult")));
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
                LoadStrip(Path.Combine(directory, "Cast.png")),
                figure,
                dead is null ? 0 : RestingFrame(deadPath, figure));
        }

        return new SpriteLibrary(loaded, projectiles, themes);
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
    /// <remarks>
    /// <b>#296 follow-up (qc, non-blocking):</b> kept aligned with <see cref="FrameBoxes"/>'s
    /// narrow-sheet fix rather than left as its half-fixed sibling. Every shipped
    /// <c>Dead.png</c> is a genuine multi-frame strip today (a death animation needs more
    /// than one frame), so <c>width &lt; frameSize</c> does not currently fire here — this
    /// is a consistency fix, not a behaviour change, and there is no before/after to show
    /// for it.
    /// </remarks>
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

        if (frameSize == 0)
        {
            return frames;
        }

        var narrow = width < frameSize;
        var frameWidth = narrow ? width : frameSize;
        var frameCount = narrow ? 1 : width / frameSize;

        for (var frame = 0; frame < frameCount; frame++)
        {
            int top = frameSize, bottom = -1, pixels = 0;
            var frameLeft = frame * frameWidth;

            for (var y = 0; y < frameSize; y++)
            {
                for (var x = 0; x < frameWidth; x++)
                {
                    // Weighed by opacity rather than counted: these strips fade a body
                    // out as well as flattening it, and a ghost is not a corpse either.
                    var alpha = data[(((y * width) + frameLeft + x) * 4) + 3];

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
    /// <remarks>
    /// <b>#296 follow-up:</b> this used to treat a sheet narrower than its own height as
    /// "not a strip at all" and return nothing, the same test <see cref="LoadStrip"/>
    /// uses to decide a sheet needs horizontal padding — but here it meant every
    /// single-drawing pack (a hand-painted portrait, not an animated strip: Fighter,
    /// Rogue, Bandit, Goblin and two dozen more) measured zero frames. <see
    /// cref="Median"/> defaults an empty set to 1, so <see cref="Measure"/> put every
    /// one of their <see cref="Figure.Stature"/>/<see cref="Figure.Breadth"/> on that
    /// floor. The footprint-clamp half of that genuinely never showed — dividing by a
    /// floor of 1 makes the clamp's own bound enormous, and every affected pack's real
    /// proportions already fall well inside a merely generous bound anyway (verified:
    /// identical <em>scale</em> before and after, for all of them). <b>The
    /// <see cref="Figure.Stature"/> half did show</b>, and qc's review of #296 caught
    /// it: a pack with no <c>Dead.png</c> draws its fallen body by rotating the idle
    /// frame about <c>ground - (Stature / 2)</c> (see <c>FightScreen.DrawSpriteToken</c>'s
    /// <c>lying</c> branch), so a corpse that used to pivot at
    /// <c>Stature = 1</c> — effectively its feet — now pivots at its real mid-height,
    /// moving roughly half a cell for every one of the ~22 affected packs with no death
    /// strip (Bandit, Skeleton, Zombie and the rest). That is almost certainly the
    /// position the expression always intended — a body centred on the square it fell
    /// in, not one hinged at the floor — so this reads as a second, previously-unseen
    /// bug fixed alongside the first, not a side effect to explain away. <see
    /// cref="Figure.CentreX"/> and a couple of packs' <see cref="Figure.GroundY"/> also
    /// shift by a few pixels for the same reason. The fix centres the one frame exactly
    /// where <see cref="LoadStrip"/> draws it, rather than rejecting it.
    /// </remarks>
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

        if (frameSize == 0)
        {
            return boxes;
        }

        var narrow = width < frameSize;
        var offsetX = narrow ? (frameSize - width) / 2 : 0;
        var frameWidth = narrow ? width : frameSize;
        var frameCount = narrow ? 1 : width / frameSize;

        for (var frame = 0; frame < frameCount; frame++)
        {
            int minX = frameSize, minY = frameSize, maxX = -1, maxY = -1;
            var frameLeft = frame * frameWidth;

            for (var y = 0; y < frameSize; y++)
            {
                for (var x = 0; x < frameWidth; x++)
                {
                    if (data[(((y * width) + frameLeft + x) * 4) + 3] < 32)
                    {
                        continue;
                    }

                    minX = Math.Min(minX, x + offsetX);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x + offsetX);
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
