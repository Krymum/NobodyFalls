using System.Net;
using NobodyFalls.Api.Tests.Support;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Api.Tests;

public sealed class DevToolsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Reset_starts_over_with_an_empty_world_and_an_open_season()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        await app.SignUpAsync("before");
        var client = app.Browser();

        var reset = await client.PostAsync(new Uri("/dev/reset", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(0L, await app.Seed.ScalarAsync<long>("select count(*) from players"));
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from seasons"));
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from game_days where resolved_at is null"));
        Assert.True(await app.Seed.ScalarAsync<long>("select count(*) from skin_items") > 0);

        // The same person can sign up again.
        await app.SignUpAsync("before");
    }
}
