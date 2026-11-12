using System.Net;
using System.Net.Http.Json;

namespace HelpDeskPro.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CatalogTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    // ---------- Categorias ----------

    [Fact]
    public async Task Categories_AnyUserReads_OnlyAdminWrites()
    {
        var ana = await As(Seeded.Ana);
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/v1/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsJsonAsync("/api/v1/categories", new { name = Unique("X") })).StatusCode);

        var camila = await As(Seeded.Camila);
        Assert.Equal(HttpStatusCode.Forbidden, (await camila.PostAsJsonAsync("/api/v1/categories", new { name = Unique("X") })).StatusCode);
    }

    [Fact]
    public async Task Categories_AdminCrud_DeactivatedIsHiddenAndRejectedForNewTickets()
    {
        var admin = await As(Seeded.Admin);
        var name = Unique("Cat");
        var created = await admin.PostAsJsonAsync("/api/v1/categories", new { name, description = "teste", defaultTeamId = await TeamId("Suporte N1") });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Json()).Guid("id");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/v1/categories", new { name })).StatusCode);

        var renamed = Unique("Renomeada");
        var updated = await admin.PutAsJsonAsync($"/api/v1/categories/{id}", new { name = renamed, description = "novo" });
        Assert.Equal(renamed, (await updated.Json()).Str("name"));

        var ana = await As(Seeded.Ana);
        var body = new { title = "t", description = "d", categoryId = id, priorityId = await PriorityId("Baixa") };
        Assert.Equal(HttpStatusCode.Created, (await ana.PostAsJsonAsync("/api/v1/tickets", body)).StatusCode);

        await admin.PatchJsonAsync($"/api/v1/categories/{id}/active", new { isActive = false });
        var anaList = await (await ana.GetAsync("/api/v1/categories")).Json();
        Assert.DoesNotContain(anaList.EnumerateArray(), c => c.Guid("id") == id);
        var adminList = await (await admin.GetAsync("/api/v1/categories")).Json();
        Assert.Contains(adminList.EnumerateArray(), c => c.Guid("id") == id && !c.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ana.PostAsJsonAsync("/api/v1/tickets", body)).StatusCode);
    }

    [Fact]
    public async Task Categories_InvalidPayload_Returns400_UnknownId404()
    {
        var admin = await As(Seeded.Admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/categories", new { name = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/v1/categories/{Guid.NewGuid()}", new { name = "Algo" })).StatusCode);
    }

    // ---------- Prioridades ----------

    [Fact]
    public async Task Priorities_SeededWithSlaFromSpec()
    {
        var admin = await As(Seeded.Admin);
        var list = await (await admin.GetAsync("/api/v1/priorities")).Json();
        var byName = list.EnumerateArray().ToDictionary(p => p.Str("name"));

        Assert.Equal((480, 4320), (byName["Baixa"].GetProperty("firstResponseMinutes").GetInt32(), byName["Baixa"].GetProperty("resolutionMinutes").GetInt32()));
        Assert.Equal((240, 2880), (byName["Média"].GetProperty("firstResponseMinutes").GetInt32(), byName["Média"].GetProperty("resolutionMinutes").GetInt32()));
        Assert.Equal((60, 480), (byName["Alta"].GetProperty("firstResponseMinutes").GetInt32(), byName["Alta"].GetProperty("resolutionMinutes").GetInt32()));
        Assert.Equal((15, 240), (byName["Crítica"].GetProperty("firstResponseMinutes").GetInt32(), byName["Crítica"].GetProperty("resolutionMinutes").GetInt32()));
    }

    [Fact]
    public async Task Priorities_OnlyAdminWrites_InvalidSlaRejected_ChangesDoNotAffectExistingTickets()
    {
        var ana = await As(Seeded.Ana);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ana.PostAsJsonAsync("/api/v1/priorities", new { name = "Z", level = 9, firstResponseMinutes = 10, resolutionMinutes = 20 })).StatusCode);

        var admin = await As(Seeded.Admin);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/v1/priorities", new { name = Unique("P"), level = 9, firstResponseMinutes = 60, resolutionMinutes = 30 })).StatusCode);

        // Cria uma prioridade própria, abre um chamado, altera prazos e confirma que o chamado não muda.
        var level = Random.Shared.Next(5, 10);
        var created = await admin.PostAsJsonAsync("/api/v1/priorities", new { name = Unique("Prio"), level, firstResponseMinutes = 30, resolutionMinutes = 120 });
        if (created.StatusCode == HttpStatusCode.UnprocessableEntity) return; // nível já usado por execução anterior
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var priority = await created.Json();

        var res = await ana.PostAsJsonAsync("/api/v1/tickets", new { title = "t", description = "d", categoryId = await CategoryId("Hardware"), priorityId = priority.Guid("id") });
        var ticket = await res.Json();
        var due = ticket.GetProperty("sla").GetProperty("resolutionDueAt").GetDateTime();

        await admin.PutAsJsonAsync($"/api/v1/priorities/{priority.Guid("id")}",
            new { name = priority.Str("name"), level, firstResponseMinutes = 10, resolutionMinutes = 20 });
        var again = await (await ana.GetAsync(Url(ticket))).Json();
        Assert.Equal(due, again.GetProperty("sla").GetProperty("resolutionDueAt").GetDateTime());

        await admin.PatchJsonAsync($"/api/v1/priorities/{priority.Guid("id")}/active", new { isActive = false });
    }

    // ---------- Equipes ----------

    [Fact]
    public async Task Teams_RequesterForbidden_AgentCanList_OnlyAdminWrites()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As(Seeded.Ana)).GetAsync("/api/v1/teams")).StatusCode);

        var camila = await As(Seeded.Camila);
        Assert.Equal(HttpStatusCode.OK, (await camila.GetAsync("/api/v1/teams")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await camila.PostAsJsonAsync("/api/v1/teams", new { name = Unique("T") })).StatusCode);
    }

    [Fact]
    public async Task Teams_AdminManagesMembers_WithValidation()
    {
        var admin = await As(Seeded.Admin);
        var created = await admin.PostAsJsonAsync("/api/v1/teams", new { name = Unique("Equipe"), description = "d" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var teamId = (await created.Json()).Guid("id");

        var bruno = await UserId(Seeded.Bruno);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/v1/teams/{teamId}/members", new { userId = bruno })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/teams/{teamId}/members", new { userId = bruno })).StatusCode);
        // Solicitante não pode ser membro de equipe.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/teams/{teamId}/members", new { userId = await UserId(Seeded.Ana) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/v1/teams/{teamId}/members", new { userId = Guid.NewGuid() })).StatusCode);

        var members = await (await admin.GetAsync($"/api/v1/teams/{teamId}/members")).Json();
        Assert.Equal(1, members.GetArrayLength());

        // Membro vê a equipe; agente de fora não.
        Assert.Equal(HttpStatusCode.OK, (await (await As(Seeded.Bruno)).GetAsync($"/api/v1/teams/{teamId}/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As(Seeded.Camila)).GetAsync($"/api/v1/teams/{teamId}/members")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/teams/{teamId}/members/{bruno}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/teams/{teamId}/members/{bruno}")).StatusCode);

        await admin.PatchJsonAsync($"/api/v1/teams/{teamId}/active", new { isActive = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/teams/{teamId}/members", new { userId = bruno })).StatusCode);
    }

    [Fact]
    public async Task Agents_ListAvailableToStaffOnly()
    {
        var staff = await (await (await As(Seeded.Camila)).GetAsync("/api/v1/users/agents")).Json();
        Assert.True(staff.GetArrayLength() >= 3);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As(Seeded.Ana)).GetAsync("/api/v1/users/agents")).StatusCode);
    }
}
