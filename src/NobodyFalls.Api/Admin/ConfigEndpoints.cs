using System.Globalization;
using NobodyFalls.Api.Http;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Config;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Domain.Staff;

namespace NobodyFalls.Api.Admin;

public sealed record RulesRequest(GameRules? Rules, string? Comment);

public sealed record StormRequest(string? Day);

public sealed record SeasonRequest(string? Name, string? OpeningDay, string? LastDay);

public sealed record FlagRequest(bool? Enabled);

/// <summary>
/// Live configuration (GAME_DESIGN §9): rules, storms, seasons and feature flags. Reads are
/// open to the permission; every change asks for a fresh code and is audited by its store.
/// </summary>
internal static class ConfigEndpoints
{
    public static void MapConfig(RouteGroupBuilder admin)
    {
        var config = admin.MapGroup("/config");
        MapRules(config.MapGroup("/rules"));
        MapStorms(config.MapGroup("/storms"));
        MapSeasons(config.MapGroup("/seasons"));
        MapFlags(config.MapGroup("/flags"));
    }

    private static void MapRules(RouteGroupBuilder rules)
    {
        rules.MapGet("", async (IRulesConfig config, IClock clock, CancellationToken ct) =>
            Results.Ok(await config.GetAsync(GameDay.Of(clock.UtcNow), ct))).Requires(Permission.ConfigRules);

        rules.MapGet("/versions", async (IRulesConfig config, CancellationToken ct) =>
            Results.Ok(await config.VersionsAsync(20, ct))).Requires(Permission.ConfigRules);

        rules.MapPut("", async (RulesRequest body, HttpContext http, IRulesConfig config, IClock clock, CancellationToken ct) =>
        {
            var saved = await config.SaveAsync(body.Rules, body.Comment, http.Actor(), clock.UtcNow, ct);
            return saved.IsSuccess ? Results.Ok(new { version = saved.Value }) : Problems.From(saved.Error!);
        }).Requires(Permission.ConfigRules, sensitive: true);
    }

    private static void MapStorms(RouteGroupBuilder storms)
    {
        // The last month too, so that the team sees the storms that just happened.
        // The game's today (simulated in dev) tells the admin which days can still change.
        storms.MapGet("", async (IStormSchedule schedule, IClock clock, CancellationToken ct) =>
        {
            var today = GameDay.Of(clock.UtcNow);
            return Results.Ok(new { today = today.ToString(), storms = await schedule.ListAsync(today.AddDays(-30), ct) });
        }).Requires(Permission.ConfigStorms);

        storms.MapPost("", async (StormRequest body, HttpContext http, IStormSchedule schedule, IClock clock, CancellationToken ct) =>
            DayOf(body.Day) is { } day
                ? Problems.Of(await schedule.ScheduleAsync(day, http.Actor(), clock.UtcNow, ct))
                : Problems.From(StormErrors.Day)).Requires(Permission.ConfigStorms, sensitive: true);

        storms.MapDelete("/{day}", async (string day, HttpContext http, IStormSchedule schedule, IClock clock, CancellationToken ct) =>
            DayOf(day) is { } parsed
                ? Problems.Of(await schedule.CancelAsync(parsed, http.Actor(), clock.UtcNow, ct))
                : Problems.From(StormErrors.Day)).Requires(Permission.ConfigStorms, sensitive: true);
    }

    private static void MapSeasons(RouteGroupBuilder seasons)
    {
        seasons.MapGet("", async (ISeasonConfig config, IClock clock, CancellationToken ct) =>
            Results.Ok(new { today = GameDay.Of(clock.UtcNow).ToString(), seasons = await config.ListAsync(ct) })).Requires(Permission.ConfigSeasons);

        seasons.MapPost("", async (SeasonRequest body, HttpContext http, ISeasonConfig config, IClock clock, CancellationToken ct) =>
        {
            if (!TryDates(body, out var opening, out var last))
            {
                return Problems.From(SeasonDates);
            }

            var planned = await config.PlanAsync(body.Name, opening, last, http.Actor(), clock.UtcNow, ct);
            return planned.IsSuccess ? Results.Ok(new { number = planned.Value }) : Problems.From(planned.Error!);
        }).Requires(Permission.ConfigSeasons, sensitive: true);

        seasons.MapPatch("/{number:int}", async (int number, SeasonRequest body, HttpContext http, ISeasonConfig config, IClock clock, CancellationToken ct) =>
            TryDates(body, out var opening, out var last)
                ? Problems.Of(await config.ChangeAsync(number, body.Name, opening, last, http.Actor(), clock.UtcNow, ct))
                : Problems.From(SeasonDates)).Requires(Permission.ConfigSeasons, sensitive: true);
    }

    private static void MapFlags(RouteGroupBuilder flags)
    {
        flags.MapGet("", async (IFeatureFlags features, CancellationToken ct) =>
            Results.Ok(await features.ListAsync(ct))).Requires(Permission.ConfigFlags);

        flags.MapPut("/{key}", async (string key, FlagRequest body, HttpContext http, IFeatureFlags features, IClock clock, CancellationToken ct) =>
            Problems.Of(await features.SetAsync(key, body.Enabled, http.Actor(), clock.UtcNow, ct))).Requires(Permission.ConfigFlags, sensitive: true);
    }

    private static readonly DomainError SeasonDates = new("season.dates", "Give the days as YYYY-MM-DD; the last day is optional.");

    private static GameDay? DayOf(string? iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? new GameDay(date) : null;

    private static bool TryDates(SeasonRequest body, out GameDay opening, out GameDay? last)
    {
        last = string.IsNullOrEmpty(body.LastDay) ? null : DayOf(body.LastDay);
        if (DayOf(body.OpeningDay) is { } open && (string.IsNullOrEmpty(body.LastDay) || last is not null))
        {
            opening = open;
            return true;
        }

        opening = default;
        return false;
    }
}
