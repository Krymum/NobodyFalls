using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NobodyFalls.Api.Tests.Support;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Api.Tests;

public sealed class FriendTests(PostgresFixture postgres)
{
    private static async Task<JsonElement> GetAsync(HttpClient client, string url) =>
        await client.GetFromJsonAsync<JsonElement>(new Uri(url, UriKind.Relative));

    private static Task<HttpResponseMessage> AskAsync(HttpClient client, string pseudo) =>
        client.PostAsJsonAsync("/api/v1/friends/requests", new { pseudo });

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url) =>
        client.PostAsync(new Uri(url, UriKind.Relative), null);

    [Fact]
    public async Task A_request_accepted_makes_two_friends()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, _) = await app.SignUpAsync("lea");
        var (kenji, _) = await app.SignUpAsync("kenji");

        var sent = await AskAsync(lea, "KENJI");
        Assert.Equal("sent", (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString());

        var incoming = (await GetAsync(kenji, "/api/v1/friends/requests")).GetProperty("incoming");
        var requestId = incoming[0].GetProperty("id").GetInt64();
        Assert.Equal("lea", incoming[0].GetProperty("player").GetProperty("pseudo").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(kenji, $"/api/v1/friends/requests/{requestId}/accept")).StatusCode);
        Assert.Equal(1, (await GetAsync(lea, "/api/v1/friends")).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(kenji, $"/api/v1/friends/requests/{requestId}/accept")).StatusCode);
    }

    [Fact]
    public async Task Asking_someone_who_already_asked_you_makes_you_friends()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, _) = await app.SignUpAsync("lea");
        var (kenji, _) = await app.SignUpAsync("kenji");

        await AskAsync(lea, "kenji");
        var answer = await AskAsync(kenji, "lea");

        Assert.Equal("becameFriends", (await answer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString());
        Assert.Equal(0, (await GetAsync(kenji, "/api/v1/friends/requests")).GetProperty("incoming").GetArrayLength());
    }

    [Fact]
    public async Task Requests_can_be_declined_cancelled_and_friends_removed()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, _) = await app.SignUpAsync("lea");
        var (kenji, kenjiId) = await app.SignUpAsync("kenji");
        var (tom, _) = await app.SignUpAsync("tom");

        await AskAsync(lea, "kenji");
        var toKenji = (await GetAsync(kenji, "/api/v1/friends/requests")).GetProperty("incoming")[0].GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(kenji, $"/api/v1/friends/requests/{toKenji}/decline")).StatusCode);

        await AskAsync(tom, "kenji");
        var fromTom = (await GetAsync(tom, "/api/v1/friends/requests")).GetProperty("outgoing")[0].GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.NoContent, (await tom.DeleteAsync(new Uri($"/api/v1/friends/requests/{fromTom}", UriKind.Relative))).StatusCode);
        Assert.Equal(0, (await GetAsync(kenji, "/api/v1/friends/requests")).GetProperty("incoming").GetArrayLength());

        await AskAsync(lea, "kenji");
        await AskAsync(kenji, "lea");
        await lea.DeleteAsync(new Uri($"/api/v1/friends/{kenjiId}", UriKind.Relative));
        Assert.Equal(0, (await GetAsync(lea, "/api/v1/friends")).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Blocked_players_no_longer_see_nor_hold_each_other()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, leaId) = await app.SignUpAsync("lea");
        var (kenji, kenjiId) = await app.SignUpAsync("kenji");
        await app.Seed.ExecAsync("update player_days set immune = false");
        await AskAsync(lea, "kenji");
        await AskAsync(kenji, "lea");

        Assert.Equal(HttpStatusCode.NoContent, (await lea.PutAsync(new Uri($"/api/v1/blocks/{kenjiId}", UriKind.Relative), null)).StatusCode);

        // The friendship ends, and neither can find, view, befriend or hold the other.
        Assert.Equal(0, (await GetAsync(kenji, "/api/v1/friends")).GetProperty("items").GetArrayLength());
        Assert.Equal(0, (await GetAsync(kenji, "/api/v1/edge")).GetProperty("entries").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await kenji.GetAsync(new Uri($"/api/v1/players/{leaId}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AskAsync(kenji, "lea")).StatusCode);
        var hand = await kenji.PostAsJsonAsync("/api/v1/hands", new { receiverId = leaId, turnstileToken = FakeHumanVerifier.Valid });
        Assert.Equal(HttpStatusCode.NotFound, hand.StatusCode);

        Assert.Equal(kenjiId, (await GetAsync(lea, "/api/v1/blocks")).GetProperty("items")[0].GetProperty("id").GetGuid());
        await lea.DeleteAsync(new Uri($"/api/v1/blocks/{kenjiId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, (await kenji.GetAsync(new Uri($"/api/v1/players/{leaId}", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Players_who_held_each_other_are_suggested()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, leaId) = await app.SignUpAsync("lea");
        var (_, kenjiId) = await app.SignUpAsync("kenji");
        var (low, high) = leaId.CompareTo(kenjiId) < 0 ? (leaId, kenjiId) : (kenjiId, leaId);
        await app.Seed.ExecAsync(
            "insert into hold_pairs (player_low, player_high, nights, last_day) values (@low, @high, 3, '2026-11-12')",
            ("low", low),
            ("high", high));

        var suggestions = (await GetAsync(lea, "/api/v1/friends/suggestions")).GetProperty("items");

        Assert.Equal(3, suggestions[0].GetProperty("nights").GetInt32());
        Assert.Equal("kenji", suggestions[0].GetProperty("player").GetProperty("pseudo").GetString());
    }
}
