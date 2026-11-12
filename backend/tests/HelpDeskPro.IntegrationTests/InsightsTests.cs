using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace HelpDeskPro.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SlaTests(ApiFactory factory) : CommunicationTestBase(factory)
{
    private static IEnumerable<JsonElement> NotificationsFor(JsonElement page, JsonElement ticket) =>
        page.GetProperty("items").EnumerateArray()
            .Where(n => n.TryGetProperty("ticketId", out var id) && id.ValueKind == JsonValueKind.String && id.GetGuid() == ticket.Guid("id"));

    private static async Task<JsonElement> Inbox(HttpClient c) =>
        await (await c.GetAsync("/api/v1/notifications?pageSize=100")).Json();

    private static async Task<List<JsonElement>> History(HttpClient c, JsonElement ticket) =>
        (await (await c.GetAsync(Url(ticket, "/history"))).Json()).EnumerateArray().ToList();

    [Fact]
    public async Task NewTicket_StartsOnTrack_InDetailAndList()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana, "Hardware", "Alta");

        Assert.Equal("ON_TRACK", ticket.Str("slaState"));
        var list = await (await ana.GetAsync($"/api/v1/tickets?search={ticket.Str("ticketNumber")}")).Json();
        Assert.Equal("ON_TRACK", list.GetProperty("items")[0].Str("slaState"));
        Assert.Equal(JsonValueKind.Null, ticket.GetProperty("sla").GetProperty("firstResponseBreachedAt").ValueKind);
    }

    [Fact]
    public async Task Monitor_WarnsBeforeDeadline_Once_AndDoesNotBreach()
    {
        var ticket = await AssignedTicket(await As(Seeded.Ana), "Hardware"); // prioridade Média: 4 h
        var created = ticket.GetProperty("createdAt").GetDateTime();

        await RunSla(created.AddMinutes(200)); // 83% de 240 min
        var camila = await As(Seeded.Camila);
        Assert.Single(NotificationsFor(await Inbox(camila), ticket), n => n.Str("type") == "SLA_NEAR_DUE");

        await RunSla(created.AddMinutes(205));
        Assert.Single(NotificationsFor(await Inbox(camila), ticket), n => n.Str("type") == "SLA_NEAR_DUE"); // sem duplicar

        var detail = await (await camila.GetAsync(Url(ticket))).Json();
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("sla").GetProperty("breachedAt").ValueKind);
        Assert.DoesNotContain(await History(camila, ticket), h => h.Str("action") == "SLA_BREACHED");
    }

    [Fact]
    public async Task Monitor_BreachesUnansweredTicket_RecordsSystemHistory_NotifiesTeamAndAdmins_NotRequester()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana, "Hardware", "Alta");
        var created = ticket.GetProperty("createdAt").GetDateTime();

        var first = await RunSla(created.AddMinutes(90));
        Assert.True(first.Breaches >= 1);

        var admin = await As(Seeded.Admin);
        var entry = (await History(admin, ticket)).Single(h => h.Str("action") == "SLA_BREACHED");
        Assert.Equal("Sistema", entry.Str("actorName"));
        Assert.Equal("FIRST_RESPONSE", entry.Str("newValue"));
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("actorId").ValueKind);

        Assert.Contains(NotificationsFor(await Inbox(await As(Seeded.Camila)), ticket), n => n.Str("type") == "SLA_BREACHED");
        Assert.Contains(NotificationsFor(await Inbox(admin), ticket), n => n.Str("type") == "SLA_BREACHED");
        Assert.DoesNotContain(NotificationsFor(await Inbox(ana), ticket), n => n.Str("type").StartsWith("SLA"));
        Assert.Empty(NotificationsFor(await Inbox(await As(Seeded.Bruno)), ticket));

        var detail = await (await admin.GetAsync(Url(ticket))).Json();
        Assert.NotEqual(JsonValueKind.Null, detail.GetProperty("sla").GetProperty("firstResponseBreachedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, detail.GetProperty("sla").GetProperty("breachedAt").ValueKind);

        // Idempotente: a mesma varredura não registra nem notifica de novo.
        var second = await RunSla(created.AddMinutes(90));
        Assert.Equal(0, second.Breaches);
        Assert.Single((await History(admin, ticket)), h => h.Str("action") == "SLA_BREACHED");
    }

    [Fact]
    public async Task Monitor_RecordsResolutionBreachSeparately_AndSkipsAnsweredFirstResponse()
    {
        var ana = await As(Seeded.Ana);
        var camila = await As(Seeded.Camila);
        var ticket = await AssignedTicket(ana, "Hardware"); // Média: 240 / 2880
        await Say(camila, ticket, "Já estou verificando.");
        var created = ticket.GetProperty("createdAt").GetDateTime();

        await RunSla(created.AddDays(3));

        var kinds = (await History(camila, ticket)).Where(h => h.Str("action") == "SLA_BREACHED").Select(h => h.Str("newValue")).ToArray();
        Assert.Equal(["RESOLUTION"], kinds);
    }

    [Fact]
    public async Task Monitor_IgnoresResolvedAndCancelledTickets()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        await ana.PatchJsonAsync(Url(ticket, "/status"), new { status = "CANCELLED" });

        await RunSla(DateTime.UtcNow.AddDays(30));

        Assert.DoesNotContain(await History(await As(Seeded.Admin), ticket), h => h.Str("action") == "SLA_BREACHED");
    }

    [Fact]
    public async Task OverdueState_AndFilter_ReflectExpiredDeadlines()
    {
        var ana = await As(Seeded.Ana);
        var late = await OpenTicket(ana, "Hardware");
        var fresh = await OpenTicket(ana, "Hardware");
        await ExpireSla(late);

        var detail = await (await ana.GetAsync(Url(late))).Json();
        Assert.Equal("OVERDUE", detail.Str("slaState"));

        var admin = await As(Seeded.Admin);
        var overdue = await (await admin.GetAsync("/api/v1/tickets?overdue=true&pageSize=100")).Json();
        var items = overdue.GetProperty("items").EnumerateArray().ToList();
        Assert.All(items, i => Assert.Equal("OVERDUE", i.Str("slaState")));
        Assert.Contains(items, i => i.Guid("id") == late.Guid("id"));
        Assert.DoesNotContain(items, i => i.Guid("id") == fresh.Guid("id"));
    }

    [Fact]
    public async Task LateReply_RecordsBreachImmediately_WithoutWaitingForTheMonitor()
    {
        var ana = await As(Seeded.Ana);
        var camila = await As(Seeded.Camila);
        var ticket = await AssignedTicket(ana, "Hardware");
        await ExpireSla(ticket);

        await Say(camila, ticket, "desculpe a demora");

        var entry = (await History(camila, ticket)).Single(h => h.Str("action") == "SLA_BREACHED");
        Assert.Equal("FIRST_RESPONSE", entry.Str("newValue"));
    }

    [Fact]
    public async Task ResolvingLate_IsMissed_ResolvingInTime_IsMet()
    {
        var ana = await As(Seeded.Ana);
        var camila = await As(Seeded.Camila);

        var onTime = await AssignedTicket(ana, "Hardware");
        await Say(camila, onTime, "ok");
        var met = await (await camila.PatchJsonAsync(Url(onTime, "/status"), new { status = "RESOLVED" })).Json();
        Assert.Equal("MET", met.Str("slaState"));

        var late = await AssignedTicket(ana, "Hardware");
        await Say(camila, late, "ok");
        await ExpireSla(late, firstResponse: false, resolution: true);
        var missed = await (await camila.PatchJsonAsync(Url(late, "/status"), new { status = "RESOLVED" })).Json();
        Assert.Equal("MISSED", missed.Str("slaState"));
        Assert.Contains(await History(camila, late), h => h.Str("action") == "SLA_BREACHED" && h.Str("newValue") == "RESOLUTION");
    }

    [Fact]
    public async Task CancelledTicket_HasNoSlaState()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        var cancelled = await (await ana.PatchJsonAsync(Url(ticket, "/status"), new { status = "CANCELLED" })).Json();
        Assert.Equal("NOT_APPLICABLE", cancelled.Str("slaState"));
    }
}

