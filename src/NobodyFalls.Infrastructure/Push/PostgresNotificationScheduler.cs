using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Push;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Push;

/// <summary>
/// The two notifications that depend on the time of day, written to the outbox once per
/// day (a flag on <c>game_days</c> makes each run idempotent):
/// the storm announcement, and one reminder per player about friends still below the
/// threshold, instead of a push each time a friend is below (everyone is at 0 every morning).
/// </summary>
internal sealed class PostgresNotificationScheduler(NpgsqlDataSource dataSource, IGameSettingsProvider settings) : INotificationScheduler
{
    private const string Storm = """
        with announced as (
            update game_days set storm_announced_at = @now
            where day = @day and storm and storm_announced_at is null and resolved_at is null
            returning day)
        insert into notification_outbox (dedupe_key, player_id, kind, payload, created_at)
        select 'storm.announced:' || @day::text || ':' || pd.player_id::text, pd.player_id, 'storm.announced',
               jsonb_build_object('day', @day, 'players', '[]'::jsonb), @now
        from player_days pd join announced a on a.day = pd.day
        on conflict (dedupe_key) do nothing;
        """;

    private const string FriendsBelow = """
        with reminded as (
            update game_days set friend_reminder_at = @now
            where day = @day and friend_reminder_at is null and resolved_at is null
            returning day),
        below as (
            select pd.player_id from player_days pd join reminded r on r.day = pd.day
            where pd.received < pd.required and not pd.immune)
        insert into notification_outbox (dedupe_key, player_id, kind, payload, created_at)
        select 'friends.below:' || @day::text || ':' || f.friend::text, f.friend, 'friends.below',
               jsonb_build_object('day', @day, 'players', jsonb_agg(f.below order by f.below)), @now
        from (
            select b.player_id as below,
                   case when fr.player_low = b.player_id then fr.player_high else fr.player_low end as friend
            from below b join friendships fr on b.player_id in (fr.player_low, fr.player_high)
        ) f
        group by f.friend
        on conflict (dedupe_key) do nothing;
        """;

    public async Task<int> RunAsync(GameDay day, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        int? version = await tx.ScalarAsync<int?>(
            "select settings_version from game_days where day = @day and resolved_at is null",
            cancellationToken,
            ("day", day.Date));
        if (version is null)
        {
            return 0;
        }

        var rules = (await settings.GetVersionAsync(version.Value, cancellationToken)).Rules;
        var written = 0;
        var p = new (string, object?)[] { ("day", day.Date), ("now", Sql.Utc(now)) };

        if (now >= StormPolicy.AnnouncementAt(day, rules))
        {
            written += await tx.ExecuteAsync(Storm, cancellationToken, p);
        }

        if (now >= day.StartsAt + rules.FriendReminderAt.ToTimeSpan())
        {
            written += await tx.ExecuteAsync(FriendsBelow, cancellationToken, p);
        }

        await tx.CommitAsync(cancellationToken);
        return written;
    }
}
