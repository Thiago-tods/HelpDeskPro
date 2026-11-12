using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HelpDeskPro.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TicketTests(ApiFactory factory) : ApiTestBase(factory)
{
    // ---------- Criação ----------

    [Fact]
    public async Task Create_AssignsReadableNumber_TeamFromCategory_AndCreatedHistory()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana, "Hardware");

        Assert.Matches(@"^HDP-\d{4}-\d{6}$", ticket.Str("ticketNumber"));
        Assert.Equal("NEW", ticket.Str("status"));
        Assert.Equal(await TeamId("Suporte N1"), ticket.Guid("teamId"));
        Assert.Equal(await UserId(Seeded.Ana), ticket.Guid("requesterId"));

        var history = await (await ana.GetAsync(Url(ticket, "/history"))).Json();
        Assert.Equal("CREATED", history[0].Str("action"));
    }

    [Fact]
    public async Task Create_RoutesInfraCategoryToInfraTeam()
    {
        var ticket = await OpenTicket(await As(Seeded.Ana), "Rede e Internet");
        Assert.Equal(await TeamId("Infraestrutura"), ticket.Guid("teamId"));
    }

    [Fact]
    public async Task Create_SlaDeadlinesFollowPriority()
    {
        var ticket = await OpenTicket(await As(Seeded.Ana), "Hardware", "Crítica");
        var sla = ticket.GetProperty("sla");
        var created = ticket.GetProperty("createdAt").GetDateTime();

        Assert.Equal(created.AddMinutes(15), sla.GetProperty("firstResponseDueAt").GetDateTime());
        Assert.Equal(created.AddMinutes(240), sla.GetProperty("resolutionDueAt").GetDateTime());
    }

    [Fact]
    public async Task Create_Validation_Returns400And422()
    {
        var ana = await As(Seeded.Ana);
        var bad = await ana.PostAsJsonAsync("/api/v1/tickets", new { title = "", description = "x", categoryId = Guid.NewGuid(), priorityId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var unknownCategory = await ana.PostAsJsonAsync("/api/v1/tickets",
            new { title = "ok", description = "ok", categoryId = Guid.NewGuid(), priorityId = await PriorityId("Baixa") });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownCategory.StatusCode);
    }

    [Fact]
    public async Task Create_RequesterCannotActOnBehalfOfOthersOrPickTeam()
    {
        var ana = await As(Seeded.Ana);
        var body = new
        {
            title = "x",
            description = "y",
            categoryId = await CategoryId("Hardware"),
            priorityId = await PriorityId("Baixa"),
            requesterId = await UserId(Seeded.Carlos),
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsJsonAsync("/api/v1/tickets", body)).StatusCode);

        var withTeam = new
        {
            title = "x",
            description = "y",
            categoryId = await CategoryId("Hardware"),
            priorityId = await PriorityId("Baixa"),
            teamId = await TeamId("Infraestrutura"),
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsJsonAsync("/api/v1/tickets", withTeam)).StatusCode);
    }

    [Fact]
    public async Task Create_StaffCanOpenOnBehalfOfRequester()
    {
        var admin = await As(Seeded.Admin);
        var res = await admin.PostAsJsonAsync("/api/v1/tickets", new
        {
            title = "Aberto pelo admin",
            description = "y",
            categoryId = await CategoryId("Hardware"),
            priorityId = await PriorityId("Baixa"),
            requesterId = await UserId(Seeded.Carlos),
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(await UserId(Seeded.Carlos), (await res.Json()).Guid("requesterId"));
    }

    // ---------- Escopo / IDOR ----------

    [Fact]
    public async Task Requester_CannotSeeAnotherRequestersTicket_IdorReturns404()
    {
        var ticket = await OpenTicket(await As(Seeded.Ana));
        var carlos = await As(Seeded.Carlos);

        Assert.Equal(HttpStatusCode.NotFound, (await carlos.GetAsync(Url(ticket))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await carlos.GetAsync(Url(ticket, "/history"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await carlos.PatchJsonAsync(Url(ticket), new { title = "invadido" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await carlos.PatchJsonAsync(Url(ticket, "/status"), new { status = "CANCELLED" })).StatusCode);
    }

    [Fact]
    public async Task Requester_ListContainsOnlyOwnTickets()
    {
        var ana = await As(Seeded.Ana);
        await OpenTicket(ana);
        var anaId = await UserId(Seeded.Ana);

        var list = await (await ana.GetAsync("/api/v1/tickets?pageSize=100")).Json();
        Assert.All(list.GetProperty("items").EnumerateArray(), t => Assert.Equal(anaId, t.Guid("requesterId")));

        // O filtro por solicitante é ignorado para solicitantes.
        var carlosId = await UserId(Seeded.Carlos);
        var spoof = await (await ana.GetAsync($"/api/v1/tickets?pageSize=100&requesterId={carlosId}")).Json();
        Assert.All(spoof.GetProperty("items").EnumerateArray(), t => Assert.Equal(anaId, t.Guid("requesterId")));
    }

    [Fact]
    public async Task Agent_SeesOnlyOwnTeamTickets()
    {
        var ticket = await OpenTicket(await As(Seeded.Ana), "Hardware"); // equipe N1
        var camila = await As(Seeded.Camila);
        var bruno = await As(Seeded.Bruno);

        Assert.Equal(HttpStatusCode.OK, (await camila.GetAsync(Url(ticket))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bruno.GetAsync(Url(ticket))).StatusCode);

        var search = ticket.Str("title");
        var brunoList = await (await bruno.GetAsync($"/api/v1/tickets?search={search}")).Json();
        Assert.Equal(0, brunoList.GetProperty("totalCount").GetInt32());
        var camilaList = await (await camila.GetAsync($"/api/v1/tickets?search={search}")).Json();
        Assert.Equal(1, camilaList.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Admin_SeesEverything()
    {
        var ticket = await OpenTicket(await As(Seeded.Carlos), "Rede e Internet");
        Assert.Equal(HttpStatusCode.OK, (await (await As(Seeded.Admin)).GetAsync(Url(ticket))).StatusCode);
    }

    // ---------- Atribuição ----------

    [Fact]
    public async Task Agent_CanAssumeFreeTicketOfOwnTeam_MovingItToInProgress()
    {
        var ticket = await OpenTicket(await As(Seeded.Ana), "Hardware");
        var camila = await As(Seeded.Camila);

        var detail = await (await camila.GetAsync(Url(ticket))).Json();
        Assert.True(detail.GetProperty("canAssignToMe").GetBoolean());

        var res = await camila.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Camila) });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var updated = await res.Json();
        Assert.Equal("IN_PROGRESS", updated.Str("status"));
        Assert.Equal(await UserId(Seeded.Camila), updated.Guid("assignedToId"));
    }

    [Fact]
    public async Task Agent_CannotAssumeTicketOfOtherTeam_Or_AssignToOthers()
    {
        var ticket = await OpenTicket(await As(Seeded.Ana), "Hardware");
        var bruno = await As(Seeded.Bruno);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bruno.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Bruno) })).StatusCode);

        var camila = await As(Seeded.Camila);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await camila.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Rafael) })).StatusCode);
    }

    [Fact]
    public async Task Agent_CannotTakeTicketAlreadyAssignedToColleague()
    {
        var ticket = await OpenTicket(await As(Seeded.Ana), "Hardware");
        var camila = await As(Seeded.Camila);
        await camila.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Camila) });

        var rafael = await As(Seeded.Rafael);
        var res = await rafael.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Rafael) });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Requester_CannotAssign()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        var res = await ana.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Camila) });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Admin_AssignmentValidatesAssigneeAndTeamMembership()
    {
        var ticket = await OpenTicket(await As(Seeded.Ana), "Hardware"); // equipe N1
        var admin = await As(Seeded.Admin);

        // Bruno é agente, mas não integra a equipe N1.
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Bruno) })).StatusCode);
        // Solicitante não pode ser responsável.
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Carlos) })).StatusCode);

        var ok = await admin.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Rafael) });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    // ---------- Status ----------

    [Fact]
    public async Task Status_FullLifecycle_ByRoleWithHistory()
    {
        var ana = await As(Seeded.Ana);
        var camila = await As(Seeded.Camila);
        var ticket = await OpenTicket(ana, "Hardware");

        await camila.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Camila) });

        Assert.Equal("WAITING_FOR_USER", (await (await camila.PatchJsonAsync(Url(ticket, "/status"), new { status = "WAITING_FOR_USER" })).Json()).Str("status"));
        Assert.Equal("IN_PROGRESS", (await (await ana.PatchJsonAsync(Url(ticket, "/status"), new { status = "IN_PROGRESS" })).Json()).Str("status"));

        var resolved = await (await camila.PatchJsonAsync(Url(ticket, "/status"), new { status = "RESOLVED" })).Json();
        Assert.Equal("RESOLVED", resolved.Str("status"));
        Assert.NotEqual(JsonValueKind.Null, resolved.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(new[] { "CLOSED", "IN_PROGRESS" }, ((await (await ana.GetAsync(Url(ticket))).Json()).GetProperty("allowedStatuses")).EnumerateArray().Select(x => x.GetString()).Order().ToArray());

        var reopened = await (await ana.PatchJsonAsync(Url(ticket, "/status"), new { status = "IN_PROGRESS" })).Json();
        Assert.Equal("IN_PROGRESS", reopened.Str("status"));
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("resolvedAt").ValueKind);

        await camila.PatchJsonAsync(Url(ticket, "/status"), new { status = "RESOLVED" });
        var closed = await (await camila.PatchJsonAsync(Url(ticket, "/status"), new { status = "CLOSED" })).Json();
        Assert.Equal("CLOSED", closed.Str("status"));

        var actions = (await (await ana.GetAsync(Url(ticket, "/history"))).Json()).EnumerateArray().Select(h => h.Str("action")).ToList();
        Assert.Equal(
            ["CREATED", "ASSIGNED", "STATUS_CHANGED", "STATUS_CHANGED", "STATUS_CHANGED", "RESOLVED", "REOPENED", "RESOLVED", "CLOSED"],
            actions);
    }

    [Fact]
    public async Task Status_NewToInProgress_IsOnlyViaAssignment()
    {
        var ticket = await OpenTicket(await As(Seeded.Ana));
        var res = await (await As(Seeded.Admin)).PatchJsonAsync(Url(ticket, "/status"), new { status = "IN_PROGRESS" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task Status_InvalidTransition_Returns422_AndUnknownStatus400()
    {
        var admin = await As(Seeded.Admin);
        var ticket = await OpenTicket(await As(Seeded.Ana));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PatchJsonAsync(Url(ticket, "/status"), new { status = "RESOLVED" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchJsonAsync(Url(ticket, "/status"), new { status = "QUALQUER" })).StatusCode);
    }

    [Fact]
    public async Task Status_RequesterCannotResolve_NonAssigneeAgentCannotChange()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana, "Hardware");
        var camila = await As(Seeded.Camila);
        await camila.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Camila) });

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PatchJsonAsync(Url(ticket, "/status"), new { status = "RESOLVED" })).StatusCode);
        var rafael = await As(Seeded.Rafael);
        Assert.Equal(HttpStatusCode.Forbidden, (await rafael.PatchJsonAsync(Url(ticket, "/status"), new { status = "RESOLVED" })).StatusCode);
    }

    [Fact]
    public async Task Status_RequesterCanCancelOwnNewTicket_ThenTicketIsFinal()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);

        var cancelled = await ana.PatchJsonAsync(Url(ticket, "/status"), new { status = "CANCELLED" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ana.PatchJsonAsync(Url(ticket, "/status"), new { status = "NEW" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await (await As(Seeded.Admin)).PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Camila) })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PatchJsonAsync(Url(ticket), new { title = "novo título" })).StatusCode);
    }

    // ---------- Edição ----------

    [Fact]
    public async Task Update_RequesterEditsWhileNew_ButNotAfterAssignment_OrPriority()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana, "Hardware");

        var edited = await ana.PatchJsonAsync(Url(ticket), new { title = "Título corrigido" });
        Assert.Equal("Título corrigido", (await edited.Json()).Str("title"));

        Assert.Equal(HttpStatusCode.Forbidden,
            (await ana.PatchJsonAsync(Url(ticket), new { priorityId = await PriorityId("Crítica") })).StatusCode);

        await (await As(Seeded.Camila)).PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Camila) });
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PatchJsonAsync(Url(ticket), new { title = "tarde demais" })).StatusCode);
    }

    [Fact]
    public async Task Update_AssigneeChangesPriority_RecalculatesSlaAndRecordsNamedHistory()
    {
        var ticket = await OpenTicket(await As(Seeded.Ana), "Hardware", "Baixa");
        var camila = await As(Seeded.Camila);
        await camila.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Camila) });

        var updated = await (await camila.PatchJsonAsync(Url(ticket), new { priorityId = await PriorityId("Alta") })).Json();
        Assert.Equal("Alta", updated.Str("priorityName"));
        var created = updated.GetProperty("createdAt").GetDateTime();
        Assert.Equal(created.AddMinutes(60), updated.GetProperty("sla").GetProperty("firstResponseDueAt").GetDateTime());

        var history = await (await camila.GetAsync(Url(ticket, "/history"))).Json();
        var change = history.EnumerateArray().First(h => h.Str("action") == "PRIORITY_CHANGED");
        Assert.Equal("Baixa", change.Str("oldValue"));
        Assert.Equal("Alta", change.Str("newValue"));
        var assigned = history.EnumerateArray().First(h => h.Str("action") == "ASSIGNED");
        Assert.Equal("Camila Torres", assigned.Str("newValue"));
    }

    [Fact]
    public async Task Update_AgentWithoutAssignment_AndTeamChangeByAgent_AreForbidden()
    {
        var ticket = await OpenTicket(await As(Seeded.Ana), "Hardware");
        var camila = await As(Seeded.Camila);
        Assert.Equal(HttpStatusCode.Forbidden, (await camila.PatchJsonAsync(Url(ticket), new { title = "x" })).StatusCode);

        await camila.PatchJsonAsync(Url(ticket, "/assignment"), new { assignedToId = await UserId(Seeded.Camila) });
        Assert.Equal(HttpStatusCode.Forbidden,
            (await camila.PatchJsonAsync(Url(ticket), new { teamId = await TeamId("Infraestrutura") })).StatusCode);
    }

    [Fact]
    public async Task Update_EmptyPatch_Returns400()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PatchJsonAsync(Url(ticket), new { })).StatusCode);
    }

    // ---------- Listagem ----------

    [Fact]
    public async Task List_FilterBySearchAndStatus_Paginates_AndSorts()
    {
        var ana = await As(Seeded.Ana);
        var marker = $"LISTA{Guid.NewGuid():N}";
        var first = await OpenTicket(ana, title: $"{marker} A");
        await Task.Delay(20);
        await OpenTicket(ana, title: $"{marker} B");
        await Task.Delay(20);
        var third = await OpenTicket(ana, title: $"{marker} C");
        await ana.PatchJsonAsync(Url(third, "/status"), new { status = "CANCELLED" });

        var all = await (await ana.GetAsync($"/api/v1/tickets?search={marker}")).Json();
        Assert.Equal(3, all.GetProperty("totalCount").GetInt32());

        var open = await (await ana.GetAsync($"/api/v1/tickets?search={marker}&status=NEW,IN_PROGRESS")).Json();
        Assert.Equal(2, open.GetProperty("totalCount").GetInt32());

        var page1 = await (await ana.GetAsync($"/api/v1/tickets?search={marker}&pageSize=2&page=1&sortBy=createdAt&sortDir=asc")).Json();
        var page2 = await (await ana.GetAsync($"/api/v1/tickets?search={marker}&pageSize=2&page=2&sortBy=createdAt&sortDir=asc")).Json();
        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());
        Assert.Equal(1, page2.GetProperty("items").GetArrayLength());
        Assert.Equal(2, page1.GetProperty("totalPages").GetInt32());
        Assert.Equal(first.Guid("id"), page1.GetProperty("items")[0].Guid("id"));
        Assert.Equal(third.Guid("id"), page2.GetProperty("items")[0].Guid("id"));

        var desc = await (await ana.GetAsync($"/api/v1/tickets?search={marker}&sortBy=createdAt&sortDir=desc")).Json();
        Assert.Equal(third.Guid("id"), desc.GetProperty("items")[0].Guid("id"));
    }

    [Fact]
    public async Task List_SearchTreatsWildcardsLiterally()
    {
        var ana = await As(Seeded.Ana);
        await OpenTicket(ana);
        var res = await (await ana.GetAsync("/api/v1/tickets?search=%25")).Json();
        Assert.Equal(0, res.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_InvalidQueryParameters_Return400()
    {
        var ana = await As(Seeded.Ana);
        foreach (var query in new[] { "sortBy=senha", "sortDir=up", "pageSize=1000", "page=0", "status=XYZ" })
            Assert.Equal(HttpStatusCode.BadRequest, (await ana.GetAsync($"/api/v1/tickets?{query}")).StatusCode);
    }

    [Fact]
    public async Task List_FilterByAssigneeUnassignedAndMine()
    {
        var marker = $"ASSIGN{Guid.NewGuid():N}";
        var ana = await As(Seeded.Ana);
        var camila = await As(Seeded.Camila);
        var assigned = await OpenTicket(ana, title: $"{marker} 1");
        await OpenTicket(ana, title: $"{marker} 2");
        await camila.PatchJsonAsync(Url(assigned, "/assignment"), new { assignedToId = await UserId(Seeded.Camila) });

        var mine = await (await camila.GetAsync($"/api/v1/tickets?search={marker}&mine=true")).Json();
        Assert.Equal(1, mine.GetProperty("totalCount").GetInt32());
        var free = await (await camila.GetAsync($"/api/v1/tickets?search={marker}&unassigned=true")).Json();
        Assert.Equal(1, free.GetProperty("totalCount").GetInt32());
        var byAgent = await (await camila.GetAsync($"/api/v1/tickets?search={marker}&assignedToId={await UserId(Seeded.Camila)}")).Json();
        Assert.Equal(1, byAgent.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_FilterByPriorityCategoryAndTeam()
    {
        var marker = $"FILTRO{Guid.NewGuid():N}";
        var ana = await As(Seeded.Ana);
        await OpenTicket(ana, "Hardware", "Alta", $"{marker} h");
        await OpenTicket(ana, "Rede e Internet", "Baixa", $"{marker} r");
        var admin = await As(Seeded.Admin);

        async Task<int> Count(string query) =>
            (await (await admin.GetAsync($"/api/v1/tickets?search={marker}&{query}")).Json()).GetProperty("totalCount").GetInt32();

        Assert.Equal(1, await Count($"priorityId={await PriorityId("Alta")}"));
        Assert.Equal(1, await Count($"categoryId={await CategoryId("Rede e Internet")}"));
        Assert.Equal(1, await Count($"teamId={await TeamId("Infraestrutura")}"));
        Assert.Equal(0, await Count($"teamId={await TeamId("Infraestrutura")}&priorityId={await PriorityId("Alta")}"));
    }

    // ---------- Detalhe ----------

    [Fact]
    public async Task Detail_AllowedStatusesDependOnRole()
    {
        var ana = await As(Seeded.Ana);
        var ticket = await OpenTicket(ana, "Hardware");

        string[] Allowed(JsonElement d) => d.GetProperty("allowedStatuses").EnumerateArray().Select(x => x.GetString()!).Order().ToArray();

        Assert.Equal(["CANCELLED"], Allowed(await (await ana.GetAsync(Url(ticket))).Json()));
        Assert.Equal(["CANCELLED"], Allowed(await (await (await As(Seeded.Admin)).GetAsync(Url(ticket))).Json()));
        Assert.Empty(Allowed(await (await (await As(Seeded.Camila)).GetAsync(Url(ticket))).Json()));
    }

    [Fact]
    public async Task Tickets_RequireAuthentication()
    {
        var anonymous = Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/tickets", new { })).StatusCode);
    }
}
