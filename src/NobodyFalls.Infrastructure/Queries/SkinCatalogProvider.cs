using NobodyFalls.Application.Queries;
using NobodyFalls.Domain.Skins;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Queries;

/// <summary>The catalogue, cached for a minute: it only changes through the admin.</summary>
internal sealed class SkinCatalogProvider(NpgsqlDataSource dataSource, TimeProvider time) : ISkinCatalogProvider
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);
    private (SkinCatalog Catalog, long ReadAt)? _cached;

    public async Task<SkinCatalog> GetAsync(CancellationToken cancellationToken)
    {
        if (_cached is { } cached && time.GetElapsedTime(cached.ReadAt) < Ttl)
        {
            return cached.Catalog;
        }

        var items = new List<SkinItem>();
        await using (var command = dataSource.CreateCommand(
                         "select id, slot, rarity, render_key, colors, unlock_metric, unlock_threshold from skin_items where active order by sort"))
        await using (var r = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await r.ReadAsync(cancellationToken))
            {
                items.Add(SkinMapping.ToDomain(new SkinItemRow
                {
                    Id = r.GetString(0),
                    Slot = r.GetString(1),
                    Rarity = r.GetString(2),
                    RenderKey = r.GetString(3),
                    Colors = [.. r.GetFieldValue<string[]>(4)],
                    UnlockMetric = r.IsDBNull(5) ? null : r.GetString(5),
                    UnlockThreshold = r.IsDBNull(6) ? null : r.GetInt32(6),
                }));
            }
        }

        var tones = new List<string>();
        await using (var command = dataSource.CreateCommand("select color from skin_tones order by sort"))
        await using (var r = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await r.ReadAsync(cancellationToken))
            {
                tones.Add(r.GetString(0));
            }
        }

        var catalog = new SkinCatalog(items, tones);
        _cached = (catalog, time.GetTimestamp());
        return catalog;
    }
}
