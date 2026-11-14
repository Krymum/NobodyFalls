using System.Collections.Concurrent;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Settings;

/// <summary>
/// Game rules from <c>game_settings</c>. Versions are immutable, so they are cached
/// forever; "which version is current" is re-read every few seconds, which is how a
/// change reaches every server instance without a message bus (ADR 0005).
/// </summary>
internal sealed class DbGameSettingsProvider(NpgsqlDataSource dataSource, TimeProvider time) : IGameSettingsProvider
{
    private static readonly TimeSpan CurrentTtl = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<int, GameSettings> _versions = new();
    private (GameSettings Settings, long ReadAt)? _current;

    public async Task<GameSettings> GetCurrentAsync(CancellationToken cancellationToken)
    {
        if (_current is { } cached && time.GetElapsedTime(cached.ReadAt) < CurrentTtl)
        {
            return cached.Settings;
        }

        await using var command = dataSource.CreateCommand("select max(version) from game_settings");
        var version = await command.ExecuteScalarAsync(cancellationToken) as int?
            ?? throw new InvalidOperationException("No game settings found. Were the migrations applied?");

        var settings = await GetVersionAsync(version, cancellationToken);
        _current = (settings, time.GetTimestamp());
        return settings;
    }

    public async Task<GameSettings> GetVersionAsync(int version, CancellationToken cancellationToken)
    {
        if (_versions.TryGetValue(version, out var known))
        {
            return known;
        }

        await using var command = dataSource.CreateCommand("select document::text from game_settings where version = @version");
        command.Parameters.AddWithValue("version", version);
        var json = await command.ExecuteScalarAsync(cancellationToken) as string
            ?? throw new InvalidOperationException($"Game settings version {version} does not exist.");

        return _versions.GetOrAdd(version, new GameSettings(version, GameRulesJson.Deserialize(json)));
    }

    public async Task<Result<GameSettings>> SaveAsync(GameRules rules, string comment, PlayerId? author, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var errors = rules.Validate();
        if (errors.Count > 0)
        {
            return errors[0];
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        // Serialize writers so that version numbers never collide.
        await tx.ExecuteAsync("lock table game_settings in share row exclusive mode", cancellationToken);
        var version = await tx.ScalarAsync<int>(
            """
            insert into game_settings (version, document, created_at, created_by, comment)
            select coalesce(max(version), 0) + 1, @document::jsonb, @now, @author, @comment from game_settings
            returning version
            """,
            cancellationToken,
            ("document", GameRulesJson.Serialize(rules)),
            ("now", time.GetUtcNow()),
            ("author", author?.Value),
            ("comment", comment));
        await tx.CommitAsync(cancellationToken);

        var saved = new GameSettings(version, rules);
        _versions[version] = saved;
        _current = (saved, time.GetTimestamp());
        return Result.Success(saved);
    }
}
