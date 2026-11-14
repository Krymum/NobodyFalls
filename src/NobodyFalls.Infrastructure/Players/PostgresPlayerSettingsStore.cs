using NobodyFalls.Application.Players;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Skins;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Players;

internal sealed class PostgresPlayerSettingsStore(NpgsqlDataSource dataSource, TimeProvider time) : IPlayerSettingsStore
{
    public async Task UpdateAsync(PlayerId player, string? locale, string? mapNameVisibility, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            update players
            set locale = coalesce(@locale, locale), map_name_visibility = coalesce(@visibility, map_name_visibility)
            where id = @id
            """);
        command.Parameters.AddWithValue("id", player.Value);
        command.Parameters.AddWithValue("locale", (object?)locale ?? DBNull.Value).NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text;
        command.Parameters.AddWithValue("visibility", (object?)mapNameVisibility ?? DBNull.Value).NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<string>> OwnedItemsAsync(PlayerId player, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select item_id from player_skin_unlocks where player_id = @id");
        command.Parameters.AddWithValue("id", player.Value);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var owned = new HashSet<string>(StringComparer.Ordinal);
        while (await r.ReadAsync(cancellationToken))
        {
            owned.Add(r.GetString(0));
        }

        return owned;
    }

    public async Task SaveAppearanceAsync(PlayerId player, Appearance appearance, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            insert into appearances (player_id, document, updated_at) values (@id, @document::jsonb, @now)
            on conflict (player_id) do update set document = excluded.document, updated_at = excluded.updated_at
            """);
        command.Parameters.AddWithValue("id", player.Value);
        command.Parameters.AddWithValue("document", AppearanceJson.Serialize(appearance));
        command.Parameters.AddWithValue("now", Sql.Utc(time.GetUtcNow()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
