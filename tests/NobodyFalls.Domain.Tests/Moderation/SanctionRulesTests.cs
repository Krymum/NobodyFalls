using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Moderation;
using NobodyFalls.Domain.Staff;
using NobodyFalls.Testing;

namespace NobodyFalls.Domain.Tests.Moderation;

public sealed class SanctionRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_warning_and_a_ban_need_a_message_for_the_player()
    {
        Assert.Equal(SanctionErrors.PublicMessage, SanctionRules.Draft("warning", "Insultes", null, null, Now).Error);
        Assert.Equal(SanctionErrors.PublicMessage, SanctionRules.Draft("ban", "Triche", "  ", Now.AddDays(3), Now).Error);
        Assert.True(SanctionRules.Draft("pseudo_reset", "Pseudo injurieux", null, null, Now).IsSuccess);
        Assert.Equal(SanctionErrors.Reason, SanctionRules.Draft("warning", "x", "Calme-toi", null, Now).Error);
        Assert.Equal(SanctionErrors.Kind, SanctionRules.Draft("jail", "Raison", "Message", null, Now).Error);
    }

    [Fact]
    public void Periods_are_in_the_future_within_a_year_and_only_for_bans_and_exclusions()
    {
        Assert.Equal(SanctionErrors.Period, SanctionRules.Draft("ban", "Triche", "Banni", Now.AddMinutes(-1), Now).Error);
        Assert.Equal(SanctionErrors.Period, SanctionRules.Draft("score_exclusion", "Triche", null, Now.AddDays(400), Now).Error);
        Assert.Equal(SanctionErrors.NoPeriod, SanctionRules.Draft("warning", "Raison", "Message", Now.AddDays(1), Now).Error);
        Assert.True(SanctionRules.Draft("score_exclusion", "Mains arrangées", null, null, Now).IsSuccess);
    }

    [Theory]
    [InlineData("warning", false, Permission.SanctionsWarn)]
    [InlineData("pseudo_reset", false, Permission.SanctionsResetPseudo)]
    [InlineData("score_exclusion", true, Permission.SanctionsExcludeScore)]
    [InlineData("ban", true, Permission.SanctionsBanTemporary)]
    [InlineData("ban", false, Permission.SanctionsBanPermanent)]
    public void Each_sanction_needs_its_permission(string kind, bool withEnd, Permission permission)
    {
        var draft = SanctionRules.Draft(kind, "Une raison", "Un message", withEnd ? Now.AddDays(7) : null, Now).Value;

        Assert.Equal(permission, SanctionRules.RequiredPermission(draft));
        Assert.Equal(kind == "ban" && !withEnd, SanctionRules.IsSensitive(draft));
    }

    [Fact]
    public void Moderators_can_ban_for_a_while_but_not_forever()
    {
        Assert.True(PermissionMatrix.Allows([StaffRole.Moderator], Permission.SanctionsBanTemporary));
        Assert.False(PermissionMatrix.Allows([StaffRole.Moderator], Permission.SanctionsBanPermanent));
    }

    [Fact]
    public void Nobody_sanctions_themselves_nor_a_team_member()
    {
        PlayerId me = Ids.Of("me"), other = Ids.Of("other");
        Assert.Equal(SanctionErrors.Self, SanctionRules.CanSanction(me, me, false).Error);
        Assert.Equal(SanctionErrors.Staff, SanctionRules.CanSanction(me, other, true).Error);
        Assert.True(SanctionRules.CanSanction(me, other, false).IsSuccess);
    }

    [Fact]
    public void A_neutral_pseudo_is_a_valid_pseudo()
    {
        Assert.True(Pseudo.Create(SanctionRules.NeutralPseudo()).IsSuccess);
    }

    [Fact]
    public void A_report_is_resolved_once_and_a_sanction_means_actioned()
    {
        Assert.True(ReportModeration.CanResolve("open", ReportOutcome.Actioned, withSanction: true).IsSuccess);
        Assert.Equal(ReportModerationErrors.SanctionNeedsActioned, ReportModeration.CanResolve("in_review", ReportOutcome.Dismissed, true).Error);
        Assert.Equal(ReportModerationErrors.AlreadyResolved, ReportModeration.CanResolve("dismissed", ReportOutcome.Actioned, false).Error);
    }
}
