using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using NobodyFalls.Application.Push;
using NobodyFalls.Domain.Common;
using NobodyFalls.Infrastructure.Push;
using NobodyFalls.Infrastructure.Tests.Support;
using NobodyFalls.Testing;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Infrastructure.Tests.Push;

/// <summary>Records what would have been pushed; a subscription can be set to fail or be gone.</summary>
public sealed class FakePushSender : IPushSender
{
    public ConcurrentQueue<(string Endpoint, PushMessage Message)> Sent { get; } = new();

    public ConcurrentDictionary<string, PushDelivery> Outcomes { get; } = new();

    public Task<PushDelivery> SendAsync(PushSubscription subscription, PushMessage message, CancellationToken cancellationToken)
    {
        var outcome = Outcomes.GetValueOrDefault(subscription.Endpoint, PushDelivery.Delivered);
        if (outcome == PushDelivery.Delivered)
        {
            Sent.Enqueue((subscription.Endpoint, message));
        }

        return Task.FromResult(outcome);
    }
}

public sealed class PushTests(PostgresFixture postgres)
{
    private static readonly GameDay Day = GameDay.Parse("2026-11-13");
    private static readonly CancellationToken Ct = CancellationToken.None;

    private sealed record World(TestApp App, FakePushSender Sender) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();

        public Task<DispatchRun> DispatchAsync()
        {
            using var scope = App.Get<IServiceScopeFactory>().CreateScope();
            return scope.ServiceProvider.GetRequiredService<DispatchNotifications>().RunOnceAsync(Ct);
        }

