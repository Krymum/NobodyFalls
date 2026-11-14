using System.Collections.Concurrent;
using NobodyFalls.Application.Audit;
using NobodyFalls.Application.Moderation;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Moderation;
using NobodyFalls.Infrastructure.Audit;
using NobodyFalls.Infrastructure.Persistence;
using NobodyFalls.Infrastructure.Staff;
using Npgsql;

namespace NobodyFalls.Infrastructure.Moderation;

/// <summary>Writes a notification to the outbox inside the caller's transaction (ADR 0009).</summary>
internal static class OutboxWriter
{
    public static Task EnqueueAsync(NpgsqlTransaction tx, string dedupeKey, PlayerId player, string kind, DateTimeOffset now, CancellationToken cancellationToken) =>
        tx.ExecuteAsync(
            """
            insert into notification_outbox (dedupe_key, player_id, kind, payload, created_at)
            values (@key, @player, @kind, jsonb_build_object('day', @day, 'players', '[]'::jsonb), @now)
            on conflict (dedupe_key) do nothing
            """,
            cancellationToken,
            ("key", dedupeKey),
            ("player", player.Value),
            ("kind", kind),
            ("day", GameDay.Of(now).Date),
            ("now", Sql.Utc(now)));
}

/// <summary>
/// Issues a sanction inside a transaction and applies its effects (GAME_DESIGN §9, ADR 0021):
/// a ban takes the player out of today's play (their day row is removed, under the night
/// lock) and ends their sessions; a score exclusion and a ban are copied onto the player
/// row (until when); a pseudo reset gives a neutral pseudo to change on the next visit.
/// </summary>
internal static class SanctionWriter
{
    public static async Task<Result<long>> IssueAsync(
        NpgsqlTransaction tx,
        PlayerId target,
        SanctionDraft draft,
        long? reportId,
        AuditActor actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await tx.LockNightSharedAsync(cancellationToken);
        string? pseudo = null;
        bool isStaff;
        await using (var r = await tx.ReaderAsync(
                         """
                         select p.pseudo, exists (select 1 from staff_members m where m.player_id = p.id)
                         from players p where p.id = @id and p.kind = 'human' and p.deleted_at is null for update
                         """,
                         cancellationToken,
                         ("id", target.Value)))
        {
            if (!await r.ReadAsync(cancellationToken))
            {
                return SanctionErrors.PlayerNotFound;
            }

            pseudo = r.GetString(0);
            isStaff = r.GetBoolean(1);
        }

        var allowed = SanctionRules.CanSanction(actor.Id ?? default, target, isStaff);
        if (allowed.IsFailure)
        {
            return allowed.Error!;
        }

        var id = await tx.ScalarAsync<long>(
            """
            insert into sanctions (player_id, kind, reason, public_message, starts_at, ends_at, issued_by, report_id)
            values (@player, @kind, @reason, @message, @now, @ends, @by, @report)
            returning id
            """,
            cancellationToken,
            ("player", target.Value),
            ("kind", draft.Kind.Name()),
            ("reason", draft.Reason),
            ("message", draft.PublicMessage),
            ("now", Sql.Utc(now)),
            ("ends", draft.EndsAt is { } end ? Sql.Utc(end) : null),
            ("by", actor.Id?.Value),
            ("report", reportId));

        string? newPseudo = null;
        switch (draft.Kind)
        {
            case SanctionKind.PseudoReset:
                newPseudo = await ResetPseudoAsync(tx, target, cancellationToken);
                break;
            case SanctionKind.ScoreExclusion:
                await tx.ExecuteAsync(
                    "update players set score_excluded_until = greatest(coalesce(score_excluded_until, @until), @until) where id = @id",
                    cancellationToken,
                    ("id", target.Value),
                    ("until", Sql.Utc(SanctionRules.EffectUntil(draft))));
                break;
            case SanctionKind.Ban:
                await tx.ExecuteAsync(
                    """
                    update players set banned_until = greatest(coalesce(banned_until, @until), @until),
                                       session_version = session_version + 1
                    where id = @id;
                    delete from player_days where day = @day and player_id = @id;
                    """,
                    cancellationToken,
                    ("id", target.Value),
                    ("until", Sql.Utc(SanctionRules.EffectUntil(draft))),
                    ("day", GameDay.Of(now).Date));
                break;
            case SanctionKind.Warning:
            default:
                break;
        }

        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(
                actor,
                AuditActions.SanctionIssued,
                AuditTargets.Player,
                target.ToString(),
                Before: newPseudo is null ? null : new { pseudo },
                After: new
                {
                    sanction = id,
                    kind = draft.Kind.Name(),
                    endsAt = draft.EndsAt,
                    publicMessage = draft.PublicMessage,
                    report = reportId,
                    pseudo = newPseudo,
                },
                Reason: draft.Reason),
            now,
            cancellationToken);
        return Result.Success(id);
    }

    private static async Task<string> ResetPseudoAsync(NpgsqlTransaction tx, PlayerId target, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var candidate = Pseudo.FromTrusted(SanctionRules.NeutralPseudo());
            var taken = await tx.ScalarAsync<bool>(
                "select exists (select 1 from players where pseudo_key = @key)", cancellationToken, ("key", candidate.Key));
            if (!taken || attempt > 20)
            {
                await tx.ExecuteAsync(
                    "update players set pseudo = @pseudo, pseudo_key = @key, pseudo_reset_pending = true where id = @id",
                    cancellationToken,
                    ("id", target.Value),
                    ("pseudo", candidate.Value),
                    ("key", candidate.Key));
                return candidate.Value;
            }
        }
    }

    /// <summary>After a revocation, the player's "until" columns follow the sanctions still active.</summary>
    public static Task RecomputeAsync(NpgsqlTransaction tx, PlayerId player, DateTimeOffset now, CancellationToken cancellationToken) =>
        tx.ExecuteAsync(
            """
            update players p set
              banned_until = (select max(coalesce(s.ends_at, @forever)) from sanctions s
                              where s.player_id = p.id and s.kind = 'ban' and s.revoked_at is null
                                and (s.ends_at is null or s.ends_at > @now)),
              score_excluded_until = (select max(coalesce(s.ends_at, @forever)) from sanctions s
                                      where s.player_id = p.id and s.kind = 'score_exclusion' and s.revoked_at is null
                                        and (s.ends_at is null or s.ends_at > @now))
            where p.id = @id
            """,
            cancellationToken,
            ("id", player.Value),
            ("now", Sql.Utc(now)),
            ("forever", Sql.Utc(SanctionRules.Forever)));
}

