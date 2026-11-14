using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Friends;
using Npgsql;

namespace NobodyFalls.Testing.Postgres;

/// <summary>Writes test data straight into the tables, bypassing the rules on purpose.</summary>
public sealed class Seeder(NpgsqlDataSource data)
{
    public Task SeasonAsync(GameDay openingDay, GameDay? lastDay = null) => ExecAsync(
        "insert into seasons (id, number, name, opening_day, last_day, deaths) values (1, 1, 'S1', @opening, @last, 0)",
        ("opening", openingDay.Date),
        ("last", lastDay?.Date));

    public Task PlayerAsync(
        PlayerId id,
        string status = "active",
        string kind = "human",
        GameDay? joinedDay = null,
        GameDay? returnsOnDay = null) => ExecAsync(
        """
        insert into players (id, kind, pseudo, pseudo_key, status, locale, joined_day, invite_code,
                             guardian_score, falls_count, returns_on_day, created_at, map_name_visibility)
        values (@id, @kind, @pseudo, @pseudo, @status, 'fr', @joined, @pseudo, 0, 0, @returns, now(), 'friends')
        """,
        ("id", id.Value),
        ("kind", kind),
        ("pseudo", "p" + id.Value.ToString("N")[12..]),
        ("status", status),
        ("joined", (joinedDay ?? GameDay.Parse("2026-01-01")).Date),
        ("returns", returnsOnDay?.Date));

    public Task GameDayAsync(
        GameDay day,
        int baseRequired = 1,
        bool resolved = false,
        int streakAfter = 0,
        long deathsAfter = 0,
        int watcherCount = 0) => ExecAsync(
        """
        insert into game_days (day, season_id, night_number, base_required, storm, alive_at_start, watcher_count,
                               settings_version, started_at, resolved_at, streak_after, deaths_all_time_after)
        values (@day, 1, 1, @base, false, 0, @watchers, 1, now(),
                case when @resolved then now() end,
                case when @resolved then @streak end,
                case when @resolved then @deaths end)
        """,
        ("day", day.Date),
        ("base", baseRequired),
        ("resolved", resolved),
        ("streak", streakAfter),
        ("deaths", deathsAfter),
        ("watchers", watcherCount));

    public Task PlayerDayAsync(GameDay day, PlayerId player, int required, bool immune = false) => ExecAsync(
        "insert into player_days (day, player_id, required, immune, received, joined_at, score_gained) values (@day, @id, @required, @immune, 0, now(), 0)",
        ("day", day.Date),
        ("id", player.Value),
        ("required", required),
        ("immune", immune));

    public Task HandAsync(long id, GameDay day, PlayerId giver, PlayerId receiver, short slot, DateTimeOffset givenAt, string giverKind, bool friend) => ExecAsync(
        """
        insert into hands (id, day, giver_id, receiver_id, slot, given_at, giver_kind, giver_was_friend)
        overriding system value
        values (@id, @day, @giver, @receiver, @slot, @at, @kind, @friend);

        update player_days set received = received + 1 where day = @day and player_id = @receiver;

        -- Explicit ids do not advance the identity sequence; keep it ahead of them.
        select setval(pg_get_serial_sequence('hands', 'id'), (select max(id) from hands));
        """,
        ("id", id),
        ("day", day.Date),
        ("giver", giver.Value),
        ("receiver", receiver.Value),
        ("slot", slot),
        ("at", givenAt.ToUniversalTime()),
        ("kind", giverKind),
        ("friend", friend));

    public Task FriendsAsync(PlayerId a, PlayerId b)
    {
        var pair = PlayerPair.Of(a, b);
        return ExecAsync(
            "insert into friendships (player_low, player_high, source, created_at) values (@low, @high, 'request', now())",
            ("low", pair.Low.Value),
            ("high", pair.High.Value));
    }

    public Task BlockAsync(PlayerId blocker, PlayerId blocked) => ExecAsync(
        "insert into blocks (blocker_id, blocked_id, created_at) values (@a, @b, now())",
        ("a", blocker.Value),
        ("b", blocked.Value));

    public Task StormAsync(GameDay day) => ExecAsync(
        "insert into storms (day, created_at) values (@day, now())", ("day", day.Date));

    public async Task<T?> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    public async Task ExecAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> map, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    private NpgsqlCommand Command(string sql, (string Name, object? Value)[] parameters)
    {
        var command = data.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}
