using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDeskPro.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

public static class Seeded
{
    public const string Admin = "admin@nexora.example";
    public const string Rafael = "rafael.mendes@nexora.example";   // N1 + Infraestrutura
    public const string Camila = "camila.torres@nexora.example";   // N1
    public const string Bruno = "bruno.figueira@nexora.example";   // Infraestrutura
    public const string Ana = "ana.lima@nexora.example";           // solicitante
    public const string Carlos = "carlos.souza@nexora.example";    // solicitante
}

public static class HttpExtensions
{
    public static async Task<JsonElement> Json(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static Task<HttpResponseMessage> PatchJsonAsync(this HttpClient client, string url, object body) =>
        client.PatchAsync(url, JsonContent.Create(body));

    public static Guid Guid(this JsonElement e, string property) => e.GetProperty(property).GetGuid();
    public static string Str(this JsonElement e, string property) => e.GetProperty(property).GetString()!;
}

/// <summary>Base com utilitários para testes que usam a API autenticada.</summary>
public abstract class ApiTestBase(ApiFactory factory)
{
    protected ApiFactory Factory { get; } = factory;

    protected async Task<HttpClient> As(string email, string? password = null)
    {
        var client = Factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = password ?? ApiFactory.TestPassword });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var token = (await res.Json()).Str("accessToken");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected async Task<Guid> CategoryId(string name)
    {
        var client = await As(Seeded.Admin);
        var list = await (await client.GetAsync("/api/v1/categories")).Json();
        return list.EnumerateArray().First(c => c.Str("name") == name).Guid("id");
    }

    protected async Task<Guid> PriorityId(string name)
    {
        var client = await As(Seeded.Admin);
        var list = await (await client.GetAsync("/api/v1/priorities")).Json();
        return list.EnumerateArray().First(c => c.Str("name") == name).Guid("id");
    }

    protected async Task<Guid> TeamId(string name)
    {
        var client = await As(Seeded.Admin);
        var list = await (await client.GetAsync("/api/v1/teams")).Json();
        return list.EnumerateArray().First(c => c.Str("name") == name).Guid("id");
    }

    protected async Task<Guid> UserId(string email)
    {
        var client = await As(Seeded.Admin);
        var list = await (await client.GetAsync("/api/v1/users")).Json();
        return list.EnumerateArray().First(c => c.Str("email") == email).Guid("id");
    }

    /// <summary>Abre um chamado como o cliente informado e devolve o detalhe.</summary>
    protected async Task<JsonElement> OpenTicket(HttpClient requester, string category = "Hardware",
        string priority = "Média", string? title = null)
    {
        var res = await requester.PostAsJsonAsync("/api/v1/tickets", new
        {
            title = title ?? $"Chamado {System.Guid.NewGuid():N}",
            description = "Descrição do problema para teste.",
            categoryId = await CategoryId(category),
            priorityId = await PriorityId(priority),
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.Json();
    }

    protected static string Url(JsonElement ticket, string suffix = "") => $"/api/v1/tickets/{ticket.Guid("id")}{suffix}";

    protected async Task WithDb(Func<HelpDeskPro.Infrastructure.Persistence.AppDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<HelpDeskPro.Infrastructure.Persistence.AppDbContext>());
    }

    /// <summary>Executa a varredura de SLA como se fosse o instante informado.</summary>
    protected async Task<HelpDeskPro.Application.Notifications.SlaRunResult> RunSla(DateTime now)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HelpDeskPro.Application.Notifications.ISlaMonitor>()
            .RunOnceAsync(now, CancellationToken.None);
    }

    /// <summary>Move os prazos de SLA do chamado para o passado (simula envelhecimento sem esperar).</summary>
    protected Task ExpireSla(JsonElement ticket, bool firstResponse = true, bool resolution = false) =>
        WithDb(async db =>
        {
            var id = ticket.Guid("id");
            var past = DateTime.UtcNow.AddMinutes(-5);
            var sla = await db.TicketSlas.SingleAsync(s => s.TicketId == id);
            await db.Database.ExecuteSqlAsync(
                $"""UPDATE "TicketSlas" SET "FirstResponseDueAt" = {(firstResponse ? past : sla.FirstResponseDueAt)}, "ResolutionDueAt" = {(resolution ? past : sla.ResolutionDueAt)} WHERE "TicketId" = {id}""");
        });
}
