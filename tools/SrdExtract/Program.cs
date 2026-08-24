using System.Globalization;
using SRDCombat.Core.Definitions;
using SRDCombat.Core.Rules;
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

if (options.CensusPath is { } censusPath)
{
    return RunCensus(options.OutputDirectory, censusPath);
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

var classColumnLines = PageTextReader.Read(
    options.PdfPath,
    SrdPages.ClassesFirstPage,
    SrdPages.ClassesLastPage,
    PageLayout.TwoColumn);

// A class page mixes both layouts: the Core Traits table and the feature prose sit in
// two columns, while the Features table spans the full width at the bottom.
var classTableLines = PageTextReader.Read(
    options.PdfPath,
    SrdPages.ClassesFirstPage,
    SrdPages.ClassesLastPage,
    PageLayout.FullWidth);

var monsterResult = MonsterParser.Parse(monsterLines);
var originResult = OriginParser.Parse(originLines);
var classResult = ClassParser.Parse(classColumnLines, classTableLines);

var spellResult = SpellParser.Parse(PageTextReader.Read(
    options.PdfPath,
    SrdPages.SpellsFirstPage,
    SrdPages.SpellsLastPage,
    PageLayout.TwoColumn));
var equipmentResult = EquipmentParser.Parse(weaponLines, armorLines);

var magicItemResult = MagicItemParser.Parse(PageTextReader.Read(
    options.PdfPath,
    SrdPages.MagicItemsFirstPage,
    SrdPages.MagicItemsLastPage,
    PageLayout.TwoColumn));

var (monsters, correctionDiagnostics) = KnownCorrections.Apply(monsterResult.Monsters);

Console.WriteLine();
Console.WriteLine($"Parsed {monsters.Count} monsters, " +
                  $"{equipmentResult.Weapons.Count} weapons, {equipmentResult.Armor.Count} armor, " +
                  $"{originResult.Species.Count} species, {originResult.Backgrounds.Count} backgrounds, " +
                  $"{classResult.Classes.Count} classes, {spellResult.Spells.Count} spells, " +
                  $"{magicItemResult.Items.Count} magic items.");

Report(
    "Parse diagnostics",
    monsterResult.Diagnostics
        .Concat(equipmentResult.Diagnostics)
        .Concat(correctionDiagnostics)
        .Concat(originResult.Diagnostics)
        .Concat(classResult.Diagnostics)
        .Concat(spellResult.Diagnostics)
        .Concat(magicItemResult.Diagnostics)
        .Select(d => d.ToString()));

ReportMechanicsCoverage(monsters);
ReportTraitCoverage(originResult.Species);
ReportClassFeatureCoverage(classResult.Classes);

// The registry split, so what the loot pool may draw from stays visible: an item the
// engine executes is usable, everything else is extracted and counted.
Console.WriteLine();
Console.WriteLine(
    $"Magic items: {magicItemResult.Items.Count(MagicItemRegistry.Executes)} executed by the registry, " +
    $"{magicItemResult.Items.Count(item => !MagicItemRegistry.Executes(item))} extracted and counted as unmodelled.");

var validation = new List<ValidationIssue>();
validation.AddRange(MonsterValidator.Validate(monsters).Issues);
validation.AddRange(EquipmentValidator.ValidateWeapons(equipmentResult.Weapons).Issues);
validation.AddRange(EquipmentValidator.ValidateArmor(equipmentResult.Armor).Issues);
validation.AddRange(OriginValidator.ValidateSpecies(originResult.Species).Issues);
validation.AddRange(OriginValidator.ValidateBackgrounds(originResult.Backgrounds).Issues);
validation.AddRange(ClassValidator.Validate(classResult.Classes).Issues);
validation.AddRange(SpellValidator.Validate(spellResult.Spells).Issues);
validation.AddRange(MagicItemValidator.Validate(magicItemResult.Items).Issues);

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
ContentLoader.WritePack(options.OutputDirectory, ContentLoader.ClassesFileName, "classes", classResult.Classes);
ContentLoader.WritePack(options.OutputDirectory, ContentLoader.SpellsFileName, "spells", spellResult.Spells);
ContentLoader.WritePack(
    options.OutputDirectory,
    ContentLoader.MagicItemsFileName,
    "magic items",
    magicItemResult.Items);

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

/// <summary>Class features are mechanics too, and are counted on the same terms.</summary>
static void ReportClassFeatureCoverage(IReadOnlyList<ClassDefinition> classes)
{
    var features = classes.SelectMany(definition => definition.Features).ToList();

    if (features.Count == 0)
    {
        return;
    }

    var modelled = features.Count(feature => feature.IsFullyModelled);

    Console.WriteLine();
    Console.WriteLine(
        $"Class feature coverage: {modelled}/{features.Count} fully modelled " +
        $"({modelled * 100.0 / features.Count:F0}%)");
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

/// <summary>
/// Re-classifies every stored monster entry and dumps every span nothing claimed — the
/// census #382's stage 3 asks for (docs/2026-08-24-span-accounting-design.md §10).
/// Reads only the committed content directory; no PDF is needed, since the corpus's own
/// stored (name, section, text) is enough to reproduce what the parser does — the same
/// reasoning <c>CorpusRoundTripTests</c> rests on. Coverage is not wired to any output
/// yet (<c>UnmodelledClauses</c> still comes from the old accounting); this is read-only
/// plumbing for review before the stage 4/5 switch.
/// </summary>
static int RunCensus(string contentDirectory, string outputPath)
{
    var monsters = ContentLoader.Load(contentDirectory).Monsters;

    var lines = new List<string>();
    var frequency = new Dictionary<string, int>(StringComparer.Ordinal);
    var entriesTotal = 0;
    var entriesWithResidue = 0;
    var zeroResidueGainingResidue = 0;

    // The population where residue can newly appear: entries the old accounting
    // already credited whole (design §10 stage 3's own bar, 558 on the corpus at
    // debb5d7 — 342 Attack, 108 Multiattack, 96 SavingThrow, 12 Reaction). Passive and
    // Narrative are excluded on purpose: both claim their whole entry by fiat (§2.6)
    // and can never gain residue under coverage either.
    var structuredZeroResidueMechanics = new HashSet<EntryMechanics>
    {
        EntryMechanics.Attack,
        EntryMechanics.Multiattack,
        EntryMechanics.SavingThrow,
        EntryMechanics.Reaction,
    };

    foreach (var monster in monsters)
    {
        foreach (var entry in monster.Entries)
        {
            entriesTotal++;

            EntryMechanicsParser.Classify(entry.Name, entry.Section, entry.Text, out var coverage);

            // Residue() is the honest count — glue-absorbed, chunked — matching what
            // stage 4 would actually put in UnmodelledClauses. Uncovered() is the raw,
            // unfiltered detail underneath it (glue runs included), useful for seeing
            // exactly what a matcher did or did not claim, but not for counting: a
            // one-space gap between two adjacent claims is not a lost rule.
            var residue = coverage.Residue();

            if (residue.Count == 0)
            {
                continue;
            }

            entriesWithResidue++;

            var hadNoResidueBefore = entry.UnmodelledClauses.Count == 0
                && structuredZeroResidueMechanics.Contains(entry.Mechanics);

            if (hadNoResidueBefore)
            {
                zeroResidueGainingResidue++;
            }

            lines.Add($"{monster.Name} :: {entry.Name} ({entry.Section}, {entry.Mechanics})");

            foreach (var clause in residue)
            {
                var normalised = string.Join(
                    ' ',
                    clause.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
                frequency[normalised] = frequency.GetValueOrDefault(normalised) + 1;

                lines.Add($"  residue: {clause}");
            }

            foreach (var (span, text, before, after) in coverage.Uncovered())
            {
                lines.Add($"    raw [{span.Start},{span.End}) before={before ?? "(edge)"} after={after ?? "(edge)"}: {text}");
            }
        }
    }

    lines.Add(string.Empty);
    lines.Add($"Entries: {entriesTotal} total, {entriesWithResidue} carry at least one uncovered run.");
    lines.Add(
        $"Currently-zero-residue structured entries (Attack/Multiattack/SavingThrow/Reaction) " +
        $"gaining residue under coverage: {zeroResidueGainingResidue}");
    lines.Add(string.Empty);
    lines.Add("Frequency table (normalised, sorted by count then text):");

    foreach (var group in frequency
                 .OrderByDescending(pair => pair.Value)
                 .ThenBy(pair => pair.Key, StringComparer.Ordinal))
    {
        lines.Add($"  {group.Value,5}  {group.Key}");
    }

    File.WriteAllLines(outputPath, lines);

    Console.WriteLine(
        $"Census: {entriesTotal} entries, {entriesWithResidue} carry uncovered text, " +
        $"{zeroResidueGainingResidue} previously zero-residue structured entries would gain residue.");
    Console.WriteLine($"Wrote {outputPath}");

    return 0;
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

        /// <summary>The Classes chapter, Barbarian through Wizard.</summary>
        public const int ClassesFirstPage = 28;

        public const int ClassesLastPage = 82;

        /// <summary>Spell Descriptions, before the Rules Glossary begins on 176.</summary>
        public const int SpellsFirstPage = 107;

        public const int SpellsLastPage = 175;

        /// <summary>
        /// Magic Items A–Z. Deliberately starts after the category and rarity rules
        /// (204–208), whose section headers would otherwise need excluding one by one.
        /// </summary>
        public const int MagicItemsFirstPage = 209;

        /// <summary>The last item page, before the Monsters chapter begins on 254.</summary>
        public const int MagicItemsLastPage = 253;
    }

    internal sealed record ExtractOptions(string PdfPath, string OutputDirectory, bool Force, string? CensusPath)
    {
        public const string Usage = """
            Usage: SrdExtract [--pdf <path>] [--out <directory>] [--force]
                   SrdExtract --census <path> [--out <directory>]

              --pdf     Path to SRD_CC_v5.2.1.pdf. Defaults to ~/Downloads/SRD_CC_v5.2.1.pdf.
              --out     Directory to write content into, or read it from for --census.
                        Defaults to ./data/srd.
              --force   Write the content even when validation reports errors.
              --census  Skip extraction. Re-classify every stored monster entry from
                        --out (no PDF needed) and write every span nothing claimed to
                        <path> (#382's span-coverage census, stage 3 — read-only,
                        changes nothing under --out).
            """;

        public static ExtractOptions? Parse(string[] args)
        {
            var pdf = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "SRD_CC_v5.2.1.pdf");

            var output = Path.Combine("data", "srd");
            var force = false;
            string? census = null;

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
                    case "--census" when index + 1 < args.Length:
                        census = args[++index];
                        break;
                    case "-h" or "--help":
                        return null;
                    default:
                        Console.Error.WriteLine($"Unrecognised argument '{args[index]}'.");
                        return null;
                }
            }

            return new ExtractOptions(pdf, output, force, census);
        }
    }
}
