using System.Globalization;
using System.Text.Json;
using NobodyFalls.Application.Audit;
using NobodyFalls.Application.Moderation;
using NobodyFalls.Application.Push;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Feedback;
using NobodyFalls.Domain.Moderation;
using NobodyFalls.Infrastructure.Audit;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace NobodyFalls.Infrastructure.Moderation;

internal static class ModerationSql
{
    public static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Report columns, with a readable label for the target and the people involved.</summary>
    public const string ReportColumns =
        """
        r.id, r.target_type, r.target_id, r.target_player_id,
        coalesce(tp.pseudo, r.target_snapshot->>'title', r.target_snapshot->>'pseudo', r.target_id),
        r.category, r.status, r.reporter_id, rp.pseudo, ap.pseudo, r.created_at, r.resolved_at
        """;

    public const string ReportJoins =
        """
        left join players tp on tp.id = r.target_player_id
        left join players rp on rp.id = r.reporter_id
        left join players ap on ap.id = r.assignee_id
        """;

    public static ReportSummary ReadReport(NpgsqlDataReader r) => new(
        r.GetInt64(0),
        r.GetString(1),
        r.GetString(2),
        r.IsDBNull(3) ? null : r.GetGuid(3),
        r.GetString(4),
        r.GetString(5),
        r.GetString(6),
        r.GetGuid(7),
        r.IsDBNull(8) ? null : r.GetString(8),
        r.IsDBNull(9) ? null : r.GetString(9),
        r.GetFieldValue<DateTimeOffset>(10),
        r.IsDBNull(11) ? null : r.GetFieldValue<DateTimeOffset>(11));

    public static async Task<List<T>> ListAsync<T>(NpgsqlCommand command, Func<NpgsqlDataReader, T> read, CancellationToken cancellationToken)
    {
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<T>();
        while (await r.ReadAsync(cancellationToken))
        {
            items.Add(read(r));
        }

        return items;
    }

    public static NpgsqlParameter Text(string name, string? value) => new(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };
}

