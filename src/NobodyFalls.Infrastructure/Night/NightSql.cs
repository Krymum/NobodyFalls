namespace NobodyFalls.Infrastructure.Night;

/// <summary>
/// The midnight resolution as set-based SQL. Every statement mirrors a step of the
/// reference implementation (<c>NobodyFalls.Domain.Night.NightResolver</c>); the
/// differential tests require both to agree (ADR 0003).
/// </summary>
/// <remarks>Parameters: <c>@day</c> (date), <c>@season</c>, <c>@now</c>, and the rule values.</remarks>
internal static class NightSql
{
    /// <summary>1. Hands received, recounted from <c>hands</c>, the source of truth.</summary>
    public const string RecountReceived = """
        update player_days pd
        set received = (select count(*) from hands h where h.day = pd.day and h.receiver_id = pd.player_id)
        where pd.day = @day;
        """;

    /// <summary>2. Who falls: immune newcomers cannot; others need their threshold.</summary>
    public const string Outcomes = """
        update player_days
        set outcome = case when immune then 'immune' when received >= required then 'survived' else 'fell' end
        where day = @day;
        """;

    /// <summary>3. Falls are recorded; nothing else is taken from the player.</summary>
    public const string Falls = """
        update players p
        set status = 'fallen', falls_count = p.falls_count + 1, last_fall_day = @day, returns_on_day = null
        from player_days pd
        where pd.day = @day and pd.outcome = 'fell' and pd.player_id = p.id and p.status = 'active';

        insert into player_falls (player_id, day, season_id)
        select player_id, @day, @season from player_days where day = @day and outcome = 'fell'
        on conflict do nothing;
        """;

    /// <summary>
    /// 4. Guardian points per hand. The rank of a hand among those its receiver got,
    /// in giving order, tells which hand reached the threshold (the "last stand").
    /// </summary>
    public const string ScoreHands = """
        with ranked as (
            select h.id, h.giver_id, h.receiver_id, h.giver_kind, h.giver_was_friend, h.given_at,
                   row_number() over (partition by h.receiver_id order by h.given_at, h.id) as rank
            from hands h
            where h.day = @day
        ), flagged as (
            select r.id, r.giver_was_friend,
                   (r.giver_kind = 'human' and exists (
                        select 1 from hands i
                        where i.day = @day and i.giver_id = r.receiver_id and i.receiver_id = r.giver_id
                          and i.giver_kind = 'human')) as reciprocal,
                   r.giver_kind, r.rank, r.given_at, pd.outcome, pd.required
            from ranked r
            left join player_days pd on pd.day = @day and pd.player_id = r.receiver_id
        ), decided as (
            select id, giver_was_friend, reciprocal,
                   (giver_kind = 'human' and not reciprocal and outcome = 'survived'
                        and given_at >= @last_rampart_from and rank = required) as last_rampart,
                   (giver_kind = 'human' and not reciprocal and outcome in ('survived', 'immune')) as earns
            from flagged
        )
        update hands h
        set reciprocal = d.reciprocal,
            last_rampart = d.last_rampart,
            points = case when d.earns
                          then (case when d.giver_was_friend then @points_friend else @points_stranger end)
                             + (case when d.last_rampart then @last_rampart_bonus else 0 end)
                          else 0 end
        from decided d
        where h.id = d.id;
        """;

    /// <summary>Points earned by each giver tonight, kept for the following statements.</summary>
    public const string Awards = """
        create temporary table night_awards (giver_id uuid primary key, points int not null) on commit drop;

        insert into night_awards (giver_id, points)
        select giver_id, sum(points)::int
        from hands
        where day = @day
        group by giver_id
        having sum(points) > 0;

        update players p set guardian_score = p.guardian_score + a.points
        from night_awards a where p.id = a.giver_id;

        update player_days pd set score_gained = a.points
        from night_awards a where pd.day = @day and pd.player_id = a.giver_id;

        insert into season_scores (season_id, player_id, points)
        select @season, giver_id, points from night_awards
        on conflict (season_id, player_id) do update set points = season_scores.points + excluded.points;
        """;

