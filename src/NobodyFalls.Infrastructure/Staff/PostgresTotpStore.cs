using Microsoft.AspNetCore.DataProtection;
using NobodyFalls.Application.Audit;
using NobodyFalls.Application.Staff;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Security;
using NobodyFalls.Infrastructure.Audit;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Staff;

internal sealed class PostgresTotpStore(NpgsqlDataSource dataSource, TimeProvider time, StaffAccessCache staffCache, SessionVersionCache sessions) : ITotpStore
{
    public async Task<TotpCredential?> GetAsync(PlayerId player, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "select secret, confirmed_at is not null, last_step, failed_attempts, locked_until from totp_credentials where player_id = @id");
        command.Parameters.AddWithValue("id", player.Value);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TotpCredential(
            r.GetFieldValue<byte[]>(0),
            r.GetBoolean(1),
            r.IsDBNull(2) ? null : r.GetInt64(2),
            r.GetInt32(3),
            r.IsDBNull(4) ? null : r.GetFieldValue<DateTimeOffset>(4));
    }

    public async Task<bool> SavePendingAsync(PlayerId player, byte[] protectedSecret, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            insert into totp_credentials (player_id, secret, created_at) values (@id, @secret, @now)
            on conflict (player_id) do update set secret = excluded.secret, created_at = excluded.created_at, last_step = null
            where totp_credentials.confirmed_at is null
            """);
        command.Parameters.AddWithValue("id", player.Value);
        command.Parameters.AddWithValue("secret", protectedSecret);
        command.Parameters.AddWithValue("now", Sql.Utc(time.GetUtcNow()));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> ConfirmAsync(PlayerId player, long timeStep, IReadOnlyList<StoredRecoveryCode> recoveryCodes, AuditActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recoveryCodes);
        var now = time.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var confirmed = await tx.ExecuteAsync(
            """
            update totp_credentials set confirmed_at = @now, last_step = @step, failed_attempts = 0, locked_until = null
            where player_id = @id and confirmed_at is null and (last_step is null or last_step < @step)
            """,
            cancellationToken,
            ("id", player.Value),
            ("step", timeStep),
            ("now", Sql.Utc(now)));
        if (confirmed == 0)
        {
            return false;
        }

        await tx.ExecuteAsync("delete from totp_recovery_codes where player_id = @id", cancellationToken, ("id", player.Value));
        foreach (var code in recoveryCodes)
        {
            await tx.ExecuteAsync(
                "insert into totp_recovery_codes (player_id, salt, hash) values (@id, @salt, @hash)",
                cancellationToken,
                ("id", player.Value),
                ("salt", code.Salt),
                ("hash", code.Hash));
        }

        await AuditWriter.AppendAsync(tx, new AuditRecord(actor, AuditActions.TotpEnrolled, AuditTargets.Player, player.ToString()), now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        staffCache.Forget(player);
        return true;
    }

    public async Task<bool> TryUseStepAsync(PlayerId player, long timeStep, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            update totp_credentials set last_step = @step, failed_attempts = 0
            where player_id = @id and confirmed_at is not null and (last_step is null or last_step < @step)
            """);
        command.Parameters.AddWithValue("id", player.Value);
        command.Parameters.AddWithValue("step", timeStep);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<(int Attempts, bool Locked)> RecordFailureAsync(PlayerId player, int maxAttempts, DateTimeOffset lockedUntil, CancellationToken cancellationToken)
    {
        // Reaching the limit locks and starts counting again, for the next window.
        await using var command = dataSource.CreateCommand(
            """
            update totp_credentials
            set failed_attempts = case when failed_attempts + 1 >= @max then 0 else failed_attempts + 1 end,
                locked_until = case when failed_attempts + 1 >= @max then @until else locked_until end
            where player_id = @id
            returning case when failed_attempts = 0 then @max else failed_attempts end, failed_attempts = 0
            """);
        command.Parameters.AddWithValue("id", player.Value);
        command.Parameters.AddWithValue("max", maxAttempts);
        command.Parameters.AddWithValue("until", Sql.Utc(lockedUntil));
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? (r.GetInt32(0), r.GetBoolean(1)) : (0, false);
    }

    public async Task<IReadOnlyList<StoredRecovery>> UnusedRecoveryCodesAsync(PlayerId player, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "select id, salt, hash from totp_recovery_codes where player_id = @id and used_at is null order by id");
        command.Parameters.AddWithValue("id", player.Value);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var codes = new List<StoredRecovery>();
        while (await r.ReadAsync(cancellationToken))
        {
            codes.Add(new StoredRecovery(r.GetInt64(0), new StoredRecoveryCode(r.GetFieldValue<byte[]>(1), r.GetFieldValue<byte[]>(2))));
        }

        return codes;
    }

    public async Task<bool> UseRecoveryCodeAsync(long id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("update totp_recovery_codes set used_at = @now where id = @id and used_at is null");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("now", Sql.Utc(now));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<Result> ResetAsync(PlayerId target, AuditActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Id == target)
        {
            return new DomainError("staff.self", "Nobody resets their own authenticator.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var isMember = await tx.ScalarAsync<bool>(
            "select exists (select 1 from staff_members where player_id = @id)", cancellationToken, ("id", target.Value));
        if (!isMember)
        {
            return new DomainError("staff.not_found", "This person is not in the team.");
        }

        await tx.ExecuteAsync("delete from totp_recovery_codes where player_id = @id", cancellationToken, ("id", target.Value));
        await tx.ExecuteAsync("delete from totp_credentials where player_id = @id", cancellationToken, ("id", target.Value));
        await tx.ExecuteAsync("update players set session_version = session_version + 1 where id = @id", cancellationToken, ("id", target.Value));
        await AuditWriter.AppendAsync(tx, new AuditRecord(actor, AuditActions.StaffTotpReset, AuditTargets.Player, target.ToString()), time.GetUtcNow(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        staffCache.Forget(target);
        sessions.Forget(target);
        return Result.Success();
    }
}

/// <summary>TOTP secrets are encrypted at rest with the shared Data Protection keys.</summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("NobodyFalls.Staff.Totp.v1");

    public byte[] Protect(byte[] secret) => _protector.Protect(secret);

    public byte[] Unprotect(byte[] protectedSecret) => _protector.Unprotect(protectedSecret);
}

/// <summary>
/// Session versions are checked on every authenticated request; a short cache spares
/// the database. A bump made by this instance is seen at once; by another, within the
/// cache lifetime (ADR 0021).
/// </summary>
internal sealed class SessionVersionCache(NpgsqlDataSource dataSource, TimeProvider time) : ISessionVersions
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<PlayerId, (int Version, DateTimeOffset Until)> _entries = new();

    public async Task<int> GetAsync(PlayerId player, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (_entries.TryGetValue(player, out var entry) && entry.Until > now)
        {
            return entry.Version;
        }

        await using var command = dataSource.CreateCommand("select session_version from players where id = @id and deleted_at is null");
        command.Parameters.AddWithValue("id", player.Value);
        // A deleted or unknown player has no valid session: -1 never matches a cookie.
        var version = await command.ExecuteScalarAsync(cancellationToken) is int v ? v : -1;
        _entries[player] = (version, now + Lifetime);
        return version;
    }

    public void Forget(PlayerId player) => _entries.TryRemove(player, out _);
}
