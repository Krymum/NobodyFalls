using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Hands;
using NobodyFalls.Application.Night;
using NobodyFalls.Application.Players;
using NobodyFalls.Infrastructure.Clock;
using NobodyFalls.Infrastructure.Hands;
using NobodyFalls.Infrastructure.Night;
using NobodyFalls.Infrastructure.Persistence;
using NobodyFalls.Infrastructure.Players;
using NobodyFalls.Infrastructure.Settings;
using Npgsql;

namespace NobodyFalls.Infrastructure;

public sealed class InfrastructureOptions
{
    public required string ConnectionString { get; init; }

    /// <summary>Development only: the clock can be moved forward (ADR 0006).</summary>
    public bool SimulatedClock { get; init; }

    /// <summary>Run the night cycle in this process (disable in tests that drive it by hand).</summary>
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
        }

        return services;
    }

    internal static DbContextOptionsBuilder UseNobodyFalls(this DbContextOptionsBuilder builder, NpgsqlDataSource dataSource) =>
        builder
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
