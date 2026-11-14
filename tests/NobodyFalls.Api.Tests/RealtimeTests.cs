using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using NobodyFalls.Api.Tests.Support;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Api.Tests;

public sealed class RealtimeTests(PostgresFixture postgres)
{
    /// <summary>Collects the events a connection receives.</summary>
    private sealed class Inbox
    {
        private readonly ConcurrentQueue<JsonElement> _events = new();

        public Inbox(HubConnection hub) =>
            hub.On<JsonElement>("batch", batch =>
            {
                foreach (var e in batch.GetProperty("events").EnumerateArray())
                {
                    _events.Enqueue(e.Clone());
                }
            });

        public async Task<JsonElement> WaitForAsync(string type)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                var match = _events.FirstOrDefault(e => e.GetProperty("type").GetString() == type);
                if (match.ValueKind != JsonValueKind.Undefined)
                {
                    return match;
                }

                await Task.Delay(25);
            }

            throw new TimeoutException($"No '{type}' event. Received: {string.Join(", ", _events.Select(e => e.GetProperty("type").GetString()))}");
        }

        public bool Has(string type) => _events.Any(e => e.GetProperty("type").GetString() == type);
    }

    [Fact]
    public async Task A_hand_reaches_the_receiver_their_friends_and_the_edge_live()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (giver, _, giverHub) = await app.ConnectAsync("giver");
        var (yuki, yukiId, yukiHub) = await app.ConnectAsync("yuki");
        var (friend, _, friendHub) = await app.ConnectAsync("friend");
        var (_, _, watcherHub) = await app.ConnectAsync("watcher");
        await friend.PostAsJsonAsync("/api/v1/friends/requests", new { pseudo = "yuki" });
        await yuki.PostAsJsonAsync("/api/v1/friends/requests", new { pseudo = "friend" });

        var giverInbox = new Inbox(giverHub);
        var yukiInbox = new Inbox(yukiHub);
        var friendInbox = new Inbox(friendHub);
        var watcherInbox = new Inbox(watcherHub);
        foreach (var hub in new[] { giverHub, yukiHub, friendHub, watcherHub })
        {
            await hub.StartAsync();
        }

        await watcherHub.InvokeAsync("WatchEdge", true);

        var hand = await giver.PostAsJsonAsync("/api/v1/hands", new { receiverId = yukiId, turnstileToken = FakeHumanVerifier.Valid });
        Assert.Equal(HttpStatusCode.Created, hand.StatusCode);

        Assert.Equal(2, (await giverInbox.WaitForAsync("me.hands")).GetProperty("handsLeft").GetInt32());
        Assert.Equal(1, (await yukiInbox.WaitForAsync("me.handReceived")).GetProperty("received").GetInt32());
        Assert.Equal(yukiId, (await friendInbox.WaitForAsync("player.standing")).GetProperty("playerId").GetGuid());
        Assert.Equal(yukiId, (await watcherInbox.WaitForAsync("edge.changed")).GetProperty("playerId").GetGuid());
        Assert.False(giverInbox.Has("edge.changed")); // the giver is not watching the Edge
    }

    [Fact]
    public async Task A_friend_request_appears_live()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, _, _) = await app.ConnectAsync("lea");
        var (_, _, kenjiHub) = await app.ConnectAsync("kenji");
        var inbox = new Inbox(kenjiHub);
        await kenjiHub.StartAsync();

        await lea.PostAsJsonAsync("/api/v1/friends/requests", new { pseudo = "kenji" });

        await inbox.WaitForAsync("friend.request");
    }

    [Fact]
    public async Task Everyone_learns_that_the_night_was_resolved()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (client, _, hub) = await app.ConnectAsync("someone");
        var inbox = new Inbox(hub);
        await hub.StartAsync();

        app.Clock.UtcNow = ApiApp.Day.MidnightAt;
        await client.PostAsync(new Uri("/dev/night/tick", UriKind.Relative), null);

        var night = await inbox.WaitForAsync("night.resolved");
        Assert.Equal("2026-11-13", night.GetProperty("day").GetString());
    }

    [Fact]
    public async Task The_hub_refuses_anonymous_connections()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri("https://localhost/hubs/game"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
            })
            .Build();

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => hub.StartAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task Push_subscriptions_and_preferences_round_trip()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (me, _) = await app.SignUpAsync("pushed");

        var key = await app.Browser().GetFromJsonAsync<JsonElement>(new Uri("/api/v1/push/vapid-key", UriKind.Relative));
        Assert.Equal(87, key.GetProperty("publicKey").GetString()!.Length);

        var insecure = await me.PostAsJsonAsync("/api/v1/push/subscriptions", new { endpoint = "http://insecure.example/x", keys = new { p256dh = "k", auth = "a" } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, insecure.StatusCode);
        var valid = await me.PostAsJsonAsync("/api/v1/push/subscriptions", new { endpoint = "https://push.example/abc", keys = new { p256dh = "k", auth = "a" } });
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from push_subscriptions"));

        await me.PutAsJsonAsync("/api/v1/me/notifications", new { friendBelow = false, friendFell = true, savedMe = true, fell = true, storm = false });
        var preferences = await me.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/me/notifications", UriKind.Relative));
        Assert.False(preferences.GetProperty("friendBelow").GetBoolean());
        Assert.False(preferences.GetProperty("storm").GetBoolean());

        await me.DeleteAsync(new Uri("/api/v1/push/subscriptions?endpoint=https%3A%2F%2Fpush.example%2Fabc", UriKind.Relative));
        Assert.Equal(0L, await app.Seed.ScalarAsync<long>("select count(*) from push_subscriptions"));
    }
}
