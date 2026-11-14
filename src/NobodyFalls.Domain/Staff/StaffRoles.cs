using System.Globalization;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Staff;

public static class StaffErrors
{
    public static readonly DomainError Self = new("staff.self", "Nobody changes their own roles.");
    public static readonly DomainError NoRoles = new("staff.no_roles", "Give at least one role, or remove the member.");
    public static readonly DomainError UnknownRole = new("staff.unknown_role", "Unknown role.");
    public static readonly DomainError NotFound = new("staff.not_found", "This person is not in the team.");
    public static readonly DomainError PlayerNotFound = new("staff.player_not_found", "No player has this name.");
    public static readonly DomainError AlreadyMember = new("staff.already_member", "This player is already in the team.");
    public static readonly DomainError LastAdmin = new("staff.last_admin", "The team needs at least one admin.");
    public static readonly DomainError NotHuman = new("staff.not_human", "Only human players can join the team.");
}

/// <summary>Rules for giving and taking team roles (ADR 0018).</summary>
public static class StaffRoles
{
    /// <summary>The name stored in the database and sent to clients.</summary>
    public static string Name(this StaffRole role) => role switch
    {
        StaffRole.Admin => "admin",
        StaffRole.Moderator => "moderator",
        StaffRole.Support => "support",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public static StaffRole? Parse(string? name) => name?.ToLower(CultureInfo.InvariantCulture) switch
    {
        "admin" => StaffRole.Admin,
        "moderator" => StaffRole.Moderator,
        "support" => StaffRole.Support,
        _ => null,
    };

    /// <summary>Parses, deduplicates and orders a set of role names.</summary>
    public static Result<IReadOnlyList<StaffRole>> ParseSet(IEnumerable<string>? names)
    {
        var roles = new SortedSet<StaffRole>();
        foreach (var name in names ?? [])
        {
            if (Parse(name) is not { } role)
            {
                return StaffErrors.UnknownRole;
            }

            roles.Add(role);
        }

        return roles.Count == 0 ? StaffErrors.NoRoles : Result.Success<IReadOnlyList<StaffRole>>([.. roles]);
    }

    /// <summary>
    /// Checks a change of <paramref name="target"/>'s roles by <paramref name="actor"/>.
    /// <paramref name="newRoles"/> empty means removal from the team.
    /// </summary>
    public static Result CanChange(PlayerId actor, PlayerId target, IReadOnlyCollection<StaffRole> currentRoles, IReadOnlyCollection<StaffRole> newRoles, int adminCount)
    {
        ArgumentNullException.ThrowIfNull(currentRoles);
        ArgumentNullException.ThrowIfNull(newRoles);
        if (actor == target)
        {
            return StaffErrors.Self;
        }

        var losesAdmin = currentRoles.Contains(StaffRole.Admin) && !newRoles.Contains(StaffRole.Admin);
        return losesAdmin && adminCount <= 1 ? StaffErrors.LastAdmin : Result.Success();
    }
}
