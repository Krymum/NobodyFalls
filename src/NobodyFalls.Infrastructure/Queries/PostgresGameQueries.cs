using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Queries;
using NobodyFalls.Domain.Common;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Queries;

internal sealed class PostgresGameQueries(NpgsqlDataSource dataSource, IGameSettingsProvider settings, ISkinCatalogProvider catalogs) : IGameQueries
{
    /// <summary>Same tie-breaker as <c>StableShuffle.Key</c> in the domain.</summary>
    private const string ShuffleKey = "('x' || substr(md5(pd.day::text || ':' || pd.player_id::text), 1, 16))::bit(64)::bigint";

    public async Task<SessionPlayer?> SessionAsync(PlayerId player, CancellationToken cancellationToken)
    {
        await using var command = Command(
            $"select {PlayerRefs.Columns}, p.status, p.locale from players p {PlayerRefs.Join} where p.id = @id and p.deleted_at is null",
            ("id", player.Value));
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
        {
            return null;
        }

        var reference = PlayerRefs.Read(r, 0);
        return new SessionPlayer(reference.Id, reference.Pseudo, PlayerRefs.Status(r.GetString(4)), r.GetString(5), reference.Appearance);
    }

    public async Task<TonightView> TonightAsync(PlayerId player, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var day = GameDay.Of(now);
        var p = new (string, object?)[] { ("day", day.Date), ("me", player.Value), ("yesterday", day.Previous().Date) };

        string status;
        GameDay? lastFall;
        await using (var command = Command("select status, last_fall_day from players where id = @me", p))
        await using (var r = await command.ExecuteReaderAsync(cancellationToken))
        {
            await r.ReadAsync(cancellationToken);
            status = r.GetString(0);
            lastFall = r.IsDBNull(1) ? null : new GameDay(r.GetFieldValue<DateOnly>(1));
        }

        int? nightNumber = null;
        var storm = false;
        var dayOpen = false;
        int? settingsVersion = null;
        await using (var command = Command(
                         "select night_number, storm, resolved_at is null, settings_version from game_days where day = @day", p))
        await using (var r = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await r.ReadAsync(cancellationToken))
            {
                nightNumber = r.GetInt32(0);
                storm = r.GetBoolean(1);
                dayOpen = r.GetBoolean(2);
                settingsVersion = r.GetInt32(3);
            }
        }

