using Npgsql;

namespace NobodyFalls.Infrastructure.Persistence;

/// <summary>Small helpers for hand-written SQL on an open connection and transaction.</summary>
internal static class Sql
{
    /// <summary>
    /// Advisory lock taken exclusively by the midnight resolution and the start of a day,
    /// and shared by everything that writes hands. Resolution therefore waits for hands
    /// in flight, and no hand can slip into a day while it is being resolved (ADR 0004).
    /// </summary>
    public const long NightLock = 0x4E46_4E49_4748_5401; // "NFNIGHT" + 1

    public static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    public static async Task<int> ExecuteAsync(this NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(transaction.Connection!, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<T?> ScalarAsync<T>(this NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(transaction.Connection!, transaction, sql, parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? default : (T)value;
    }

    public static async Task<NpgsqlDataReader> ReaderAsync(this NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(transaction.Connection!, transaction, sql, parameters);
        return await command.ExecuteReaderAsync(cancellationToken);
    }

    public static Task LockNightExclusiveAsync(this NpgsqlTransaction transaction, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync("select pg_advisory_xact_lock(@key)", cancellationToken, ("key", NightLock));

    public static Task LockNightSharedAsync(this NpgsqlTransaction transaction, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync("select pg_advisory_xact_lock_shared(@key)", cancellationToken, ("key", NightLock));

    /// <summary>Npgsql only accepts UTC offsets for <c>timestamptz</c>.</summary>
    public static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();
}
