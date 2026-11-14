using NobodyFalls.Application.Abstractions;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Players;

namespace NobodyFalls.Application.Players;

public interface IPlayerLifecycleStore
{
    /// <summary>Puts a fallen player back in play (today or tomorrow). Idempotent.</summary>
    Task<ReturnOutcome> ReturnAsync(PlayerId player, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// A fallen player comes back automatically on their next visit (GAME_DESIGN §2).
/// The API calls this on the first authenticated request of a fallen player.
/// </summary>
public sealed class ComeBack(IPlayerLifecycleStore store, IClock clock)
{
    public Task<ReturnOutcome> ExecuteAsync(PlayerId player, CancellationToken cancellationToken) =>
        store.ReturnAsync(player, clock.UtcNow, cancellationToken);
}
