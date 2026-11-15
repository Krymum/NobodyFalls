using System.Text.Json.Nodes;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Audit;
using NobodyFalls.Application.Config;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Config;
using NobodyFalls.Domain.Feedback;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Domain.Seasons;
using NobodyFalls.Infrastructure.Audit;
using NobodyFalls.Infrastructure.Moderation;
using NobodyFalls.Infrastructure.Persistence;
using NobodyFalls.Infrastructure.Settings;
using Npgsql;

namespace NobodyFalls.Infrastructure.Config;

/// <summary>Game rules as versions of <c>game_settings</c>, written with their audit line.</summary>
internal sealed class PostgresRulesConfig(NpgsqlDataSource dataSource, IGameSettingsProvider settings) : IRulesConfig
{
    public async Task<RulesState> GetAsync(GameDay today, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select (select max(version) from game_settings),
                   (select settings_version from game_days where day = @day)
            """,
            connection);
        command.Parameters.AddWithValue("day", today.Date);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);
        var latest = r.GetInt32(0);
        var todayVersion = r.IsDBNull(1) ? latest : r.GetInt32(1);
        await r.DisposeAsync();
        var current = await settings.GetVersionAsync(latest, cancellationToken);
        return new RulesState(latest, todayVersion, current.Rules);
    }

    public async Task<IReadOnlyList<RulesVersion>> VersionsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select s.version, s.created_at, p.pseudo, s.comment, s.document::text
            from game_settings s left join players p on p.id = s.created_by
            order by s.version desc
            limit @limit
            """,
            connection);
        command.Parameters.AddWithValue("limit", limit);
        return await ModerationSql.ListAsync(
            command,
            r => new RulesVersion(r.GetInt32(0), r.GetFieldValue<DateTimeOffset>(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), GameRulesJson.Deserialize(r.GetString(4))),
            cancellationToken);
    }

    public async Task<Result<int>> SaveAsync(GameRules? rules, string? comment, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (rules is null)
        {
            return RulesErrors.Missing;
        }

        var errors = rules.Validate();
        if (errors.Count > 0)
        {
            return errors[0];
        }

        var why = Text.Clean(comment);
        if (why.Length is < 3 or > 200)
        {
            return RulesErrors.Comment;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        // Serialize writers so that version numbers never collide.
        await tx.ExecuteAsync("lock table game_settings in share row exclusive mode", cancellationToken);
        var (latest, document) = await LatestAsync(tx, cancellationToken);
        var (before, after) = Diff(document, GameRulesJson.Serialize(rules));
        if (after.Count == 0)
        {
            return RulesErrors.Unchanged;
        }

        var version = latest + 1;
        await tx.ExecuteAsync(
            "insert into game_settings (version, document, created_at, created_by, comment) values (@version, @document::jsonb, @now, @author, @comment)",
            cancellationToken,
            ("version", version),
            ("document", GameRulesJson.Serialize(rules)),
            ("now", Sql.Utc(now)),
            ("author", actor.Id?.Value),
            ("comment", why));
        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(actor, AuditActions.RulesChanged, AuditTargets.Rules, version.ToString(System.Globalization.CultureInfo.InvariantCulture), before, after, why),
            now,
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Result.Success(version);
    }

    private static async Task<(int Version, string Document)> LatestAsync(NpgsqlTransaction tx, CancellationToken cancellationToken)
    {
        await using var r = await tx.ReaderAsync("select version, document::text from game_settings order by version desc limit 1", cancellationToken);
        return await r.ReadAsync(cancellationToken) ? (r.GetInt32(0), r.GetString(1)) : (0, "{}");
    }

    /// <summary>Only the fields that change, by path ("watchers.maxCount"), before and after.</summary>
    internal static (Dictionary<string, JsonNode?> Before, Dictionary<string, JsonNode?> After) Diff(string beforeJson, string afterJson)
    {
        var before = Flatten(GameRulesJson.Serialize(GameRulesJson.Deserialize(beforeJson)));
        var after = Flatten(afterJson);
        var changedBefore = new Dictionary<string, JsonNode?>();
        var changedAfter = new Dictionary<string, JsonNode?>();
        foreach (var (path, value) in after)
        {
            before.TryGetValue(path, out var old);
            if (!JsonNode.DeepEquals(old, value))
            {
                changedBefore[path] = old?.DeepClone();
                changedAfter[path] = value?.DeepClone();
            }
        }

        return (changedBefore, changedAfter);
    }

    private static Dictionary<string, JsonNode?> Flatten(string json)
    {
        var flat = new Dictionary<string, JsonNode?>();
        void Walk(JsonNode? node, string prefix)
        {
            if (node is JsonObject obj)
            {
                foreach (var (key, child) in obj)
                {
                    Walk(child, prefix.Length == 0 ? key : $"{prefix}.{key}");
                }
            }
            else
            {
                flat[prefix] = node;
            }
        }

        Walk(JsonNode.Parse(json), "");
        return flat;
    }
}

