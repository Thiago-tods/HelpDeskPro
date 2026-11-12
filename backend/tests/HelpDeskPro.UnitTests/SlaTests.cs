using HelpDeskPro.Application.Dashboard;
using HelpDeskPro.Application.Reports;
using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Enums;

namespace HelpDeskPro.UnitTests;

public class SlaTests
{
    private static readonly DateTime T0 = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Agent = Guid.NewGuid();

    // Alta: primeira resposta em 60 min, resolução em 480 min.
    private static Ticket NewTicket() =>
        Ticket.Create("HDP-2026-000001", "T", "D", Requester, Guid.NewGuid(), new Priority("Alta", 3, 60, 480), null, T0);

    private static Ticket Assigned()
    {
        var t = NewTicket();
        t.Assign(Agent, Agent, T0);
        return t;
    }

    [Fact]
    public void BeforeWarningWindow_NoEvents() =>
        Assert.Empty(NewTicket().EvaluateSla(T0.AddMinutes(30)));

    [Fact]
    public void At80PercentOfFirstResponse_WarnsOnce()
    {
        var t = NewTicket();
        Assert.Equal([SlaEvent.FirstResponseWarning], t.EvaluateSla(T0.AddMinutes(49)));
        Assert.Empty(t.EvaluateSla(T0.AddMinutes(55)));
        Assert.Null(t.Sla!.BreachedAt);
    }

    [Fact]
    public void AfterFirstResponseDue_BreachesOnce_WithSystemHistory()
    {
        var t = NewTicket();
        Assert.Equal([SlaEvent.FirstResponseBreached], t.EvaluateSla(T0.AddMinutes(61)));
        Assert.Empty(t.EvaluateSla(T0.AddMinutes(90)));

        var entry = Assert.Single(t.History, h => h.Action == HistoryAction.SlaBreached);
        Assert.Null(entry.ActorId);
        Assert.Equal(SlaBreachKind.FirstResponse, entry.NewValue);
        Assert.Equal(T0.AddMinutes(61), t.Sla!.BreachedAt);
        Assert.Equal(T0.AddMinutes(61), t.Sla.FirstResponseBreachedAt);
    }

    [Fact]
    public void AnsweredTicket_NeverBreachesFirstResponse()
    {
        var t = Assigned();
        t.AddMessage(Agent, "oi", false, true, T0.AddMinutes(10));
        Assert.DoesNotContain(SlaEvent.FirstResponseBreached, t.EvaluateSla(T0.AddMinutes(120)));
        Assert.DoesNotContain(SlaEvent.FirstResponseWarning, t.EvaluateSla(T0.AddMinutes(120)));
    }

    [Fact]
    public void Resolution_WarnsThenBreaches_BothOnce()
    {
        var t = Assigned();
        t.AddMessage(Agent, "oi", false, true, T0.AddMinutes(5));

        Assert.Equal([SlaEvent.ResolutionWarning], t.EvaluateSla(T0.AddMinutes(400)));
        Assert.Empty(t.EvaluateSla(T0.AddMinutes(410)));
        Assert.Equal([SlaEvent.ResolutionBreached], t.EvaluateSla(T0.AddMinutes(481)));
        Assert.Empty(t.EvaluateSla(T0.AddMinutes(600)));
        Assert.Contains(t.History, h => h.Action == HistoryAction.SlaBreached && h.NewValue == SlaBreachKind.Resolution);
    }

