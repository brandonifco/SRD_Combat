using System.Text.Json;
using System.Text.Json.Serialization;
using SRDCombat.Core.Dice;

namespace SRDCombat.Content.Json;

/// <summary>
/// Writes a <see cref="DiceExpression"/> as its printed form — <c>"2d6 + 3"</c> —
/// rather than as an object of three numbers. Content files are read by people as
/// often as by the loader, and <c>{"count":2,"sides":6,"modifier":3}</c> is
/// meaningfully harder to check against the SRD than the expression itself.
/// </summary>
public sealed class DiceExpressionJsonConverter : JsonConverter<DiceExpression>
{
    public override DiceExpression Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a dice expression string, found {reader.TokenType}.");
        }

        var text = reader.GetString();

        return DiceExpression.TryParse(text, out var expression)
            ? expression
            : throw new JsonException($"'{text}' is not a valid dice expression.");
    }

    public override void Write(Utf8JsonWriter writer, DiceExpression value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStringValue(value.ToString());
    }
}
