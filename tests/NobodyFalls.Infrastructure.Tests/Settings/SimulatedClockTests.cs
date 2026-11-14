using Microsoft.Extensions.DependencyInjection;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Infrastructure.Clock;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Infrastructure.Tests.Settings;

public sealed class SimulatedClockTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_offset_is_shared_through_the_database()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var options = new InfrastructureOptions { ConnectionString = connectionString, SimulatedClock = true, RunNightWorker = false };
        await using var first = new ServiceCollection().AddInfrastructure(options).BuildServiceProvider();
        await using var second = new ServiceCollection().AddInfrastructure(options).BuildServiceProvider();

        await first.GetRequiredService<IClockControl>().SetOffsetAsync(TimeSpan.FromHours(5), CancellationToken.None);
        await second.GetRequiredService<SimulatedClock>().RefreshAsync(CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        Assert.InRange(second.GetRequiredService<IClock>().UtcNow - now, TimeSpan.FromHours(5) - TimeSpan.FromSeconds(5), TimeSpan.FromHours(5) + TimeSpan.FromSeconds(5));
    }
}