    [Fact]
    public void WaitingForUser_DoesNotPauseTheClock()
    {
        var t = Assigned();
        t.AddMessage(Agent, "oi", false, true, T0.AddMinutes(5));
        t.ChangeStatus(TicketStatus.WaitingForUser, Agent, T0.AddMinutes(6));
        Assert.Contains(SlaEvent.ResolutionBreached, t.EvaluateSla(T0.AddMinutes(500)));
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public void NonActiveTickets_AreNotEvaluated(TicketStatus final)
    {
        var t = Assigned();
        t.AddMessage(Agent, "oi", false, true, T0.AddMinutes(5));
        if (final == TicketStatus.Cancelled) t.ChangeStatus(final, Agent, T0.AddMinutes(6));
        else
        {
            t.ChangeStatus(TicketStatus.Resolved, Agent, T0.AddMinutes(6));
            if (final == TicketStatus.Closed) t.ChangeStatus(final, Agent, T0.AddMinutes(7));
        }
        Assert.Empty(t.EvaluateSla(T0.AddDays(10)));
    }

    [Fact]
    public void LateFirstReply_RecordsBreach_EvenWithoutMonitor()
    {
        var t = Assigned();
        t.AddMessage(Agent, "tarde", false, true, T0.AddMinutes(75));

        Assert.Equal(T0.AddMinutes(75), t.Sla!.FirstResponseBreachedAt);
        Assert.Contains(t.History, h => h.Action == HistoryAction.SlaBreached && h.ActorId is null);
    }

    [Fact]
    public void InternalNote_DoesNotRecordBreach()
    {
        var t = Assigned();
        t.AddMessage(Agent, "nota", true, true, T0.AddMinutes(75));
        Assert.Null(t.Sla!.FirstResponseBreachedAt);
    }

    [Fact]
    public void LateResolution_RecordsResolutionBreach()
    {
        var t = Assigned();
        t.AddMessage(Agent, "oi", false, true, T0.AddMinutes(5));
        t.ChangeStatus(TicketStatus.Resolved, Agent, T0.AddMinutes(500));

        Assert.Equal(T0.AddMinutes(500), t.Sla!.ResolutionBreachedAt);
        Assert.Equal(SlaState.Missed, t.GetSlaState(T0.AddMinutes(501)));
    }

    [Fact]
    public void ChangingPriority_ReArmsWarnings_ButKeepsRecordedBreaches()
    {
        var t = NewTicket();
        t.EvaluateSla(T0.AddMinutes(61));
        t.ChangePriority(new Priority("Baixa", 1, 480, 4320), Agent, T0.AddMinutes(62));

        Assert.NotNull(t.Sla!.FirstResponseBreachedAt);
        Assert.Equal(T0.AddMinutes(480), t.Sla.FirstResponseDueAt);
        Assert.Null(t.Sla.FirstResponseWarnedAt);
    }

    [Fact]
    public void SlaState_ForActiveTickets()
    {
        var t = Assigned();
        t.AddMessage(Agent, "oi", false, true, T0.AddMinutes(5));

        Assert.Equal(SlaState.OnTrack, t.GetSlaState(T0.AddMinutes(100)));
        Assert.Equal(SlaState.AtRisk, t.GetSlaState(T0.AddMinutes(400)));
        Assert.Equal(SlaState.Overdue, t.GetSlaState(T0.AddMinutes(481)));
    }

    [Fact]
    public void SlaState_UnansweredTicketIsOverdueAfterFirstResponseDue()
    {
        var t = NewTicket();
        Assert.Equal(SlaState.AtRisk, t.GetSlaState(T0.AddMinutes(50)));
        Assert.Equal(SlaState.Overdue, t.GetSlaState(T0.AddMinutes(61)));
    }

    [Fact]
    public void SlaState_ResolvedInTimeIsMet_AndCancelledIsNotApplicable()
    {
        var met = Assigned();
        met.AddMessage(Agent, "oi", false, true, T0.AddMinutes(5));
        met.ChangeStatus(TicketStatus.Resolved, Agent, T0.AddMinutes(100));
        Assert.Equal(SlaState.Met, met.GetSlaState(T0.AddDays(5)));

        var cancelled = NewTicket();
        cancelled.ChangeStatus(TicketStatus.Cancelled, Requester, T0.AddMinutes(1));
        Assert.Equal(SlaState.NotApplicable, cancelled.GetSlaState(T0.AddDays(5)));
    }

    [Fact]
    public void Priority_SlaIsAlwaysPositiveAndOrdered()
    {
        Assert.Throws<HelpDeskPro.Domain.Exceptions.DomainException>(() => new Priority("X", 1, 0, 10));
        Assert.Throws<HelpDeskPro.Domain.Exceptions.DomainException>(() => new Priority("X", 1, 60, 30));
    }
}

public class CsvTests
{
    [Fact]
    public void Cell_QuotesAndDoublesQuotes() =>
        Assert.Equal("\"diz \"\"oi\"\"\"", Csv.Cell("diz \"oi\""));

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    public void Cell_NeutralizesFormulaInjection(string input) =>
        Assert.StartsWith("\"'", Csv.Cell(input));

    [Fact]
    public void Cell_NullBecomesEmpty() => Assert.Equal("\"\"", Csv.Cell(null));

    [Fact]
    public void Row_UsesSemicolonSeparator() =>
        Assert.Equal("\"a\";\"b\"", Csv.Row(["a", "b"]));

    [Fact]
    public void Encode_StartsWithUtf8Bom()
    {
        var bytes = Csv.Encode(["\"x\""]);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
    }
}

public class PresentationTimeTests
{
    [Fact]
    public void ToLocal_ConvertsUtcToSaoPaulo()
    {
        var local = PresentationTime.ToLocal(new DateTime(2026, 1, 15, 2, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 1, 14, 23, 0, 0), local);
    }

    [Fact]
    public void LocalMidnightToUtc_RoundTripsWithToLocal()
    {
        var utc = PresentationTime.LocalMidnightToUtc(new DateTime(2026, 1, 14));
        Assert.Equal(new DateTime(2026, 1, 14, 3, 0, 0), utc);
        Assert.Equal(new DateTime(2026, 1, 14), PresentationTime.ToLocal(utc));
    }
}
