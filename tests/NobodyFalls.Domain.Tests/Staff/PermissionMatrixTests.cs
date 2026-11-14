using NobodyFalls.Domain.Staff;
using NobodyFalls.Testing;

namespace NobodyFalls.Domain.Tests.Staff;

public sealed class PermissionMatrixTests
{
    [Fact]
    public void Admins_can_do_everything() =>
        Assert.Equal(Enum.GetValues<Permission>().ToHashSet(), PermissionMatrix.Of(StaffRole.Admin));

    [Theory]
    [InlineData(Permission.SanctionsBanPermanent)]
    [InlineData(Permission.GdprDelete)]
    [InlineData(Permission.ConfigRules)]
    [InlineData(Permission.ConfigStorms)]
    [InlineData(Permission.ConfigSeasons)]
    [InlineData(Permission.ConfigFlags)]
    [InlineData(Permission.AnnouncementsSend)]
    [InlineData(Permission.AuditView)]
    [InlineData(Permission.StaffManage)]
    public void Only_admins_hold_the_most_sensitive_permissions(Permission permission)
    {
        Assert.False(PermissionMatrix.Allows([StaffRole.Moderator], permission));
        Assert.False(PermissionMatrix.Allows([StaffRole.Support], permission));
        Assert.True(PermissionMatrix.Allows([StaffRole.Admin], permission));
    }

    [Fact]
    public void Moderators_handle_reports_and_sanctions_but_not_support_tickets()
    {
        Assert.True(PermissionMatrix.Allows([StaffRole.Moderator], Permission.ReportsHandle));
        Assert.True(PermissionMatrix.Allows([StaffRole.Moderator], Permission.SanctionsBanTemporary));
        Assert.True(PermissionMatrix.Allows([StaffRole.Moderator], Permission.TicketsAppeal));
        Assert.False(PermissionMatrix.Allows([StaffRole.Moderator], Permission.TicketsSupport));
        Assert.False(PermissionMatrix.Allows([StaffRole.Moderator], Permission.GdprExport));
    }

    [Fact]
    public void Support_answers_players_but_cannot_sanction_nor_see_linked_accounts()
    {
        Assert.True(PermissionMatrix.Allows([StaffRole.Support], Permission.TicketsSupport));
        Assert.True(PermissionMatrix.Allows([StaffRole.Support], Permission.GdprExport));
        Assert.False(PermissionMatrix.Allows([StaffRole.Support], Permission.SanctionsWarn));
        Assert.False(PermissionMatrix.Allows([StaffRole.Support], Permission.PlayersViewLinks));
        Assert.False(PermissionMatrix.Allows([StaffRole.Support], Permission.ReportsView));
    }

    [Fact]
    public void Roles_add_up()
    {
        Assert.True(PermissionMatrix.Allows([StaffRole.Moderator, StaffRole.Support], Permission.TicketsSupport));
        Assert.True(PermissionMatrix.Allows([StaffRole.Moderator, StaffRole.Support], Permission.ReportsHandle));
        Assert.Empty(PermissionMatrix.Of([]));
    }

    [Theory]
    [InlineData(Permission.DashboardView, "dashboard.view")]
    [InlineData(Permission.PlayersViewLinks, "players.view_links")]
    [InlineData(Permission.SanctionsBanTemporary, "sanctions.ban_temporary")]
    [InlineData(Permission.StaffManage, "staff.manage")]
    public void Permissions_have_stable_names(Permission permission, string name) =>
        Assert.Equal(name, permission.Name());

    [Fact]
    public void Role_sets_are_parsed_deduplicated_and_never_empty()
    {
        Assert.Equal([StaffRole.Admin, StaffRole.Support], StaffRoles.ParseSet(["support", "ADMIN", "support"]).Value);
        Assert.Equal(StaffErrors.NoRoles, StaffRoles.ParseSet([]).Error);
        Assert.Equal(StaffErrors.UnknownRole, StaffRoles.ParseSet(["owner"]).Error);
    }

    [Fact]
    public void Nobody_changes_their_own_roles()
    {
        var me = Ids.Of("alice");
        Assert.Equal(StaffErrors.Self, StaffRoles.CanChange(me, me, [StaffRole.Admin], [StaffRole.Support], adminCount: 3).Error);
    }

    [Fact]
    public void The_last_admin_cannot_be_removed()
    {
        var (actor, target) = (Ids.Of("alice"), Ids.Of("bob"));

        Assert.Equal(StaffErrors.LastAdmin, StaffRoles.CanChange(actor, target, [StaffRole.Admin], [], adminCount: 1).Error);
        Assert.True(StaffRoles.CanChange(actor, target, [StaffRole.Admin], [], adminCount: 2).IsSuccess);
        Assert.True(StaffRoles.CanChange(actor, target, [StaffRole.Support], [], adminCount: 1).IsSuccess);
    }
}
