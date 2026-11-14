using System.Security.Cryptography;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Accounts;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Friends;
using NobodyFalls.Domain.Players;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Accounts;

internal sealed class PostgresAccountStore(NpgsqlDataSource dataSource, IGameSettingsProvider settings) : IAccountStore
{
    // Crockford base32 without look-alikes: easy to read aloud and to type from a screenshot.
    private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    public async Task<PlayerId?> FindByLoginAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            select l.player_id from external_logins l join players p on p.id = l.player_id
            where l.provider = @provider and l.provider_key = @key and p.deleted_at is null
            """);
        command.Parameters.AddWithValue("provider", identity.Provider);
        command.Parameters.AddWithValue("key", identity.Key);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? new PlayerId(id) : null;
    }

    public async Task<Result<PlayerId>> CreateAsync(NewPlayer player, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await TryCreateAsync(player, now, cancellationToken);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Two sign-ups racing for the same pseudo or the same login.
            return e.ConstraintName switch
            {
                "ix_players_pseudo_key" => PseudoErrors.Taken,
                "pk_external_logins" => AccountErrors.AlreadyRegistered,
                _ => throw new InvalidOperationException("Unexpected unique violation while registering.", e),
            };
        }
    }

    public async Task TouchAsync(PlayerId player, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("update players set last_seen_at = @now where id = @id");
        command.Parameters.AddWithValue("id", player.Value);
        command.Parameters.AddWithValue("now", Sql.Utc(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<Result<PlayerId>> TryCreateAsync(NewPlayer player, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = GameDay.Of(now);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await tx.LockNightSharedAsync(cancellationToken);

        if (await tx.ScalarAsync<bool>(
                "select exists (select 1 from external_logins where provider = @provider and provider_key = @key)",
                cancellationToken,
                ("provider", player.Identity.Provider),
                ("key", player.Identity.Key)))
        {
            return AccountErrors.AlreadyRegistered;
        }

        if (await tx.ScalarAsync<bool>(
                "select exists (select 1 from players where pseudo_key = @key)", cancellationToken, ("key", player.Pseudo.Key)))
        {
            return PseudoErrors.Taken;
        }

        // An unknown invitation code does not block the sign-up; it simply invites nobody.
        var inviter = player.InviteCode is null
            ? null
            : await tx.ScalarAsync<Guid?>(
                "select id from players where invite_code = @code and kind = 'human' and deleted_at is null",
                cancellationToken,
                ("code", player.InviteCode.ToUpperInvariant()));

        var seasonOpen = await tx.ScalarAsync<bool>(
            "select exists (select 1 from seasons where opening_day <= @day and (last_day is null or last_day >= @day))",
            cancellationToken,
            ("day", today.Date));

        var id = new PlayerId(Guid.CreateVersion7(now));
        var created = Player.Register(id, player.Pseudo, today, seasonOpen, inviter is { } g ? new PlayerId(g) : null);

        await tx.ExecuteAsync(
            """
            insert into players (id, kind, pseudo, pseudo_key, status, locale, joined_day, invited_by_id, invite_code, created_at, last_seen_at)
            values (@id, 'human', @pseudo, @key, @status, @locale, @joined, @inviter, @code, @now, @now);

            insert into appearances (player_id, document, updated_at) values (@id, @appearance::jsonb, @now);

            insert into external_logins (provider, provider_key, player_id, created_at) values (@provider, @provider_key, @id, @now);
            """,
            cancellationToken,
            ("id", id.Value),
            ("pseudo", created.Pseudo.Value),
            ("key", created.Pseudo.Key),
            ("status", DbValues.Of(created.Status)),
            ("locale", player.Locale),
            ("joined", created.JoinedDay?.Date),
            ("inviter", inviter),
            ("code", NewInviteCode()),
            ("now", Sql.Utc(now)),
            ("appearance", AppearanceJson.Serialize(player.Appearance)),
            ("provider", player.Identity.Provider),
            ("provider_key", player.Identity.Key));

        if (created.IsAlive)
        {
            await JoinTodayAsync(tx, id, today, now, cancellationToken);
        }

        if (inviter is { } inviterId)
        {
            await BefriendInviterAsync(tx, id, new PlayerId(inviterId), now, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return Result.Success(id);
    }

    /// <summary>A newcomer is in play at once and immune at the coming midnight.</summary>
    private async Task JoinTodayAsync(NpgsqlTransaction tx, PlayerId id, GameDay today, DateTimeOffset now, CancellationToken cancellationToken)
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
                return; // the day starts later and will include the newcomer
            }

            baseRequired = r.GetInt32(0);
            storm = r.GetBoolean(1);
            version = r.GetInt32(2);
        }

        var rules = (await settings.GetVersionAsync(version, cancellationToken)).Rules;
        await tx.ExecuteAsync(
            """
            insert into player_days (day, player_id, required, immune, received, joined_at, score_gained)
            values (@day, @id, @required, true, 0, @now, 0)
            """,
            cancellationToken,
            ("day", today.Date),
            ("id", id.Value),
            ("required", ThresholdPolicy.Required(rules, baseRequired, storm, PlayerStanding.Newcomer)),
            ("now", Sql.Utc(now)));
    }

    /// <summary>Joining through someone's link makes you friends, within the friend limit.</summary>
    private async Task BefriendInviterAsync(NpgsqlTransaction tx, PlayerId newcomer, PlayerId inviter, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rules = (await settings.GetCurrentAsync(cancellationToken)).Rules;
        var inviterFriends = await tx.ScalarAsync<long>(
            "select count(*) from friendships where player_low = @id or player_high = @id",
            cancellationToken,
            ("id", inviter.Value));
        if (!FriendPolicy.BefriendsOnInvitation((int)inviterFriends, 0, rules))
        {
            return;
        }

        var pair = PlayerPair.Of(newcomer, inviter);
        await tx.ExecuteAsync(
            "insert into friendships (player_low, player_high, source, created_at) values (@low, @high, 'invite', @now) on conflict do nothing",
            cancellationToken,
            ("low", pair.Low.Value),
            ("high", pair.High.Value),
            ("now", Sql.Utc(now)));
    }

    private static string NewInviteCode() =>
        string.Create(10, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            }
        });
}
