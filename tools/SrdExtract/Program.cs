using System.Globalization;
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

var monsterResult = MonsterParser.Parse(monsterLines);
var equipmentResult = EquipmentParser.Parse(weaponLines, armorLines);

var (monsters, correctionDiagnostics) = KnownCorrections.Apply(monsterResult.Monsters);

Console.WriteLine();
Console.WriteLine($"Parsed {monsters.Count} monsters, " +
                  $"{equipmentResult.Weapons.Count} weapons, {equipmentResult.Armor.Count} armor.");

Report(
    "Parse diagnostics",
    monsterResult.Diagnostics
        .Concat(equipmentResult.Diagnostics)
        .Concat(correctionDiagnostics)
        .Select(d => d.ToString()));

var validation = new List<ValidationIssue>();
validation.AddRange(MonsterValidator.Validate(monsters).Issues);
validation.AddRange(EquipmentValidator.ValidateWeapons(equipmentResult.Weapons).Issues);
validation.AddRange(EquipmentValidator.ValidateArmor(equipmentResult.Armor).Issues);

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

Console.WriteLine();
Console.WriteLine($"Wrote content to {Path.GetFullPath(options.OutputDirectory)}");

return errorCount > 0 ? 1 : 0;

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
