using NobodyFalls.Domain.Night;
using NobodyFalls.Domain.Players;

namespace NobodyFalls.Infrastructure.Persistence;

/// <summary>
/// Text values stored in enum-like columns. Lower-case words keep hand-written SQL
/// readable (<c>where status = 'fallen'</c>); check constraints restrict each column.
/// </summary>
internal static class DbValues
{
    public const string Human = "human";
    public const string Watcher = "watcher";

    public const string Waitlisted = "waitlisted";
    public const string Active = "active";
    public const string Fallen = "fallen";

    public const string Survived = "survived";
    public const string Fell = "fell";
    public const string Immune = "immune";

    public static string Of(PlayerKind kind) => kind switch
    {
        PlayerKind.Human => Human,
        PlayerKind.Watcher => Watcher,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static PlayerKind ToKind(string value) => value switch
    {
        Human => PlayerKind.Human,
        Watcher => PlayerKind.Watcher,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown player kind."),
    };

    public static string Of(PlayerStatus status) => status switch
    {
        PlayerStatus.Waitlisted => Waitlisted,
        PlayerStatus.Active => Active,
        PlayerStatus.Fallen => Fallen,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static PlayerStatus ToStatus(string value) => value switch
    {
        Waitlisted => PlayerStatus.Waitlisted,
        Active => PlayerStatus.Active,
        Fallen => PlayerStatus.Fallen,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown player status."),
    };

    public static NightResult ToNightResult(string value) => value switch
    {
        Survived => NightResult.Survived,
        Fell => NightResult.Fell,
        Immune => NightResult.Immune,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown night result."),
    };

    /// <summary>SQL list for check constraints: <c>'a', 'b'</c>.</summary>
    public static string In(params string[] values) => string.Join(", ", values.Select(v => $"'{v}'"));
}
