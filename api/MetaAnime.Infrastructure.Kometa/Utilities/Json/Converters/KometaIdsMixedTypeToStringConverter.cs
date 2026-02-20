#nullable disable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaAnime.Infrastructure.Kometa.Infrastructure.Kometa.Utilities.Json.Converters;

public class KometaIdsMixedTypeToStringConverter : JsonConverter<string[]>
{
    public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string rawResult = reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt64().ToString(),
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token type {reader.TokenType}"),
        };
        return rawResult is not null ? [.. rawResult.Split(",").Select(s => s.Trim())] : [];
    }

    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }
        writer.WriteEndArray();
    }
}
