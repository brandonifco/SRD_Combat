namespace SrdExtract.Parsing;

/// <summary>
/// The SRD's typography, used as a parsing signal.
/// </summary>
/// <remarks>
/// Every kind of line in a stat block has its own font, which makes classification a
/// lookup rather than a pile of text heuristics. These names are matched exactly:
/// <c>GillSans</c>, <c>GillSans-SemiBold</c> and <c>GillSans-SemiBold-SC700</c> are
/// three different signals, and a substring test would confuse them.
/// </remarks>
internal static class StatBlockFonts
{
    /// <summary>Section headers — Traits, Actions, Bonus Actions, Reactions.</summary>
    public const string SectionHeader = "GillSans";

    /// <summary>Monster names, and the larger A–Z group headings above them.</summary>
    public const string Name = "GillSans-SemiBold";

    /// <summary>The small-caps ability score table.</summary>
    public const string AbilityTable = "GillSans-SemiBold-SC700";

    /// <summary>Body prose.</summary>
    public const string Body = "Optima-Regular";

    /// <summary>The size/type/alignment line, and the italic run inside attack entries.</summary>
    public const string Italic = "Optima-Italic";

    /// <summary>Stat lines — AC, HP, Speed, Senses, CR and the rest.</summary>
    public const string Stat = "Optima-Bold";

    /// <summary>The name that opens a trait or action entry.</summary>
    public const string EntryName = "Optima-BoldItalic";

    /// <summary>
    /// A monster name is set at roughly 10.2pt and the A–Z group heading above it at
    /// 12.3pt, in the same font. Height is the only thing telling them apart.
    /// </summary>
    public const double MaximumNameHeight = 11.5;

    /// <summary>
    /// Section headers are about 8.3pt. The <c>MOD SAVE</c> labels over the ability
    /// table are the same font at about 4.2pt, and are not headers.
    /// </summary>
    public const double MinimumSectionHeaderHeight = 6.0;
}