/// <summary>Storms, under the night lock so that a day never starts while its storm is being changed.</summary>
internal sealed class PostgresStormSchedule(NpgsqlDataSource dataSource) : IStormSchedule
{
    public async Task<IReadOnlyList<ScheduledStorm>> ListAsync(GameDay from, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "select s.day::text, s.created_at, p.pseudo from storms s left join players p on p.id = s.created_by where s.day >= @from order by s.day",
            connection);
        command.Parameters.AddWithValue("from", from.Date);
        return await ModerationSql.ListAsync(command, r => new ScheduledStorm(r.GetString(0), r.GetFieldValue<DateTimeOffset>(1), r.IsDBNull(2) ? null : r.GetString(2)), cancellationToken);
    }

    public Task<Result> ScheduleAsync(GameDay day, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken) =>
        ChangeAsync(day, actor, now, schedule: true, cancellationToken);

    public Task<Result> CancelAsync(GameDay day, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken) =>
        ChangeAsync(day, actor, now, schedule: false, cancellationToken);

    private async Task<Result> ChangeAsync(GameDay day, AuditActor actor, DateTimeOffset now, bool schedule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await tx.LockNightSharedAsync(cancellationToken);
        var allowed = StormPolicy.CanSchedule(day, now);
        // The day may have been started early (dev tools): it is frozen as soon as it exists.
        if (allowed.IsFailure || await tx.ScalarAsync<bool>("select exists (select 1 from game_days where day = @day)", cancellationToken, ("day", day.Date)))
        {
            return StormPolicy.TooLate;
        }

        var changed = schedule
            ? await tx.ExecuteAsync(
                "insert into storms (day, created_at, created_by) values (@day, @now, @actor) on conflict (day) do nothing",
                cancellationToken,
                ("day", day.Date),
                ("now", Sql.Utc(now)),
                ("actor", actor.Id?.Value))
            : await tx.ExecuteAsync("delete from storms where day = @day", cancellationToken, ("day", day.Date));
        if (changed == 0)
        {
            return schedule ? StormErrors.AlreadyScheduled : StormErrors.NotFound;
        }

        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(actor, schedule ? AuditActions.StormScheduled : AuditActions.StormCancelled, AuditTargets.Storm, day.ToString()),
            now,
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>The calendar of seasons. Writers take a table lock: plans are rare and must see each other.</summary>
internal sealed class PostgresSeasonConfig(NpgsqlDataSource dataSource) : ISeasonConfig
{
    public async Task<IReadOnlyList<SeasonView>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "select number, name, opening_day::text, last_day::text, deaths from seasons order by number desc",
            connection);
        return await ModerationSql.ListAsync(
            command,
            r => new SeasonView(r.GetInt32(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetInt64(4)),
            cancellationToken);
    }

    public async Task<Result<int>> PlanAsync(string? name, GameDay openingDay, GameDay? lastDay, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var seasons = await LockAndReadAsync(tx, cancellationToken);
        var planned = SeasonCalendar.Plan(seasons, name, openingDay, lastDay, GameDay.Of(now));
        if (planned.IsFailure)
        {
            return planned.Error!;
        }

        var season = planned.Value;
        await tx.ExecuteAsync(
            "insert into seasons (number, name, opening_day, last_day, deaths) values (@number, @name, @opening, @last, 0)",
            cancellationToken,
            ("number", season.Number),
            ("name", season.Name),
            ("opening", season.OpeningDay.Date),
            ("last", season.LastDay?.Date));
        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(actor, AuditActions.SeasonPlanned, AuditTargets.Season, Number(season.Number), After: View(season)),
            now,
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Result.Success(season.Number);
    }

    public async Task<Result> ChangeAsync(int number, string? name, GameDay openingDay, GameDay? lastDay, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var seasons = await LockAndReadAsync(tx, cancellationToken);
        var changed = SeasonCalendar.Change(seasons, number, name, openingDay, lastDay, GameDay.Of(now));
        if (changed.IsFailure)
        {
            return changed.Error!;
        }

        var before = seasons.Single(s => s.Number == number);
        var season = changed.Value;
        await tx.ExecuteAsync(
            "update seasons set name = @name, opening_day = @opening, last_day = @last where number = @number",
            cancellationToken,
            ("number", number),
            ("name", season.Name),
            ("opening", season.OpeningDay.Date),
            ("last", season.LastDay?.Date));
        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(actor, AuditActions.SeasonChanged, AuditTargets.Season, Number(number), View(before), View(season)),
            now,
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private static string Number(int number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static object View(Season season) => new { name = season.Name, openingDay = season.OpeningDay.ToString(), lastDay = season.LastDay?.ToString() };

    private static async Task<List<Season>> LockAndReadAsync(NpgsqlTransaction tx, CancellationToken cancellationToken)
    {
        await tx.ExecuteAsync("lock table seasons in share row exclusive mode", cancellationToken);
        await using var r = await tx.ReaderAsync("select number, name, opening_day, last_day from seasons order by number", cancellationToken);
        var seasons = new List<Season>();
        while (await r.ReadAsync(cancellationToken))
        {
            var last = r.IsDBNull(3) ? (GameDay?)null : new GameDay(r.GetFieldValue<DateOnly>(3));
            seasons.Add(Season.Plan(r.GetInt32(0), r.GetString(1), new GameDay(r.GetFieldValue<DateOnly>(2)), last).Value);
        }

        return seasons;
    }
}

/// <summary>
/// Feature flags: overrides in <c>feature_flags</c>, re-read every 10 seconds by each
/// instance (ADR 0022). A change made here is seen at once by this instance.
/// </summary>
internal sealed class PostgresFeatureFlags(NpgsqlDataSource dataSource, TimeProvider time) : IFeatureFlags
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);
    private (Dictionary<string, bool> Overrides, long ReadAt)? _cache;

    public async Task<bool> IsEnabledAsync(FeatureFlag flag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flag);
        var overrides = await OverridesAsync(cancellationToken);
        return overrides.TryGetValue(flag.Key, out var enabled) ? enabled : flag.Default;
    }

    public async Task<IReadOnlyList<FlagState>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "select f.key, f.enabled, f.updated_at, p.pseudo from feature_flags f left join players p on p.id = f.updated_by",
            connection);
        var rows = (await ModerationSql.ListAsync(
            command,
            r => (Key: r.GetString(0), Enabled: r.GetBoolean(1), At: r.GetFieldValue<DateTimeOffset>(2), By: r.IsDBNull(3) ? null : r.GetString(3)),
            cancellationToken)).ToDictionary(r => r.Key);
        return FeatureFlags.All
            .Select(f => rows.TryGetValue(f.Key, out var o)
                ? new FlagState(f.Key, f.Description, o.Enabled, f.Default, o.At, o.By)
                : new FlagState(f.Key, f.Description, f.Default, f.Default, null, null))
            .ToList();
    }

    public async Task<Result> SetAsync(string key, bool? enabled, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (FeatureFlags.Find(key) is not { } flag)
        {
            return FeatureErrors.UnknownFlag;
        }

        if (enabled is not { } on)
        {
            return FeatureErrors.EnabledMissing;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var before = await tx.ScalarAsync<bool?>("select enabled from feature_flags where key = @key for update", cancellationToken, ("key", key)) ?? flag.Default;
        // Only overrides are stored: back to the default, the row goes.
        if (on == flag.Default)
        {
            await tx.ExecuteAsync("delete from feature_flags where key = @key", cancellationToken, ("key", key));
        }
        else
        {
            await tx.ExecuteAsync(
                """
                insert into feature_flags (key, enabled, updated_at, updated_by) values (@key, @enabled, @now, @actor)
                on conflict (key) do update set enabled = excluded.enabled, updated_at = excluded.updated_at, updated_by = excluded.updated_by
                """,
                cancellationToken,
                ("key", key),
                ("enabled", on),
                ("now", Sql.Utc(now)),
                ("actor", actor.Id?.Value));
        }

        if (before != on)
        {
            await AuditWriter.AppendAsync(tx, new AuditRecord(actor, AuditActions.FlagChanged, AuditTargets.Flag, key, new { enabled = before }, new { enabled = on }), now, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        _cache = null;
        return Result.Success();
    }

    public void Forget() => _cache = null;

    private async Task<Dictionary<string, bool>> OverridesAsync(CancellationToken cancellationToken)
    {
        if (_cache is { } cached && time.GetElapsedTime(cached.ReadAt) < Ttl)
        {
            return cached.Overrides;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select key, enabled from feature_flags", connection);
        var overrides = (await ModerationSql.ListAsync(command, r => (r.GetString(0), r.GetBoolean(1)), cancellationToken))
            .ToDictionary(r => r.Item1, r => r.Item2);
        _cache = (overrides, time.GetTimestamp());
        return overrides;
    }
}
