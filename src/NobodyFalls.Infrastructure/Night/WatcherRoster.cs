using System.Globalization;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Night;

/// <summary>
/// Watchers are stored as players of kind <c>watcher</c> so that their hands are
/// ordinary hands. They are created on demand and reused, in a stable order, so that
/// the same Watchers appear night after night. Their pseudos are reserved (<c>veilleur.NN</c>);
/// the interface always labels them as Watchers.
/// </summary>
internal static class WatcherRoster
{
    public const string PseudoPrefix = "veilleur.";

    public static async Task<IReadOnlyList<Guid>> EnsureAsync(NpgsqlTransaction tx, int count, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await tx.ScalarAsync<long>(
            "select count(*) from players where kind = 'watcher'", cancellationToken);

        for (var n = (int)existing + 1; n <= count; n++)
        {
            var pseudo = PseudoPrefix + n.ToString("D2", CultureInfo.InvariantCulture);
            await tx.ExecuteAsync(
                """
                insert into players (id, kind, pseudo, pseudo_key, status, locale, invite_code, guardian_score,
                                     falls_count, created_at, map_name_visibility)
                values (@id, 'watcher', @pseudo, @pseudo, 'active', 'fr', @code, 0, 0, @now, 'everyone')
                on conflict do nothing
                """,
                cancellationToken,
                ("id", Guid.CreateVersion7(now)),
                ("pseudo", pseudo),
                ("code", "watcher-" + n.ToString(CultureInfo.InvariantCulture)),
                ("now", Sql.Utc(now)));
        }

        var ids = new List<Guid>(count);
        await using var reader = await tx.ReaderAsync(
            "select id from players where kind = 'watcher' order by pseudo_key limit @count",
            cancellationToken,
            ("count", count));
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }
}