[Collection(ApiCollection.Name)]
public sealed class DashboardTests(ApiFactory factory) : CommunicationTestBase(factory)
{
    private static async Task<JsonElement> Summary(HttpClient c, int days = 30) =>
        await (await c.GetAsync($"/api/v1/dashboard/summary?days={days}")).Json();

    private static int Int(JsonElement e, string name) => e.GetProperty(name).GetInt32();

    private static int Sum(JsonElement array, string field = "count") => array.EnumerateArray().Sum(x => x.GetProperty(field).GetInt32());

    [Fact]
    public async Task Admin_Summary_IsConsistentWithTheTicketList()
    {
        var admin = await As(Seeded.Admin);
        var s = await Summary(admin);
        var list = await (await admin.GetAsync("/api/v1/tickets?pageSize=1")).Json();

        Assert.Equal(list.GetProperty("totalCount").GetInt32(), Int(s, "total"));
        Assert.Equal(Int(s, "total"),
            Int(s, "open") + Int(s, "inProgress") + Int(s, "waitingForUser") + Int(s, "resolved") + Int(s, "closed") + Int(s, "cancelled"));
        Assert.Equal(30, Int(s, "days"));
        Assert.Equal(30, s.GetProperty("timeline").GetArrayLength());

        var overdue = await (await admin.GetAsync("/api/v1/tickets?overdue=true&pageSize=1")).Json();
        Assert.Equal(overdue.GetProperty("totalCount").GetInt32(), Int(s, "overdue"));
    }

