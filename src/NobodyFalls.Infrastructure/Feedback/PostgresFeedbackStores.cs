using System.Text.Json;
using NobodyFalls.Application.Feedback;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Feedback;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace NobodyFalls.Infrastructure.Feedback;

/// <summary>Helpers shared by the feedback stores.</summary>
internal static class FeedbackSql
{
    /// <summary>
    /// Serializes one player's writes of one kind, so that daily limits hold even when
    /// requests race (check then insert, under the lock).
    /// </summary>
    public static Task LockPlayerAsync(this NpgsqlTransaction tx, string kind, PlayerId player, CancellationToken cancellationToken) =>
        tx.ExecuteAsync("select pg_advisory_xact_lock(hashtextextended(@key, 0))", cancellationToken, ("key", $"{kind}:{player}"));

    public static async Task<int> CountSinceAsync(this NpgsqlTransaction tx, string table, string column, PlayerId player, DateTimeOffset since, CancellationToken cancellationToken) =>
        (int)await tx.ScalarAsync<long>(
            $"select count(*) from {table} where {column} = @player and created_at >= @since",
            cancellationToken,
            ("player", player.Value),
            ("since", Sql.Utc(since)));
}

internal sealed class PostgresIdeaStore(NpgsqlDataSource dataSource) : IIdeaStore
{
    private const string Columns =
        """
        i.id, i.title, i.body, i.category, i.status, i.locale, p.pseudo, i.author_id = @viewer,
        i.hidden_at is not null, i.votes_count,
        exists (select 1 from idea_votes v where v.idea_id = i.id and v.player_id = @viewer),
        i.staff_answer, i.answered_at, i.merged_into_id, i.created_at
        """;

    public async Task<Result<long>> ProposeAsync(PlayerId author, IdeaDraft draft, string locale, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await tx.LockPlayerAsync("idea", author, cancellationToken);
        var today = await tx.CountSinceAsync("ideas", "author_id", author, GameDay.Of(now).StartsAt, cancellationToken);
        var allowed = IdeaRules.CanPropose(today);
        if (allowed.IsFailure)
        {
            return allowed.Error!;
        }

        var id = await tx.ScalarAsync<long>(
            """
            insert into ideas (author_id, title, body, category, locale, created_at)
            values (@author, @title, @body, @category, @locale, @now)
            returning id
            """,
            cancellationToken,
            ("author", author.Value),
            ("title", draft.Title),
            ("body", draft.Body),
            ("category", draft.Category.Name()),
            ("locale", locale),
            ("now", Sql.Utc(now)));
        await tx.CommitAsync(cancellationToken);
        return Result.Success(id);
    }

