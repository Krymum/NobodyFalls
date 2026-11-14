using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;

namespace NobodyFalls.Application.Abstractions;

/// <summary>A numbered version of the game rules.</summary>
public sealed record GameSettings(int Version, GameRules Rules);

/// <summary>
/// Game rules live in the database and can change without redeploying (ADR 0005).
/// Each game day records the version it started with, and is resolved with it.
/// </summary>
public interface IGameSettingsProvider
{
    /// <summary>The latest version; may be cached for a few seconds.</summary>
    Task<GameSettings> GetCurrentAsync(CancellationToken cancellationToken);

    Task<GameSettings> GetVersionAsync(int version, CancellationToken cancellationToken);

    /// <summary>Validates and stores a new version. It applies from the next game day.</summary>
    Task<Result<GameSettings>> SaveAsync(GameRules rules, string comment, PlayerId? author, CancellationToken cancellationToken);
}