    /// <summary>5. A survivor who had exactly their threshold owes their life to each human giver.</summary>
    public const string LifeDebts = """
        insert into life_debts (day, saved_id, saver_id)
        select distinct @day, h.receiver_id, h.giver_id
        from hands h
        join player_days pd on pd.day = @day and pd.player_id = h.receiver_id
        where h.day = @day and h.giver_kind = 'human'
          and pd.outcome = 'survived' and pd.required > 0 and pd.received = pd.required
        on conflict do nothing;
        """;

    /// <summary>6. Players who held each other, in either direction, feed friend suggestions.</summary>
    public const string HoldPairs = """
        insert into hold_pairs (player_low, player_high, nights, last_day)
        select distinct least(giver_id, receiver_id), greatest(giver_id, receiver_id), 1, @day
        from hands
        where day = @day and giver_kind = 'human'
        on conflict (player_low, player_high) do update
        set nights = hold_pairs.nights + 1, last_day = excluded.last_day
        where hold_pairs.last_day < excluded.last_day;
        """;

    /// <summary>Counters behind skin unlock conditions, updated for everyone involved tonight.</summary>
    public const string Stats = """
        insert into player_stats (player_id, rescues, strangers_held, nights_survived, storms_survived, updated_day)
        select player_id, sum(rescues), sum(strangers), sum(nights), sum(storms), @day
        from (
            select saver_id as player_id, 1 as rescues, 0 as strangers, 0 as nights, 0 as storms
            from life_debts where day = @day
            union all
            select giver_id, 0, 1, 0, 0
            from hands where day = @day and giver_kind = 'human' and points > 0 and not giver_was_friend
            union all
            select player_id, 0, 0, 1, case when @storm then 1 else 0 end
            from player_days where day = @day and outcome = 'survived'
            union all
            select giver_id, 0, 0, 0, 0 from night_awards
        ) s
        group by player_id
        on conflict (player_id) do update
        set rescues = player_stats.rescues + excluded.rescues,
            strangers_held = player_stats.strangers_held + excluded.strangers_held,
            nights_survived = player_stats.nights_survived + excluded.nights_survived,
            storms_survived = player_stats.storms_survived + excluded.storms_survived,
            updated_day = excluded.updated_day;
        """;

    /// <summary>8. Skins unlocked by solidarity; only players whose counters moved tonight are checked.</summary>
    public const string SkinUnlocks = """
        insert into player_skin_unlocks (player_id, item_id, day, reason)
        select s.player_id, i.id, @day, 'condition'
        from player_stats s
        join players p on p.id = s.player_id
        join skin_items i on i.rarity = 'unlockable' and i.active
        where s.updated_day = @day
          and case i.unlock_metric
                  when 'rescues' then s.rescues
                  when 'strangers_held' then s.strangers_held
                  when 'nights_survived' then s.nights_survived
                  when 'storms_survived' then s.storms_survived
                  when 'guardian_score' then p.guardian_score
              end >= i.unlock_threshold
        on conflict do nothing;
        """;

    /// <summary>7. World counters, from the last resolved night (0 for the very first).</summary>
    public const string PreviousCounters = """
        select coalesce(streak_after, 0), coalesce(deaths_all_time_after, 0)
        from game_days
        where day < @day and resolved_at is not null
        order by day desc
        limit 1;
        """;

    public const string CloseDay = """
        update game_days
        set falls = @falls, streak_after = @streak, deaths_all_time_after = @deaths, resolved_at = @now
        where day = @day;

        update seasons set deaths = deaths + @falls where id = @season;
        """;

