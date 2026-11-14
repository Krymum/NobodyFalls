using NobodyFalls.Application.Feedback;
using NobodyFalls.Domain.Feedback;
using NobodyFalls.Infrastructure.Tests.Support;
using NobodyFalls.Testing;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Infrastructure.Tests.Feedback;

/// <summary>Limits and counts hold when players click at the same time.</summary>
public sealed class FeedbackConcurrencyTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Simultaneous_votes_are_counted_exactly()
    {
        await using var app = await TestApp.StartAsync(postgres, Now);
        var author = Ids.Of("author");
        await app.Seed.PlayerAsync(author);
        var voters = Enumerable.Range(0, 30).Select(i => Ids.Of($"voter{i}")).ToList();
        foreach (var voter in voters)
        {
            await app.Seed.PlayerAsync(voter);
        }

        var ideas = app.Get<IIdeaStore>();
        var id = (await ideas.ProposeAsync(author, new IdeaDraft("Une carte", "Voir le filet entier", IdeaCategory.Gameplay), "fr", Now, Ct)).Value;

        // Everyone votes twice at once, and half take their vote back at once.
        await Task.WhenAll(voters.Concat(voters).Select(v => ideas.VoteAsync(v, id, vote: true, Ct)));
        await Task.WhenAll(voters.Take(15).Select(v => ideas.VoteAsync(v, id, vote: false, Ct)));

        Assert.Equal(15, (await ideas.GetAsync(author, id, Ct))!.Votes);
        Assert.Equal(15L, await app.Seed.ScalarAsync<long>("select count(*) from idea_votes"));
    }

    [Fact]
    public async Task The_daily_limit_holds_under_simultaneous_proposals()
    {
        await using var app = await TestApp.StartAsync(postgres, Now);
        var author = Ids.Of("author");
        await app.Seed.PlayerAsync(author);
        var ideas = app.Get<IIdeaStore>();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            ideas.ProposeAsync(author, new IdeaDraft($"Idée {i:00}", "Une description assez longue", IdeaCategory.Other), "fr", Now, Ct)));

        Assert.Equal(IdeaRules.MaxPerDay, results.Count(r => r.IsSuccess));
        Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal(IdeaErrors.DailyLimit, r.Error));
        // A new game day, three more.
        Assert.True((await ideas.ProposeAsync(author, new IdeaDraft("Le lendemain", "Une description assez longue", IdeaCategory.Other), "fr", Now.AddDays(1), Ct)).IsSuccess);
    }
}
