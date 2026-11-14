using NobodyFalls.Application.Insights;
using NobodyFalls.Infrastructure.Tests.Support;
using NobodyFalls.Testing;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Infrastructure.Tests.Insights;

/// <summary>Connection traces (decision Q5): one row per day, address and device; forgotten after 90 days.</summary>
public sealed class ActivityTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Traces_older_than_ninety_days_are_forgotten()
    {
        await using var app = await TestApp.StartAsync(postgres, Now);
        var lea = Ids.Of("lea");
        await app.Seed.PlayerAsync(lea);
        var activity = app.Get<IActivityRecorder>();

        await activity.RecordAsync(new AccessSeen(lea, "203.0.113.7", "device-aaaaaaaa", "firefox", SignIn: true), Now.AddDays(-91), CancellationToken.None);
        await activity.RecordAsync(new AccessSeen(lea, "203.0.113.7", "device-aaaaaaaa", "firefox", SignIn: false), Now.AddDays(-10), CancellationToken.None);
        await activity.RecordAsync(new AccessSeen(lea, "203.0.113.7", "device-aaaaaaaa", "firefox", SignIn: false), Now.AddDays(-10).AddHours(1), CancellationToken.None);

        Assert.Equal(2L, await app.Seed.ScalarAsync<long>("select count(*) from login_events"));
        Assert.Equal(1, await activity.PurgeAsync(Now.AddDays(-90), CancellationToken.None));
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from login_events"));
        // Visits (retention) are kept: they hold no address.
        Assert.Equal(2L, await app.Seed.ScalarAsync<long>("select count(*) from player_visits"));
    }
}
