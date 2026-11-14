using System.Text.Json;
using System.Text.Json.Serialization;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Api.Http;

/// <summary>JSON conventions of the API: camelCase, enums as camelCase strings, ids as strings, days as "yyyy-MM-dd".</summary>
internal static class Json
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new PlayerIdConverter());
        options.Converters.Add(new GameDayConverter());
    }

    private sealed class PlayerIdConverter : JsonConverter<PlayerId>
    {
        public override PlayerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetGuid());

        public override void Write(Utf8JsonWriter writer, PlayerId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }

    private sealed class GameDayConverter : JsonConverter<GameDay>
    {
        public override GameDay Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            GameDay.Parse(reader.GetString() ?? throw new JsonException("A game day is required."));

        public override void Write(Utf8JsonWriter writer, GameDay value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
