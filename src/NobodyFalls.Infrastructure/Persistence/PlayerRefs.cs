using System.Text.Json;
using System.Text.Json.Serialization;
using NobodyFalls.Application.Queries;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Skins;
using Npgsql;

namespace NobodyFalls.Infrastructure.Persistence;

/// <summary>How a character is stored in <c>appearances.document</c>.</summary>
internal static class AppearanceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(Appearance appearance) => JsonSerializer.Serialize(appearance, Options);

    public static Appearance? Deserialize(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<Appearance>(json, Options);
}

/// <summary>
/// The four columns every list needs to show a player. Select them with
/// <see cref="Columns"/> and read them with <see cref="Read"/>.
/// </summary>
internal static class PlayerRefs
{
    /// <summary>Expects <c>players p</c> left-joined to <c>appearances a</c>.</summary>
    public const string Columns = "p.id, p.pseudo, p.kind, a.document::text";

    public const string Join = "left join appearances a on a.player_id = p.id";

    public static PlayerRef Read(NpgsqlDataReader r, int first) => new(
        new PlayerId(r.GetGuid(first)),
        r.GetString(first + 1),
        r.GetString(first + 2) == DbValues.Watcher,
        r.IsDBNull(first + 3) ? null : AppearanceJson.Deserialize(r.GetString(first + 3)));

    public static LifeStatus Status(string status) => status switch
    {
        DbValues.Active => LifeStatus.Alive,
        DbValues.Fallen => LifeStatus.Fallen,
        _ => LifeStatus.Waitlisted,
    };

    /// <summary>"Either player blocked the other" for two uuid expressions.</summary>
    public static string BlockedBetween(string a, string b) =>
        $"exists (select 1 from blocks bl where (bl.blocker_id = {a} and bl.blocked_id = {b}) or (bl.blocker_id = {b} and bl.blocked_id = {a}))";
}
