using System.Collections.Concurrent;
using System.Security.Cryptography;
using NobodyFalls.Api.Auth;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Insights;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Api.Hosting;

/// <summary>
/// Remembers which device and address each player uses (decision Q5: plain IPs, 90 days) and
/// on which days they come (retention). A random, long-lived <c>nf-device</c> cookie names the
/// device. Each instance records a player once per day, address and device.
/// </summary>
internal static partial class ActivityTracking
{
    public const string DeviceCookie = "nf-device";
    private static readonly ConcurrentDictionary<string, byte> Seen = new();
    private static GameDay _seenDay;

    public static IApplicationBuilder UseActivityTracking(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var device = DeviceOf(context);
            if (context.User.PlayerId() is { } player && context.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal))
            {
                await RecordAsync(context, player, device, signIn: false);
            }

            await next(context);
        });

    /// <summary>A new session: recorded at once, whatever was seen today.</summary>
    public static Task RecordSignInAsync(HttpContext context, PlayerId player) =>
        RecordAsync(context, player, DeviceOf(context), signIn: true);

    private static async Task RecordAsync(HttpContext context, PlayerId player, string device, bool signIn)
    {
        var services = context.RequestServices;
        var now = services.GetRequiredService<IClock>().UtcNow;
        var today = GameDay.Of(now);
        if (_seenDay != today)
        {
            Seen.Clear();
            _seenDay = today;
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!signIn && !Seen.TryAdd($"{player}|{ip}|{device}", 0))
        {
            return;
        }

        try
        {
            await services.GetRequiredService<IActivityRecorder>().RecordAsync(
                new AccessSeen(player, ip, device, AgentFamily(context.Request.Headers.UserAgent.ToString()), signIn),
                now,
                context.RequestAborted);
        }
#pragma warning disable CA1031 // Statistics must never break a player's request.
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogFailed(services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ActivityTracking)), e);
        }
    }

    private static string DeviceOf(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(DeviceCookie, out var known) && known.Length is > 10 and <= 64)
        {
            return known;
        }

        if (context.Items.TryGetValue(DeviceCookie, out var assigned) && assigned is string id)
        {
            return id;
        }

        var device = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        context.Items[DeviceCookie] = device;
        context.Response.Cookies.Append(DeviceCookie, device, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromDays(400),
            IsEssential = true,
        });
        return device;
    }

    /// <summary>A coarse browser name, enough to tell devices apart in the admin.</summary>
    public static string AgentFamily(string userAgent)
    {
        var mobile = userAgent.Contains("Mobile", StringComparison.Ordinal) ? "-mobile" : "";
        var family = userAgent switch
        {
            _ when userAgent.Contains("Firefox/", StringComparison.Ordinal) => "firefox",
            _ when userAgent.Contains("Edg/", StringComparison.Ordinal) => "edge",
            _ when userAgent.Contains("Chrome/", StringComparison.Ordinal) => "chrome",
            _ when userAgent.Contains("Safari/", StringComparison.Ordinal) => "safari",
            "" => "unknown",
            _ => "other",
        };
        return family + mobile;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording a player's activity failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
