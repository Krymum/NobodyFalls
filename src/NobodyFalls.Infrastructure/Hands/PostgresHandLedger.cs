using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Hands;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Hands;

/// <summary>
/// Gives hands under concurrency (ADR 0004): facts are read and the hand inserted in one
/// transaction holding the shared night lock; unique indexes settle any race, and a lost
/// race on a slot is retried with fresh facts.
/// </summary>
internal sealed class PostgresHandLedger(NpgsqlDataSource dataSource, IGameSettingsProvider settings) : IHandLedger
{
    private const string Facts = """
        select gd.day is not null as day_started,
               gd.resolved_at is null as day_unresolved,
               gd.settings_version,
               exists (select 1 from seasons s
                       where s.opening_day <= @day and (s.last_day is null or s.last_day >= @day)) as season_open,
               (select kind from players where id = @giver and deleted_at is null) as giver_kind,
               (select kind from players where id = @receiver and deleted_at is null) as receiver_kind,
               exists (select 1 from player_days where day = @day and player_id = @receiver) as receiver_in_play,
               (select count(*)::int from hands where giver_id = @giver and day = @day) as used,
               exists (select 1 from hands where giver_id = @giver and receiver_id = @receiver and day = @day) as already,
               exists (select 1 from blocks
                       where (blocker_id = @giver and blocked_id = @receiver)
                          or (blocker_id = @receiver and blocked_id = @giver)) as blocked,
               exists (select 1 from friendships
                       where player_low = least(@giver, @receiver) and player_high = greatest(@giver, @receiver)) as friends
        from (select 1) as one
        left join game_days gd on gd.day = @day;
        """;

    public async Task<Result<GivenHand>> GiveAsync(PlayerId giver, PlayerId receiver, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var day = GameDay.Of(now);

        // Each lost race means another hand of this giver was recorded, so a handful of
        // attempts always settles: either a free slot is found or none is left.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await TryGiveAsync(giver, receiver, day, now, cancellationToken);
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                if (e.ConstraintName == "ux_hands_giver_receiver_day")
                {
                    return HandErrors.Duplicate;
                }

                if (e.ConstraintName != "ux_hands_giver_day_slot" || attempt >= 10)
                {
                    throw;
                }
            }
        }
    }

    private async Task<Result<GivenHand>> TryGiveAsync(PlayerId giver, PlayerId receiver, GameDay day, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await tx.LockNightSharedAsync(cancellationToken);

        HandAttempt attempt;
        int? settingsVersion;
        string giverKind;
        bool friends;
        await using (var r = await tx.ReaderAsync(
                         Facts,
                         cancellationToken,
                         ("day", day.Date),
                         ("giver", giver.Value),
                         ("receiver", receiver.Value)))
        {
            await r.ReadAsync(cancellationToken);
            var dayStarted = r.GetBoolean(0);
            settingsVersion = r.IsDBNull(2) ? null : r.GetInt32(2);
            giverKind = r.IsDBNull(4)
                ? throw new InvalidOperationException($"Giver {giver} does not exist.")
                : r.GetString(4);
            attempt = new HandAttempt
            {
                Giver = giver,
                Receiver = receiver,
                ReceiverKind = r.IsDBNull(5) ? null : DbValues.ToKind(r.GetString(5)),
                ReceiverInPlay = r.GetBoolean(6),
                GiverHandsUsedToday = r.GetInt32(7),
                GiverAlreadyHoldsReceiverToday = r.GetBoolean(8),
                Blocked = r.GetBoolean(9),
                SeasonOpen = r.GetBoolean(3),

                // A day not started yet while the season is open means its midnight is being resolved.
                DayOpen = dayStarted && r.GetBoolean(1),
            };
            friends = r.GetBoolean(10);
        }

        var rules = settingsVersion is { } version
            ? (await settings.GetVersionAsync(version, cancellationToken)).Rules
            : (await settings.GetCurrentAsync(cancellationToken)).Rules;

        var check = HandPolicy.Check(attempt, rules);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        var handId = await tx.ScalarAsync<long>(
            """
            insert into hands (day, giver_id, receiver_id, slot, given_at, giver_kind, giver_was_friend)
            values (@day, @giver, @receiver, @slot, @now, @giver_kind, @friends)
            returning id
            """,
            cancellationToken,
            ("day", day.Date),
            ("giver", giver.Value),
            ("receiver", receiver.Value),
            ("slot", (short)(attempt.GiverHandsUsedToday + 1)),
            ("now", Sql.Utc(now)),
            ("giver_kind", giverKind),
            ("friends", friends));

        int received;
        int required;
        await using (var r = await tx.ReaderAsync(
                         "update player_days set received = received + 1 where day = @day and player_id = @receiver returning received, required",
                         cancellationToken,
                         ("day", day.Date),
                         ("receiver", receiver.Value)))
        {
            await r.ReadAsync(cancellationToken);
            received = r.GetInt32(0);
            required = r.GetInt32(1);
        }

        await tx.CommitAsync(cancellationToken);
        return Result.Success(new GivenHand(
            handId,
            day,
            now,
            giver,
            receiver,
            rules.HandsPerDay - attempt.GiverHandsUsedToday - 1,
            received,
            required));
    }
}
