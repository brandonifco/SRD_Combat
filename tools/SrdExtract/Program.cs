using System.Globalization;
using SRDCombat.Core.Definitions;
using SRDCombat.Content;
using SRDCombat.Content.Validation;
using SrdExtract;
using SrdExtract.Parsing;
using SrdExtract.Pdf;

var options = ExtractOptions.Parse(args);

if (options is null)
{
    Console.WriteLine(ExtractOptions.Usage);
    return 2;
}

if (!File.Exists(options.PdfPath))
{
    Console.Error.WriteLine($"SRD PDF not found at '{options.PdfPath}'.");
    Console.Error.WriteLine("Pass --pdf <path>. The PDF is not committed — see CLAUDE.md.");
    return 1;
}

Console.WriteLine($"Reading {options.PdfPath}");

var monsterLines = PageTextReader.Read(
    options.PdfPath,
    SrdPages.MonstersFirstPage,
    SrdPages.AnimalsLastPage,
    PageLayout.TwoColumn);

var weaponLines = PageTextReader.Read(
    options.PdfPath,
    SrdPages.WeaponsTablePage,
    SrdPages.WeaponsTablePage,
    PageLayout.FullWidth);

var armorLines = PageTextReader.Read(
    options.PdfPath,
    SrdPages.ArmorTablePage,
    SrdPages.ArmorTablePage,
    PageLayout.FullWidth);

var originLines = PageTextReader.Read(
    options.PdfPath,
    SrdPages.OriginsFirstPage,
    SrdPages.OriginsLastPage,
    PageLayout.TwoColumn);

var monsterResult = MonsterParser.Parse(monsterLines);
var originResult = OriginParser.Parse(originLines);
var equipmentResult = EquipmentParser.Parse(weaponLines, armorLines);

var (monsters, correctionDiagnostics) = KnownCorrections.Apply(monsterResult.Monsters);

Console.WriteLine();
Console.WriteLine($"Parsed {monsters.Count} monsters, " +
                  $"{equipmentResult.Weapons.Count} weapons, {equipmentResult.Armor.Count} armor, " +
                  $"{originResult.Species.Count} species, {originResult.Backgrounds.Count} backgrounds.");

Report(
    "Parse diagnostics",
    monsterResult.Diagnostics
        .Concat(equipmentResult.Diagnostics)
        .Concat(correctionDiagnostics)
        .Concat(originResult.Diagnostics)
        .Select(d => d.ToString()));

ReportMechanicsCoverage(monsters);
ReportTraitCoverage(originResult.Species);

var validation = new List<ValidationIssue>();
validation.AddRange(MonsterValidator.Validate(monsters).Issues);
validation.AddRange(EquipmentValidator.ValidateWeapons(equipmentResult.Weapons).Issues);
validation.AddRange(EquipmentValidator.ValidateArmor(equipmentResult.Armor).Issues);
validation.AddRange(OriginValidator.ValidateSpecies(originResult.Species).Issues);
validation.AddRange(OriginValidator.ValidateBackgrounds(originResult.Backgrounds).Issues);

Report("Validation errors", validation
    .Where(issue => issue.Severity == ValidationSeverity.Error)
    .Select(issue => issue.ToString()));

Report("Validation warnings", validation
    .Where(issue => issue.Severity == ValidationSeverity.Warning)
    .Select(issue => issue.ToString()));

var errorCount = validation.Count(issue => issue.Severity == ValidationSeverity.Error);

if (errorCount > 0 && !options.Force)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Refusing to write: {errorCount} validation error(s). Pass --force to write anyway.");
    return 1;
}

ContentLoader.WritePack(options.OutputDirectory, ContentLoader.MonstersFileName, "monsters", monsters);
ContentLoader.WritePack(options.OutputDirectory, ContentLoader.WeaponsFileName, "weapons", equipmentResult.Weapons);
ContentLoader.WritePack(options.OutputDirectory, ContentLoader.ArmorFileName, "armor", equipmentResult.Armor);
ContentLoader.WritePack(options.OutputDirectory, ContentLoader.SpeciesFileName, "species", originResult.Species);
ContentLoader.WritePack(
    options.OutputDirectory,
    ContentLoader.BackgroundsFileName,
    "backgrounds",
    originResult.Backgrounds);

Console.WriteLine();
Console.WriteLine($"Wrote content to {Path.GetFullPath(options.OutputDirectory)}");

return errorCount > 0 ? 1 : 0;

