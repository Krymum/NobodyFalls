using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Accounts;
using NobodyFalls.Application.Audit;
using NobodyFalls.Application.Dev;
using NobodyFalls.Application.Friends;
using NobodyFalls.Application.Hands;
using NobodyFalls.Application.Night;
using NobodyFalls.Application.Players;
using NobodyFalls.Application.Push;
using NobodyFalls.Application.Queries;
using NobodyFalls.Application.Realtime;
using NobodyFalls.Application.Staff;
using NobodyFalls.Infrastructure.Accounts;
using NobodyFalls.Infrastructure.Audit;
using NobodyFalls.Infrastructure.Clock;
using NobodyFalls.Infrastructure.Dev;
using NobodyFalls.Infrastructure.Friends;
using NobodyFalls.Infrastructure.Hands;
using NobodyFalls.Infrastructure.Night;
using NobodyFalls.Infrastructure.Persistence;
using NobodyFalls.Infrastructure.Players;
using NobodyFalls.Infrastructure.Push;
using NobodyFalls.Infrastructure.Queries;
using NobodyFalls.Infrastructure.Settings;
using NobodyFalls.Infrastructure.Staff;
using Npgsql;

namespace NobodyFalls.Infrastructure;

public sealed class InfrastructureOptions
{
    public required string ConnectionString { get; init; }

    /// <summary>Development only: the clock can be moved forward (ADR 0006).</summary>
    public bool SimulatedClock { get; init; }

    /// <summary>Run the background workers (night cycle, notifications); tests drive them by hand.</summary>
    public bool RunNightWorker { get; init; } = true;
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, InfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(options.ConnectionString).Build());
        services.AddDbContext<NobodyFallsDbContext>((sp, db) => db.UseNobodyFalls(sp.GetRequiredService<NpgsqlDataSource>()));

        services.AddSingleton<IGameSettingsProvider, DbGameSettingsProvider>();
        services.AddSingleton<INightStore, PostgresNightStore>();
        services.AddSingleton<IHandLedger, PostgresHandLedger>();
        services.AddSingleton<IPlayerLifecycleStore, PostgresPlayerLifecycleStore>();
        services.AddScoped(sp => new DatabaseMigrator(sp.GetRequiredService<NobodyFallsDbContext>()));
        services.AddSingleton<ISkinCatalogProvider, SkinCatalogProvider>();
        services.AddSingleton<IGameQueries, PostgresGameQueries>();
        services.AddSingleton<IAccountStore, PostgresAccountStore>();
        services.AddSingleton<IPlayerSettingsStore, PostgresPlayerSettingsStore>();
        services.AddSingleton<PostgresFriendStore>();
        services.AddSingleton<IFriendStore>(sp => sp.GetRequiredService<PostgresFriendStore>());
        services.AddSingleton<IFriendGraph>(sp => sp.GetRequiredService<PostgresFriendStore>());
        services.AddSingleton<PostgresPushStore>();
        services.AddSingleton<IPushStore>(sp => sp.GetRequiredService<PostgresPushStore>());
        services.AddSingleton<INotificationOutbox>(sp => sp.GetRequiredService<PostgresPushStore>());
        services.AddSingleton<INotificationDirectory>(sp => sp.GetRequiredService<PostgresPushStore>());
        services.AddSingleton<INotificationScheduler, PostgresNotificationScheduler>();
        services.AddSingleton<INotificationTexts, ResxNotificationTexts>();
        services.AddSingleton<VapidKeyStore>();
        services.AddSingleton<IPushKeys>(sp => sp.GetRequiredService<VapidKeyStore>());
        services.AddHttpClient<IPushSender, WebPushSender>(http => http.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<IDevStore, PostgresDevStore>();
        services.AddHttpClient<IHumanVerifier, TurnstileVerifier>(http => http.Timeout = TimeSpan.FromSeconds(5));

        // Phase 2: team, two-factor sign-in and audit (ADR 0018–0020).
        services.AddSingleton<IAuditLog, PostgresAuditLog>();
        services.AddSingleton<StaffAccessCache>();
        services.AddSingleton<SessionVersionCache>();
        services.AddSingleton<ISessionVersions>(sp => sp.GetRequiredService<SessionVersionCache>());
        services.AddSingleton<IStaffStore, PostgresStaffStore>();
        services.AddSingleton<ITotpStore, PostgresTotpStore>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        if (options.SimulatedClock)
        {
            services.AddSingleton<SimulatedClock>();
            services.AddSingleton<IClock>(sp => sp.GetRequiredService<SimulatedClock>());
            services.AddSingleton<IClockControl>(sp => sp.GetRequiredService<SimulatedClock>());
            services.AddHostedService<SimulatedClockSync>();
        }
        else
        {
            services.AddSingleton<IClock, SystemClock>();
        }

        if (options.RunNightWorker)
        {
            services.AddHostedService<NightWorker>();
            services.AddHostedService<NotificationWorker>();
        }

        return services;
    }

    /// <summary>Stores Data Protection keys (session cookies) in PostgreSQL.</summary>
    public static IDataProtectionBuilder PersistKeysToNobodyFallsDatabase(this IDataProtectionBuilder builder) =>
        builder.PersistKeysToDbContext<NobodyFallsDbContext>();

    internal static DbContextOptionsBuilder UseNobodyFalls(this DbContextOptionsBuilder builder, NpgsqlDataSource dataSource) =>
        builder
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
