using System.Text.Json;
using NobodyFalls.Application.Audit;
using NobodyFalls.Domain.Staff;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace NobodyFalls.Infrastructure.Audit;

/// <summary>
/// Appends to the audit log inside the caller's transaction, so that an action and its
/// entry commit or roll back together (ADR 0019). Appends are serialized by an advisory
/// lock so that each entry chains to the one before it.
/// </summary>
internal static class AuditWriter
{
    /// <summary>"NFAUDIT" + 1.</summary>
    public const long ChainLock = 0x4E46_4155_4449_5401;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task AppendAsync(NpgsqlTransaction tx, AuditRecord record, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await tx.ExecuteAsync("select pg_advisory_xact_lock(@key)", cancellationToken, ("key", ChainLock));

        await using var command = Sql.Command(
            tx.Connection!,
            tx,
            """
            with prev as (select (select hash from audit_log order by id desc limit 1) as hash)
            insert into audit_log (at, actor_id, actor_roles, action, target_type, target_id, before, after,
                                   reason, ip, request_id, prev_hash, hash)
            select @at, @actor, @roles, @action, @target_type, @target_id, @before, @after,
                   @reason, @ip, @request_id, prev.hash,
                   audit_digest(prev.hash, @at, @actor, @roles, @action, @target_type, @target_id,
                                @before, @after, @reason, @ip, @request_id)
            from prev
            """);
        command.Parameters.Add(new NpgsqlParameter("at", NpgsqlDbType.TimestampTz) { Value = Sql.Utc(at) });
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = (object?)record.Actor.Id?.Value ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("roles", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = record.Actor.Roles.Select(r => r.Name()).ToArray() });
        command.Parameters.Add(Text("action", record.Action));
        command.Parameters.Add(Text("target_type", record.TargetType));
        command.Parameters.Add(Text("target_id", record.TargetId));
        command.Parameters.Add(JsonB("before", record.Before));
        command.Parameters.Add(JsonB("after", record.After));
        command.Parameters.Add(Text("reason", record.Reason));
        command.Parameters.Add(Text("ip", record.Actor.Ip));
        command.Parameters.Add(Text("request_id", record.Actor.RequestId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlParameter Text(string name, string? value) =>
        new(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter JsonB(string name, object? value) =>
        new(name, NpgsqlDbType.Jsonb) { Value = value is null ? DBNull.Value : JsonSerializer.Serialize(value, value.GetType(), Json) };
}

internal sealed class PostgresAuditLog(NpgsqlDataSource dataSource, TimeProvider time) : IAuditLog
{
    private const string DigestColumns =
        "at, actor_id, actor_roles, action, target_type, target_id, before, after, reason, ip, request_id";

    public async Task AppendAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await AuditWriter.AppendAsync(tx, record, time.GetUtcNow(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<AuditPage> ListAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var limit = Math.Clamp(query.Limit, 1, 200);
        await using var command = dataSource.CreateCommand(
            """
            select a.id, a.at, a.actor_id, p.pseudo, a.actor_roles, a.action, a.target_type, a.target_id,
                   a.before::text, a.after::text, a.reason, a.ip
            from audit_log a
            left join players p on p.id = a.actor_id
            where (@actor is null or a.actor_id = @actor)
              and (@action is null or a.action = @action or starts_with(a.action, @action || '.'))
              and (@target_type is null or a.target_type = @target_type)
              and (@target_id is null or a.target_id = @target_id)
              and (@from is null or a.at >= @from)
              and (@to is null or a.at < @to)
              and (@before_id is null or a.id < @before_id)
            order by a.id desc
            limit @limit
            """);
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = (object?)query.ActorId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("action", NpgsqlDbType.Text) { Value = (object?)query.Action ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("target_type", NpgsqlDbType.Text) { Value = (object?)query.TargetType ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("target_id", NpgsqlDbType.Text) { Value = (object?)query.TargetId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = query.From is { } from ? Sql.Utc(from) : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = query.To is { } to ? Sql.Utc(to) : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("before_id", NpgsqlDbType.Bigint) { Value = (object?)query.BeforeId ?? DBNull.Value });
        command.Parameters.AddWithValue("limit", limit + 1);

        var entries = new List<AuditEntry>();
        await using (var r = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await r.ReadAsync(cancellationToken))
            {
                entries.Add(new AuditEntry(
                    r.GetInt64(0),
                    r.GetFieldValue<DateTimeOffset>(1),
                    r.IsDBNull(2) ? null : r.GetGuid(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.GetFieldValue<string[]>(4),
                    r.GetString(5),
                    r.GetString(6),
                    r.IsDBNull(7) ? null : r.GetString(7),
                    r.IsDBNull(8) ? null : JsonDocument.Parse(r.GetString(8)).RootElement.Clone(),
                    r.IsDBNull(9) ? null : JsonDocument.Parse(r.GetString(9)).RootElement.Clone(),
                    r.IsDBNull(10) ? null : r.GetString(10),
                    r.IsDBNull(11) ? null : r.GetString(11)));
            }
        }

        var hasMore = entries.Count > limit;
        if (hasMore)
        {
            entries.RemoveAt(entries.Count - 1);
        }

        return new AuditPage(entries, hasMore ? entries[^1].Id : null);
    }

    /// <summary>Recomputes every digest in order; the first entry that does not match is reported.</summary>
    public async Task<AuditVerification> VerifyAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            select count(*), min(id) filter (where not ok)
            from (
                select id,
                       prev_hash is not distinct from lag(hash) over w
                       and hash = audit_digest(lag(hash) over w, {DigestColumns}) as ok
                from audit_log
                window w as (order by id)
            ) chain
            """);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);
        var broken = r.IsDBNull(1) ? (long?)null : r.GetInt64(1);
        return new AuditVerification(broken is null, r.GetInt64(0), broken);
    }
}
