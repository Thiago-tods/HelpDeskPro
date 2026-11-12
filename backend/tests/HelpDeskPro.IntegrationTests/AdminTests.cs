using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HelpDeskPro.Domain.Enums;

namespace HelpDeskPro.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AdminUserTests(ApiFactory factory) : ApiTestBase(factory)
{
    private const string StrongPassword = "Senha!Forte#2026";

    private static string NewEmail(string prefix = "novo") => $"{prefix}.{Guid.NewGuid():N}@teste.example";

    private async Task<JsonElement> Create(HttpClient admin, string role, string? email = null, string password = StrongPassword)
    {
        var res = await admin.PostAsJsonAsync("/api/v1/users", new { fullName = "Pessoa Teste", email = email ?? NewEmail(), role, password });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.Json();
    }

    private static string RefreshCookieOf(HttpResponseMessage r) =>
        r.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("hdp_refresh=")).Split(';')[0]["hdp_refresh=".Length..];

    private async Task<(HttpResponseMessage Response, string Refresh)> Login(string email, string password)
    {
        var res = await Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        return (res, res.IsSuccessStatusCode ? RefreshCookieOf(res) : string.Empty);
    }

    private async Task<HttpStatusCode> RefreshStatus(string refresh)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        req.Headers.Add("Cookie", $"hdp_refresh={refresh}");
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return (await Factory.CreateClient().SendAsync(req)).StatusCode;
    }

    // ---------- Gestão de usuários ----------

    [Fact]
    public async Task Admin_CreatesUser_WhoCanThenSignIn()
    {
        var admin = await As(Seeded.Admin);
        var email = NewEmail("agente");
        var created = await Create(admin, Roles.Agent, email);

        Assert.Equal("AGENT", created.Str("role"));
        Assert.True(created.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await Login(email, StrongPassword)).Response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_Validation()
    {
        var admin = await As(Seeded.Admin);
        var email = NewEmail();
        await Create(admin, Roles.Requester, email);

        Task<HttpStatusCode> Post(object body) => admin.PostAsJsonAsync("/api/v1/users", body).ContinueWith(t => t.Result.StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(new { fullName = "X", email, role = "REQUESTER", password = StrongPassword })); // duplicado
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(new { fullName = "X", email = NewEmail(), role = "REQUESTER", password = "somenteletras" })); // política do Identity
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { fullName = "X", email = NewEmail(), role = "REQUESTER", password = "curta" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { fullName = "X", email = NewEmail(), role = "SUPERUSER", password = StrongPassword }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { fullName = "", email = "nao-e-email", role = "AGENT", password = StrongPassword }));
    }

    [Theory]
    [InlineData(Seeded.Ana)]
    [InlineData(Seeded.Camila)]
    public async Task UserManagement_IsAdminOnly(string email)
    {
        var client = await As(email);
        var id = await UserId(Seeded.Carlos);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/users", new { fullName = "X", email = NewEmail(), role = "AGENT", password = StrongPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/v1/users/{id}", new { fullName = "X", role = "ADMIN" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchJsonAsync($"/api/v1/users/{id}/active", new { isActive = false })).StatusCode);
    }

    [Fact]
    public async Task Update_ChangesNameAndRole_RevokesSessions_AndRemovesTeamMemberships()
    {
        var admin = await As(Seeded.Admin);
        var email = NewEmail("promo");
        var agent = await Create(admin, Roles.Agent, email);
        var (_, refresh) = await Login(email, StrongPassword);

        var team = await (await admin.PostAsJsonAsync("/api/v1/teams", new { name = $"Equipe {Guid.NewGuid():N}" })).Json();
        await admin.PostAsJsonAsync($"/api/v1/teams/{team.Guid("id")}/members", new { userId = agent.Guid("id") });

        var updated = await admin.PutAsJsonAsync($"/api/v1/users/{agent.Guid("id")}", new { fullName = "Novo Nome", role = "REQUESTER" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.Json();
        Assert.Equal("Novo Nome", body.Str("fullName"));
        Assert.Equal("REQUESTER", body.Str("role"));

        Assert.Equal(HttpStatusCode.Unauthorized, await RefreshStatus(refresh)); // papel mudou: sessões renovadas
        var members = await (await admin.GetAsync($"/api/v1/teams/{team.Guid("id")}/members")).Json();
        Assert.Equal(0, members.GetArrayLength());
    }

    [Fact]
    public async Task Update_CannotChangeOwnRole_OrTouchUnknownUser()
    {
        var admin = await As(Seeded.Admin);
        var self = await UserId(Seeded.Admin);

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.PutAsJsonAsync($"/api/v1/users/{self}", new { fullName = "Admin", role = "AGENT" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"/api/v1/users/{self}", new { fullName = "Marina Albuquerque", role = "ADMIN" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PutAsJsonAsync($"/api/v1/users/{Guid.NewGuid()}", new { fullName = "X", role = "AGENT" })).StatusCode);
    }

    [Fact]
    public async Task Deactivate_BlocksLoginAndRefresh_ReactivateRestores_SelfDeactivationRefused()
    {
        var admin = await As(Seeded.Admin);
        var email = NewEmail("desativar");
        var user = await Create(admin, Roles.Requester, email);
        var (_, refresh) = await Login(email, StrongPassword);

        var off = await admin.PatchJsonAsync($"/api/v1/users/{user.Guid("id")}/active", new { isActive = false });
        Assert.False((await off.Json()).GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(email, StrongPassword)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await RefreshStatus(refresh));

        await admin.PatchJsonAsync($"/api/v1/users/{user.Guid("id")}/active", new { isActive = true });
        Assert.Equal(HttpStatusCode.OK, (await Login(email, StrongPassword)).Response.StatusCode);

        var self = await UserId(Seeded.Admin);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await admin.PatchJsonAsync($"/api/v1/users/{self}/active", new { isActive = false })).StatusCode);
    }

    [Fact]
    public async Task UserList_IncludesRoleAndActiveState()
    {
        var list = await (await (await As(Seeded.Admin)).GetAsync("/api/v1/users")).Json();
        var camila = list.EnumerateArray().Single(u => u.Str("email") == Seeded.Camila);
        Assert.Equal("AGENT", camila.Str("role"));
        Assert.True(camila.GetProperty("isActive").GetBoolean());
    }

    // ---------- Perfil ----------

    [Fact]
    public async Task Profile_UpdatesOwnName_AndRejectsBlank()
    {
        var user = await Factory.CreateUserAsync(Roles.Requester);
        var client = await As(user.Email!, ApiFactory.TestPassword);

        var ok = await client.PatchJsonAsync("/api/v1/auth/me", new { fullName = "Nome Atualizado" });
        Assert.Equal("Nome Atualizado", (await ok.Json()).Str("fullName"));
        Assert.Equal("Nome Atualizado", (await (await client.GetAsync("/api/v1/auth/me")).Json()).Str("fullName"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchJsonAsync("/api/v1/auth/me", new { fullName = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Factory.CreateClient().PatchJsonAsync("/api/v1/auth/me", new { fullName = "x" })).StatusCode);
    }

    private static HttpRequestMessage ChangePassword(HttpClient c, object body, bool csrf = true)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-password") { Content = JsonContent.Create(body) };
        req.Headers.Authorization = c.DefaultRequestHeaders.Authorization;
        if (csrf) req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return req;
    }

    [Fact]
    public async Task ChangePassword_RotatesCredentials_AndRevokesPreviousSessions()
    {
        var user = await Factory.CreateUserAsync(Roles.Requester);
        var (_, oldRefresh) = await Login(user.Email!, ApiFactory.TestPassword);
        var client = await As(user.Email!, ApiFactory.TestPassword);
        const string newPassword = "OutraSenha!2026x";

        var res = await Factory.CreateClient().SendAsync(ChangePassword(client, new { currentPassword = ApiFactory.TestPassword, newPassword }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.False(string.IsNullOrEmpty((await res.Json()).Str("accessToken")));
        var newRefresh = RefreshCookieOf(res);

        Assert.Equal(HttpStatusCode.Unauthorized, await RefreshStatus(oldRefresh));
        Assert.Equal(HttpStatusCode.OK, await RefreshStatus(newRefresh));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(user.Email!, ApiFactory.TestPassword)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(user.Email!, newPassword)).Response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_Validation()
    {
        var user = await Factory.CreateUserAsync(Roles.Requester);
        var client = await As(user.Email!, ApiFactory.TestPassword);
        var http = Factory.CreateClient();

        var wrong = await http.SendAsync(ChangePassword(client, new { currentPassword = "Errada!12345", newPassword = "OutraSenha!2026x" }));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("Senha atual incorreta", await wrong.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await http.SendAsync(ChangePassword(client, new { currentPassword = ApiFactory.TestPassword, newPassword = ApiFactory.TestPassword }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await http.SendAsync(ChangePassword(client, new { currentPassword = ApiFactory.TestPassword, newPassword = "semnumeroaqui" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await http.SendAsync(ChangePassword(client, new { currentPassword = ApiFactory.TestPassword, newPassword = "OutraSenha!2026x" }, csrf: false))).StatusCode);
    }

    // ---------- Configurações ----------

    [Fact]
    public async Task Settings_AdminReadsRuntimeConfig_WithoutSecrets()
    {
        var res = await (await As(Seeded.Admin)).GetAsync("/api/v1/settings");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var raw = await res.Content.ReadAsStringAsync();
        var s = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(80, s.GetProperty("sla").GetProperty("warningPercent").GetInt32());
        Assert.Equal(15, s.GetProperty("auth").GetProperty("accessTokenMinutes").GetInt32());
        Assert.Contains(".png", s.GetProperty("attachments").GetProperty("allowedExtensions").EnumerateArray().Select(e => e.GetString()));
        foreach (var leak in new[] { "Host=", "Username=", "Password=", "ConnectionString", "\"key\"", "secret", ApiFactory.TestPassword })
            Assert.DoesNotContain(leak, raw, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(Seeded.Ana)]
    [InlineData(Seeded.Camila)]
    public async Task Settings_AdminOnly(string email) =>
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As(email)).GetAsync("/api/v1/settings")).StatusCode);
}
