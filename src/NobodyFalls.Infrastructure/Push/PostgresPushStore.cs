using NobodyFalls.Application.Push;
using NobodyFalls.Domain.Common;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Push;

/// <summary>Subscriptions, preferences, the outbox and the names the texts need.</summary>
internal sealed class PostgresPushStore(NpgsqlDataSource dataSource) : IPushStore, INotificationOutbox, INotificationDirectory
{
    /// <summary>A "you fell" from three days ago helps nobody: old notifications expire unsent.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private const int MaxAttempts = 5;

    public async Task SubscribeAsync(PlayerId player, PushSubscription subscription, string? userAgent, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // An endpoint belongs to one browser: if someone else signs in there, it moves to them.
        await using var command = Command(
            """
            insert into push_subscriptions (endpoint, player_id, p256dh, auth, user_agent, created_at)
            values (@endpoint, @player, @p256dh, @auth, @agent, @now)
            on conflict (endpoint) do update
            set player_id = excluded.player_id, p256dh = excluded.p256dh, auth = excluded.auth,
                user_agent = excluded.user_agent, created_at = excluded.created_at
            """,
            ("endpoint", subscription.Endpoint),
            ("player", player.Value),
            ("p256dh", subscription.P256dh),
            ("auth", subscription.Auth),
            ("agent", userAgent is { Length: > 300 } ? userAgent[..300] : userAgent),
            ("now", Sql.Utc(now)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UnsubscribeAsync(PlayerId player, string endpoint, CancellationToken cancellationToken)
    {
        await using var command = Command(
            "delete from push_subscriptions where endpoint = @endpoint and player_id = @player",
            ("endpoint", endpoint),
            ("player", player.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PushSubscription>> SubscriptionsAsync(PlayerId player, CancellationToken cancellationToken)
    {
        await using var command = Command("select endpoint, p256dh, auth from push_subscriptions where player_id = @player", ("player", player.Value));
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var subscriptions = new List<PushSubscription>();
        while (await r.ReadAsync(cancellationToken))
        {
            subscriptions.Add(new PushSubscription(r.GetString(0), r.GetString(1), r.GetString(2)));
        }

        return subscriptions;
    }

    public async Task ForgetAsync(string endpoint, CancellationToken cancellationToken)
    {
        await using var command = Command("delete from push_subscriptions where endpoint = @endpoint", ("endpoint", endpoint));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<NotificationPreferences> PreferencesAsync(PlayerId player, CancellationToken cancellationToken)
    {
        await using var command = Command(
            "select friend_below, friend_fell, saved_me, fell, storm from notification_preferences where player_id = @player",
            ("player", player.Value));
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken)
            ? new NotificationPreferences(r.GetBoolean(0), r.GetBoolean(1), r.GetBoolean(2), r.GetBoolean(3), r.GetBoolean(4))
            : NotificationPreferences.Default;
    }

    public async Task SavePreferencesAsync(PlayerId player, NotificationPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        await using var command = Command(
            """
            insert into notification_preferences (player_id, friend_below, friend_fell, saved_me, fell, storm)
            values (@player, @below, @friend_fell, @saved, @fell, @storm)
            on conflict (player_id) do update
            set friend_below = excluded.friend_below, friend_fell = excluded.friend_fell,
                saved_me = excluded.saved_me, fell = excluded.fell, storm = excluded.storm
            """,
            ("player", player.Value),
            ("below", preferences.FriendBelow),
            ("friend_fell", preferences.FriendFell),
            ("saved", preferences.SavedMe),
            ("fell", preferences.Fell),
            ("storm", preferences.Storm));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PendingNotification>> ClaimAsync(int count, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using (var expire = Command(
                         "update notification_outbox set sent_at = @now where sent_at is null and (created_at < @oldest or attempts >= @max)",
                         ("now", Sql.Utc(now)),
                         ("oldest", Sql.Utc(now - MaxAge)),
                         ("max", MaxAttempts)))
        {
            await expire.ExecuteNonQueryAsync(cancellationToken);
        }

        // The lease (locked_until) lets several instances share the work without
        // sending twice; SKIP LOCKED keeps them from waiting on each other.
        await using var command = Command(
            """
            update notification_outbox o
            set locked_until = @until, attempts = o.attempts + 1
            where o.id in (
                select id from notification_outbox
                where sent_at is null and (locked_until is null or locked_until < @now)
                order by id
                limit @count
                for update skip locked)
            returning o.id, o.player_id, o.kind, (o.payload->>'day')::date,
                      coalesce((select array_agg(value::uuid) from jsonb_array_elements_text(o.payload->'players')), '{}')
            """,
            ("until", Sql.Utc(now + Lease)),
            ("now", Sql.Utc(now)),
            ("count", count));
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var claimed = new List<PendingNotification>();
        while (await r.ReadAsync(cancellationToken))
        {
            claimed.Add(new PendingNotification(
                r.GetInt64(0),
                new PlayerId(r.GetGuid(1)),
                r.GetString(2),
                new GameDay(r.GetFieldValue<DateOnly>(3)),
                r.GetFieldValue<Guid[]>(4).Select(g => new PlayerId(g)).ToList()));
        }

        return claimed.OrderBy(n => n.Id).ToList();
    }

    public async Task MarkSentAsync(long id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = Command("update notification_outbox set sent_at = @now where id = @id", ("id", id), ("now", Sql.Utc(now)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string> LocaleOfAsync(PlayerId player, CancellationToken cancellationToken)
    {
        await using var command = Command("select locale from players where id = @player", ("player", player.Value));
        return await command.ExecuteScalarAsync(cancellationToken) as string ?? "fr";
    }

    public async Task<IReadOnlyList<string>> PseudosAsync(IReadOnlyList<PlayerId> players, CancellationToken cancellationToken)
    {
        if (players.Count == 0)
        {
            return [];
        }

        await using var command = Command(
            "select id, pseudo from players where id = any(@ids) and deleted_at is null",
            ("ids", players.Select(p => p.Value).ToArray()));
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var names = new Dictionary<Guid, string>();
        while (await r.ReadAsync(cancellationToken))
        {
            names[r.GetGuid(0)] = r.GetString(1);
        }

        // Keep the order of the notification (first name shown is the first listed).
        return players.Where(p => names.ContainsKey(p.Value)).Select(p => names[p.Value]).ToList();
    }

    private NpgsqlCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var command = dataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}
