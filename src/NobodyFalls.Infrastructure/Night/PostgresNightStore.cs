using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Night;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Night;

/// <summary>
/// The night cycle on PostgreSQL. Each operation runs in one transaction under the
/// night advisory lock, so it is all-or-nothing and safe to repeat (ADR 0003).
/// </summary>
internal sealed class PostgresNightStore(NpgsqlDataSource dataSource, IGameSettingsProvider settings) : INightStore
{
    public async Task<IReadOnlyList<GameDay>> GetDaysToResolveAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "select day from game_days where resolved_at is null and day < @today order by day", connection);
        command.Parameters.AddWithValue("today", GameDay.Of(now).Date);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var days = new List<GameDay>();
        while (await reader.ReadAsync(cancellationToken))
        {
            days.Add(new GameDay(reader.GetFieldValue<DateOnly>(0)));
        }

        return days;
    }

    public async Task<NightRunResult> ResolveAsync(GameDay day, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (now < day.MidnightAt)
        {
            return new NightRunResult(day, NightRunStatus.TooEarly);
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await tx.LockNightExclusiveAsync(cancellationToken);

        var state = await LoadDayAsync(tx, day, cancellationToken);
        if (state is null)
        {
            return new NightRunResult(day, NightRunStatus.NotStarted);
        }

        if (state.Resolved)
        {
            return new NightRunResult(day, NightRunStatus.AlreadyResolved);
        }

        var rules = (await settings.GetVersionAsync(state.SettingsVersion, cancellationToken)).Rules;
        var p = new (string, object?)[]
        {
            ("day", day.Date),
            ("season", state.SeasonId),
            ("now", Sql.Utc(now)),
            ("storm", state.Storm),
            ("last_rampart_from", Sql.Utc(day.MidnightAt - rules.LastRampartWindow)),
            ("points_friend", rules.PointsFriend),
            ("points_stranger", rules.PointsStranger),
            ("last_rampart_bonus", rules.LastRampartBonus),
        };

        foreach (var step in new[]
                 {
                     NightSql.RecountReceived, NightSql.Outcomes, NightSql.Falls, NightSql.ScoreHands,
                     NightSql.Awards, NightSql.LifeDebts, NightSql.HoldPairs, NightSql.Stats, NightSql.SkinUnlocks,
                 })
        {
            await tx.ExecuteAsync(step, cancellationToken, p);
        }

        var falls = await tx.ScalarAsync<long>(
            "select count(*) from player_days where day = @day and outcome = 'fell'", cancellationToken, ("day", day.Date));

        var (previousStreak, previousDeaths) = await PreviousCountersAsync(tx, day, cancellationToken);
        var streak = falls == 0 ? previousStreak + 1 : 0;
        var deaths = previousDeaths + falls;

        await tx.ExecuteAsync(
            NightSql.CloseDay,
            cancellationToken,
            ("day", day.Date),
            ("season", state.SeasonId),
            ("now", Sql.Utc(now)),
            ("falls", (int)falls),
            ("streak", streak),
            ("deaths", deaths));
        await tx.ExecuteAsync(NightSql.Notifications, cancellationToken, p);

        var nextStarted = await StartDayAsync(tx, day.Next(), now, cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return new NightRunResult(day, NightRunStatus.Resolved, (int)falls, streak, deaths, nextStarted);
    }

    public async Task<bool> EnsureDayStartedAsync(GameDay day, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        // Cheap check first: on almost every tick the day is already there.
        await using (var check = new NpgsqlCommand("select exists (select 1 from game_days where day = @day)", connection))
        {
            check.Parameters.AddWithValue("day", day.Date);
            if ((bool)(await check.ExecuteScalarAsync(cancellationToken))!)
            {
                return false;
            }
        }

        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await tx.LockNightExclusiveAsync(cancellationToken);

        // The previous day is still waiting for its midnight: its resolution will start this one.
        var previousPending = await tx.ScalarAsync<bool>(
            "select exists (select 1 from game_days where day = @day and resolved_at is null)",
            cancellationToken,
            ("day", day.Previous().Date));
        if (previousPending)
        {
            return false;
        }

        var started = await StartDayAsync(tx, day, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return started;
    }

    public async Task<WatcherPassResult> RunWatcherPassAsync(GameDay day, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await tx.LockNightSharedAsync(cancellationToken);

        // The row lock serializes passes started by several instances.
        int watcherCount;
        int settingsVersion;
        await using (var reader = await tx.ReaderAsync(
                         """
                         select watcher_count, settings_version from game_days
                         where day = @day and resolved_at is null and watcher_pass_at is null
                         for update
                         """,
                         cancellationToken,
                         ("day", day.Date)))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new WatcherPassResult(day, false, 0);
            }

            watcherCount = reader.GetInt32(0);
            settingsVersion = reader.GetInt32(1);
        }

        var rules = (await settings.GetVersionAsync(settingsVersion, cancellationToken)).Rules;
        if (now < day.StartsAt + rules.Watchers.PassAt.ToTimeSpan())
        {
            return new WatcherPassResult(day, false, 0);
        }

        var given = watcherCount > 0
            ? await GiveWatcherHandsAsync(tx, day, now, watcherCount, rules, cancellationToken)
            : 0;

        await tx.ExecuteAsync(
            "update game_days set watcher_pass_at = @now where day = @day",
            cancellationToken,
            ("day", day.Date),
            ("now", Sql.Utc(now)));
        await tx.CommitAsync(cancellationToken);
        return new WatcherPassResult(day, true, given);
    }

    private static async Task<int> GiveWatcherHandsAsync(
        NpgsqlTransaction tx, GameDay day, DateTimeOffset now, int watcherCount, GameRules rules, CancellationToken cancellationToken)
    {
        var watchers = await WatcherRoster.EnsureAsync(tx, watcherCount, now, cancellationToken);

        var used = new Dictionary<Guid, int>();
        await using (var reader = await tx.ReaderAsync(
                         "select giver_id, count(*)::int from hands where day = @day and giver_kind = 'watcher' group by giver_id",
                         cancellationToken,
                         ("day", day.Date)))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                used[reader.GetGuid(0)] = reader.GetInt32(1);
            }
        }

        var candidates = new List<WatcherCandidate>();
        await using (var reader = await tx.ReaderAsync(
                         """
                         select pd.player_id, pd.received, pd.immune, p.joined_day
                         from player_days pd join players p on p.id = pd.player_id
                         where pd.day = @day and pd.received = 0 and not pd.immune
                         """,
                         cancellationToken,
                         ("day", day.Date)))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var joined = reader.IsDBNull(3) ? day : new GameDay(reader.GetFieldValue<DateOnly>(3));
                candidates.Add(new WatcherCandidate(
                    new PlayerId(reader.GetGuid(0)),
                    reader.GetInt32(1),
                    reader.GetBoolean(2),
                    ThresholdPolicy.StandingOn(day, joined)));
            }
        }

        var slots = watchers
            .Select(w => new WatcherSlot(new PlayerId(w), rules.Watchers.HandsPerWatcher - used.GetValueOrDefault(w)))
            .ToList();
        var plan = WatcherPolicy.Plan(day, candidates, slots);
        if (plan.Count == 0)
        {
            return 0;
        }

        var nextSlot = watchers.ToDictionary(w => w, w => used.GetValueOrDefault(w));
        var givers = new Guid[plan.Count];
        var receivers = new Guid[plan.Count];
        var slotNumbers = new short[plan.Count];
        for (var i = 0; i < plan.Count; i++)
        {
            givers[i] = plan[i].Watcher.Value;
            receivers[i] = plan[i].Receiver.Value;
            slotNumbers[i] = (short)++nextSlot[givers[i]];
        }

        await tx.ExecuteAsync(
            """
            insert into hands (day, giver_id, receiver_id, slot, given_at, giver_kind, giver_was_friend)
            select @day, g, r, s, @now, 'watcher', false
            from unnest(@givers, @receivers, @slots) as t(g, r, s);

            update player_days set received = received + 1
            where day = @day and player_id = any(@receivers);
            """,
            cancellationToken,
            ("day", day.Date),
            ("now", Sql.Utc(now)),
            ("givers", givers),
            ("receivers", receivers),
            ("slots", slotNumbers));

        return plan.Count;
    }

    /// <summary>
    /// Starts a day: players enter, the threshold is computed from the living population
    /// with the current rules, and those rules are frozen for the day (ADR 0005).
    /// </summary>
    private async Task<bool> StartDayAsync(NpgsqlTransaction tx, GameDay day, DateTimeOffset now, CancellationToken cancellationToken)
    {
        int seasonId;
        DateOnly openingDay;
        await using (var reader = await tx.ReaderAsync(NightSql.OpenSeasonOn, cancellationToken, ("day", day.Date)))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return false;
            }

            seasonId = reader.GetInt32(0);
            openingDay = reader.GetFieldValue<DateOnly>(1);
        }

        await tx.ExecuteAsync(NightSql.EnterPlayers, cancellationToken, ("day", day.Date));
        var alive = await tx.ScalarAsync<int>(NightSql.CountAlive, cancellationToken);

        var current = await settings.GetCurrentAsync(cancellationToken);
        var rules = current.Rules;
        var baseRequired = ThresholdPolicy.BaseRequired(rules, alive);

        bool storm;
        await using (var reader = await tx.ReaderAsync(
                         NightSql.InsertGameDay,
                         cancellationToken,
                         ("day", day.Date),
                         ("season", seasonId),
                         ("night_number", day.DaysSince(new GameDay(openingDay)) + 1),
                         ("base_required", baseRequired),
                         ("alive", alive),
                         ("watchers", WatcherPolicy.Count(rules, alive)),
                         ("settings_version", current.Version),
                         ("now", Sql.Utc(now))))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return false; // already started
            }

            storm = reader.GetBoolean(0);
        }

        await tx.ExecuteAsync(
            NightSql.InsertPlayerDays,
            cancellationToken,
            ("day", day.Date),
            ("base_required", baseRequired),
            ("storm", storm),
            ("storm_extra", rules.StormExtra),
            ("fragile_extra", rules.FragileExtra),
            ("now", Sql.Utc(now)));
        return true;
    }

    private static async Task<DayState?> LoadDayAsync(NpgsqlTransaction tx, GameDay day, CancellationToken cancellationToken)
    {
        await using var reader = await tx.ReaderAsync(
            "select season_id, settings_version, storm, resolved_at is not null from game_days where day = @day",
            cancellationToken,
            ("day", day.Date));
        return await reader.ReadAsync(cancellationToken)
            ? new DayState(reader.GetInt32(0), reader.GetInt32(1), reader.GetBoolean(2), reader.GetBoolean(3))
            : null;
    }

    private static async Task<(int Streak, long Deaths)> PreviousCountersAsync(NpgsqlTransaction tx, GameDay day, CancellationToken cancellationToken)
    {
        await using var reader = await tx.ReaderAsync(NightSql.PreviousCounters, cancellationToken, ("day", day.Date));
        return await reader.ReadAsync(cancellationToken) ? (reader.GetInt32(0), reader.GetInt64(1)) : (0, 0);
    }

    private sealed record DayState(int SeasonId, int SettingsVersion, bool Storm, bool Resolved);
}