/// <summary>
/// Prints how much of the bestiary's mechanics the model actually expresses.
/// </summary>
/// <remarks>
/// The tier-1 breakdown is separate because that is the band the gauntlet spends its
/// encounter budget in, so it is the number that decides whether the game plays
/// correctly. A gap here is not a warning to be silenced — it is the work remaining,
/// stated as a number instead of hidden inside prose the engine ignores.
/// </remarks>
static void ReportMechanicsCoverage(IReadOnlyList<MonsterDefinition> monsters)
{
    void Summarise(string label, IReadOnlyList<MonsterDefinition> subject)
    {
        var entries = subject.SelectMany(monster => monster.Entries).ToList();

        if (entries.Count == 0)
        {
            return;
        }

        var modelled = entries.Count(entry => entry.IsFullyModelled);

        Console.WriteLine();
        Console.WriteLine(
            $"{label}: {modelled}/{entries.Count} entries fully modelled " +
            $"({modelled * 100.0 / entries.Count:F0}%)");

        foreach (var group in entries
                     .GroupBy(entry => entry.Mechanics)
                     .OrderByDescending(group => group.Count()))
        {
            var partial = group.Count(entry => !entry.IsFullyModelled);
            var note = partial > 0 && group.Key != EntryMechanics.Unmodelled
                ? $"  ({partial} carry clauses the model cannot express)"
                : string.Empty;

            Console.WriteLine($"  {group.Count(),5}  {group.Key}{note}");
        }

    }

    Summarise("Mechanics coverage, whole bestiary", monsters);
    Summarise(
        "Mechanics coverage, CR 0-4 (the gauntlet's band)",
        monsters.Where(monster => monster.ChallengeRating <= 4m).ToList());
}

/// <summary>
/// Prints how much of the species traits the model expresses, on the same terms as the
/// bestiary. Species traits are mechanics too — Dwarven Resilience is Poison resistance
/// plus Advantage on a save — so they are counted rather than assumed to be flavour.
/// </summary>
static void ReportTraitCoverage(IReadOnlyList<SpeciesDefinition> species)
{
    var traits = species.SelectMany(entry => entry.Traits).ToList();

    if (traits.Count == 0)
    {
        return;
    }

    var modelled = traits.Count(trait => trait.IsFullyModelled);

    Console.WriteLine();
    Console.WriteLine(
        $"Species trait coverage: {modelled}/{traits.Count} fully modelled " +
        $"({modelled * 100.0 / traits.Count:F0}%)");

    foreach (var group in traits.GroupBy(trait => trait.Mechanics).OrderByDescending(group => group.Count()))
    {
        Console.WriteLine($"  {group.Count(),5}  {group.Key}");
    }
}

static void Report(string heading, IEnumerable<string> messages)
{
    var lines = messages.ToList();

    Console.WriteLine();
    Console.WriteLine($"{heading}: {lines.Count}");

    // Enough to see the shape of a systemic problem without burying the summary.
    const int Shown = 25;

    foreach (var line in lines.Take(Shown))
    {
        Console.WriteLine("  " + line);
    }

    if (lines.Count > Shown)
    {
        Console.WriteLine($"  ... and {lines.Count - Shown} more");
    }
}

namespace SrdExtract
{
    /// <summary>
    /// Printed page numbers in SRD 5.2.1, which happen to match the PDF's own page
    /// indices exactly — verified, not assumed.
    /// </summary>
    internal static class SrdPages
    {
        /// <summary>
        /// Monsters A–Z. Deliberately starts after the "Stat Block Overview" pages
        /// (254–257), which contain an annotated example block that would otherwise
        /// parse as a real creature.
        /// </summary>
        public const int MonstersFirstPage = 258;

        /// <summary>The last page of the Animals section, which follows Monsters A–Z.</summary>
        public const int AnimalsLastPage = 364;

        public const int WeaponsTablePage = 91;

        public const int ArmorTablePage = 92;

        /// <summary>Character Origins: backgrounds, then the species descriptions.</summary>
        public const int OriginsFirstPage = 83;

        /// <summary>The last species page, before Feats begins on 87.</summary>
        public const int OriginsLastPage = 86;
    }

    internal sealed record ExtractOptions(string PdfPath, string OutputDirectory, bool Force)
    {
        public const string Usage = """
            Usage: SrdExtract [--pdf <path>] [--out <directory>] [--force]

              --pdf    Path to SRD_CC_v5.2.1.pdf. Defaults to ~/Downloads/SRD_CC_v5.2.1.pdf.
              --out    Directory to write content into. Defaults to ./data/srd.
              --force  Write the content even when validation reports errors.
            """;

        public static ExtractOptions? Parse(string[] args)
        {
            var pdf = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "SRD_CC_v5.2.1.pdf");

            var output = Path.Combine("data", "srd");
            var force = false;

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index].ToLower(CultureInfo.InvariantCulture))
                {
                    case "--pdf" when index + 1 < args.Length:
                        pdf = args[++index];
                        break;
                    case "--out" when index + 1 < args.Length:
                        output = args[++index];
                        break;
                    case "--force":
                        force = true;
                        break;
                    case "-h" or "--help":
                        return null;
                    default:
                        Console.Error.WriteLine($"Unrecognised argument '{args[index]}'.");
                        return null;
                }
            }

            return new ExtractOptions(pdf, output, force);
        }
    }
}
