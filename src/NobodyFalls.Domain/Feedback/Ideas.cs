using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Feedback;

/// <summary>Public status of an idea, set by the team (GAME_DESIGN §9).</summary>
public enum IdeaStatus
{
    New,
    UnderReview,
    Planned,
    Shipped,
    Declined,
}

public enum IdeaCategory
{
    Gameplay,
    Social,
    Interface,
    Other,
}

public static class IdeaErrors
{
    public static readonly DomainError Title = new("idea.title", "A title has 5 to 80 characters.");
    public static readonly DomainError Body = new("idea.body", "A description has 10 to 1000 characters.");
    public static readonly DomainError Category = new("idea.category", "Unknown category.");
    public static readonly DomainError DailyLimit = new("idea.limit_reached", "Three ideas a day at most. Come back tomorrow.");
    public static readonly DomainError NotFound = new("idea.not_found", "This idea cannot be found.");
    public static readonly DomainError OwnIdea = new("idea.own", "You cannot vote for your own idea.");
    public static readonly DomainError Closed = new("idea.closed", "This idea no longer takes votes.");
    public static readonly DomainError MergeSelf = new("idea.merge_self", "An idea cannot be merged into itself.");
    public static readonly DomainError MergeTarget = new("idea.merge_target", "Merge into an idea that is visible and not merged itself.");
}

/// <summary>A proposal, checked before it is stored.</summary>
public sealed record IdeaDraft(string Title, string Body, IdeaCategory Category);

/// <summary>What the rules need to know about an idea.</summary>
public sealed record IdeaFacts(long Id, PlayerId Author, IdeaStatus Status, bool Hidden, long? MergedInto);

/// <summary>
/// Players propose ideas and vote for others' (GAME_DESIGN §8); the team moderates,
/// merges duplicates and answers (§9). Pure rules: the stores apply them.
/// </summary>
public static class IdeaRules
{
    public const int MaxPerDay = 3;

    public static Result<IdeaDraft> Draft(string? title, string? body, string? category)
    {
        var cleanTitle = Text.Clean(title);
        if (cleanTitle.Length is < 5 or > 80)
        {
            return IdeaErrors.Title;
        }

        var cleanBody = Text.Clean(body, keepLines: true);
        if (cleanBody.Length is < 10 or > 1000)
        {
            return IdeaErrors.Body;
        }

        return ParseCategory(category) is { } parsed
            ? Result.Success(new IdeaDraft(cleanTitle, cleanBody, parsed))
            : IdeaErrors.Category;
    }

    public static Result CanPropose(int proposedToday) =>
        proposedToday >= MaxPerDay ? IdeaErrors.DailyLimit : Result.Success();

    /// <summary>
    /// A vote goes to a visible idea that is still open, and never to one's own.
    /// A vote for a merged idea is refused: the interface sends voters to the main one.
    /// </summary>
    public static Result CanVote(IdeaFacts idea, PlayerId voter)
    {
        ArgumentNullException.ThrowIfNull(idea);
        if (idea.Hidden)
        {
            return IdeaErrors.NotFound;
        }

        if (idea.Author == voter)
        {
            return IdeaErrors.OwnIdea;
        }

        return idea.MergedInto is not null || idea.Status is IdeaStatus.Shipped or IdeaStatus.Declined
            ? IdeaErrors.Closed
            : Result.Success();
    }

    /// <summary>A duplicate merges into another visible idea that is not merged itself (no chains).</summary>
    public static Result CanMerge(IdeaFacts duplicate, IdeaFacts into)
    {
        ArgumentNullException.ThrowIfNull(duplicate);
        ArgumentNullException.ThrowIfNull(into);
        if (duplicate.Id == into.Id)
        {
            return IdeaErrors.MergeSelf;
        }

        return into.Hidden || into.MergedInto is not null || duplicate.MergedInto is not null
            ? IdeaErrors.MergeTarget
            : Result.Success();
    }

    /// <summary>
    /// Votes after a merge: the union of both sets, without double votes, and without the
    /// main idea's author voting for their own idea.
    /// </summary>
    public static IReadOnlySet<PlayerId> MergedVotes(IEnumerable<PlayerId> mainVotes, IEnumerable<PlayerId> duplicateVotes, PlayerId mainAuthor)
    {
        var votes = new HashSet<PlayerId>(mainVotes);
        votes.UnionWith(duplicateVotes);
        votes.Remove(mainAuthor);
        return votes;
    }

    public static string Name(this IdeaStatus status) => status switch
    {
        IdeaStatus.New => "new",
        IdeaStatus.UnderReview => "under_review",
        IdeaStatus.Planned => "planned",
        IdeaStatus.Shipped => "shipped",
        IdeaStatus.Declined => "declined",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static IdeaStatus? ParseStatus(string? name) => name switch
    {
        "new" => IdeaStatus.New,
        "under_review" => IdeaStatus.UnderReview,
        "planned" => IdeaStatus.Planned,
        "shipped" => IdeaStatus.Shipped,
        "declined" => IdeaStatus.Declined,
        _ => null,
    };

    public static string Name(this IdeaCategory category) => category switch
    {
        IdeaCategory.Gameplay => "gameplay",
        IdeaCategory.Social => "social",
        IdeaCategory.Interface => "interface",
        IdeaCategory.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static IdeaCategory? ParseCategory(string? name) => name switch
    {
        "gameplay" => IdeaCategory.Gameplay,
        "social" => IdeaCategory.Social,
        "interface" => IdeaCategory.Interface,
        "other" => IdeaCategory.Other,
        _ => null,
    };
}
