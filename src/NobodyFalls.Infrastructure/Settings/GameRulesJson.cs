using System.Text.Json;
using System.Text.Json.Serialization;
using NobodyFalls.Domain.Rules;

namespace NobodyFalls.Infrastructure.Settings;

/// <summary>How <see cref="GameRules"/> is stored in <c>game_settings.document</c>.</summary>
internal static class GameRulesJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public static string Serialize(GameRules rules) => JsonSerializer.Serialize(rules, Options);

    /// <summary>Missing fields keep their defaults, so adding a rule never breaks stored versions.</summary>
    public static GameRules Deserialize(string json) =>
        JsonSerializer.Deserialize<GameRules>(json, Options)
        ?? throw new InvalidOperationException("Stored game rules are empty.");
}
