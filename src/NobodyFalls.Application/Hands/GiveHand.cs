using NobodyFalls.Application.Abstractions;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Application.Hands;

/// <summary>A hand that was just given.</summary>
/// <param name="HandsLeft">Hands the giver can still give today.</param>
/// <param name="ReceiverReceived">Hands the receiver now has today.</param>
public sealed record GivenHand(
    long HandId,
    GameDay Day,
    DateTimeOffset GivenAt,
    PlayerId Giver,
    PlayerId Receiver,
    int HandsLeft,
    int ReceiverReceived,
    int ReceiverRequired);

/// <summary>
/// Records hands atomically: the rules are checked and the hand inserted in one
/// transaction, with database constraints as the final guard (ADR 0004).
/// </summary>
public interface IHandLedger
{
    Task<Result<GivenHand>> GiveAsync(PlayerId giver, PlayerId receiver, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Give one of today's hands to another player.</summary>
public sealed class GiveHand(IHandLedger ledger, IClock clock)
{
    public Task<Result<GivenHand>> ExecuteAsync(PlayerId giver, PlayerId receiver, CancellationToken cancellationToken) =>
        ledger.GiveAsync(giver, receiver, clock.UtcNow, cancellationToken);
}