internal sealed class PostgresSanctionStore(NpgsqlDataSource dataSource, SessionVersionCache sessions, BanCache bans) : ISanctionStore, IPlayerNotices
{
    public async Task<Result<long>> IssueAsync(PlayerId target, SanctionDraft draft, long? reportId, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var result = await SanctionWriter.IssueAsync(tx, target, draft, reportId, actor, now, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        await tx.CommitAsync(cancellationToken);
        Forget(target);
        return result;
    }

    public async Task<Result> RevokeAsync(long sanctionId, string? reason, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        Guid player;
        string kind;
        await using (var r = await tx.ReaderAsync(
                         "select player_id, kind, revoked_at is not null from sanctions where id = @id for update",
                         cancellationToken,
                         ("id", sanctionId)))
        {
            if (!await r.ReadAsync(cancellationToken))
            {
                return SanctionErrors.NotFound;
            }

            if (r.GetBoolean(2))
            {
                return SanctionErrors.AlreadyRevoked;
            }

            player = r.GetGuid(0);
            kind = r.GetString(1);
        }

        var cleanReason = NobodyFalls.Domain.Feedback.Text.Clean(reason, keepLines: true);
        await tx.ExecuteAsync(
            "update sanctions set revoked_at = @now, revoked_by = @by, revoke_reason = @reason where id = @id",
            cancellationToken,
            ("id", sanctionId),
            ("now", Sql.Utc(now)),
            ("by", actor.Id?.Value),
            ("reason", cleanReason.Length == 0 ? null : cleanReason[..Math.Min(500, cleanReason.Length)]));
        await SanctionWriter.RecomputeAsync(tx, new PlayerId(player), now, cancellationToken);
        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(actor, AuditActions.SanctionRevoked, AuditTargets.Sanction, sanctionId.ToString(System.Globalization.CultureInfo.InvariantCulture), After: new { player, kind }, Reason: cleanReason),
            now,
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        Forget(new PlayerId(player));
        return Result.Success();
    }

    public async Task<PlayerId?> IssuerAsync(long sanctionId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select issued_by from sanctions where id = @id");
        command.Parameters.AddWithValue("id", sanctionId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? new PlayerId(id) : null;
    }

    public async Task<PlayerNotices> NoticesAsync(PlayerId player, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var warnings = new List<WarningNotice>();
        BanNotice? ban = null;
        await using (var command = new NpgsqlCommand(
                         """
                         select id, kind, public_message, starts_at, ends_at from sanctions
                         where player_id = @id and revoked_at is null
                           and ((kind = 'warning' and acknowledged_at is null)
                                or (kind = 'ban' and (ends_at is null or ends_at > @now)))
                         order by starts_at
                         """,
                         connection))
        {
            command.Parameters.AddWithValue("id", player.Value);
            command.Parameters.AddWithValue("now", Sql.Utc(now));
            await using var r = await command.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var at = r.GetFieldValue<DateTimeOffset>(3);
                if (r.GetString(1) == "warning")
                {
                    warnings.Add(new WarningNotice(r.GetInt64(0), r.IsDBNull(2) ? "" : r.GetString(2), at));
                }
                else
                {
                    DateTimeOffset? until = r.IsDBNull(4) ? null : r.GetFieldValue<DateTimeOffset>(4);
                    // The ban that lasts longest is the one the player sees.
                    if (ban is null || until is null || (ban.Until is { } current && until > current))
                    {
                        ban = new BanNotice(r.GetInt64(0), r.IsDBNull(2) ? null : r.GetString(2), until);
                    }
                }
            }
        }

        await using var pending = new NpgsqlCommand("select pseudo_reset_pending from players where id = @id", connection);
        pending.Parameters.AddWithValue("id", player.Value);
        var resetPending = await pending.ExecuteScalarAsync(cancellationToken) is true;
        return new PlayerNotices(warnings, ban, resetPending);
    }

    public async Task<Result> AcknowledgeAsync(PlayerId player, long sanctionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "update sanctions set acknowledged_at = @now where id = @id and player_id = @player and kind = 'warning' and acknowledged_at is null");
        command.Parameters.AddWithValue("id", sanctionId);
        command.Parameters.AddWithValue("player", player.Value);
        command.Parameters.AddWithValue("now", Sql.Utc(now));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? Result.Success() : SanctionErrors.NotFound;
    }