    public async Task<IdeaPage> ListAsync(PlayerId viewer, string viewerLocale, IdeaStatus? status, IdeaSort sort, int offset, int limit, CancellationToken cancellationToken)
    {
        var order = sort == IdeaSort.Votes ? "i.votes_count desc, i.created_at desc" : "i.created_at desc";
        await using var command = dataSource.CreateCommand(
            $"""
            select {Columns}
            from ideas i left join players p on p.id = i.author_id and p.deleted_at is null
            where i.merged_into_id is null
              and (i.hidden_at is null or i.author_id = @viewer)
              and (@status is null or i.status = @status)
            order by (i.locale = @locale) desc, {order}, i.id desc
            offset @offset limit @limit
            """);
        command.Parameters.AddWithValue("viewer", viewer.Value);
        command.Parameters.AddWithValue("locale", viewerLocale);
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Text) { Value = (object?)status?.Name() ?? DBNull.Value });
        command.Parameters.AddWithValue("offset", offset);
        command.Parameters.AddWithValue("limit", limit + 1);
        var items = await ReadAllAsync(command, cancellationToken);
        var more = items.Count > limit;
        return new IdeaPage(more ? items[..limit] : items, more ? offset + limit : null);
    }

    public async Task<IdeaView?> GetAsync(PlayerId viewer, long id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            select {Columns}
            from ideas i left join players p on p.id = i.author_id and p.deleted_at is null
            where i.id = @id and (i.hidden_at is null or i.author_id = @viewer)
            """);
        command.Parameters.AddWithValue("viewer", viewer.Value);
        command.Parameters.AddWithValue("id", id);
        return (await ReadAllAsync(command, cancellationToken)).FirstOrDefault();
    }

    public async Task<Result<VoteResult>> VoteAsync(PlayerId voter, long ideaId, bool vote, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        // The row lock serializes votes on one idea, so the count stays exact.
        IdeaFacts? facts = null;
        await using (var r = await tx.ReaderAsync(
                         "select id, author_id, status, hidden_at is not null, merged_into_id from ideas where id = @id for update",
                         cancellationToken,
                         ("id", ideaId)))
        {
            if (await r.ReadAsync(cancellationToken))
            {
                facts = new IdeaFacts(
                    r.GetInt64(0),
                    new PlayerId(r.GetGuid(1)),
                    IdeaRules.ParseStatus(r.GetString(2)) ?? IdeaStatus.New,
                    r.GetBoolean(3),
                    r.IsDBNull(4) ? null : r.GetInt64(4));
            }
        }

        if (facts is null || (facts.Hidden && !vote))
        {
            return IdeaErrors.NotFound;
        }

        if (vote)
        {
            var allowed = IdeaRules.CanVote(facts, voter);
            if (allowed.IsFailure)
            {
                return allowed.Error!;
            }

            await tx.ExecuteAsync(
                "insert into idea_votes (idea_id, player_id, created_at) values (@id, @player, now()) on conflict do nothing",
                cancellationToken,
                ("id", ideaId),
                ("player", voter.Value));
        }
        else
        {
            // Taking a vote back is always possible, even on a closed idea.
            await tx.ExecuteAsync(
                "delete from idea_votes where idea_id = @id and player_id = @player",
                cancellationToken,
                ("id", ideaId),
                ("player", voter.Value));
        }

        var count = await tx.ScalarAsync<int>(
            "update ideas set votes_count = (select count(*) from idea_votes where idea_id = @id) where id = @id returning votes_count",
            cancellationToken,
            ("id", ideaId));
        await tx.CommitAsync(cancellationToken);
        return Result.Success(new VoteResult(ideaId, count, vote));
    }

    private static async Task<List<IdeaView>> ReadAllAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<IdeaView>();
        while (await r.ReadAsync(cancellationToken))
        {
            items.Add(new IdeaView(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.GetBoolean(7),
                r.GetBoolean(8),
                r.GetInt32(9),
                r.GetBoolean(10),
                r.IsDBNull(11) ? null : r.GetString(11),
                r.IsDBNull(12) ? null : r.GetFieldValue<DateTimeOffset>(12),
                r.IsDBNull(13) ? null : r.GetInt64(13),
                r.GetFieldValue<DateTimeOffset>(14)));
        }

        return items;
    }
}

internal sealed class PostgresTicketStore(NpgsqlDataSource dataSource) : ITicketStore
{
    public async Task<Result<long>> OpenAsync(PlayerId player, TicketDraft draft, string locale, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await tx.LockPlayerAsync("ticket", player, cancellationToken);
        var today = await tx.CountSinceAsync("tickets", "player_id", player, GameDay.Of(now).StartsAt, cancellationToken);
        var allowed = TicketRules.CanOpen(today);
        if (allowed.IsFailure)
        {
            return allowed.Error!;
        }

        var id = await tx.ScalarAsync<long>(
            """
            insert into tickets (player_id, category, subject, locale, created_at, updated_at)
            values (@player, @category, @subject, @locale, @now, @now)
            returning id
            """,
            cancellationToken,
            ("player", player.Value),
            ("category", draft.Category.Name()),
            ("subject", draft.Subject),
            ("locale", locale),
            ("now", Sql.Utc(now)));
        await InsertMessageAsync(tx, id, player, draft.Message, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Result.Success(id);
    }

    public async Task<Result> ReplyAsync(PlayerId player, long ticketId, string message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var status = await tx.ScalarAsync<string>(
            "select status from tickets where id = @id and player_id = @player for update",
            cancellationToken,
            ("id", ticketId),
            ("player", player.Value));
        if (status is null || TicketRules.ParseStatus(status) is not { } current)
        {
            return TicketErrors.NotFound;
        }

        var next = TicketRules.AfterPlayerReply(current);
        if (next.IsFailure)
        {
            return next.Error!;
        }

        await InsertMessageAsync(tx, ticketId, player, message, now, cancellationToken);
        await tx.ExecuteAsync(
            "update tickets set status = @status, updated_at = @now where id = @id",
            cancellationToken,
            ("id", ticketId),
            ("status", next.Value.Name()),
            ("now", Sql.Utc(now)));
        await tx.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyList<TicketSummary>> MineAsync(PlayerId player, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            select id, category, subject, status, updated_at from tickets
            where player_id = @player order by updated_at desc, id desc limit 100
            """);
        command.Parameters.AddWithValue("player", player.Value);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var tickets = new List<TicketSummary>();
        while (await r.ReadAsync(cancellationToken))
        {
            var status = r.GetString(3);
            tickets.Add(new TicketSummary(r.GetInt64(0), r.GetString(1), r.GetString(2), status, r.GetFieldValue<DateTimeOffset>(4), status == TicketStatus.WaitingPlayer.Name()));
        }

        return tickets;
    }

    public async Task<TicketView?> MineAsync(PlayerId player, long ticketId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        TicketView? ticket = null;
        await using (var command = new NpgsqlCommand(
                         "select id, category, subject, status, created_at from tickets where id = @id and player_id = @player",
                         connection))
        {
            command.Parameters.AddWithValue("id", ticketId);
            command.Parameters.AddWithValue("player", player.Value);
            await using var r = await command.ExecuteReaderAsync(cancellationToken);
            if (await r.ReadAsync(cancellationToken))
            {
                ticket = new TicketView(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetFieldValue<DateTimeOffset>(4), []);
            }
        }

        if (ticket is null)
        {
            return null;
        }

        // Team notes (internal) never leave the team; staff authors are shown as "the team".
        await using var messages = new NpgsqlCommand(
            "select id, author_kind, body, created_at from ticket_messages where ticket_id = @id and not internal order by id",
            connection);
        messages.Parameters.AddWithValue("id", ticketId);
        await using var m = await messages.ExecuteReaderAsync(cancellationToken);
        var list = new List<TicketMessageView>();
        while (await m.ReadAsync(cancellationToken))
        {
            list.Add(new TicketMessageView(m.GetInt64(0), m.GetString(1) == "staff" ? "team" : "player", m.GetString(2), m.GetFieldValue<DateTimeOffset>(3)));
        }

        return ticket with { Messages = list };
    }

    private static async Task InsertMessageAsync(NpgsqlTransaction tx, long ticketId, PlayerId player, string body, DateTimeOffset now, CancellationToken cancellationToken) =>
        await tx.ExecuteAsync(
            "insert into ticket_messages (ticket_id, author_kind, author_id, body, internal, created_at) values (@ticket, 'player', @player, @body, false, @now)",
            cancellationToken,
            ("ticket", ticketId),
            ("player", player.Value),
            ("body", body),
            ("now", Sql.Utc(now)));
}

