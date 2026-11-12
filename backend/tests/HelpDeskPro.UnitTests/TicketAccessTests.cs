using HelpDeskPro.Application.Common;
using HelpDeskPro.Application.Tickets;
using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Domain.Exceptions;

namespace HelpDeskPro.UnitTests;

public class TicketAccessTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public void Admin_GetsEveryDomainTransition_ExceptStartingFromNew()
    {
        var fromNew = TicketAccess.AllowedStatusTargets(Roles.Admin, false, false, TicketStatus.New);
        Assert.Equal([TicketStatus.Cancelled], fromNew);

        var fromInProgress = TicketAccess.AllowedStatusTargets(Roles.Admin, false, false, TicketStatus.InProgress);
        Assert.Equal(3, fromInProgress.Count);
    }

    [Fact]
    public void Agent_OnlyAssigneeMayChangeStatus()
    {
        Assert.NotEmpty(TicketAccess.AllowedStatusTargets(Roles.Agent, false, true, TicketStatus.InProgress));
        Assert.Empty(TicketAccess.AllowedStatusTargets(Roles.Agent, false, false, TicketStatus.InProgress));
    }

    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.Cancelled, true)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Cancelled, true)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved, false)]
    [InlineData(TicketStatus.WaitingForUser, TicketStatus.InProgress, true)]
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress, true)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed, true)]
    [InlineData(TicketStatus.InProgress, TicketStatus.WaitingForUser, false)]
    public void Requester_MayOnlyCancelReplyReopenOrClose(TicketStatus from, TicketStatus to, bool allowed) =>
        Assert.Equal(allowed, TicketAccess.AllowedStatusTargets(Roles.Requester, true, false, from).Contains(to));

    [Fact]
    public void Requester_NotOwner_GetsNothing() =>
        Assert.Empty(TicketAccess.AllowedStatusTargets(Roles.Requester, false, false, TicketStatus.New));

    [Fact]
    public void FinalStates_OfferNoTargets()
    {
        foreach (var role in Roles.All)
        {
            Assert.Empty(TicketAccess.AllowedStatusTargets(role, true, true, TicketStatus.Closed));
            Assert.Empty(TicketAccess.AllowedStatusTargets(role, true, true, TicketStatus.Cancelled));
        }
    }

    [Fact]
    public void Assignment_AdminMayAlways() =>
        Assert.Null(TicketAccess.CheckAssignment(Roles.Admin, Actor, Other, Other, false));

    [Fact]
    public void Assignment_AgentMayAssumeFreeTicketOfOwnTeam() =>
        Assert.Null(TicketAccess.CheckAssignment(Roles.Agent, Actor, null, Actor, actorInTicketTeam: true));

    [Fact]
    public void Assignment_AgentCannotAssumeOutsideTeam() =>
        Assert.NotNull(TicketAccess.CheckAssignment(Roles.Agent, Actor, null, Actor, actorInTicketTeam: false));

    [Fact]
    public void Assignment_AgentCannotTakeOverOrAssignOthers()
    {
        Assert.NotNull(TicketAccess.CheckAssignment(Roles.Agent, Actor, Other, Actor, true));
        Assert.NotNull(TicketAccess.CheckAssignment(Roles.Agent, Actor, null, Other, true));
    }

    [Fact]
    public void Assignment_AgentMayReleaseOwnTicketOnly()
    {
        Assert.Null(TicketAccess.CheckAssignment(Roles.Agent, Actor, Actor, null, true));
        Assert.NotNull(TicketAccess.CheckAssignment(Roles.Agent, Actor, Other, null, true));
    }

    [Fact]
    public void Assignment_RequesterNeverMay() =>
        Assert.NotNull(TicketAccess.CheckAssignment(Roles.Requester, Actor, null, Actor, true));

    [Fact]
    public void RequesterEdit_OnlyOwnAndOnlyWhileNew()
    {
        Assert.True(TicketAccess.CanEditAsRequester(true, TicketStatus.New));
        Assert.False(TicketAccess.CanEditAsRequester(true, TicketStatus.InProgress));
        Assert.False(TicketAccess.CanEditAsRequester(false, TicketStatus.New));
    }

    [Theory]
    [InlineData("InProgress", "IN_PROGRESS")]
    [InlineData("WaitingForUser", "WAITING_FOR_USER")]
    [InlineData("New", "NEW")]
    public void EnumCodes_RoundTrip(string name, string code)
    {
        Assert.Equal(code, EnumCodes.ToCode(name));
        Assert.True(EnumCodes.TryParse<TicketStatus>(code, out var parsed));
        Assert.Equal(name, parsed.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("QUALQUER")]
    [InlineData("99")]
    public void EnumCodes_RejectsGarbage(string text) =>
        Assert.False(EnumCodes.TryParse<TicketStatus>(text, out _));
}

public class TicketDomainGuardTests
{
    private static readonly DateTime T0 = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    private static Ticket NewTicket() =>
        Ticket.Create("HDP-2026-000001", "T", "D", Guid.NewGuid(), Guid.NewGuid(), new Priority("Alta", 3, 60, 480), null, T0);

    [Fact]
    public void NewToInProgress_WithoutAssignee_IsRejected() =>
        Assert.Throws<DomainException>(() => NewTicket().ChangeStatus(TicketStatus.InProgress, Guid.NewGuid(), T0));

    [Fact]
    public void History_SameInstant_KeepsStrictlyIncreasingTimestamps()
    {
        var t = NewTicket();
        var agent = Guid.NewGuid();
        t.Assign(agent, agent, T0); // Assigned + StatusChanged no mesmo instante

        var times = t.History.Select(h => h.CreatedAt).ToList();
        Assert.Equal(times.OrderBy(x => x).ToList(), times);
        Assert.Equal(times.Count, times.Distinct().Count());
        Assert.Equal(HistoryAction.Assigned, t.History[1].Action);
        Assert.Equal(HistoryAction.StatusChanged, t.History[2].Action);
    }

    [Fact]
    public void UpdateDetails_RejectsBlankAndFinalTickets()
    {
        var t = NewTicket();
        Assert.Throws<DomainException>(() => t.UpdateDetails(" ", "d", Guid.NewGuid(), T0));
        t.ChangeStatus(TicketStatus.Cancelled, Guid.NewGuid(), T0);
        Assert.Throws<DomainException>(() => t.UpdateDetails("x", "d", Guid.NewGuid(), T0));
    }
}
