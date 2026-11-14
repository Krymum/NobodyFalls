using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NobodyFalls.Application;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Hands;
using NobodyFalls.Application.Night;
using NobodyFalls.Application.Players;
using NobodyFalls.Testing.Postgres;
using Npgsql;

namespace NobodyFalls.Infrastructure.Tests.Support;

/// <summary>A clock the test moves by hand.</summary>
public sealed class ManualClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>The real infrastructure wired against a fresh database.</summary>
public sealed class TestApp : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private TestApp(ServiceProvider services, string connectionString)
    {
        _services = services;
        ConnectionString = connectionString;
        Data = services.GetRequiredService<NpgsqlDataSource>();
        Seed = new Seeder(Data);
    }

    public string ConnectionString { get; }

    public NpgsqlDataSource Data { get; }

    public Seeder Seed { get; }

    public ManualClock Clock => (ManualClock)_services.GetRequiredService<IClock>();

    public static async Task<TestApp> StartAsync(PostgresFixture postgres, DateTimeOffset now, Action<IServiceCollection>? configure = null)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection()
            .AddLogging(l => l.SetMinimumLevel(LogLevel.Warning))
            .AddApplication()
            .AddInfrastructure(new InfrastructureOptions { ConnectionString = connectionString, RunNightWorker = false })
            .AddSingleton<IClock>(new ManualClock(now));
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        configure?.Invoke(services);
        var provider = services
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        return new TestApp(provider, connectionString);
    }

    public T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

    public INightStore Night => Get<INightStore>();

    public IHandLedger Hands => Get<IHandLedger>();

    public IPlayerLifecycleStore Lifecycle => Get<IPlayerLifecycleStore>();

    public NightCycle Cycle => _services.CreateScope().ServiceProvider.GetRequiredService<NightCycle>();

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();
}
