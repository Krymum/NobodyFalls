namespace NobodyFalls.Domain.Staff;

/// <summary>The team roles (GAME_DESIGN §9). A member can hold several; permissions add up.</summary>
public enum StaffRole
{
    Admin,
    Moderator,
    Support,
}

/// <summary>
/// What a team member may do. Every admin endpoint requires exactly one permission;
/// <see cref="PermissionMatrix"/> says which roles grant it (ADR 0017).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "The game design's own word; unrelated to code access security.")]
public enum Permission
{
    DashboardView,
    PlayersView,
    PlayersViewLinks,
    SanctionsWarn,
    SanctionsResetPseudo,
    SanctionsExcludeScore,
    SanctionsBanTemporary,
    SanctionsBanPermanent,
    SanctionsRevoke,
    ReportsView,
    ReportsHandle,
    TicketsSupport,
    TicketsAppeal,
    IdeasModerate,
    GdprExport,
    GdprDelete,
    ConfigRules,
    ConfigStorms,
    ConfigSeasons,
    ConfigFlags,
    AnnouncementsSend,
    AuditView,
    StaffManage,
}

/// <summary>
/// The single source of truth for roles × permissions: the server enforces it, the
/// admin interface hides what it forbids, and the authorization tests are built from it.
/// </summary>
public static class PermissionMatrix
{
    private static readonly Permission[] ModeratorPermissions =
    [
        Permission.DashboardView,
        Permission.PlayersView,
        Permission.PlayersViewLinks,
        Permission.SanctionsWarn,
        Permission.SanctionsResetPseudo,
        Permission.SanctionsExcludeScore,
        Permission.SanctionsBanTemporary,
        Permission.SanctionsRevoke,
        Permission.ReportsView,
        Permission.ReportsHandle,
        Permission.TicketsAppeal,
        Permission.IdeasModerate,
    ];

    private static readonly Permission[] SupportPermissions =
    [
        Permission.DashboardView,
        Permission.PlayersView,
        Permission.TicketsSupport,
        Permission.IdeasModerate,
        Permission.GdprExport,
    ];

    private static readonly Dictionary<StaffRole, IReadOnlySet<Permission>> Grants =
        new()
        {
            [StaffRole.Admin] = Enum.GetValues<Permission>().ToHashSet(),
            [StaffRole.Moderator] = ModeratorPermissions.ToHashSet(),
            [StaffRole.Support] = SupportPermissions.ToHashSet(),
        };

    public static IReadOnlySet<Permission> Of(StaffRole role) => Grants[role];

    /// <summary>Every permission granted by any of the roles.</summary>
    public static IReadOnlySet<Permission> Of(IEnumerable<StaffRole> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var permissions = new HashSet<Permission>();
        foreach (var role in roles)
        {
            permissions.UnionWith(Grants[role]);
        }

        return permissions;
    }

    public static bool Allows(IEnumerable<StaffRole> roles, Permission permission) => Of(roles).Contains(permission);

    /// <summary>The stable name sent to clients: <c>PlayersViewLinks</c> → <c>players.view_links</c>.</summary>
    public static string Name(this Permission permission)
    {
        var words = System.Text.RegularExpressions.Regex.Split(permission.ToString(), "(?<!^)(?=[A-Z])");
        var area = words[0].ToLowerInvariant();
        return words.Length == 1 ? area : $"{area}.{string.Join('_', words.Skip(1)).ToLowerInvariant()}";
    }
}