    [Fact]
    public async Task Breakdowns_AndTimeline_CoverTheSameTicketsInTheWindow()
    {
        var s = await Summary(await As(Seeded.Admin), 90);
        var created = Sum(s.GetProperty("timeline"), "created");
        Assert.Equal(created, Sum(s.GetProperty("byPriority")));
        Assert.Equal(created, Sum(s.GetProperty("byCategory")));
    }

    [Fact]
    public async Task Summary_ReflectsNewTickets_ResolutionsAndResponseTimes()
    {
        var admin = await As(Seeded.Admin);
        var before = await Summary(admin);
        var today = DateTime.UtcNow.AddHours(-3).ToString("yyyy-MM-dd");
        int Created(JsonElement s) => s.GetProperty("timeline").EnumerateArray().Single(p => p.Str("date") == today).GetProperty("created").GetInt32();
        int Resolved(JsonElement s) => s.GetProperty("timeline").EnumerateArray().Single(p => p.Str("date") == today).GetProperty("resolved").GetInt32();
        int Category(JsonElement s, string name) => s.GetProperty("byCategory").EnumerateArray().FirstOrDefault(c => c.ValueKind != JsonValueKind.Undefined && c.Str("name") == name) is { ValueKind: JsonValueKind.Object } c ? c.GetProperty("count").GetInt32() : 0;

        var ana = await As(Seeded.Ana);
        var camila = await As(Seeded.Camila);
        var ticket = await AssignedTicket(ana, "Hardware");

        var afterOpen = await Summary(admin);
        Assert.Equal(Int(before, "total") + 1, Int(afterOpen, "total"));
        Assert.Equal(Int(before, "inProgress") + 1, Int(afterOpen, "inProgress"));
        Assert.Equal(Created(before) + 1, Created(afterOpen));
        Assert.Equal(Category(before, "Hardware") + 1, Category(afterOpen, "Hardware"));

        await Say(camila, ticket, "resposta");
        await camila.PatchJsonAsync(Url(ticket, "/status"), new { status = "RESOLVED" });

        var afterResolve = await Summary(admin);
        Assert.Equal(Int(afterOpen, "resolved") + 1, Int(afterResolve, "resolved"));
        Assert.Equal(Int(afterOpen, "inProgress") - 1, Int(afterResolve, "inProgress"));
        Assert.Equal(Resolved(afterOpen) + 1, Resolved(afterResolve));
        Assert.True(afterResolve.GetProperty("avgFirstResponseMinutes").GetDouble() >= 0);
        Assert.True(afterResolve.GetProperty("avgResolutionMinutes").GetDouble() >= 0);
    }

