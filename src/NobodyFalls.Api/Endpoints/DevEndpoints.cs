using NobodyFalls.Api.Auth;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Dev;
using NobodyFalls.Application.Night;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Api.Endpoints;

public sealed record ClockRequest(double? AdvanceMinutes, int? ToMidnightMinusSeconds, bool? Reset);

public sealed record ClockResponse(DateTimeOffset Now, GameDay Day, DateTimeOffset MidnightAt, double OffsetHours);

public sealed record SeedRequest(int? Players, int? FriendsPerPlayer);

public sealed record BotsRequest(double? Ratio);

/// <summary>
/// Development tools (GAME_DESIGN §14): fake players, bots, simulated clock, running the
/// night on demand. Only mapped when <c>DevTools:Enabled</c>, never in Production.
/// </summary>
internal static class DevEndpoints
{
    public static void MapDevEndpoints(this RouteGroupBuilder dev)
    {
        dev.WithTags("Dev");

        dev.MapGet("/clock", (IClock clock, IClockControl control) => Results.Ok(Clock(clock, control)));

        dev.MapPost("/clock", async (ClockRequest body, IClock clock, IClockControl control, CancellationToken ct) =>
        {
            var offset = control.Offset;
            if (body.Reset == true)
            {
                offset = TimeSpan.Zero;
            }
            else if (body.ToMidnightMinusSeconds is { } seconds)
            {
                var now = clock.UtcNow;
                offset += GameDay.Of(now).MidnightAt.AddSeconds(-seconds) - now;
            }
            else if (body.AdvanceMinutes is { } minutes)
            {
                offset += TimeSpan.FromMinutes(minutes);
            }

            await control.SetOffsetAsync(offset, ct);
            return Results.Ok(Clock(clock, control));
        });

        // Start over: an empty world, the real time, a season opening today.
        dev.MapPost("/reset", async (IDevStore store, IClockControl control, IClock clock, INightStore night, CancellationToken ct) =>
        {
            await store.ResetAsync(ct);
            await control.SetOffsetAsync(TimeSpan.Zero, ct);
            var now = clock.UtcNow;
            await store.EnsureSeasonAsync(GameDay.Of(now), ct);
            await night.EnsureDayStartedAsync(GameDay.Of(now), now, ct);
            return Results.Ok(Clock(clock, control));
        });

        dev.MapPost("/season", async (IDevStore store, IClock clock, CancellationToken ct) =>
            Results.Ok(new { created = await store.EnsureSeasonAsync(GameDay.Of(clock.UtcNow), ct) }));

        dev.MapPost("/seed", async (SeedRequest body, IDevStore store, IClock clock, CancellationToken ct) =>
        {
            await store.EnsureSeasonAsync(GameDay.Of(clock.UtcNow), ct);
            var ids = await store.SeedAsync(Math.Clamp(body.Players ?? 20, 1, 5000), Math.Clamp(body.FriendsPerPlayer ?? 3, 0, 50), clock.UtcNow, ct);
            return Results.Ok(new { players = ids.Count });
        });

        dev.MapPost("/bots/play", async (BotsRequest body, SimulateBots bots, CancellationToken ct) =>
            Results.Ok(await bots.ExecuteAsync(Math.Clamp(body.Ratio ?? 0.7, 0, 1), Environment.TickCount, ct)));

        dev.MapPost("/night/tick", async (NightCycle cycle, CancellationToken ct) =>
            Results.Ok(await cycle.TickAsync(ct)));

        dev.MapPost("/login-as/{id:guid}", async (Guid id, HttpContext context) =>
        {
            await context.SignInPlayerAsync(new PlayerId(id));
            return Results.NoContent();
        });
    }

    private static ClockResponse Clock(IClock clock, IClockControl control)
    {
        var now = clock.UtcNow;
        return new ClockResponse(now, GameDay.Of(now), GameDay.Of(now).MidnightAt, control.Offset.TotalHours);
    }
}