internal sealed class PostgresReportStore(NpgsqlDataSource dataSource) : IReportStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<long>> ReportAsync(PlayerId reporter, ReportRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await tx.LockPlayerAsync("report", reporter, cancellationToken);

        var target = request.Target == ReportTarget.Player
            ? await FindPlayerAsync(tx, request, cancellationToken)
            : await FindIdeaAsync(tx, request, cancellationToken);
        if (target is null)
        {
            return ReportErrors.TargetNotFound;
        }

        var (targetId, targetPlayer, snapshot) = target.Value;
        var alreadyOpen = await tx.ScalarAsync<bool>(
            """
            select exists (select 1 from reports where reporter_id = @reporter and target_type = @type
                           and target_id = @target and status in ('open', 'in_review'))
            """,
            cancellationToken,
            ("reporter", reporter.Value),
            ("type", request.Target.Name()),
            ("target", targetId));
        var today = await tx.CountSinceAsync("reports", "reporter_id", reporter, GameDay.Of(now).StartsAt, cancellationToken);
        var allowed = ReportRules.CanReport(reporter, targetPlayer, today, alreadyOpen);
        if (allowed.IsFailure)
        {
            return allowed.Error!;
        }

        var id = await tx.ScalarAsync<long>(
            """
            insert into reports (reporter_id, target_type, target_id, target_player_id, category, details, target_snapshot, created_at)
            values (@reporter, @type, @target, @target_player, @category, @details, @snapshot::jsonb, @now)
            returning id
            """,
            cancellationToken,
            ("reporter", reporter.Value),
            ("type", request.Target.Name()),
            ("target", targetId),
            ("target_player", targetPlayer?.Value),
            ("category", request.Draft.Category.Name()),
            ("details", request.Draft.Details),
            ("snapshot", JsonSerializer.Serialize(snapshot, Json)),
            ("now", Sql.Utc(now)));
        await tx.CommitAsync(cancellationToken);
        return Result.Success(id);
    }

    private static async Task<(string Id, PlayerId? Player, object Snapshot)?> FindPlayerAsync(NpgsqlTransaction tx, ReportRequest request, CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(request.Pseudo) ? null : Pseudo.FromTrusted(request.Pseudo.Trim()).Key;
        await using var command = Sql.Command(
            tx.Connection!,
            tx,
            """
            select id, pseudo, status from players
            where (id = @id or pseudo_key = @key) and kind = 'human' and deleted_at is null
            limit 1
            """);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = (object?)request.PlayerId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Text) { Value = (object?)key ?? DBNull.Value });
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
        {
            return null;
        }

        var id = r.GetGuid(0);
        return (id.ToString(), new PlayerId(id), new { pseudo = r.GetString(1), status = r.GetString(2) });
    }

    private static async Task<(string Id, PlayerId? Player, object Snapshot)?> FindIdeaAsync(NpgsqlTransaction tx, ReportRequest request, CancellationToken cancellationToken)
    {
        if (request.IdeaId is not { } ideaId)
        {
            return null;
        }

        await using var r = await tx.ReaderAsync(
            """
            select i.id, i.author_id, i.title, p.pseudo from ideas i join players p on p.id = i.author_id
            where i.id = @id and i.hidden_at is null
            """,
            cancellationToken,
            ("id", ideaId));
        if (!await r.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (r.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture), new PlayerId(r.GetGuid(1)), new { title = r.GetString(2), author = r.GetString(3) });
    }
}
