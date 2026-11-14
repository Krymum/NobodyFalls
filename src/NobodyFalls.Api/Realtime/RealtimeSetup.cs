using Microsoft.AspNetCore.SignalR;
using NobodyFalls.Api.Http;
using NobodyFalls.Application.Realtime;

namespace NobodyFalls.Api.Realtime;

internal static class RealtimeSetup
{
    /// <summary>
    /// SignalR with every transport (WebSockets, Server-Sent Events, long polling as the
    /// fallback), JSON with the API's conventions, and the Redis backplane when Redis is
    /// configured, so that several instances reach each other's connections.
    /// </summary>
    public static IServiceCollection AddNobodyFallsRealtime(this IServiceCollection services, IConfiguration configuration, bool runWorkers)
    {
        var signalR = services
            .AddSignalR(options => options.EnableDetailedErrors = false)
            .AddJsonProtocol(options => Json.Configure(options.PayloadSerializerOptions));

        if (configuration.GetConnectionString("Redis") is { Length: > 0 } redis)
        {
            signalR.AddStackExchangeRedis(redis, options => options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("nobodyfalls"));
        }

        services.AddSingleton<IUserIdProvider, PlayerUserIdProvider>();
        services.AddSingleton(configuration.GetSection("Realtime").Get<RealtimeOptions>() ?? new RealtimeOptions());
        services.AddSingleton<BatchingRealtimePublisher>();
        services.AddSingleton<IRealtimePublisher>(sp => sp.GetRequiredService<BatchingRealtimePublisher>());
        services.AddHostedService(sp => sp.GetRequiredService<BatchingRealtimePublisher>());

        if (runWorkers)
        {
            services.AddHostedService<WorldStatsBroadcaster>();
        }

        return services;
    }
}
