using System.Text.Json;
using System.Text.Json.Serialization;

namespace CineLibraryEssentials.Services;

/// <summary>
/// JSON options for TMDb responses. TMDb sends null for numbers it doesn't have
/// yet, e.g. "runtime": null on an episode that hasn't aired. Plain
/// System.Text.Json throws on that, which used to lose a whole season's episode
/// list (or a whole movie). These options read such nulls as 0.
/// </summary>
public static class TmdbJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new NullAsZeroInt(), new NullAsZeroDouble() },
    };

    private sealed class NullAsZeroInt : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.Null ? 0 : reader.GetInt32();

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }

    private sealed class NullAsZeroDouble : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.Null ? 0 : reader.GetDouble();

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }
}
