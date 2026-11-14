using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Feedback;

/// <summary>What a report is about (GAME_DESIGN §8).</summary>
public enum ReportCategory
{
    Pseudo,
    Harassment,
    Cheating,
    Other,
}

public enum ReportTarget
{
    Player,
    Idea,
}

public static class ReportErrors
{
    public static readonly DomainError Category = new("report.category", "Unknown category.");
    public static readonly DomainError Details = new("report.details", "Details have at most 1000 characters.");
    public static readonly DomainError Self = new("report.self", "You cannot report yourself.");
    public static readonly DomainError TargetNotFound = new("report.target_not_found", "This player or idea cannot be found.");
    public static readonly DomainError AlreadyOpen = new("report.already_open", "You already reported this; the team is on it.");
    public static readonly DomainError DailyLimit = new("report.limit_reached", "Ten reports a day at most.");
}

public sealed record ReportDraft(ReportCategory Category, string Details);

/// <summary>
/// Reports go to the moderation queue. The reporter is never revealed to the target;
/// one open report per reporter and target; ten a day at most. Pure rules.
/// </summary>
public static class ReportRules
{
    public const int MaxPerDay = 10;

    public static Result<ReportDraft> Draft(string? category, string? details)
    {
        if (ParseCategory(category) is not { } parsed)
        {
            return ReportErrors.Category;
        }

        var clean = Text.Clean(details, keepLines: true);
        return clean.Length > 1000 ? ReportErrors.Details : Result.Success(new ReportDraft(parsed, clean));
    }

    public static Result CanReport(PlayerId reporter, PlayerId? targetPlayer, int reportedToday, bool alreadyOpen)
    {
        if (targetPlayer == reporter)
        {
            return ReportErrors.Self;
        }

        if (alreadyOpen)
        {
            return ReportErrors.AlreadyOpen;
        }

        return reportedToday >= MaxPerDay ? ReportErrors.DailyLimit : Result.Success();
    }

    public static string Name(this ReportCategory category) => category switch
    {
        ReportCategory.Pseudo => "pseudo",
        ReportCategory.Harassment => "harassment",
        ReportCategory.Cheating => "cheating",
        ReportCategory.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static ReportCategory? ParseCategory(string? name) => name switch
    {
        "pseudo" => ReportCategory.Pseudo,
        "harassment" => ReportCategory.Harassment,
        "cheating" => ReportCategory.Cheating,
        "other" => ReportCategory.Other,
        _ => null,
    };

    public static string Name(this ReportTarget target) => target == ReportTarget.Player ? "player" : "idea";

    public static ReportTarget? ParseTarget(string? name) => name switch
    {
        "player" => ReportTarget.Player,
        "idea" => ReportTarget.Idea,
        _ => null,
    };
}
