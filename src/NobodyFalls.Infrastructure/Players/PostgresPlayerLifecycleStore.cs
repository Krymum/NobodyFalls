using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Players;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Players;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Players;

internal sealed class PostgresPlayerLifecycleStore(NpgsqlDataSource dataSource, IGameSettingsProvider settings) : IPlayerLifecycleStore
{
    public async Task<ReturnOutcome> ReturnAsync(PlayerId playerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await tx.LockNightSharedAsync(cancellationToken);

        Player? player;
        await using (var r = await tx.ReaderAsync(
                         """
                         select kind, pseudo, status, joined_day, invited_by_id, falls_count, last_fall_day, returns_on_day
                         from players where id = @id and deleted_at is null
                         for update
                         """,
                         cancellationToken,
                         ("id", playerId.Value)))
        {
            player = await r.ReadAsync(cancellationToken)
                ? Player.Restore(
                    playerId,
                    DbValues.ToKind(r.GetString(0)),
                    Pseudo.FromTrusted(r.GetString(1)),
                    DbValues.ToStatus(r.GetString(2)),
                    r.IsDBNull(3) ? null : new GameDay(r.GetFieldValue<DateOnly>(3)),
                    r.IsDBNull(4) ? null : new PlayerId(r.GetGuid(4)),
                    r.GetInt32(5),
                    r.IsDBNull(6) ? null : new GameDay(r.GetFieldValue<DateOnly>(6)),
                    r.IsDBNull(7) ? null : new GameDay(r.GetFieldValue<DateOnly>(7)))
                : null;
        }

        if (player is null)
        {
            return ReturnOutcome.NotFallen;
        }

        var current = await settings.GetCurrentAsync(cancellationToken);
        var outcome = player.Return(now, current.Rules);
        if (outcome == ReturnOutcome.NotFallen)
        {
            return outcome;
        }

        await tx.ExecuteAsync(
            "update players set status = @status, returns_on_day = @returns_on where id = @id",
            cancellationToken,
            ("id", playerId.Value),
            ("status", DbValues.Of(player.Status)),
            ("returns_on", player.ReturnsOnDay?.Date));

        if (outcome == ReturnOutcome.BackToday)
        {
            await JoinTodayAsync(tx, player, GameDay.Of(now), now, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return outcome;
    }

    /// <summary>A player back today needs the day's normal threshold (no immunity, no extra).</summary>
    private async Task JoinTodayAsync(NpgsqlTransaction tx, Player player, GameDay today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        int baseRequired;
        bool storm;
        int version;
        await using (var r = await tx.ReaderAsync(
                         "select base_required, storm, settings_version from game_days where day = @day and resolved_at is null",
                         cancellationToken,
                         ("day", today.Date)))
        {
            if (!await r.ReadAsync(cancellationToken))
            {
                return; // the day starts later and will include the player
            }

            baseRequired = r.GetInt32(0);
            storm = r.GetBoolean(1);
            version = r.GetInt32(2);
        }

        var rules = (await settings.GetVersionAsync(version, cancellationToken)).Rules;
        var standing = player.JoinedDay is { } joined ? ThresholdPolicy.StandingOn(today, joined) : PlayerStanding.Regular;
        var required = ThresholdPolicy.Required(rules, baseRequired, storm, standing);

        await tx.ExecuteAsync(
            """
            insert into player_days (day, player_id, required, immune, received, joined_at, score_gained)
            values (@day, @id, @required, false,
                    (select count(*) from hands where day = @day and receiver_id = @id), @now, 0)
            on conflict do nothing
            """,
            cancellationToken,
            ("day", today.Date),
            ("id", player.Id.Value),
            ("required", required),
            ("now", Sql.Utc(now)));
    }
}
