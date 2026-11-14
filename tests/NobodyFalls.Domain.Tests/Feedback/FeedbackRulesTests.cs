using NobodyFalls.Domain.Feedback;
using NobodyFalls.Testing;

namespace NobodyFalls.Domain.Tests.Feedback;

public sealed class FeedbackRulesTests
{
    private static readonly NobodyFalls.Domain.Common.PlayerId Lea = Ids.Of("lea");
    private static readonly NobodyFalls.Domain.Common.PlayerId Tom = Ids.Of("tom");

    [Fact]
    public void Ideas_are_cleaned_then_checked()
    {
        var draft = IdeaRules.Draft("  Une   carte  ", "Voir  le filet\n\n\n\nen entier", "gameplay");

        Assert.Equal(new IdeaDraft("Une carte", "Voir le filet\n\nen entier", IdeaCategory.Gameplay), draft.Value);
        Assert.Equal(IdeaErrors.Title, IdeaRules.Draft("Hi", "A long enough body", "other").Error);
        Assert.Equal(IdeaErrors.Body, IdeaRules.Draft("A title", "short", "other").Error);
        Assert.Equal(IdeaErrors.Category, IdeaRules.Draft("A title", "A long enough body", "money").Error);
        Assert.Equal(IdeaErrors.Title, IdeaRules.Draft(new string('x', 81), "A long enough body", "other").Error);
    }

    [Fact]
    public void Control_characters_never_reach_the_team()
    {
        Assert.Equal("ab c", Text.Clean("a\u0000b​ c\u0007"));
    }

    [Fact]
    public void Three_ideas_a_day()
    {
        Assert.True(IdeaRules.CanPropose(2).IsSuccess);
        Assert.Equal(IdeaErrors.DailyLimit, IdeaRules.CanPropose(3).Error);
    }

    [Fact]
    public void Votes_go_to_open_visible_ideas_of_others()
    {
        var open = new IdeaFacts(1, Lea, IdeaStatus.New, Hidden: false, MergedInto: null);

        Assert.True(IdeaRules.CanVote(open, Tom).IsSuccess);
        Assert.True(IdeaRules.CanVote(open with { Status = IdeaStatus.Planned }, Tom).IsSuccess);
        Assert.Equal(IdeaErrors.OwnIdea, IdeaRules.CanVote(open, Lea).Error);
        Assert.Equal(IdeaErrors.NotFound, IdeaRules.CanVote(open with { Hidden = true }, Tom).Error);
        Assert.Equal(IdeaErrors.Closed, IdeaRules.CanVote(open with { MergedInto = 2 }, Tom).Error);
        Assert.Equal(IdeaErrors.Closed, IdeaRules.CanVote(open with { Status = IdeaStatus.Shipped }, Tom).Error);
        Assert.Equal(IdeaErrors.Closed, IdeaRules.CanVote(open with { Status = IdeaStatus.Declined }, Tom).Error);
    }

    [Fact]
    public void Duplicates_merge_into_a_visible_main_idea_without_chains()
    {
        var main = new IdeaFacts(1, Lea, IdeaStatus.New, false, null);
        var duplicate = new IdeaFacts(2, Tom, IdeaStatus.New, false, null);

        Assert.True(IdeaRules.CanMerge(duplicate, main).IsSuccess);
        Assert.Equal(IdeaErrors.MergeSelf, IdeaRules.CanMerge(main, main).Error);
        Assert.Equal(IdeaErrors.MergeTarget, IdeaRules.CanMerge(duplicate, main with { MergedInto = 3 }).Error);
        Assert.Equal(IdeaErrors.MergeTarget, IdeaRules.CanMerge(duplicate, main with { Hidden = true }).Error);
        Assert.Equal(IdeaErrors.MergeTarget, IdeaRules.CanMerge(duplicate with { MergedInto = 1 }, main).Error);
    }

    [Fact]
    public void Merged_votes_are_the_union_without_the_main_author()
    {
        var ines = Ids.Of("ines");
        var votes = IdeaRules.MergedVotes([Tom, ines], [Tom, Lea, Ids.Of("yuki")], mainAuthor: Lea);

        Assert.Equal(3, votes.Count);
        Assert.DoesNotContain(Lea, votes);
        Assert.Contains(ines, votes);
    }

    [Fact]
    public void Requests_have_a_category_a_subject_and_a_message()
    {
        Assert.Equal(TicketCategory.Appeal, TicketRules.Draft("appeal", "Mon ban", "Je conteste.").Value.Category);
        Assert.Equal(TicketErrors.Category, TicketRules.Draft("refund", "Subject", "Body").Error);
        Assert.Equal(TicketErrors.Subject, TicketRules.Draft("support", "Hi", "Body").Error);
        Assert.Equal(TicketErrors.Message, TicketRules.Draft("support", "Subject", "   ").Error);
        Assert.Equal(TicketErrors.Message, TicketRules.Draft("support", "Subject", new string('x', 2001)).Error);
        Assert.Equal(TicketErrors.DailyLimit, TicketRules.CanOpen(5).Error);
    }

    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.Open)]
    [InlineData(TicketStatus.WaitingPlayer, TicketStatus.Open)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Open)]
    public void A_player_reply_puts_the_request_back_to_the_team(TicketStatus before, TicketStatus after) =>
        Assert.Equal(after, TicketRules.AfterPlayerReply(before).Value);

    [Fact]
    public void Closed_requests_take_no_more_messages()
    {
        Assert.Equal(TicketErrors.Closed, TicketRules.AfterPlayerReply(TicketStatus.Closed).Error);
        Assert.Equal(TicketErrors.Closed, TicketRules.AfterStaffReply(TicketStatus.Closed).Error);
        Assert.Equal(TicketStatus.WaitingPlayer, TicketRules.AfterStaffReply(TicketStatus.Open).Value);
    }

    [Fact]
    public void Reports_are_limited_and_never_against_oneself()
    {
        Assert.Equal(ReportCategory.Harassment, ReportRules.Draft("harassment", "").Value.Category);
        Assert.Equal(ReportErrors.Category, ReportRules.Draft("spam", "").Error);
        Assert.Equal(ReportErrors.Details, ReportRules.Draft("other", new string('x', 1001)).Error);
        Assert.True(ReportRules.CanReport(Lea, Tom, 9, alreadyOpen: false).IsSuccess);
        Assert.True(ReportRules.CanReport(Lea, null, 0, alreadyOpen: false).IsSuccess);
        Assert.Equal(ReportErrors.Self, ReportRules.CanReport(Lea, Lea, 0, false).Error);
        Assert.Equal(ReportErrors.AlreadyOpen, ReportRules.CanReport(Lea, Tom, 0, true).Error);
        Assert.Equal(ReportErrors.DailyLimit, ReportRules.CanReport(Lea, Tom, 10, false).Error);
    }

    [Fact]
    public void Wire_names_round_trip()
    {
        foreach (var s in Enum.GetValues<IdeaStatus>())
        {
            Assert.Equal(s, IdeaRules.ParseStatus(s.Name()));
        }

        foreach (var c in Enum.GetValues<TicketCategory>())
        {
            Assert.Equal(c, TicketRules.ParseCategory(c.Name()));
        }

        foreach (var s in Enum.GetValues<TicketStatus>())
        {
            Assert.Equal(s, TicketRules.ParseStatus(s.Name()));
        }

        foreach (var c in Enum.GetValues<ReportCategory>())
        {
            Assert.Equal(c, ReportRules.ParseCategory(c.Name()));
        }
    }
}
