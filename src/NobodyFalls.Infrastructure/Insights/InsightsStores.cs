using System.Security.Cryptography;
using System.Text.Json;
using NobodyFalls.Application.Audit;
using NobodyFalls.Application.Insights;
using NobodyFalls.Domain.Common;
using NobodyFalls.Infrastructure.Audit;
using NobodyFalls.Infrastructure.Moderation;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Insights;

internal sealed class PostgresActivityRecorder(NpgsqlDataSource dataSource) : IActivityRecorder
{
    public async Task RecordAsync(AccessSeen access, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        await using var command = dataSource.CreateCommand(
            """
            insert into player_visits (player_id, day) values (@player, @day) on conflict do nothing;

            insert into login_events (player_id, day, at, ip, device_id, user_agent_family, kind)
            values (@player, @day, @now, @ip, @device, @agent, @kind)
            on conflict (player_id, day, ip, device_id) do update
              set at = excluded.at,
                  kind = case when excluded.kind = 'sign_in' then 'sign_in' else login_events.kind end;
            """);
        command.Parameters.AddWithValue("player", access.Player.Value);
        command.Parameters.AddWithValue("day", GameDay.Of(now).Date);
        command.Parameters.AddWithValue("now", Sql.Utc(now));
        command.Parameters.AddWithValue("ip", access.Ip);
        command.Parameters.AddWithValue("device", access.DeviceId);
        command.Parameters.Add(ModerationSql.Text("agent", access.UserAgentFamily));
        command.Parameters.AddWithValue("kind", access.SignIn ? "sign_in" : "visit");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("delete from login_events where at < @before");
        command.Parameters.AddWithValue("before", Sql.Utc(olderThan));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

internal sealed class PostgresDashboardQueries(NpgsqlDataSource dataSource) : IDashboardQueries
{
    private static readonly int[] Horizons = [1, 7, 30];

    public async Task<Dashboard> GetAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = GameDay.Of(now).Date;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        int alive, players, visited, streak, openReports, openTickets;
        long deaths;
        await using (var command = new NpgsqlCommand(
                         """
                         select
                           (select count(*)::int from players where kind = 'human' and status = 'active' and deleted_at is null
                              and (banned_until is null or banned_until <= @now)),
                           (select count(*)::int from players where kind = 'human' and deleted_at is null),
                           (select count(*)::int from player_visits where day = @today),
                           coalesce((select streak_after from game_days where resolved_at is not null order by day desc limit 1), 0),
                           coalesce((select deaths_all_time_after from game_days where resolved_at is not null order by day desc limit 1), 0),
                           (select count(*)::int from reports where status in ('open', 'in_review')),
                           (select count(*)::int from tickets where status = 'open')
                         """,
                         connection))
        {
            command.Parameters.AddWithValue("now", Sql.Utc(now));
            command.Parameters.AddWithValue("today", today);
            await using var r = await command.ExecuteReaderAsync(cancellationToken);
            await r.ReadAsync(cancellationToken);
            (alive, players, visited, streak, deaths, openReports, openTickets) =
                (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt64(4), r.GetInt32(5), r.GetInt32(6));
        }

        await using var nights = new NpgsqlCommand(
            """
            select day::text, night_number, coalesce(falls, 0), alive_at_start, storm
            from (select * from game_days where resolved_at is not null order by day desc limit 30) d
            order by day
            """,
            connection);
        var nightPoints = await ModerationSql.ListAsync(nights, r => new NightPoint(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetBoolean(4)), cancellationToken);

        // Retention: of the players who joined during the last 60 days (and long enough ago),
        // how many came back on day +1, +7 and +30.
        var retention = new List<RetentionPoint>();
        foreach (var after in Horizons)
        {
            await using var command = new NpgsqlCommand(
                """
                select count(*)::int,
                       count(*) filter (where exists (select 1 from player_visits v where v.player_id = p.id and v.day = p.joined_day + @after))::int
                from players p
                where p.kind = 'human' and p.joined_day is not null
                  and p.joined_day >= @today - 60 and p.joined_day + @after < @today
                """,
                connection);
            command.Parameters.AddWithValue("after", after);
            command.Parameters.AddWithValue("today", today);
            await using var r = await command.ExecuteReaderAsync(cancellationToken);
            await r.ReadAsync(cancellationToken);
            retention.Add(new RetentionPoint(after, r.GetInt32(0), r.GetInt32(1)));
        }

        await using var invitations = new NpgsqlCommand(
            """
            select joined_day::text, count(*)::int from players
            where invited_by_id is not null and joined_day >= @today - 29
            group by joined_day order by joined_day
            """,
            connection);
        invitations.Parameters.AddWithValue("today", today);
        var invitationPoints = await ModerationSql.ListAsync(invitations, r => new InvitationPoint(r.GetString(0), r.GetInt32(1)), cancellationToken);

        // Suspicious: a device used by three accounts or more; five sign-ups from one address in a day.
        await using var devices = new NpgsqlCommand(
            """
            select l.device_id, count(distinct l.player_id)::int, array_agg(distinct p.pseudo)
            from login_events l join players p on p.id = l.player_id
            where l.at >= @since
            group by l.device_id having count(distinct l.player_id) >= 3
            order by 2 desc limit 20
            """,
            connection);
        devices.Parameters.AddWithValue("since", Sql.Utc(now.AddDays(-30)));
        var shared = await ModerationSql.ListAsync(devices, r => new SharedDevice(r.GetString(0), r.GetInt32(1), r.GetFieldValue<string[]>(2)), cancellationToken);

        await using var bursts = new NpgsqlCommand(
            """
            select l.ip, count(distinct l.player_id)::int, min(l.at)
            from login_events l join players p on p.id = l.player_id
            where l.kind = 'sign_in' and p.created_at >= @since and l.at >= @since
            group by l.ip having count(distinct l.player_id) >= 5
            order by 2 desc limit 20
            """,
            connection);
        bursts.Parameters.AddWithValue("since", Sql.Utc(now.AddDays(-1)));
        var burstList = await ModerationSql.ListAsync(bursts, r => new SignUpBurst(r.GetString(0), r.GetInt32(1), r.GetFieldValue<DateTimeOffset>(2)), cancellationToken);

        return new Dashboard(alive, players, visited, streak, deaths, nightPoints, retention, invitationPoints, openReports, openTickets, shared, burstList);
    }
}

internal sealed class PostgresAccountLinks(NpgsqlDataSource dataSource) : IAccountLinks
{
    public async Task<AccountLinks> LinksAsync(PlayerId player, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var ips = new NpgsqlCommand(
            "select ip, count(distinct day)::int, max(at) from login_events where player_id = @id group by ip order by 3 desc",
            connection);
        ips.Parameters.AddWithValue("id", player.Value);
        var addresses = await ModerationSql.ListAsync(ips, r => new SeenAddress(r.GetString(0), r.GetInt32(1), r.GetFieldValue<DateTimeOffset>(2)), cancellationToken);

        await using var devices = new NpgsqlCommand(
            "select device_id, max(user_agent_family), count(distinct day)::int, max(at) from login_events where player_id = @id group by device_id order by 4 desc",
            connection);
        devices.Parameters.AddWithValue("id", player.Value);
        var seenDevices = await ModerationSql.ListAsync(devices, r => new SeenDevice(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetInt32(2), r.GetFieldValue<DateTimeOffset>(3)), cancellationToken);

        await using var others = new NpgsqlCommand(
            """
            with mine as (select distinct ip, device_id from login_events where player_id = @id)
            select p.id, p.pseudo, p.status,
                   bool_or(l.device_id in (select device_id from mine)),
                   bool_or(l.ip in (select ip from mine)),
                   max(l.at), p.banned_until
            from login_events l join players p on p.id = l.player_id
            where l.player_id <> @id
              and (l.device_id in (select device_id from mine) or l.ip in (select ip from mine))
            group by p.id, p.pseudo, p.status, p.banned_until
            order by 4 desc, 6 desc
            limit 50
            """,
            connection);
        others.Parameters.AddWithValue("id", player.Value);
        var accounts = await ModerationSql.ListAsync(others, r => new LinkedAccount(
            r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetBoolean(4), r.GetFieldValue<DateTimeOffset>(5),
            r.IsDBNull(6) ? null : r.GetFieldValue<DateTimeOffset>(6)), cancellationToken);
        return new AccountLinks(addresses, seenDevices, accounts);
    }
}

internal sealed class PostgresPersonalData(NpgsqlDataSource dataSource, SanctionCacheInvalidation caches) : IPersonalData
{
    public async Task<JsonDocument?> ExportAsync(PlayerId player, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // One JSON document built by PostgreSQL: everything about the player, in plain fields.
        await using var command = dataSource.CreateCommand(
            """
            select jsonb_build_object(
              'exportedAt', @now,
              'account', jsonb_build_object(
                  'id', p.id, 'pseudo', p.pseudo, 'locale', p.locale, 'status', p.status, 'joinedDay', p.joined_day,
                  'createdAt', p.created_at, 'lastSeenAt', p.last_seen_at, 'guardianScore', p.guardian_score,
                  'falls', p.falls_count, 'inviteCode', p.invite_code, 'mapNameVisibility', p.map_name_visibility),
              'signIns', coalesce((select jsonb_agg(jsonb_build_object('provider', e.provider, 'since', e.created_at)) from external_logins e where e.player_id = p.id), '[]'),
              'appearance', (select a.document from appearances a where a.player_id = p.id),
              'handsGiven', coalesce((select jsonb_agg(jsonb_build_object('day', h.day, 'to', r.pseudo, 'at', h.given_at, 'points', h.points) order by h.given_at)
                                      from hands h join players r on r.id = h.receiver_id where h.giver_id = p.id), '[]'),
              'handsReceived', coalesce((select jsonb_agg(jsonb_build_object('day', h.day, 'from', g.pseudo, 'at', h.given_at) order by h.given_at)
                                         from hands h join players g on g.id = h.giver_id where h.receiver_id = p.id), '[]'),
              'nights', coalesce((select jsonb_agg(jsonb_build_object('day', d.day, 'required', d.required, 'received', d.received, 'outcome', d.outcome) order by d.day)
                                  from player_days d where d.player_id = p.id), '[]'),
              'friends', coalesce((select jsonb_agg(jsonb_build_object('pseudo', o.pseudo, 'since', f.created_at))
                                   from friendships f join players o on o.id = case when f.player_low = p.id then f.player_high else f.player_low end
                                   where f.player_low = p.id or f.player_high = p.id), '[]'),
              'blocked', coalesce((select jsonb_agg(o.pseudo) from blocks b join players o on o.id = b.blocked_id where b.blocker_id = p.id), '[]'),
              'ideas', coalesce((select jsonb_agg(jsonb_build_object('title', i.title, 'body', i.body, 'status', i.status, 'createdAt', i.created_at)) from ideas i where i.author_id = p.id), '[]'),
              'votes', coalesce((select jsonb_agg(i.title) from idea_votes v join ideas i on i.id = v.idea_id where v.player_id = p.id), '[]'),
              'requests', coalesce((select jsonb_agg(jsonb_build_object('category', t.category, 'subject', t.subject, 'status', t.status,
                                      'messages', (select jsonb_agg(jsonb_build_object('from', m.author_kind, 'body', m.body, 'at', m.created_at) order by m.id)
                                                   from ticket_messages m where m.ticket_id = t.id and not m.internal)))
                                    from tickets t where t.player_id = p.id), '[]'),
              'reportsMade', coalesce((select jsonb_agg(jsonb_build_object('category', r.category, 'details', r.details, 'at', r.created_at, 'status', r.status))
                                       from reports r where r.reporter_id = p.id), '[]'),
              'sanctions', coalesce((select jsonb_agg(jsonb_build_object('kind', s.kind, 'message', s.public_message, 'startsAt', s.starts_at, 'endsAt', s.ends_at, 'revokedAt', s.revoked_at))
                                     from sanctions s where s.player_id = p.id), '[]'),
              'connections', coalesce((select jsonb_agg(jsonb_build_object('day', l.day, 'ip', l.ip, 'device', l.device_id, 'browser', l.user_agent_family) order by l.at)
                                       from login_events l where l.player_id = p.id), '[]'),
              'notificationPreferences', (select to_jsonb(n) - 'player_id' from notification_preferences n where n.player_id = p.id),
              'pushDevices', (select count(*) from push_subscriptions s where s.player_id = p.id))
            from players p where p.id = @id and p.kind = 'human'
            """);
        command.Parameters.AddWithValue("id", player.Value);
        command.Parameters.AddWithValue("now", Sql.Utc(now));
        return await command.ExecuteScalarAsync(cancellationToken) is string json ? JsonDocument.Parse(json) : null;
    }