    public async Task<Result> ChoosePseudoAsync(PlayerId player, Pseudo pseudo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pseudo);
        await using var command = dataSource.CreateCommand(
            "update players set pseudo = @pseudo, pseudo_key = @key, pseudo_reset_pending = false where id = @id and pseudo_reset_pending");
        command.Parameters.AddWithValue("id", player.Value);
        command.Parameters.AddWithValue("pseudo", pseudo.Value);
        command.Parameters.AddWithValue("key", pseudo.Key);
        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1
                ? Result.Success()
                : new DomainError("pseudo.no_reset", "Your pseudo can only be changed after a reset by the team.");
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return PseudoErrors.Taken;
        }
    }

    public Task<DateTimeOffset?> BannedUntilAsync(PlayerId player, DateTimeOffset now, CancellationToken cancellationToken) =>
        bans.GetAsync(player, now, async ct =>
        {
            await using var command = dataSource.CreateCommand("select banned_until from players where id = @id");
            command.Parameters.AddWithValue("id", player.Value);
            return await command.ExecuteScalarAsync(ct) is DateTime value
                ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
                : (DateTimeOffset?)null;
        }, cancellationToken);

    private void Forget(PlayerId player)
    {
        sessions.Forget(player);
        bans.Forget(player);
    }
}

/// <summary>Whether a player is banned is read on every game request: a short cache spares the database.</summary>
internal sealed class BanCache(TimeProvider time)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);
    private readonly ConcurrentDictionary<PlayerId, (DateTimeOffset? Until, DateTimeOffset Expires)> _entries = new();

    public async Task<DateTimeOffset?> GetAsync(PlayerId player, DateTimeOffset now, Func<CancellationToken, Task<DateTimeOffset?>> load, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);
        var stamp = time.GetUtcNow();
        if (!_entries.TryGetValue(player, out var entry) || entry.Expires <= stamp)
        {
            entry = (await load(cancellationToken), stamp + Lifetime);
            _entries[player] = entry;
        }

        return entry.Until is { } until && until > now ? until : null;
    }

    public void Forget(PlayerId player) => _entries.TryRemove(player, out _);
}