    [Fact]
    public async Task Requester_SeesOnlyOwnNumbers_AndNoAgentWorkload()
    {
        var ana = await As(Seeded.Ana);
        await OpenTicket(ana);
        var s = await Summary(ana);
        var own = await (await ana.GetAsync("/api/v1/tickets?pageSize=1")).Json();

        Assert.Equal(own.GetProperty("totalCount").GetInt32(), Int(s, "total"));
        Assert.Equal(0, s.GetProperty("byAgent").GetArrayLength());
    }

    [Fact]
    public async Task Agent_SummaryIsLimitedToTheirScope()
    {
        var bruno = await As(Seeded.Bruno);
        var s = await Summary(bruno);
        var scoped = await (await bruno.GetAsync("/api/v1/tickets?pageSize=1")).Json();
        var admin = await Summary(await As(Seeded.Admin));

        Assert.Equal(scoped.GetProperty("totalCount").GetInt32(), Int(s, "total"));
        Assert.True(Int(s, "total") < Int(admin, "total"));
    }

    [Fact]
    public async Task ByAgent_ListsWorkloadForStaff()
    {
        var ana = await As(Seeded.Ana);
        await AssignedTicket(ana, "Hardware");

        var s = await Summary(await As(Seeded.Admin));
        var camila = s.GetProperty("byAgent").EnumerateArray().Single(a => a.Str("name") == "Camila Torres");
        Assert.True(camila.GetProperty("total").GetInt32() >= 1);
        Assert.True(camila.GetProperty("active").GetInt32() >= 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(366)]
    public async Task Summary_InvalidWindow_Returns400(int days) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await (await As(Seeded.Admin)).GetAsync($"/api/v1/dashboard/summary?days={days}")).StatusCode);

    [Fact]
    public async Task Summary_RequiresAuthentication() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Factory.CreateClient().GetAsync("/api/v1/dashboard/summary")).StatusCode);
}