    public async Task<Result> DeleteAsync(PlayerId player, string reason, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var cleanReason = NobodyFalls.Domain.Feedback.Text.Clean(reason, keepLines: true);
        if (cleanReason.Length is < 3 or > 500)
        {
            return PersonalDataErrors.Reason;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await tx.LockNightSharedAsync(cancellationToken);
        string? pseudo = null;
        bool staff = false;
        await using (var r = await tx.ReaderAsync(
                         """
                         select pseudo, exists (select 1 from staff_members m where m.player_id = p.id)
                         from players p where id = @id and kind = 'human' and deleted_at is null for update
                         """,
                         cancellationToken,
                         ("id", player.Value)))
        {
            if (await r.ReadAsync(cancellationToken))
            {
                pseudo = r.GetString(0);
                staff = r.GetBoolean(1);
            }
        }

        if (pseudo is null)
        {
            return PersonalDataErrors.NotFound;
        }

        if (staff)
        {
            return PersonalDataErrors.Staff;
        }

        var replacement = $"supprime_{RandomNumberGenerator.GetInt32(100_000, 1_000_000)}";
        await tx.ExecuteAsync(
            """
            delete from player_days where day = @day and player_id = @id;
            delete from external_logins where player_id = @id;
            delete from push_subscriptions where player_id = @id;
            delete from notification_preferences where player_id = @id;
            delete from notification_outbox where player_id = @id and sent_at is null;
            delete from appearances where player_id = @id;
            delete from friendships where player_low = @id or player_high = @id;
            delete from friend_requests where from_id = @id or to_id = @id;
            delete from blocks where blocker_id = @id or blocked_id = @id;
            delete from login_events where player_id = @id;
            delete from player_visits where player_id = @id;
            delete from tickets where player_id = @id;
            with removed as (delete from idea_votes where player_id = @id returning idea_id)
            update ideas set votes_count = (select count(*) from idea_votes v where v.idea_id = ideas.id)
            where id in (select idea_id from removed);
            update players set deleted_at = @now, pseudo = @pseudo, pseudo_key = @pseudo, invite_code = @invite,
                               session_version = session_version + 1, pseudo_reset_pending = false
            where id = @id;
            """,
            cancellationToken,
            ("id", player.Value),
            ("day", GameDay.Of(now).Date),
            ("now", Sql.Utc(now)),
            ("pseudo", replacement),
            ("invite", Convert.ToHexString(RandomNumberGenerator.GetBytes(6))));
        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(actor, AuditActions.PlayerDeleted, AuditTargets.Player, player.ToString(), Before: new { pseudo }, After: new { pseudo = replacement }, Reason: cleanReason),
            now,
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        caches.Forget(player);
        return Result.Success();
    }
}
