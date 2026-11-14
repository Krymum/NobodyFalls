using System.Security.Cryptography;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Feedback;
using NobodyFalls.Domain.Staff;

namespace NobodyFalls.Domain.Moderation;

/// <summary>Sanctions of GAME_DESIGN §9 (with the decisions of 14/11/2026).</summary>
public enum SanctionKind
{
    /// <summary>A message the player must acknowledge.</summary>
    Warning,

    /// <summary>The pseudo is replaced by a neutral one; the player picks a new one.</summary>
    PseudoReset,

    /// <summary>The player keeps playing and stays counted, but their hands earn no points and their score is hidden.</summary>
    ScoreExclusion,

    /// <summary>Out of play, not counted as a death; hands already given stay valid. Temporary or permanent.</summary>
    Ban,
}

public static class SanctionErrors
{
    public static readonly DomainError Kind = new("sanction.kind", "Unknown sanction.");
    public static readonly DomainError Reason = new("sanction.reason", "Give a reason of 3 to 500 characters (for the team).");
    public static readonly DomainError PublicMessage = new("sanction.public_message", "A warning or a ban needs a message for the player (1 to 500 characters).");
    public static readonly DomainError Period = new("sanction.period", "The end must be in the future, within a year; leave it empty for no end.");
    public static readonly DomainError NoPeriod = new("sanction.no_period", "A warning or a pseudo reset has no end date.");
    public static readonly DomainError Self = new("sanction.self", "Nobody sanctions themselves.");
    public static readonly DomainError Staff = new("sanction.staff", "Take the team role away before sanctioning a team member.");
    public static readonly DomainError NotFound = new("sanction.not_found", "This sanction cannot be found.");
    public static readonly DomainError AlreadyRevoked = new("sanction.already_revoked", "This sanction was already revoked.");
    public static readonly DomainError PlayerNotFound = new("sanction.player_not_found", "This player cannot be found.");
}

public sealed record SanctionDraft(SanctionKind Kind, string Reason, string? PublicMessage, DateTimeOffset? EndsAt)
{
    public bool IsPermanentBan => Kind == SanctionKind.Ban && EndsAt is null;
}

/// <summary>Pure rules for issuing sanctions. The store applies their effects in one transaction.</summary>
public static class SanctionRules
{
    public static readonly TimeSpan MaxLength = TimeSpan.FromDays(365);

    public static Result<SanctionDraft> Draft(string? kind, string? reason, string? publicMessage, DateTimeOffset? endsAt, DateTimeOffset now)
    {
        if (Parse(kind) is not { } parsed)
        {
            return SanctionErrors.Kind;
        }

        var cleanReason = Text.Clean(reason, keepLines: true);
        if (cleanReason.Length is < 3 or > 500)
        {
            return SanctionErrors.Reason;
        }

        var message = Text.Clean(publicMessage, keepLines: true);
        var needsMessage = parsed is SanctionKind.Warning or SanctionKind.Ban;
        if ((needsMessage && message.Length == 0) || message.Length > 500)
        {
            return SanctionErrors.PublicMessage;
        }

        if (parsed is SanctionKind.Warning or SanctionKind.PseudoReset)
        {
            if (endsAt is not null)
            {
                return SanctionErrors.NoPeriod;
            }
        }
        else if (endsAt is { } end && (end <= now || end > now + MaxLength))
        {
            return SanctionErrors.Period;
        }

        return Result.Success(new SanctionDraft(parsed, cleanReason, message.Length == 0 ? null : message, endsAt));
    }

    /// <summary>The permission a sanction needs (ADR 0018): a permanent ban is for admins only.</summary>
    public static Permission RequiredPermission(SanctionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return draft.Kind switch
        {
            SanctionKind.Warning => Permission.SanctionsWarn,
            SanctionKind.PseudoReset => Permission.SanctionsResetPseudo,
            SanctionKind.ScoreExclusion => Permission.SanctionsExcludeScore,
            SanctionKind.Ban when draft.EndsAt is null => Permission.SanctionsBanPermanent,
            _ => Permission.SanctionsBanTemporary,
        };
    }

    /// <summary>A permanent ban asks for a fresh code (ADR 0019).</summary>
    public static bool IsSensitive(SanctionDraft draft) => draft?.IsPermanentBan == true;

    public static Result CanSanction(PlayerId actor, PlayerId target, bool targetIsStaff)
    {
        if (actor == target)
        {
            return SanctionErrors.Self;
        }

        return targetIsStaff ? SanctionErrors.Staff : Result.Success();
    }

    /// <summary>A neutral pseudo after a reset: valid (letters, digits, underscore) and unlikely to clash.</summary>
    public static string NeutralPseudo() => $"joueur_{RandomNumberGenerator.GetInt32(100_000, 1_000_000)}";

    /// <summary>"Until" of the effect: a far-future instant stands for "no end".</summary>
    public static DateTimeOffset EffectUntil(SanctionDraft draft) => draft?.EndsAt ?? Forever;

    public static readonly DateTimeOffset Forever = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

    public static string Name(this SanctionKind kind) => kind switch
    {
        SanctionKind.Warning => "warning",
        SanctionKind.PseudoReset => "pseudo_reset",
        SanctionKind.ScoreExclusion => "score_exclusion",
        SanctionKind.Ban => "ban",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static SanctionKind? Parse(string? name) => name switch
    {
        "warning" => SanctionKind.Warning,
        "pseudo_reset" => SanctionKind.PseudoReset,
        "score_exclusion" => SanctionKind.ScoreExclusion,
        "ban" => SanctionKind.Ban,
        _ => null,
    };
}

/// <summary>How a report ends (GAME_DESIGN §9).</summary>
public enum ReportOutcome
{
    /// <summary>Something was done (usually a sanction).</summary>
    Actioned,

    /// <summary>Nothing to do.</summary>
    Dismissed,
}

public static class ReportModerationErrors
{
    public static readonly DomainError NotFound = new("report.not_found", "This report cannot be found.");
    public static readonly DomainError AlreadyResolved = new("report.already_resolved", "This report was already handled.");
    public static readonly DomainError Outcome = new("report.outcome", "Choose: actioned or dismissed.");
    public static readonly DomainError Note = new("report.note", "A note has at most 2000 characters.");
    public static readonly DomainError SanctionNeedsActioned = new("report.sanction_needs_actioned", "A sanction means the report is actioned.");
}

public static class ReportModeration
{
    public static ReportOutcome? ParseOutcome(string? name) => name switch
    {
        "actioned" => ReportOutcome.Actioned,
        "dismissed" => ReportOutcome.Dismissed,
        _ => null,
    };

    public static string Name(this ReportOutcome outcome) => outcome == ReportOutcome.Actioned ? "actioned" : "dismissed";

    public static Result CanResolve(string status, ReportOutcome outcome, bool withSanction)
    {
        if (status is "actioned" or "dismissed")
        {
            return ReportModerationErrors.AlreadyResolved;
        }

        return withSanction && outcome != ReportOutcome.Actioned ? ReportModerationErrors.SanctionNeedsActioned : Result.Success();
    }
}