internal sealed class PostgresAdminPlayers(NpgsqlDataSource dataSource) : IAdminPlayers
{
    public async Task<IReadOnlyList<AdminPlayerRow>> SearchAsync(string? query, string? filter, int limit, CancellationToken cancellationToken)
    {
        var text = query?.Trim() ?? "";
        Guid? id = Guid.TryParse(text, out var parsed) ? parsed : null;
        var key = text.Length == 0 || id is not null ? null : Pseudo.FromTrusted(text).Key;
        await using var command = dataSource.CreateCommand(
            """
            select p.id, p.pseudo, p.kind, p.status, p.banned_until, p.score_excluded_until,
                   (select count(*)::int from reports r where r.target_player_id = p.id and r.status in ('open', 'in_review')),
                   p.created_at, p.last_seen_at
            from players p
            where p.deleted_at is null
              and (@id is null or p.id = @id)
              and (@key is null or starts_with(p.pseudo_key, @key))
              and (@filter is null
                   or (@filter = 'banned' and p.banned_until > now())
                   or (@filter = 'reported' and exists (select 1 from reports r where r.target_player_id = p.id and r.status in ('open', 'in_review'))))
            order by (p.pseudo_key = @key) desc nulls last, p.created_at desc
            limit @limit
            """);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = (object?)id ?? DBNull.Value });
        command.Parameters.Add(ModerationSql.Text("key", key));
        command.Parameters.Add(ModerationSql.Text("filter", filter is "banned" or "reported" ? filter : null));
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 100));
        return await ModerationSql.ListAsync(command, r => new AdminPlayerRow(
            r.GetGuid(0),
            r.GetString(1),
            r.GetString(2),
            r.GetString(3),
            r.IsDBNull(4) ? null : r.GetFieldValue<DateTimeOffset>(4),
            r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5),
            r.GetInt32(6),
            r.GetFieldValue<DateTimeOffset>(7),
            r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8)), cancellationToken);
    }

    public async Task<AdminPlayerSheet?> SheetAsync(PlayerId player, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        AdminPlayerSheet? sheet = null;
        await using (var command = new NpgsqlCommand(
                         """
                         select p.id, p.pseudo, p.kind, p.status, p.locale, p.guardian_score, p.falls_count, p.created_at,
                                p.last_seen_at, p.banned_until, p.score_excluded_until, p.pseudo_reset_pending,
                                (select count(*)::int from friendships f where f.player_low = p.id or f.player_high = p.id),
                                (select count(*)::int from hands h where h.giver_id = p.id and h.day >= @since),
                                (select count(*)::int from hands h where h.receiver_id = p.id and h.day >= @since),
                                coalesce((select m.roles from staff_members m where m.player_id = p.id), '{}')
                         from players p where p.id = @id
                         """,
                         connection))
        {
            command.Parameters.AddWithValue("id", player.Value);
            command.Parameters.AddWithValue("since", GameDay.Of(now).AddDays(-6).Date);
            await using var r = await command.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken))
            {
                return null;
            }

            sheet = new AdminPlayerSheet(
                r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5), r.GetInt32(6),
                r.GetFieldValue<DateTimeOffset>(7),
                r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8),
                r.IsDBNull(9) ? null : r.GetFieldValue<DateTimeOffset>(9),
                r.IsDBNull(10) ? null : r.GetFieldValue<DateTimeOffset>(10),
                r.GetBoolean(11), r.GetInt32(12), r.GetInt32(13), r.GetInt32(14),
                r.GetFieldValue<string[]>(15), [], [], []);
        }

        await using var sanctions = new NpgsqlCommand(
            """
            select s.id, s.kind, s.reason, s.public_message, s.starts_at, s.ends_at, i.pseudo, s.report_id,
                   s.acknowledged_at, s.revoked_at, v.pseudo, s.revoke_reason,
                   s.revoked_at is null and (s.ends_at is null or s.ends_at > @now) and s.kind in ('ban', 'score_exclusion')
            from sanctions s
            left join players i on i.id = s.issued_by
            left join players v on v.id = s.revoked_by
            where s.player_id = @id order by s.starts_at desc
            """,
            connection);
        sanctions.Parameters.AddWithValue("id", player.Value);
        sanctions.Parameters.AddWithValue("now", Sql.Utc(now));
        var sanctionList = await ModerationSql.ListAsync(sanctions, r => new SanctionView(
            r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
            r.GetFieldValue<DateTimeOffset>(4), r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5),
            r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetInt64(7),
            r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8), r.IsDBNull(9) ? null : r.GetFieldValue<DateTimeOffset>(9),
            r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11), r.GetBoolean(12)), cancellationToken);

        await using var about = new NpgsqlCommand(
            $"select {ModerationSql.ReportColumns} from reports r {ModerationSql.ReportJoins} where r.target_player_id = @id order by r.id desc limit 50",
            connection);
        about.Parameters.AddWithValue("id", player.Value);
        var reportsAbout = await ModerationSql.ListAsync(about, ModerationSql.ReadReport, cancellationToken);

        await using var made = new NpgsqlCommand(
            $"select {ModerationSql.ReportColumns} from reports r {ModerationSql.ReportJoins} where r.reporter_id = @id order by r.id desc limit 50",
            connection);
        made.Parameters.AddWithValue("id", player.Value);
        var reportsMade = await ModerationSql.ListAsync(made, ModerationSql.ReadReport, cancellationToken);

        return sheet with { Sanctions = sanctionList, ReportsAbout = reportsAbout, ReportsMade = reportsMade };
    }

    public async Task<IReadOnlyList<AdminHand>> HandsAsync(PlayerId player, int days, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            select h.id, h.day::text, h.giver_id, g.pseudo, h.receiver_id, rc.pseudo, h.given_at, h.points
            from hands h join players g on g.id = h.giver_id join players rc on rc.id = h.receiver_id
            where (h.giver_id = @id or h.receiver_id = @id) and h.day >= @since
            order by h.given_at desc limit 300
            """);
        command.Parameters.AddWithValue("id", player.Value);
        command.Parameters.AddWithValue("since", GameDay.Of(now).AddDays(-Math.Clamp(days, 1, 60) + 1).Date);
        return await ModerationSql.ListAsync(command, r => new AdminHand(
            r.GetInt64(0), r.GetString(1), r.GetGuid(2), r.GetString(3), r.GetGuid(4), r.GetString(5),
            r.GetFieldValue<DateTimeOffset>(6), r.IsDBNull(7) ? null : r.GetInt32(7)), cancellationToken);
    }
}

internal sealed class PostgresReportModeration(NpgsqlDataSource dataSource, SanctionCacheInvalidation caches) : IReportModeration
{
    public async Task<ReportPage> ListAsync(string? status, bool mine, PlayerId viewer, long? beforeId, int limit, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit, 1, 100);
        // "open" means everything still to handle (new or in review).
        await using var command = dataSource.CreateCommand(
            $"""
            select {ModerationSql.ReportColumns} from reports r {ModerationSql.ReportJoins}
            where (@status is null
                   or (@status = 'open' and r.status in ('open', 'in_review'))
                   or (@status <> 'open' and r.status = @status))
              and (not @mine or r.assignee_id = @viewer)
              and (@before is null or r.id < @before)
            order by r.id desc limit @limit
            """);
        command.Parameters.Add(ModerationSql.Text("status", status));
        command.Parameters.AddWithValue("mine", mine);
        command.Parameters.AddWithValue("viewer", viewer.Value);
        command.Parameters.Add(new NpgsqlParameter("before", NpgsqlDbType.Bigint) { Value = (object?)beforeId ?? DBNull.Value });
        command.Parameters.AddWithValue("limit", take + 1);
        var items = await ModerationSql.ListAsync(command, ModerationSql.ReadReport, cancellationToken);
        var more = items.Count > take;
        if (more)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new ReportPage(items, more ? items[^1].Id : null);
    }

    public async Task<ReportDetail?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            select {ModerationSql.ReportColumns}, r.details, r.target_snapshot::text, r.resolution_note, r.sanction_id,
                   (select count(*)::int from reports o where o.target_type = r.target_type and o.target_id = r.target_id and o.id <> r.id)
            from reports r {ModerationSql.ReportJoins} where r.id = @id
            """);
        command.Parameters.AddWithValue("id", id);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ReportDetail(
            ModerationSql.ReadReport(r),
            r.GetString(12),
            JsonDocument.Parse(r.GetString(13)).RootElement.Clone(),
            r.IsDBNull(14) ? null : r.GetString(14),
            r.IsDBNull(15) ? null : r.GetInt64(15),
            r.GetInt32(16));
    }

    public async Task<Result> AssignAsync(long id, PlayerId? assignee, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var updated = await tx.ExecuteAsync(
            """
            update reports set assignee_id = @assignee,
                               status = case when status = 'open' and @assignee is not null then 'in_review' else status end
            where id = @id and status in ('open', 'in_review')
            """,
            cancellationToken,
            ("id", id),
            ("assignee", assignee?.Value));
        if (updated == 0)
        {
            return await ExistsAsync(tx, id, cancellationToken) ? ReportModerationErrors.AlreadyResolved : ReportModerationErrors.NotFound;
        }

        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(actor, AuditActions.ReportAssigned, AuditTargets.Report, ModerationSql.Id(id), After: new { assignee = assignee?.Value }),
            now,
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<long?>> ResolveAsync(long id, ReportOutcome outcome, string? note, SanctionDraft? sanction, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var cleanNote = Text.Clean(note, keepLines: true);
        if (cleanNote.Length > 2000)
        {
            return ReportModerationErrors.Note;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        string status;
        Guid reporter;
        Guid? target;
        await using (var r = await tx.ReaderAsync(
                         "select status, reporter_id, target_player_id from reports where id = @id for update",
                         cancellationToken,
                         ("id", id)))
        {
            if (!await r.ReadAsync(cancellationToken))
            {
                return ReportModerationErrors.NotFound;
            }

            status = r.GetString(0);
            reporter = r.GetGuid(1);
            target = r.IsDBNull(2) ? null : r.GetGuid(2);
        }

        var allowed = ReportModeration.CanResolve(status, outcome, sanction is not null);
        if (allowed.IsFailure)
        {
            return allowed.Error!;
        }

        long? sanctionId = null;
        if (sanction is not null)
        {
            if (target is null)
            {
                return SanctionErrors.PlayerNotFound;
            }

            var issued = await SanctionWriter.IssueAsync(tx, new PlayerId(target.Value), sanction, id, actor, now, cancellationToken);
            if (issued.IsFailure)
            {
                return issued.Error!;
            }

            sanctionId = issued.Value;
        }

        await tx.ExecuteAsync(
            """
            update reports set status = @status, resolution_note = @note, sanction_id = @sanction, resolved_at = @now,
                               assignee_id = coalesce(assignee_id, @actor)
            where id = @id
            """,
            cancellationToken,
            ("id", id),
            ("status", outcome.Name()),
            ("note", cleanNote.Length == 0 ? null : cleanNote),
            ("sanction", sanctionId),
            ("now", Sql.Utc(now)),
            ("actor", actor.Id?.Value));
        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(actor, AuditActions.ReportResolved, AuditTargets.Report, ModerationSql.Id(id), Before: new { status }, After: new { status = outcome.Name(), sanction = sanctionId }, Reason: cleanNote.Length == 0 ? null : cleanNote),
            now,
            cancellationToken);
        // The reporter learns the report was handled, never what was decided.
        await OutboxWriter.EnqueueAsync(tx, $"report.handled:{id}", new PlayerId(reporter), NotificationKinds.ReportHandled, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        if (target is { } player && sanctionId is not null)
        {
            caches.Forget(new PlayerId(player));
        }

        return Result.Success(sanctionId);
    }

    private static async Task<bool> ExistsAsync(NpgsqlTransaction tx, long id, CancellationToken cancellationToken) =>
        await tx.ScalarAsync<bool>("select exists (select 1 from reports where id = @id)", cancellationToken, ("id", id));
}

internal sealed class PostgresTicketModeration(NpgsqlDataSource dataSource) : ITicketModeration
{
    private const string Columns =
        "t.id, t.player_id, p.pseudo, t.category, t.subject, t.status, a.pseudo, t.sanction_id, t.updated_at";

    private const string Joins = "join players p on p.id = t.player_id left join players a on a.id = t.assignee_id";

    public async Task<IReadOnlyList<AdminTicketSummary>> ListAsync(IReadOnlyCollection<TicketCategory> categories, string? status, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(categories);
        await using var command = dataSource.CreateCommand(
            $"""
            select {Columns} from tickets t {Joins}
            where t.category = any(@categories)
              and (@status is null
                   or (@status = 'active' and t.status in ('open', 'waiting_player'))
                   or (@status <> 'active' and t.status = @status))
            order by (t.status = 'open') desc, t.updated_at desc limit @limit
            """);
        command.Parameters.AddWithValue("categories", categories.Select(c => c.Name()).ToArray());
        command.Parameters.Add(ModerationSql.Text("status", status));
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 200));
        return await ModerationSql.ListAsync(command, Read, cancellationToken);
    }

    public async Task<AdminTicket?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        AdminTicketSummary? summary;
        await using (var command = new NpgsqlCommand($"select {Columns} from tickets t {Joins} where t.id = @id", connection))
        {
            command.Parameters.AddWithValue("id", id);
            summary = (await ModerationSql.ListAsync(command, Read, cancellationToken)).FirstOrDefault();
        }

        if (summary is null)
        {
            return null;
        }

        await using var messages = new NpgsqlCommand(
            """
            select m.id, m.author_kind, p.pseudo, m.body, m.internal, m.created_at
            from ticket_messages m left join players p on p.id = m.author_id
            where m.ticket_id = @id order by m.id
            """,
            connection);
        messages.Parameters.AddWithValue("id", id);
        var list = await ModerationSql.ListAsync(messages, r => new AdminTicketMessage(
            r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetBoolean(4), r.GetFieldValue<DateTimeOffset>(5)), cancellationToken);
        return new AdminTicket(summary, list);
    }

    public async Task<Result> ReplyAsync(long id, string body, bool isInternal, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        string? status;
        Guid player;
        await using (var r = await tx.ReaderAsync("select status, player_id from tickets where id = @id for update", cancellationToken, ("id", id)))
        {
            if (!await r.ReadAsync(cancellationToken))
            {
                return TicketErrors.NotFound;
            }

            status = r.GetString(0);
            player = r.GetGuid(1);
        }

        var next = TicketRules.AfterStaffReply(TicketRules.ParseStatus(status) ?? TicketStatus.Open);
        if (next.IsFailure)
        {
            return next.Error!;
        }

        var messageId = await tx.ScalarAsync<long>(
            """
            insert into ticket_messages (ticket_id, author_kind, author_id, body, internal, created_at)
            values (@id, 'staff', @author, @body, @internal, @now) returning id
            """,
            cancellationToken,
            ("id", id),
            ("author", actor.Id?.Value),
            ("body", body),
            ("internal", isInternal),
            ("now", Sql.Utc(now)));
        if (!isInternal)
        {
            await tx.ExecuteAsync(
                "update tickets set status = @status, updated_at = @now, assignee_id = coalesce(assignee_id, @actor) where id = @id",
                cancellationToken,
                ("id", id),
                ("status", next.Value.Name()),
                ("now", Sql.Utc(now)),
                ("actor", actor.Id?.Value));
            await OutboxWriter.EnqueueAsync(tx, $"ticket.replied:{messageId}", new PlayerId(player), NotificationKinds.TicketReplied, now, cancellationToken);
        }

        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(actor, isInternal ? AuditActions.TicketNoted : AuditActions.TicketReplied, AuditTargets.Ticket, ModerationSql.Id(id), After: new { message = messageId }),
            now,
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetStatusAsync(long id, TicketStatus status, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var before = await tx.ScalarAsync<string>("select status from tickets where id = @id for update", cancellationToken, ("id", id));
        if (before is null)
        {
            return TicketErrors.NotFound;
        }

        await tx.ExecuteAsync(
            "update tickets set status = @status, updated_at = @now where id = @id",
            cancellationToken,
            ("id", id),
            ("status", status.Name()),
            ("now", Sql.Utc(now)));
        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(actor, AuditActions.TicketStatusChanged, AuditTargets.Ticket, ModerationSql.Id(id), Before: new { status = before }, After: new { status = status.Name() }),
            now,
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> AssignAsync(long id, PlayerId? assignee, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        if (await tx.ExecuteAsync("update tickets set assignee_id = @assignee where id = @id", cancellationToken, ("id", id), ("assignee", assignee?.Value)) == 0)
        {
            return TicketErrors.NotFound;
        }

        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(actor, AuditActions.TicketAssigned, AuditTargets.Ticket, ModerationSql.Id(id), After: new { assignee = assignee?.Value }),
            now,
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private static AdminTicketSummary Read(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetInt64(7), r.GetFieldValue<DateTimeOffset>(8));
}

internal sealed class PostgresIdeaModeration(NpgsqlDataSource dataSource) : IIdeaModeration
{
    private const string Columns =
        """
        i.id, i.title, i.body, i.category, i.status, i.locale, i.author_id, p.pseudo, i.votes_count,
        i.staff_answer, i.hidden_at is not null, i.hidden_reason, i.merged_into_id, i.created_at
        """;

    public async Task<IReadOnlyList<AdminIdea>> ListAsync(string? status, bool includeHidden, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            select {Columns} from ideas i left join players p on p.id = i.author_id
            where (@status is null or i.status = @status) and (@hidden or i.hidden_at is null)
            order by i.merged_into_id is not null, (i.status = 'new') desc, i.votes_count desc, i.id desc
            limit @limit
            """);
        command.Parameters.Add(ModerationSql.Text("status", status));
        command.Parameters.AddWithValue("hidden", includeHidden);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 200));
        return await ModerationSql.ListAsync(command, Read, cancellationToken);
    }

    public async Task<Result<AdminIdea>> UpdateAsync(long id, IdeaChanges changes, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var answer = changes.StaffAnswer is null ? null : Text.Clean(changes.StaffAnswer, keepLines: true);
        if (answer is { Length: > 2000 })
        {
            return new DomainError("idea.answer", "An answer has at most 2000 characters.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        AdminIdea? before;
        await using (var command = Sql.Command(connection, tx, $"select {Columns} from ideas i left join players p on p.id = i.author_id where i.id = @id for update of i"))
        {
            command.Parameters.AddWithValue("id", id);
            before = (await ModerationSql.ListAsync(command, Read, cancellationToken)).FirstOrDefault();
        }

        if (before is null)
        {
            return IdeaErrors.NotFound;
        }

        await tx.ExecuteAsync(
            """
            update ideas set
              status = coalesce(@status, status),
              staff_answer = case when @answer::text is null then staff_answer when @answer = '' then null else @answer end,
              answered_at = case when @answer::text is null then answered_at when @answer = '' then null else @now end,
              hidden_at = case when @hidden::boolean is null then hidden_at when @hidden then coalesce(hidden_at, @now) else null end,
              hidden_reason = case when @hidden::boolean is null then hidden_reason when @hidden then @reason else null end
            where id = @id
            """,
            cancellationToken,
            ("id", id),
            ("status", changes.Status?.Name()),
            ("answer", answer),
            ("hidden", changes.Hidden),
            ("reason", changes.HiddenReason is null ? null : Text.Clean(changes.HiddenReason)),
            ("now", Sql.Utc(now)));
        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(
                actor,
                AuditActions.IdeaUpdated,
                AuditTargets.Idea,
                ModerationSql.Id(id),
                Before: new { status = before.Status, staffAnswer = before.StaffAnswer, hidden = before.Hidden },
                After: new { status = changes.Status?.Name() ?? before.Status, staffAnswer = answer ?? before.StaffAnswer, hidden = changes.Hidden ?? before.Hidden },
                Reason: changes.HiddenReason),
            now,
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Result.Success(before with
        {
            Status = changes.Status?.Name() ?? before.Status,
            StaffAnswer = answer is null ? before.StaffAnswer : answer.Length == 0 ? null : answer,
            Hidden = changes.Hidden ?? before.Hidden,
        });
    }

    public async Task<Result> MergeAsync(long duplicateId, long intoId, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var facts = new Dictionary<long, IdeaFacts>();
        await using (var r = await tx.ReaderAsync(
                         "select id, author_id, status, hidden_at is not null, merged_into_id from ideas where id in (@a, @b) order by id for update",
                         cancellationToken,
                         ("a", duplicateId),
                         ("b", intoId)))
        {
            while (await r.ReadAsync(cancellationToken))
            {
                facts[r.GetInt64(0)] = new IdeaFacts(r.GetInt64(0), new PlayerId(r.GetGuid(1)), IdeaRules.ParseStatus(r.GetString(2)) ?? IdeaStatus.New, r.GetBoolean(3), r.IsDBNull(4) ? null : r.GetInt64(4));
            }
        }

        if (!facts.TryGetValue(duplicateId, out var duplicate) || !facts.TryGetValue(intoId, out var main))
        {
            return duplicateId == intoId ? IdeaErrors.MergeSelf : IdeaErrors.NotFound;
        }

        var allowed = IdeaRules.CanMerge(duplicate, main);
        if (allowed.IsFailure)
        {
            return allowed.Error!;
        }

        // Votes are united (IdeaRules.MergedVotes): the duplicate's voters back the main idea,
        // except its author; the duplicate keeps its own votes for the record.
        await tx.ExecuteAsync(
            """
            insert into idea_votes (idea_id, player_id, created_at)
            select @into, v.player_id, v.created_at from idea_votes v
            where v.idea_id = @dup and v.player_id <> @author
            on conflict do nothing;
            update ideas set votes_count = (select count(*) from idea_votes where idea_id = @into) where id = @into;
            update ideas set merged_into_id = @into where id = @dup;
            """,
            cancellationToken,
            ("into", intoId),
            ("dup", duplicateId),
            ("author", main.Author.Value));
        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(actor, AuditActions.IdeaMerged, AuditTargets.Idea, ModerationSql.Id(duplicateId), After: new { into = intoId }),
            now,
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private static AdminIdea Read(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetGuid(6),
        r.IsDBNull(7) ? null : r.GetString(7), r.GetInt32(8), r.IsDBNull(9) ? null : r.GetString(9), r.GetBoolean(10),
        r.IsDBNull(11) ? null : r.GetString(11), r.IsDBNull(12) ? null : r.GetInt64(12), r.GetFieldValue<DateTimeOffset>(13));
}

/// <summary>Forgets the cached ban and session of a player after a sanction made outside the sanction store.</summary>
internal sealed class SanctionCacheInvalidation(Staff.SessionVersionCache sessions, BanCache bans)
{
    public void Forget(PlayerId player)
    {
        sessions.Forget(player);
        bans.Forget(player);
    }
}