        DayStanding? standing = null;
        await using (var command = Command("select received, required, immune from player_days where day = @day and player_id = @me", p))
        await using (var r = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await r.ReadAsync(cancellationToken))
            {
                standing = new DayStanding(r.GetInt32(0), r.GetInt32(1), r.GetBoolean(2));
            }
        }

        var heldBy = await LinksAsync(
            $"""
            select {PlayerRefs.Columns}, h.given_at, null::int, null::int, null::bool
            from hands h join players p on p.id = h.giver_id {PlayerRefs.Join}
            where h.day = @day and h.receiver_id = @me and not {PlayerRefs.BlockedBetween("h.giver_id", "@me")}
            order by h.given_at, h.id
            """,
            p,
            cancellationToken);
        var holding = await LinksAsync(
            $"""
            select {PlayerRefs.Columns}, h.given_at, pd.received, pd.required, pd.immune
            from hands h join players p on p.id = h.receiver_id {PlayerRefs.Join}
            left join player_days pd on pd.day = h.day and pd.player_id = h.receiver_id
            where h.day = @day and h.giver_id = @me and not {PlayerRefs.BlockedBetween("h.receiver_id", "@me")}
            order by h.given_at, h.id
            """,
            p,
            cancellationToken);

        var handsLeft = 0;
        if (dayOpen && settingsVersion is { } version && status != DbValues.Waitlisted)
        {
            var rules = (await settings.GetVersionAsync(version, cancellationToken)).Rules;
            await using var command = Command("select count(*)::int from hands where day = @day and giver_id = @me", p);
            handsLeft = Math.Max(0, rules.HandsPerDay - (int)(await command.ExecuteScalarAsync(cancellationToken))!);
        }

        return new TonightView(
            day,
            nightNumber,
            now,
            day.MidnightAt,
            nightNumber is not null,
            storm,
            PlayerRefs.Status(status),
            standing,
            handsLeft,
            heldBy,
            holding,
            lastFall == day.Previous() ? lastFall : null,
            null);
    }

    public async Task<EdgePage> EdgeAsync(PlayerId viewer, DateTimeOffset now, int offset, int limit, CancellationToken cancellationToken)
    {
        var day = GameDay.Of(now);
        await using var command = Command(
            $"""
            select {PlayerRefs.Columns}, pd.received, pd.required, pd.immune,
                   exists (select 1 from friendships f
                           where f.player_low = least(p.id, @me) and f.player_high = greatest(p.id, @me)) as is_friend
            from player_days pd
            join players p on p.id = pd.player_id {PlayerRefs.Join}
            where pd.day = @day and pd.received < pd.required and pd.player_id <> @me
              and not {PlayerRefs.BlockedBetween("pd.player_id", "@me")}
            order by pd.immune, pd.required - pd.received desc, pd.received, {ShuffleKey}, pd.player_id
            offset @offset limit @limit
            """,
            ("day", day.Date),
            ("me", viewer.Value),
            ("offset", offset),
            ("limit", limit + 1));
        await using var r = await command.ExecuteReaderAsync(cancellationToken);

        var entries = new List<EdgeEntry>();
        while (await r.ReadAsync(cancellationToken))
        {
            entries.Add(new EdgeEntry(PlayerRefs.Read(r, 0), r.GetInt32(4), r.GetInt32(5), r.GetBoolean(6), r.GetBoolean(7)));
        }

        var hasMore = entries.Count > limit;
        return new EdgePage(hasMore ? entries[..limit] : entries, hasMore ? offset + limit : null);
    }

    public async Task<WorldView> WorldAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var day = GameDay.Of(now);
        await using var command = Command(
            """
            select
                (select night_number from game_days where day = @day),
                (select name from seasons where opening_day <= @day order by number desc limit 1),
                (select streak_after from game_days where resolved_at is not null order by day desc limit 1),
                (select falls from game_days where day = @yesterday and resolved_at is not null),
                (select count(*)::int from players where kind = 'human' and status = 'active' and deleted_at is null),
                (select count(*)::int from player_days where day = @day and received < required and not immune),
                (select deaths from seasons where opening_day <= @day order by number desc limit 1),
                (select deaths_all_time_after from game_days where resolved_at is not null order by day desc limit 1),
                coalesce((select storm from game_days where day = @day), false)
            """,
            ("day", day.Date),
            ("yesterday", day.Previous().Date));
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);

        return new WorldView(
            day,
            r.IsDBNull(0) ? null : r.GetInt32(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? 0 : r.GetInt32(2),
            r.IsDBNull(3) ? null : r.GetInt32(3),
            r.GetInt32(4),
            r.GetInt32(5),
            r.IsDBNull(6) ? 0 : r.GetInt64(6),
            r.IsDBNull(7) ? 0 : r.GetInt64(7),
            r.GetBoolean(8));
    }

    public async Task<MyProfileView?> MyProfileAsync(PlayerId player, CancellationToken cancellationToken)
    {
        PlayerRef me;
        string status;
        int score;
        int falls;
        string code;
        string locale;
        string visibility;
        await using (var command = Command(
                         $"""
                         select {PlayerRefs.Columns}, p.status, p.guardian_score, p.falls_count, p.invite_code, p.locale, p.map_name_visibility
                         from players p {PlayerRefs.Join} where p.id = @me and p.deleted_at is null
                         """,
                         ("me", player.Value)))
        await using (var r = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await r.ReadAsync(cancellationToken))
            {
                return null;
            }

            me = PlayerRefs.Read(r, 0);
            status = r.GetString(4);
            score = r.GetInt32(5);
            falls = r.GetInt32(6);
            code = r.GetString(7);
            locale = r.GetString(8);
            visibility = r.GetString(9);
        }

        var savedBy = await DebtsAsync("d.saved_id", "d.saver_id", player, cancellationToken);
        var saved = await DebtsAsync("d.saver_id", "d.saved_id", player, cancellationToken);
        return new MyProfileView(me, PlayerRefs.Status(status), score, falls, code, locale, visibility, savedBy, saved);
    }

    public async Task<PublicProfileView?> PublicProfileAsync(PlayerId viewer, PlayerId target, CancellationToken cancellationToken)
    {
        await using var command = Command(
            $"""
            select {PlayerRefs.Columns}, p.status, p.guardian_score,
                   exists (select 1 from friendships f where f.player_low = least(p.id, @me) and f.player_high = greatest(p.id, @me)),
                   exists (select 1 from friend_requests fr where fr.from_id = @me and fr.to_id = p.id and fr.status = 'pending'),
                   exists (select 1 from friend_requests fr where fr.from_id = p.id and fr.to_id = @me and fr.status = 'pending'),
                   coalesce((select nights from hold_pairs hp where hp.player_low = least(p.id, @me) and hp.player_high = greatest(p.id, @me)), 0)
            from players p {PlayerRefs.Join}
            where p.id = @target and p.deleted_at is null and not {PlayerRefs.BlockedBetween("p.id", "@me")}
            """,
            ("me", viewer.Value),
            ("target", target.Value));
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
        {
            return null;
        }

        var relation = r.GetBoolean(6) ? Relation.Friends
            : r.GetBoolean(7) ? Relation.RequestSent
            : r.GetBoolean(8) ? Relation.RequestReceived
            : Relation.None;
        return new PublicProfileView(PlayerRefs.Read(r, 0), PlayerRefs.Status(r.GetString(4)), r.GetInt32(5), relation, r.GetInt32(9));
    }

    public async Task<InvitePreview?> InviteAsync(string code, CancellationToken cancellationToken)
    {
        await using var command = Command(
            $"select {PlayerRefs.Columns} from players p {PlayerRefs.Join} where p.invite_code = @code and p.kind = 'human' and p.deleted_at is null",
            ("code", code.Trim().ToUpperInvariant()));
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? new InvitePreview(code.Trim().ToUpperInvariant(), PlayerRefs.Read(r, 0)) : null;
    }

    public async Task<CatalogView> CatalogAsync(PlayerId? player, CancellationToken cancellationToken)
    {
        var catalog = await catalogs.GetAsync(cancellationToken);
        var owned = new HashSet<string>(StringComparer.Ordinal);
        if (player is { } id)
        {
            await using var command = Command("select item_id from player_skin_unlocks where player_id = @me", ("me", id.Value));
            await using var r = await command.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                owned.Add(r.GetString(0));
            }
        }

        return new CatalogView(
            catalog.Items
                .Select(i => new CatalogItemView(
                    i.Id, i.Slot, i.Rarity, i.RenderKey, i.Colors, i.Unlock,
                    i.Rarity == Domain.Skins.SkinRarity.Base || owned.Contains(i.Id)))
                .ToList(),
            catalog.SkinTones);
    }

    private async Task<IReadOnlyList<HandLink>> LinksAsync(string sql, (string, object?)[] parameters, CancellationToken cancellationToken)
    {
        await using var command = Command(sql, parameters);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var links = new List<HandLink>();
        while (await r.ReadAsync(cancellationToken))
        {
            links.Add(new HandLink(
                PlayerRefs.Read(r, 0),
                r.GetFieldValue<DateTimeOffset>(4),
                r.IsDBNull(5) ? null : new DayStanding(r.GetInt32(5), r.GetInt32(6), r.GetBoolean(7))));
        }

        return links;
    }

    private async Task<IReadOnlyList<LifeDebtView>> DebtsAsync(string mine, string theirs, PlayerId player, CancellationToken cancellationToken)
    {
        await using var command = Command(
            $"""
            select {PlayerRefs.Columns}, d.day
            from life_debts d join players p on p.id = {theirs} {PlayerRefs.Join}
            where {mine} = @me and p.deleted_at is null and not {PlayerRefs.BlockedBetween("p.id", "@me")}
            order by d.day desc
            limit 100
            """,
            ("me", player.Value));
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var debts = new List<LifeDebtView>();
        while (await r.ReadAsync(cancellationToken))
        {
            debts.Add(new LifeDebtView(PlayerRefs.Read(r, 0), new GameDay(r.GetFieldValue<DateOnly>(4))));
        }

        return debts;
    }

    private NpgsqlCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var command = dataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}
