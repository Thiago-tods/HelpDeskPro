using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Domain.Exceptions;

namespace HelpDeskPro.UnitTests;

public class TicketTests
{
    private static readonly DateTime T0 = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Agent = Guid.NewGuid();

    private static Priority High() => new("Alta", 3, 60, 480);

    private static Ticket NewTicket(Priority? p = null) =>
        Ticket.Create("HDP-2026-000001", "Título", "Descrição", Requester, Guid.NewGuid(), p ?? High(), null, T0);

    [Fact]
    public void Create_SetsNewStatusHistoryAndSla()
    {
        var t = NewTicket();

        Assert.Equal(TicketStatus.New, t.Status);
        Assert.Single(t.History, h => h.Action == HistoryAction.Created);
        Assert.Equal(T0.AddMinutes(60), t.Sla!.FirstResponseDueAt);
        Assert.Equal(T0.AddMinutes(480), t.Sla.ResolutionDueAt);
    }

    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.InProgress)]
    [InlineData(TicketStatus.New, TicketStatus.Cancelled)]
    [InlineData(TicketStatus.InProgress, TicketStatus.WaitingForUser)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Cancelled)]
    [InlineData(TicketStatus.WaitingForUser, TicketStatus.InProgress)]
    [InlineData(TicketStatus.WaitingForUser, TicketStatus.Cancelled)]
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed)]
    public void AllowedTransitions_AreAccepted(TicketStatus from, TicketStatus to) =>
        Assert.True(Ticket.CanTransition(from, to));

    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.Resolved)]
    [InlineData(TicketStatus.New, TicketStatus.Closed)]
    [InlineData(TicketStatus.WaitingForUser, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Cancelled, TicketStatus.New)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Cancelled)]
    public void InvalidTransitions_AreRejected(TicketStatus from, TicketStatus to) =>
        Assert.False(Ticket.CanTransition(from, to));

    [Fact]
    public void ChangeStatus_Invalid_Throws()
    {
        var t = NewTicket();
        Assert.Throws<InvalidStatusTransitionException>(() => t.ChangeStatus(TicketStatus.Closed, Agent, T0));
    }

    [Fact]
    public void FinalStates_AcceptNoTransition()
    {
        var t = NewTicket();
        t.ChangeStatus(TicketStatus.Cancelled, Requester, T0.AddMinutes(1));

        foreach (var s in Enum.GetValues<TicketStatus>())
            Assert.False(Ticket.CanTransition(TicketStatus.Cancelled, s));
        Assert.True(t.IsFinal);
    }

    [Fact]
    public void Assign_OnNewTicket_MovesToInProgressAndRecordsHistory()
    {
        var t = NewTicket();
        t.Assign(Agent, Agent, T0.AddMinutes(5));

        Assert.Equal(Agent, t.AssignedToId);
        Assert.Equal(TicketStatus.InProgress, t.Status);
        Assert.Contains(t.History, h => h.Action == HistoryAction.Assigned);
    }

    [Fact]
    public void Resolve_SetsResolvedAt_AndReopenClearsIt()
    {
        var t = NewTicket();
        t.Assign(Agent, Agent, T0.AddMinutes(5));
        t.ChangeStatus(TicketStatus.Resolved, Agent, T0.AddHours(2));
        Assert.Equal(T0.AddHours(2), t.ResolvedAt);

        t.ChangeStatus(TicketStatus.InProgress, Requester, T0.AddHours(3));
        Assert.Null(t.ResolvedAt);
        Assert.Contains(t.History, h => h.Action == HistoryAction.Reopened);
    }

    [Fact]
    public void Close_SetsClosedAt()
    {
        var t = NewTicket();
        t.Assign(Agent, Agent, T0);
        t.ChangeStatus(TicketStatus.Resolved, Agent, T0.AddHours(1));
        t.ChangeStatus(TicketStatus.Closed, Agent, T0.AddHours(2));

        Assert.Equal(T0.AddHours(2), t.ClosedAt);
    }

    [Fact]
    public void PublicAgentMessage_CountsAsFirstResponse()
    {
        var t = NewTicket();
        t.AddMessage(Agent, "Olá", isInternal: false, authorIsAgent: true, T0.AddMinutes(20));

        Assert.Equal(T0.AddMinutes(20), t.FirstRespondedAt);
        Assert.Equal(T0.AddMinutes(20), t.Sla!.FirstRespondedAt);
        Assert.Null(t.Sla.BreachedAt);
    }

    [Fact]
    public void InternalNote_DoesNotCountAsFirstResponse()
    {
        var t = NewTicket();
        t.AddMessage(Agent, "nota", isInternal: true, authorIsAgent: true, T0.AddMinutes(10));

        Assert.Null(t.FirstRespondedAt);
    }

    [Fact]
    public void RequesterMessage_DoesNotCountAsFirstResponse()
    {
        var t = NewTicket();
        t.AddMessage(Requester, "alô?", isInternal: false, authorIsAgent: false, T0.AddMinutes(10));

        Assert.Null(t.FirstRespondedAt);
    }

    [Fact]
    public void Requester_CannotCreateInternalNote()
    {
        var t = NewTicket();
        Assert.Throws<DomainException>(() =>
            t.AddMessage(Requester, "x", isInternal: true, authorIsAgent: false, T0));
    }

    [Fact]
    public void LateFirstResponse_MarksSlaBreached()
    {
        var t = NewTicket();
        t.AddMessage(Agent, "Olá", false, true, T0.AddMinutes(61));

        Assert.Equal(T0.AddMinutes(61), t.Sla!.BreachedAt);
    }

    [Fact]
    public void ChangePriority_RecalculatesSla()
    {
        var t = NewTicket();
        var critical = new Priority("Crítica", 4, 15, 240);
        t.ChangePriority(critical, Agent, T0.AddMinutes(1));

        Assert.Equal(T0.AddMinutes(15), t.Sla!.FirstResponseDueAt);
        Assert.Equal(T0.AddMinutes(240), t.Sla.ResolutionDueAt);
        Assert.Contains(t.History, h => h.Action == HistoryAction.PriorityChanged);
    }

    [Fact]
    public void FinalTicket_RejectsMessagesAndAssignment()
    {
        var t = NewTicket();
        t.ChangeStatus(TicketStatus.Cancelled, Requester, T0);

        Assert.Throws<DomainException>(() => t.AddMessage(Agent, "x", false, true, T0));
        Assert.Throws<DomainException>(() => t.Assign(Agent, Agent, T0));
    }

    [Fact]
    public void Priority_InvalidSla_Throws() =>
        Assert.Throws<DomainException>(() => new Priority("X", 9, 60, 30));
}