        public Task SubscribeAsync(string name, string? endpoint = null) =>
            App.Get<IPushStore>().SubscribeAsync(Ids.Of(name), new PushSubscription(endpoint ?? $"https://push.example/{name}", "p256dh", "auth"), null, Day.StartsAt, Ct);
    }

    private async Task<World> WorldAsync(DateTimeOffset now, params (string Name, int Required)[] players)
    {
        var sender = new FakePushSender();
        var app = await TestApp.StartAsync(postgres, now, services => services.AddSingleton<IPushSender>(sender));
        await app.Seed.SeasonAsync(Day.AddDays(-10));
        await app.Seed.GameDayAsync(Day);
        foreach (var (name, required) in players)
        {
            await app.Seed.PlayerAsync(Ids.Of(name));
            await app.Seed.PlayerDayAsync(Day, Ids.Of(name), required);
        }

        return new World(app, sender);
    }

    [Fact]
    public async Task Falling_is_pushed_once_in_the_players_language_and_surviving_is_not()
    {
        await using var world = await WorldAsync(Day.MidnightAt, ("faller", 1), ("survivor", 0));
        await world.App.Seed.ExecAsync("update players set locale = 'en' where id = @id", ("id", Ids.Of("faller").Value));
        await world.SubscribeAsync("faller");
        await world.SubscribeAsync("survivor");
        await world.App.Night.ResolveAsync(Day, Day.MidnightAt, Ct);

        await world.DispatchAsync();
        await world.DispatchAsync();

        var (endpoint, message) = Assert.Single(world.Sender.Sent);
        Assert.Equal("https://push.example/faller", endpoint);
        Assert.Equal("You slipped into the void.", message.Title);
        Assert.Equal("/en/", message.Url);
    }

    [Fact]
    public async Task Friends_hear_who_fell_and_the_saved_hear_who_saved_them()
    {
        await using var world = await WorldAsync(Day.MidnightAt, ("lea", 0), ("kenji", 1), ("tom", 1), ("ines", 1));
        await world.App.Seed.FriendsAsync(Ids.Of("lea"), Ids.Of("kenji"));
        await world.App.Seed.FriendsAsync(Ids.Of("lea"), Ids.Of("tom"));
        await world.App.Seed.HandAsync(1, Day, Ids.Of("lea"), Ids.Of("ines"), 1, Day.StartsAt.AddHours(9), "human", false);
        await world.SubscribeAsync("lea");
        await world.SubscribeAsync("ines");
        await world.App.Night.ResolveAsync(Day, Day.MidnightAt, Ct);

        await world.DispatchAsync();

        var toLea = world.Sender.Sent.Where(s => s.Endpoint.EndsWith("/lea", StringComparison.Ordinal)).Select(s => s.Message).ToList();
        var fell = Assert.Single(toLea);
        Assert.Matches(@"^p[0-9a-f]+ et 1 autres amis ont glissé dans le vide cette nuit\.$", fell.Body);
        var saved = Assert.Single(world.Sender.Sent, s => s.Endpoint.EndsWith("/ines", StringComparison.Ordinal)).Message;
        Assert.Equal("Quelqu'un t'a tenu", saved.Title);
    }

    [Fact]
    public async Task Preferences_are_respected_and_revoked_subscriptions_forgotten()
    {
        await using var world = await WorldAsync(Day.MidnightAt, ("quiet", 1), ("moved", 1));
        await world.SubscribeAsync("quiet");
        await world.SubscribeAsync("moved");
        await world.App.Get<IPushStore>().SavePreferencesAsync(Ids.Of("quiet"), NotificationPreferences.Default with { Fell = false }, Ct);
        world.Sender.Outcomes["https://push.example/moved"] = PushDelivery.Gone;
        await world.App.Night.ResolveAsync(Day, Day.MidnightAt, Ct);

        var run = await world.DispatchAsync();

        Assert.Empty(world.Sender.Sent);
        Assert.Equal(1, run.Skipped);
        Assert.Equal(1, run.Gone);
        Assert.Empty(await world.App.Get<IPushStore>().SubscriptionsAsync(Ids.Of("moved"), Ct));
        Assert.Equal(0L, await world.App.Seed.ScalarAsync<long>("select count(*) from notification_outbox where sent_at is null"));
    }

    [Fact]
    public async Task When_no_device_can_be_reached_the_notification_is_retried_after_its_lease()
    {
        await using var world = await WorldAsync(Day.MidnightAt, ("faller", 1));
        await world.SubscribeAsync("faller");
        world.Sender.Outcomes["https://push.example/faller"] = PushDelivery.Failed;
        await world.App.Night.ResolveAsync(Day, Day.MidnightAt, Ct);

        await world.DispatchAsync();
        world.Sender.Outcomes.Clear();
        await world.DispatchAsync(); // still leased: nothing
        Assert.Empty(world.Sender.Sent);

        world.App.Clock.UtcNow = Day.MidnightAt.AddMinutes(3);
        await world.DispatchAsync();
        Assert.Single(world.Sender.Sent);
    }

    [Fact]
    public async Task Stale_notifications_expire_instead_of_arriving_days_late()
    {
        await using var world = await WorldAsync(Day.MidnightAt, ("faller", 1));
        await world.SubscribeAsync("faller");
        await world.App.Night.ResolveAsync(Day, Day.MidnightAt, Ct);

        world.App.Clock.UtcNow = Day.MidnightAt.AddDays(2);
        var run = await world.DispatchAsync();

        Assert.Equal(0, run.Notifications);
        Assert.Empty(world.Sender.Sent);
    }

    [Fact]
    public async Task One_reminder_at_21h_about_friends_still_below_the_threshold()
    {
        await using var world = await WorldAsync(Day.StartsAt, ("me", 0), ("yuki", 2), ("ines", 1), ("safe", 0));
        foreach (var friend in new[] { "yuki", "ines", "safe" })
        {
            await world.App.Seed.FriendsAsync(Ids.Of("me"), Ids.Of(friend));
        }

        var scheduler = world.App.Get<INotificationScheduler>();
        Assert.Equal(0, await scheduler.RunAsync(Day, Day.StartsAt.AddHours(20).AddMinutes(59), Ct));
        Assert.True(await scheduler.RunAsync(Day, Day.StartsAt.AddHours(21), Ct) > 0);
        Assert.Equal(0, await scheduler.RunAsync(Day, Day.StartsAt.AddHours(22), Ct));

        var mentioned = await world.App.Seed.ScalarAsync<int>(
            "select jsonb_array_length(payload->'players') from notification_outbox where kind = 'friends.below' and player_id = @me",
            ("me", Ids.Of("me").Value));
        Assert.Equal(2, mentioned);
    }

    [Fact]
    public async Task A_storm_is_announced_to_everyone_in_play_six_hours_before_midnight()
    {
        await using var world = await WorldAsync(Day.StartsAt, ("a", 2), ("b", 2));
        await world.App.Seed.ExecAsync("update game_days set storm = true where day = @day", ("day", Day.Date));
        var scheduler = world.App.Get<INotificationScheduler>();

        await scheduler.RunAsync(Day, Day.StartsAt.AddHours(17), Ct);
        Assert.Equal(0L, await world.App.Seed.ScalarAsync<long>("select count(*) from notification_outbox where kind = 'storm.announced'"));

        await scheduler.RunAsync(Day, Day.StartsAt.AddHours(18), Ct);
        await scheduler.RunAsync(Day, Day.StartsAt.AddHours(19), Ct);
        Assert.Equal(2L, await world.App.Seed.ScalarAsync<long>("select count(*) from notification_outbox where kind = 'storm.announced'"));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public void Every_notification_has_a_text_in_both_languages(string locale)
    {
        var texts = new ResxNotificationTexts();
        string[] kinds =
        [
            NotificationKinds.Fell, NotificationKinds.FriendsFell, NotificationKinds.SavedBy,
            NotificationKinds.SkinsUnlocked, NotificationKinds.Storm, NotificationKinds.FriendsBelow,
        ];

        foreach (var kind in kinds)
        {
            foreach (var names in new[] { Array.Empty<string>(), ["yuki"], ["yuki", "lea", "tom"] })
            {
                var message = texts.Render(kind, locale, names);
                Assert.False(string.IsNullOrWhiteSpace(message.Title));
                Assert.False(string.IsNullOrWhiteSpace(message.Body));
                Assert.DoesNotContain("{", message.Body, StringComparison.Ordinal);
                Assert.StartsWith($"/{locale}/", message.Url, StringComparison.Ordinal);
            }
        }

        Assert.Equal(
            locale == "en" ? "yuki and 2 other friends are still below the threshold." : "yuki et 2 autres amis sont encore sous le seuil.",
            texts.Render(NotificationKinds.FriendsBelow, locale, ["yuki", "lea", "tom"]).Body);
    }

    [Fact]
    public void Generated_vapid_keys_have_the_expected_shape()
    {
        var keys = VapidKeyStore.Generate();

        Assert.Equal(87, keys.PublicKey.Length); // 65 bytes, base64url without padding
        Assert.Equal(43, keys.PrivateKey.Length); // 32 bytes
        Assert.DoesNotContain('=', keys.PublicKey);
    }
}
