using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Friends;
using NobodyFalls.Application.Queries;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Friends;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Friends;

internal sealed class PostgresFriendStore(NpgsqlDataSource dataSource, IGameSettingsProvider settings) : IFriendStore
{
    private const string CountFriends = "select count(*)::int from friendships where player_low = @id or player_high = @id";

    public async Task<Result<FriendRequestOutcome>> RequestAsync(PlayerId from, string targetPseudo, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pseudo = Pseudo.Create(targetPseudo);
        if (pseudo.IsFailure)
        {
            return FriendErrors.NotFound;
        }

        var rules = (await settings.GetCurrentAsync(cancellationToken)).Rules;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        var target = await tx.ScalarAsync<Guid?>(
            "select id from players where pseudo_key = @key and kind = 'human' and deleted_at is null",
            cancellationToken,
            ("key", pseudo.Value.Key));
        if (target is not { } targetId)
        {
            return FriendErrors.NotFound;
        }

        var to = new PlayerId(targetId);
        if (to == from)
        {
            return FriendErrors.Self;
        }

        // Serialize everything that happens between these two players.
        var pair = PlayerPair.Of(from, to);
        await tx.ExecuteAsync(
            "select pg_advisory_xact_lock(hashtextextended(@low::text || @high::text, 0))",
            cancellationToken,
            ("low", pair.Low.Value),
            ("high", pair.High.Value));

        var theirRequest = await tx.ScalarAsync<long?>(
            "select id from friend_requests where from_id = @to and to_id = @from and status = 'pending'",
            cancellationToken,
            ("from", from.Value),
            ("to", to.Value));

        var attempt = new FriendRequestAttempt
        {
            From = from,
            To = to,
            TargetExists = true,
            Blocked = await IsBlockedAsync(tx, from, to, cancellationToken),
            AlreadyFriends = await AreFriendsAsync(tx, pair, cancellationToken),
            AlreadyRequested = await tx.ScalarAsync<bool>(
                "select exists (select 1 from friend_requests where from_id = @from and to_id = @to and status = 'pending')",
                cancellationToken,
                ("from", from.Value),
                ("to", to.Value)),
            FromFriendCount = await tx.ScalarAsync<int>(CountFriends, cancellationToken, ("id", from.Value)),
            ToFriendCount = await tx.ScalarAsync<int>(CountFriends, cancellationToken, ("id", to.Value)),
        };

        var check = FriendPolicy.CanRequest(attempt, rules);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        if (theirRequest is { } requestId)
        {
            await AcceptInTransactionAsync(tx, requestId, pair, now, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Result.Success(FriendRequestOutcome.BecameFriends);
        }

        await tx.ExecuteAsync(
            "insert into friend_requests (from_id, to_id, status, created_at) values (@from, @to, 'pending', @now)",
            cancellationToken,
            ("from", from.Value),
            ("to", to.Value),
            ("now", Sql.Utc(now)));
        await tx.CommitAsync(cancellationToken);
        return Result.Success(FriendRequestOutcome.Sent);
    }

    public async Task<Result> AcceptAsync(PlayerId player, long requestId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rules = (await settings.GetCurrentAsync(cancellationToken)).Rules;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        var requester = await tx.ScalarAsync<Guid?>(
            "select from_id from friend_requests where id = @id and to_id = @me and status = 'pending' for update",
            cancellationToken,
            ("id", requestId),
            ("me", player.Value));
        if (requester is not { } requesterId)
        {
            return FriendRequestErrors.NotFound;
        }

        var check = FriendPolicy.CanAccept(
            await tx.ScalarAsync<int>(CountFriends, cancellationToken, ("id", player.Value)),
            await tx.ScalarAsync<int>(CountFriends, cancellationToken, ("id", requesterId)),
            rules);
        if (check.IsFailure)
        {
            return check;
        }

        await AcceptInTransactionAsync(tx, requestId, PlayerPair.Of(player, new PlayerId(requesterId)), now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public Task<Result> DeclineAsync(PlayerId player, long requestId, DateTimeOffset now, CancellationToken cancellationToken) =>
        AnswerAsync("to_id", "declined", player, requestId, now, cancellationToken);

    public Task<Result> CancelAsync(PlayerId player, long requestId, DateTimeOffset now, CancellationToken cancellationToken) =>
        AnswerAsync("from_id", "cancelled", player, requestId, now, cancellationToken);

    public async Task RemoveAsync(PlayerId player, PlayerId other, CancellationToken cancellationToken)
    {
        if (player == other)
        {
            return;
        }

        var pair = PlayerPair.Of(player, other);
        await using var command = dataSource.CreateCommand("delete from friendships where player_low = @low and player_high = @high");
        command.Parameters.AddWithValue("low", pair.Low.Value);
        command.Parameters.AddWithValue("high", pair.High.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FriendView>> ListAsync(PlayerId player, GameDay today, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            select {PlayerRefs.Columns}, p.status, pd.received, pd.required, pd.immune, f.created_at
            from friendships f
            join players p on p.id = case when f.player_low = @me then f.player_high else f.player_low end
            {PlayerRefs.Join}
            left join player_days pd on pd.day = @day and pd.player_id = p.id
            where (f.player_low = @me or f.player_high = @me) and p.deleted_at is null
            order by p.pseudo_key
            """);
        command.Parameters.AddWithValue("me", player.Value);
        command.Parameters.AddWithValue("day", today.Date);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var friends = new List<FriendView>();
        while (await r.ReadAsync(cancellationToken))
        {
            friends.Add(new FriendView(
                PlayerRefs.Read(r, 0),
                PlayerRefs.Status(r.GetString(4)),
                r.IsDBNull(5) ? null : new DayStanding(r.GetInt32(5), r.GetInt32(6), r.GetBoolean(7)),
                r.GetFieldValue<DateTimeOffset>(8)));
        }

        return friends;
    }

    public async Task<FriendRequestsView> RequestsAsync(PlayerId player, CancellationToken cancellationToken) =>
        new(await RequestListAsync("to_id", "from_id", player, cancellationToken),
            await RequestListAsync("from_id", "to_id", player, cancellationToken));

    public async Task<IReadOnlyList<SuggestionView>> SuggestionsAsync(PlayerId player, int minNights, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            select {PlayerRefs.Columns}, hp.nights
            from hold_pairs hp
            join players p on p.id = case when hp.player_low = @me then hp.player_high else hp.player_low end
            {PlayerRefs.Join}
            where (hp.player_low = @me or hp.player_high = @me)
              and hp.nights >= @min
              and p.kind = 'human' and p.deleted_at is null
              and not exists (select 1 from friendships f where f.player_low = hp.player_low and f.player_high = hp.player_high)
              and not exists (select 1 from friend_requests fr
                              where fr.status = 'pending'
                                and ((fr.from_id = @me and fr.to_id = p.id) or (fr.from_id = p.id and fr.to_id = @me)))
              and not {PlayerRefs.BlockedBetween("p.id", "@me")}
            order by hp.nights desc, hp.last_day desc
            limit @limit
            """);
        command.Parameters.AddWithValue("me", player.Value);
        command.Parameters.AddWithValue("min", minNights);
        command.Parameters.AddWithValue("limit", limit);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var suggestions = new List<SuggestionView>();
        while (await r.ReadAsync(cancellationToken))
        {
            suggestions.Add(new SuggestionView(PlayerRefs.Read(r, 0), r.GetInt32(4)));
        }

        return suggestions;
    }

    public async Task<Result> BlockAsync(PlayerId player, PlayerId target, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (player == target)
        {
            return FriendErrors.Self;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        if (!await tx.ScalarAsync<bool>(
                "select exists (select 1 from players where id = @id and kind = 'human' and deleted_at is null)",
                cancellationToken,
                ("id", target.Value)))
        {
            return FriendErrors.NotFound;
        }

        var pair = PlayerPair.Of(player, target);
        await tx.ExecuteAsync(
            """
            insert into blocks (blocker_id, blocked_id, created_at) values (@me, @target, @now) on conflict do nothing;

            delete from friendships where player_low = @low and player_high = @high;

            update friend_requests set status = 'cancelled', answered_at = @now
            where status = 'pending' and ((from_id = @me and to_id = @target) or (from_id = @target and to_id = @me));
            """,
            cancellationToken,
            ("me", player.Value),
            ("target", target.Value),
            ("low", pair.Low.Value),
            ("high", pair.High.Value),
            ("now", Sql.Utc(now)));
        await tx.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task UnblockAsync(PlayerId player, PlayerId target, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("delete from blocks where blocker_id = @me and blocked_id = @target");
        command.Parameters.AddWithValue("me", player.Value);
        command.Parameters.AddWithValue("target", target.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerRef>> BlockedAsync(PlayerId player, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            select {PlayerRefs.Columns}
            from blocks b join players p on p.id = b.blocked_id {PlayerRefs.Join}
            where b.blocker_id = @me
            order by b.created_at desc
            """);
        command.Parameters.AddWithValue("me", player.Value);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var blocked = new List<PlayerRef>();
        while (await r.ReadAsync(cancellationToken))
        {
            blocked.Add(PlayerRefs.Read(r, 0));
        }

        return blocked;
    }

    private static async Task AcceptInTransactionAsync(NpgsqlTransaction tx, long requestId, PlayerPair pair, DateTimeOffset now, CancellationToken cancellationToken) =>
        await tx.ExecuteAsync(
            """
            update friend_requests set status = 'accepted', answered_at = @now where id = @id;

            insert into friendships (player_low, player_high, source, created_at) values (@low, @high, 'request', @now)
            on conflict do nothing;
            """,
            cancellationToken,
            ("id", requestId),
            ("low", pair.Low.Value),
            ("high", pair.High.Value),
            ("now", Sql.Utc(now)));

    private async Task<Result> AnswerAsync(string ownerColumn, string newStatus, PlayerId player, long requestId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"update friend_requests set status = @status, answered_at = @now where id = @id and {ownerColumn} = @me and status = 'pending'");
        command.Parameters.AddWithValue("status", newStatus);
        command.Parameters.AddWithValue("now", Sql.Utc(now));
        command.Parameters.AddWithValue("id", requestId);
        command.Parameters.AddWithValue("me", player.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? Result.Success() : FriendRequestErrors.NotFound;
    }

    private async Task<IReadOnlyList<FriendRequestView>> RequestListAsync(string mine, string theirs, PlayerId player, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            select {PlayerRefs.Columns}, fr.id, fr.created_at
            from friend_requests fr join players p on p.id = fr.{theirs} {PlayerRefs.Join}
            where fr.{mine} = @me and fr.status = 'pending' and p.deleted_at is null
            order by fr.created_at desc
            """);
        command.Parameters.AddWithValue("me", player.Value);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var requests = new List<FriendRequestView>();
        while (await r.ReadAsync(cancellationToken))
        {
            requests.Add(new FriendRequestView(r.GetInt64(4), PlayerRefs.Read(r, 0), r.GetFieldValue<DateTimeOffset>(5)));
        }

        return requests;
    }

    private static Task<bool> IsBlockedAsync(NpgsqlTransaction tx, PlayerId a, PlayerId b, CancellationToken cancellationToken) =>
        tx.ScalarAsync<bool>($"select {PlayerRefs.BlockedBetween("@a", "@b")}", cancellationToken, ("a", a.Value), ("b", b.Value));

    private static Task<bool> AreFriendsAsync(NpgsqlTransaction tx, PlayerPair pair, CancellationToken cancellationToken) =>
        tx.ScalarAsync<bool>(
            "select exists (select 1 from friendships where player_low = @low and player_high = @high)",
            cancellationToken,
            ("low", pair.Low.Value),
            ("high", pair.High.Value));
}
