using System.Security.Cryptography;
using System.Text;
using SRDCombat.Content.Validation;
using SRDCombat.Core.Definitions;

namespace SRDCombat.Content;

/// <summary>Everything loaded from a content directory.</summary>
public sealed record SrdContent(
    IReadOnlyList<MonsterDefinition> Monsters,
    IReadOnlyList<WeaponDefinition> Weapons,
    IReadOnlyList<ArmorDefinition> Armor,
    IReadOnlyList<SpeciesDefinition> Species,
    IReadOnlyList<BackgroundDefinition> Backgrounds,
    IReadOnlyList<ClassDefinition> Classes,
    IReadOnlyList<SpellDefinition> Spells,
    IReadOnlyList<MagicItemDefinition> MagicItems)
{
    public IReadOnlyDictionary<string, MagicItemDefinition> MagicItemsById { get; } =
        MagicItems.ToDictionary(item => item.Id, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, MonsterDefinition> MonstersById { get; } =
        Monsters.ToDictionary(monster => monster.Id, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, WeaponDefinition> WeaponsById { get; } =
        Weapons.ToDictionary(weapon => weapon.Id, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ArmorDefinition> ArmorById { get; } =
        Armor.ToDictionary(armor => armor.Id, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SpeciesDefinition> SpeciesById { get; } =
        Species.ToDictionary(species => species.Id, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, BackgroundDefinition> BackgroundsById { get; } =
        Backgrounds.ToDictionary(background => background.Id, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ClassDefinition> ClassesById { get; } =
        Classes.ToDictionary(definition => definition.Id, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SpellDefinition> SpellsById { get; } =
        Spells.ToDictionary(spell => spell.Id, StringComparer.Ordinal);

    /// <summary>
    /// A stable fingerprint of every id this content build knows — every monster,
    /// weapon, armor, species, background, class, spell and magic item, by id alone,
    /// not by the numbers behind them. Two loads with the exact same roster of ids
    /// fingerprint identically even if a description changed underneath; a roster that
    /// gained, lost or renamed anything fingerprints differently.
    /// </summary>
    /// <remarks>
    /// This is the coarse, cheap gate a save's <c>ContentVersion</c> is checked
    /// against — deliberately coarse, in the same spirit as <c>FormatVersion</c>:
    /// rather than working out whether a specific save's referenced ids happen to
    /// survive a content rebuild, refuse the whole rebuild as a mismatch and let the
    /// player re-roll a run. The four <c>TryGetValue</c> conversions
    /// (<c>PregeneratedParty</c>, <c>Gauntlet</c>, <c>Loot</c>, <c>Shop</c>) are the
    /// backstop for the same-version edge case this fingerprint cannot see — a
    /// hand-edited save, or a content id renamed without the roster otherwise moving.
    /// </remarks>
    public string ContentFingerprint { get; } = Fingerprint(
        Monsters, Weapons, Armor, Species, Backgrounds, Classes, Spells, MagicItems);

    private static string Fingerprint(
        IReadOnlyList<MonsterDefinition> monsters,
        IReadOnlyList<WeaponDefinition> weapons,
        IReadOnlyList<ArmorDefinition> armor,
        IReadOnlyList<SpeciesDefinition> species,
        IReadOnlyList<BackgroundDefinition> backgrounds,
        IReadOnlyList<ClassDefinition> classes,
        IReadOnlyList<SpellDefinition> spells,
        IReadOnlyList<MagicItemDefinition> magicItems)
    {
        var ids = new List<string>();

        ids.AddRange(monsters.Select(item => "monster:" + item.Id));
        ids.AddRange(weapons.Select(item => "weapon:" + item.Id));
        ids.AddRange(armor.Select(item => "armor:" + item.Id));
        ids.AddRange(species.Select(item => "species:" + item.Id));
        ids.AddRange(backgrounds.Select(item => "background:" + item.Id));
        ids.AddRange(classes.Select(item => "class:" + item.Id));
        ids.AddRange(spells.Select(item => "spell:" + item.Id));
        ids.AddRange(magicItems.Select(item => "magicItem:" + item.Id));
        ids.Sort(StringComparer.Ordinal);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', ids)));

        return Convert.ToHexString(hash);
    }
}

/// <summary>
/// Reads content files from disk. Loading refuses invalid content rather than
/// returning it — a monster whose hit points disagree with its hit dice is a bug in
/// the extractor, and surfacing it at load time is what keeps it from becoming a
/// mysterious combat result later.
/// </summary>
public static class ContentLoader
{
    public const string MonstersFileName = "monsters.json";
    public const string WeaponsFileName = "weapons.json";
    public const string ArmorFileName = "armor.json";

    public const string SpeciesFileName = "species.json";

    public const string BackgroundsFileName = "backgrounds.json";

    public const string ClassesFileName = "classes.json";

    public const string SpellsFileName = "spells.json";

    public const string MagicItemsFileName = "magic-items.json";

    /// <summary>Loads and validates every content file under <paramref name="directory"/>.</summary>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    /// <exception cref="ContentValidationException">Any file failed validation.</exception>
    public static SrdContent Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Content directory '{directory}' does not exist.");
        }

        var monsters = ReadPack<MonsterDefinition>(directory, MonstersFileName, "monsters");
        var weapons = ReadPack<WeaponDefinition>(directory, WeaponsFileName, "weapons");
        var armor = ReadPack<ArmorDefinition>(directory, ArmorFileName, "armor");
        var species = ReadPack<SpeciesDefinition>(directory, SpeciesFileName, "species");
        var backgrounds = ReadPack<BackgroundDefinition>(directory, BackgroundsFileName, "backgrounds");
        var classes = ReadPack<ClassDefinition>(directory, ClassesFileName, "classes");
        var spells = ReadPack<SpellDefinition>(directory, SpellsFileName, "spells");
        var magicItems = ReadPack<MagicItemDefinition>(directory, MagicItemsFileName, "magic items");

        MonsterValidator.Validate(monsters, spells).ThrowIfInvalid(MonstersFileName);
        EquipmentValidator.ValidateWeapons(weapons).ThrowIfInvalid(WeaponsFileName);
        EquipmentValidator.ValidateArmor(armor).ThrowIfInvalid(ArmorFileName);
        OriginValidator.ValidateSpecies(species).ThrowIfInvalid(SpeciesFileName);
        OriginValidator.ValidateBackgrounds(backgrounds).ThrowIfInvalid(BackgroundsFileName);
        ClassValidator.Validate(classes).ThrowIfInvalid(ClassesFileName);
        SpellValidator.Validate(spells).ThrowIfInvalid(SpellsFileName);
        MagicItemValidator.Validate(magicItems).ThrowIfInvalid(MagicItemsFileName);

        return new SrdContent(monsters, weapons, armor, species, backgrounds, classes, spells, magicItems);
    }

    /// <summary>
    /// Validates without throwing, returning every issue across every file. This is
    /// what tooling reports through; the game itself loads via <see cref="Load"/> and
    /// refuses bad content outright.
    /// </summary>
    public static ValidationResult Validate(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var issues = new List<ValidationIssue>();

        var monsters = ReadPack<MonsterDefinition>(directory, MonstersFileName, "monsters");
        var spells = ReadPack<SpellDefinition>(directory, SpellsFileName, "spells");
        issues.AddRange(MonsterValidator.Validate(monsters, spells).Issues);
        issues.AddRange(EquipmentValidator
            .ValidateWeapons(ReadPack<WeaponDefinition>(directory, WeaponsFileName, "weapons")).Issues);
        issues.AddRange(EquipmentValidator
            .ValidateArmor(ReadPack<ArmorDefinition>(directory, ArmorFileName, "armor")).Issues);
        issues.AddRange(OriginValidator
            .ValidateSpecies(ReadPack<SpeciesDefinition>(directory, SpeciesFileName, "species")).Issues);
        issues.AddRange(OriginValidator
            .ValidateBackgrounds(ReadPack<BackgroundDefinition>(directory, BackgroundsFileName, "backgrounds")).Issues);
        issues.AddRange(ClassValidator
            .Validate(ReadPack<ClassDefinition>(directory, ClassesFileName, "classes")).Issues);
        issues.AddRange(SpellValidator.Validate(spells).Issues);
        issues.AddRange(MagicItemValidator
            .Validate(ReadPack<MagicItemDefinition>(directory, MagicItemsFileName, "magic items")).Issues);

        return new ValidationResult(issues);
    }

    /// <summary>Writes a content file, using the same options the loader reads with.</summary>
    public static void WritePack<TItem>(
        string directory,
        string fileName,
        string contentName,
        IReadOnlyList<TItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(items);

        Directory.CreateDirectory(directory);

        var pack = new ContentPack<TItem>
        {
            FormatVersion = ContentSerializer.CurrentFormatVersion,
            Content = contentName,
            Source = "SRD 5.2.1, CC-BY-4.0. See NOTICE.md.",
            Items = items,
        };

        // A trailing newline keeps the files well-formed for ordinary text tooling and
        // avoids a spurious "\ No newline at end of file" on every diff.
        File.WriteAllText(
            Path.Combine(directory, fileName),
            ContentSerializer.Serialize(pack) + Environment.NewLine);
    }

    private static IReadOnlyList<TItem> ReadPack<TItem>(string directory, string fileName, string expectedContent)
    {
        var path = Path.Combine(directory, fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Content file '{fileName}' is missing.", path);
        }

        var pack = ContentSerializer.Deserialize<ContentPack<TItem>>(File.ReadAllText(path));

        if (pack.FormatVersion != ContentSerializer.CurrentFormatVersion)
        {
            throw new ContentValidationException(
                $"'{fileName}' is format version {pack.FormatVersion}; this build reads " +
                $"version {ContentSerializer.CurrentFormatVersion}.");
        }

        if (!string.Equals(pack.Content, expectedContent, StringComparison.Ordinal))
        {
            throw new ContentValidationException(
                $"'{fileName}' declares content '{pack.Content}' but was loaded as '{expectedContent}'.");
        }

        return pack.Items;
    }
}
