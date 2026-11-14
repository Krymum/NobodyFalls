using System.Globalization;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Dev;
using NobodyFalls.Application.Queries;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Friends;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Domain.Skins;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Dev;

/// <summary>Fake players for development. Bots log in with the <c>bot</c> provider, so they are easy to find and remove.</summary>
internal sealed class PostgresDevStore(NpgsqlDataSource dataSource, IGameSettingsProvider settings, ISkinCatalogProvider catalogs) : IDevStore
{
    public const string BotProvider = "bot";

    private static readonly string[] Names =
    [
        "lea_m", "kenji", "tom.b", "ines", "yuki", "nora", "sami", "elio", "maud", "rafa", "ayla", "joss", "milo", "zoe.k",
        "ilan", "vera", "noam", "lou", "aiko", "theo", "sacha", "nina", "omar", "lena", "hugo", "iris", "malo", "suki",
    ];

    public async Task<bool> EnsureSeasonAsync(GameDay openingDay, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            insert into seasons (number, name, opening_day, deaths)
            select 1, 'Saison 1', @day, 0 where not exists (select 1 from seasons)
            """);
        command.Parameters.AddWithValue("day", openingDay.Date);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<PlayerId>> SeedAsync(int players, int friendsPerPlayer, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var random = new Random();
        var catalog = await catalogs.GetAsync(cancellationToken);
        var today = GameDay.Of(now);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await tx.LockNightSharedAsync(cancellationToken);

        var seasonOpen = await tx.ScalarAsync<bool>(
            "select exists (select 1 from seasons where opening_day <= @day and (last_day is null or last_day >= @day))",
            cancellationToken,
            ("day", today.Date));
        var start = await tx.ScalarAsync<long>("select count(*) from players", cancellationToken);

        var ids = new List<PlayerId>(players);
        for (var i = 0; i < players; i++)
        {
            var id = new PlayerId(Guid.CreateVersion7(now));
            var pseudo = Pseudo.Create($"{Names[random.Next(Names.Length)]}{(start + i).ToString(CultureInfo.InvariantCulture)}").Value;
            await tx.ExecuteAsync(
                """
                insert into players (id, kind, pseudo, pseudo_key, status, locale, joined_day, invite_code, created_at)
                values (@id, 'human', @pseudo, @key, @status, @locale, @joined, @code, @now);

                insert into appearances (player_id, document, updated_at) values (@id, @appearance::jsonb, @now);

                insert into external_logins (provider, provider_key, player_id, created_at) values ('bot', @id::text, @id, @now);
                """,
                cancellationToken,
                ("id", id.Value),
                ("pseudo", pseudo.Value),
                ("key", pseudo.Key),
                ("status", seasonOpen ? DbValues.Active : DbValues.Waitlisted),
                ("locale", random.Next(2) == 0 ? "fr" : "en"),
                ("joined", seasonOpen ? today.AddDays(-random.Next(2, 40)).Date : null),
                ("code", "BOT" + id.Value.ToString("N")[^7..].ToUpperInvariant()),
                ("now", Sql.Utc(now)),
                ("appearance", AppearanceJson.Serialize(RandomAppearance(catalog, random))));
            ids.Add(id);
        }

        // New bots enter today's game if it is running (regular players, normal threshold).
        (int Base, bool Storm, int Version)? todayState = null;
        await using (var r = await tx.ReaderAsync(
                         "select base_required, storm, settings_version from game_days where day = @day and resolved_at is null",
                         cancellationToken,
                         ("day", today.Date)))
        {
            if (await r.ReadAsync(cancellationToken))
            {
                todayState = (r.GetInt32(0), r.GetBoolean(1), r.GetInt32(2));
            }
        }

        if (seasonOpen && todayState is { } state)
        {
            var rules = (await settings.GetVersionAsync(state.Version, cancellationToken)).Rules;
            await tx.ExecuteAsync(
                """
                insert into player_days (day, player_id, required, immune, received, joined_at, score_gained)
                select @day, id, @required, false, 0, @now, 0 from unnest(@ids) as id
                on conflict do nothing
                """,
                cancellationToken,
                ("day", today.Date),
                ("required", ThresholdPolicy.Required(rules, state.Base, state.Storm, PlayerStanding.Regular)),
                ("now", Sql.Utc(now)),
                ("ids", ids.Select(i => i.Value).ToArray()));
        }

        var everyone = new List<Guid>();
        await using (var r = await tx.ReaderAsync("select id from players where kind = 'human' and deleted_at is null", cancellationToken))
        {
            while (await r.ReadAsync(cancellationToken))
            {
                everyone.Add(r.GetGuid(0));
            }
        }

        foreach (var id in ids)
        {
            foreach (var other in everyone.Where(o => o != id.Value).OrderBy(_ => random.Next()).Take(friendsPerPlayer))
            {
                var pair = PlayerPair.Of(id, new PlayerId(other));
                await tx.ExecuteAsync(
                    "insert into friendships (player_low, player_high, source, created_at) values (@low, @high, 'request', @now) on conflict do nothing",
                    cancellationToken,
                    ("low", pair.Low.Value),
                    ("high", pair.High.Value),
                    ("now", Sql.Utc(now)));
            }
        }

        await tx.CommitAsync(cancellationToken);
        return ids;
    }

    public async Task<IReadOnlyList<PlayerId>> BotsAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select player_id from external_logins where provider = 'bot' order by player_id");
        return await ReadIdsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerId>> InPlayByNeedAsync(GameDay day, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "select player_id from player_days where day = @day order by required - received desc, random()");
        command.Parameters.AddWithValue("day", day.Date);
        return await ReadIdsAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<PlayerId>> ReadIdsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var ids = new List<PlayerId>();
        while (await r.ReadAsync(cancellationToken))
        {
            ids.Add(new PlayerId(r.GetGuid(0)));
        }

        return ids;
    }

    private static Appearance RandomAppearance(SkinCatalog catalog, Random random)
    {
        SlotChoice Pick(Slot slot)
        {
            var options = catalog.BaseItems.Where(i => i.Slot == slot).ToList();
            var item = options[random.Next(options.Count)];
            return new SlotChoice(item.Id, item.Colors[random.Next(item.Colors.Count)]);
        }

        var slots = new Dictionary<Slot, SlotChoice>
        {
            [Slot.Coat] = Pick(Slot.Coat),
            [Slot.Trousers] = Pick(Slot.Trousers),
            [Slot.Boots] = Pick(Slot.Boots),
        };
        foreach (var optional in new[] { Slot.Hair, Slot.Headwear, Slot.Scarf, Slot.Gloves })
        {
            if (random.NextDouble() < 0.55 && catalog.BaseItems.Any(i => i.Slot == optional))
            {
                slots[optional] = Pick(optional);
            }
        }

        return new Appearance(catalog.SkinTones[random.Next(catalog.SkinTones.Count)], slots);
    }
}