[Collection(ApiCollection.Name)]
public sealed class ReportTests(ApiFactory factory) : CommunicationTestBase(factory)
{
    [Theory]
    [InlineData(Seeded.Ana)]
    [InlineData(Seeded.Camila)]
    public async Task Reports_AreAdminOnly(string email)
    {
        var client = await As(email);
        foreach (var url in new[] { "/api/v1/reports/sla", "/api/v1/reports/agents", "/api/v1/reports/tickets/export", "/api/v1/audit" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Reports_RequireAuthentication() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Factory.CreateClient().GetAsync("/api/v1/reports/sla")).StatusCode);

    [Fact]
    public async Task SlaReport_HasComplianceRatesPerPriority()
    {
        var admin = await As(Seeded.Admin);
        var report = await (await admin.GetAsync($"/api/v1/reports/sla?from={DateTime.UtcNow.AddDays(-90):O}")).Json();

        Assert.True(report.GetProperty("rows").GetArrayLength() >= 1);
        foreach (var row in report.GetProperty("rows").EnumerateArray())
        {
            var rate = row.GetProperty("complianceRate").GetDouble();
            Assert.InRange(rate, 0, 100);
            Assert.True(row.GetProperty("total").GetInt32() >= row.GetProperty("firstResponseMissed").GetInt32());
        }
        Assert.InRange(report.GetProperty("overallComplianceRate").GetDouble(), 0, 100);
    }

    [Fact]
    public async Task Reports_InvalidPeriod_Returns400()
    {
        var admin = await As(Seeded.Admin);
        var res = await admin.GetAsync($"/api/v1/reports/sla?from={DateTime.UtcNow:O}&to={DateTime.UtcNow.AddDays(-5):O}");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task AgentReport_CountsWorkPerAgent()
    {
        await AssignedTicket(await As(Seeded.Ana), "Hardware");
        var report = await (await (await As(Seeded.Admin)).GetAsync($"/api/v1/reports/agents?from={DateTime.UtcNow.AddDays(-90):O}")).Json();
        var camila = report.GetProperty("rows").EnumerateArray().Single(r => r.Str("agentName") == "Camila Torres");
        Assert.True(camila.GetProperty("assigned").GetInt32() >= 1);
    }

    [Fact]
    public async Task CsvExport_HasBomHeaderRows_AndNeutralizesFormulaInjection()
    {
        var ana = await As(Seeded.Ana);
        var evil = await OpenTicket(ana, title: "=HYPERLINK(\"http://evil.example\";\"clique\")");
        var number = evil.Str("ticketNumber");

        var res = await (await As(Seeded.Admin)).GetAsync($"/api/v1/reports/tickets/export?from={DateTime.UtcNow.AddHours(-1):O}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", res.Content.Headers.ContentDisposition?.DispositionType);
        Assert.EndsWith(".csv", res.Content.Headers.ContentDisposition?.FileName);

        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("﻿\"Número\";\"Título\"", text);
        var line = text.Split("\r\n").Single(l => l.Contains(number));
        Assert.Contains("\"'=HYPERLINK(", line);
        Assert.DoesNotContain(";\"=HYPERLINK", line);
        Assert.DoesNotContain("\"=HYPERLINK", line.Replace("\"'=HYPERLINK", string.Empty));
    }

    [Fact]
    public async Task Audit_FiltersByTicketAndAction_AndPaginates()
    {
        var admin = await As(Seeded.Admin);
        var ana = await As(Seeded.Ana);
        var ticket = await AssignedTicket(ana, "Hardware");
        await RunSla(ticket.GetProperty("createdAt").GetDateTime().AddDays(10));
        var number = ticket.Str("ticketNumber");

        var all = await (await admin.GetAsync($"/api/v1/audit?ticketNumber={number}&pageSize=100")).Json();
        var actions = all.GetProperty("items").EnumerateArray().Select(e => e.Str("action")).ToList();
        Assert.Contains("CREATED", actions);
        Assert.Contains("ASSIGNED", actions);
        Assert.Contains("SLA_BREACHED", actions);
        Assert.All(all.GetProperty("items").EnumerateArray(), e => Assert.Equal(number, e.Str("ticketNumber")));

        var breaches = await (await admin.GetAsync($"/api/v1/audit?ticketNumber={number}&action=SLA_BREACHED")).Json();
        Assert.All(breaches.GetProperty("items").EnumerateArray(), e =>
        {
            Assert.Equal("SLA_BREACHED", e.Str("action"));
            Assert.Equal("Sistema", e.Str("actorName"));
        });

        var assigned = (await (await admin.GetAsync($"/api/v1/audit?ticketNumber={number}&action=ASSIGNED")).Json())
            .GetProperty("items")[0];
        Assert.Equal("Camila Torres", assigned.Str("newValue"));

        var page = await (await admin.GetAsync($"/api/v1/audit?ticketNumber={number}&pageSize=2&page=1")).Json();
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.True(page.GetProperty("totalCount").GetInt32() > 2);
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=0")]
    [InlineData("action=NADA")]
    public async Task Audit_InvalidParameters_Return400(string query) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await (await As(Seeded.Admin)).GetAsync($"/api/v1/audit?{query}")).StatusCode);

    [Fact]
    public async Task Audit_WildcardsInTicketNumberAreLiteral()
    {
        var res = await (await (await As(Seeded.Admin)).GetAsync("/api/v1/audit?ticketNumber=%25")).Json();
        Assert.Equal(0, res.GetProperty("totalCount").GetInt32());
    }
}
