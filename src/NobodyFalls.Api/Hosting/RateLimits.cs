using System.Globalization;
using System.Threading.RateLimiting;
using NobodyFalls.Api.Auth;
using NobodyFalls.Api.Http;

namespace NobodyFalls.Api.Hosting;

internal sealed class RateLimitOptions
{
    public int PerIpPerMinute { get; set; } = 300;

    public int HandsPerPlayerPerMinute { get; set; } = 12;

    public int FriendsPerPlayerPerMinute { get; set; } = 30;

    public int RegistrationsPerIpPerHour { get; set; } = 10;
}

/// <summary>
/// Rate limiting per IP (every request) and per player (sensitive actions), in memory
/// per instance; a distributed limiter can replace it behind the same policy names.
/// </summary>
internal static class RateLimits
{
    public const string Hands = "hands";
    public const string Friends = "friends";
    public const string Registration = "registration";

    public static IServiceCollection AddNobodyFallsRateLimits(this IServiceCollection services, RateLimitOptions limits)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(IpOf(context), _ => Window(limits.PerIpPerMinute, TimeSpan.FromMinutes(1))));

            options.AddPolicy(Hands, context =>
                RateLimitPartition.GetFixedWindowLimiter(PlayerOrIp(context), _ => Window(limits.HandsPerPlayerPerMinute, TimeSpan.FromMinutes(1))));
            options.AddPolicy(Friends, context =>
                RateLimitPartition.GetFixedWindowLimiter(PlayerOrIp(context), _ => Window(limits.FriendsPerPlayerPerMinute, TimeSpan.FromMinutes(1))));
            options.AddPolicy(Registration, context =>
                RateLimitPartition.GetFixedWindowLimiter(IpOf(context), _ => Window(limits.RegistrationsPerIpPerHour, TimeSpan.FromHours(1))));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await Problems.Code(StatusCodes.Status429TooManyRequests, "request.rate_limited", "Too many requests. Please slow down.")
                    .ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) => new()
    {
        PermitLimit = permits,
        Window = window,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static string IpOf(HttpContext context) =>
        "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private static string PlayerOrIp(HttpContext context) =>
        context.User.PlayerId() is { } player ? $"player:{player}" : IpOf(context);
}
