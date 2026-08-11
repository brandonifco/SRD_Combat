using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SRDCombat.Content.Json;

namespace SRDCombat.Content;

/// <summary>
/// The one place content JSON options are configured. Both the loader and the
/// extraction tool go through this, so a file written by the tool is by construction
/// a file the game can read.
/// </summary>
/// <remarks>
/// <para>
/// Content is serialized directly from the <c>SRDCombat.Core</c> definitions rather
/// than through a hand-mirrored DTO layer. That is a deliberate divergence from the
/// neighbouring 5eGoldBox project, and the reasoning matters:
/// </para>
/// <para>
/// A versioned DTO mirror exists to keep an on-disk format stable while runtime types
/// churn — worth its cost when content is hand-authored and must survive refactors.
/// SRD content here is <em>generated</em>: if a definition changes, the extractor is
/// re-run and every file is rewritten. The mirror would protect nothing, while adding
/// the exact failure GoldBox hit twice — an unmapped property is dropped silently
/// rather than rejected, so a field added to the runtime type simply never arrives.
/// </para>
/// <para>
/// What replaces it is louder, not weaker: <c>ContentShapeTests</c> pins the serialized
/// shape of a representative value of every content type against a committed snapshot,
/// so any change to the on-disk format fails a test naming the field that moved.
/// Hand-authored game content — encounter ladders, pregenerated characters, loot
/// tables — is a separate question, and may well earn a DTO layer when it arrives.
/// </para>
/// </remarks>
public static class ContentSerializer
{
    /// <summary>
    /// Bumped when the on-disk format changes incompatibly. Every content file carries
    /// it, and the loader refuses a file it does not recognise rather than guessing.
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Options for reading content. Tolerant of nothing — unknown fields are an error.</summary>
    public static JsonSerializerOptions ReadOptions { get; } = Create(writeIndented: false);

    /// <summary>Options for writing content. Indented, because these files are committed and diffed.</summary>
    public static JsonSerializerOptions WriteOptions { get; } = Create(writeIndented: true);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, ReadOptions)
        ?? throw new JsonException($"Content deserialized to null as {typeof(T).Name}.");

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, WriteOptions);

    private static JsonSerializerOptions Create(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

            // Dictionary keys are deliberately left alone while property names are
            // camelCased. A property name is schema; a dictionary key is data — an
            // ability, a movement mode, a skill name — and camelCasing it would both
            // disagree with how enum values serialize ("Slashing", "Melee") and mangle
            // multi-word skills into "animal Handling".

            // Derived values must not be persisted: MonsterAbility.Modifier is computed
            // from Score, and writing it would let a hand-edited file disagree with
            // itself. Every field that is really data has an init accessor, and every
            // computed one is get-only, so this draws the line exactly where it belongs
            // — and does it without Core taking a dependency on serialization.
            IgnoreReadOnlyProperties = true,

            // Optional values are omitted rather than written as null. Roughly half the
            // fields on a stat block are absent for any given monster.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

            // A typo in a content file is a bug, not something to skip past silently.
            // This is the direct replacement for what a DTO mirror would have caught,
            // and it is stricter: the mirror dropped unknown properties by default.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,

            // The SRD is full of apostrophes and en dashes. Escaping them would make
            // every committed file unreadable for no security benefit — these are
            // files on disk, never injected into a web page.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new DiceExpressionJsonConverter());

        return options;
    }
}