    /// <summary>
    /// 10. Notifications, written in the same transaction (outbox): they are sent after
    /// commit, exactly once thanks to the dedupe key. Surviving is the normal case and
    /// sends nothing; falling, friends falling, being saved and new skins do. The
    /// <c>players</c> array lists who the message is about.
    /// </summary>
    public const string Notifications = """
        insert into notification_outbox (dedupe_key, player_id, kind, payload, created_at)
        select 'night.fell:' || @day::text || ':' || pd.player_id::text, pd.player_id, 'night.fell',
               jsonb_build_object('day', @day, 'players', '[]'::jsonb),
               @now
        from player_days pd
        where pd.day = @day and pd.outcome = 'fell'
        on conflict (dedupe_key) do nothing;

        insert into notification_outbox (dedupe_key, player_id, kind, payload, created_at)
        select 'friends.fell:' || @day::text || ':' || f.friend_id::text, f.friend_id, 'friends.fell',
               jsonb_build_object('day', @day, 'players', jsonb_agg(f.fallen_id order by f.fallen_id)),
               @now
        from (
            select pd.player_id as fallen_id,
                   case when fr.player_low = pd.player_id then fr.player_high else fr.player_low end as friend_id
            from player_days pd
            join friendships fr on pd.player_id in (fr.player_low, fr.player_high)
            where pd.day = @day and pd.outcome = 'fell'
        ) f
        group by f.friend_id
        on conflict (dedupe_key) do nothing;

        insert into notification_outbox (dedupe_key, player_id, kind, payload, created_at)
        select 'life.saved:' || @day::text || ':' || saved_id::text, saved_id, 'life.saved',
               jsonb_build_object('day', @day, 'players', jsonb_agg(saver_id order by saver_id)),
               @now
        from life_debts
        where day = @day
        group by saved_id
        on conflict (dedupe_key) do nothing;

        insert into notification_outbox (dedupe_key, player_id, kind, payload, created_at)
        select 'skins.unlocked:' || @day::text || ':' || player_id::text, player_id, 'skins.unlocked',
               jsonb_build_object('day', @day, 'players', '[]'::jsonb, 'items', jsonb_agg(item_id order by item_id)),
               @now
        from player_skin_unlocks
        where day = @day and reason = 'condition'
        group by player_id
        on conflict (dedupe_key) do nothing;
        """;

    // ---- Starting a day ----

    public const string OpenSeasonOn = """
        select id, opening_day from seasons
        where opening_day <= @day and (last_day is null or last_day >= @day)
        order by number desc
        limit 1;
        """;

    /// <summary>Fallen players who came back late, and the waiting list when a season is open, enter the day.</summary>
    public const string EnterPlayers = """
        update players set status = 'active', returns_on_day = null
        where kind = 'human' and status = 'fallen' and returns_on_day <= @day and deleted_at is null;

        update players set status = 'active', joined_day = @day
        where kind = 'human' and status = 'waitlisted' and deleted_at is null;
        """;

    public const string CountAlive = """
        select count(*)::int from players where kind = 'human' and status = 'active' and deleted_at is null;
        """;

    public const string InsertGameDay = """
        insert into game_days (day, season_id, night_number, base_required, storm, alive_at_start,
                               watcher_count, settings_version, started_at)
        values (@day, @season, @night_number, @base_required, exists (select 1 from storms where day = @day),
                @alive, @watchers, @settings_version, @now)
        on conflict (day) do nothing
        returning storm;
        """;

    /// <summary>Everyone alive is in play; thresholds mirror <c>ThresholdPolicy.Required</c>.</summary>
    public const string InsertPlayerDays = """
        insert into player_days (day, player_id, required, immune, received, joined_at, score_gained)
        select @day, p.id,
               @base_required
                 + case when @storm then @storm_extra else 0 end
                 + case when p.joined_day = @day - 1 then @fragile_extra else 0 end,
               p.joined_day = @day,
               0, @now, 0
        from players p
        where p.kind = 'human' and p.status = 'active' and p.deleted_at is null
        on conflict do nothing;
        """;
}
