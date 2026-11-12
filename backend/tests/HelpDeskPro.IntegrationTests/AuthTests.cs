using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Infrastructure.Auth;
using HelpDeskPro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDeskPro.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory factory)
{
    private sealed record Login(string AccessToken, DateTime ExpiresAt, JsonElement User);

    private HttpClient NewClient() => factory.CreateClient(new() { HandleCookies = false });

    private static string? RefreshCookie(HttpResponseMessage r) =>
        r.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith("hdp_refresh="))
            : null;

    private static string CookieValue(string setCookie) =>
        setCookie.Split(';')[0]["hdp_refresh=".Length..];

    private static HttpRequestMessage Post(string url, string? cookie = null, bool csrf = true, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (cookie is not null) req.Headers.Add("Cookie", $"hdp_refresh={cookie}");
        if (csrf) req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<(Login Login, string RefreshToken, HttpResponseMessage Response)> LoginAsync(string email, string? password = null)
    {
        var res = await NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = password ?? ApiFactory.TestPassword });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var cookie = RefreshCookie(res);
        Assert.NotNull(cookie);
        var login = (await res.Content.ReadFromJsonAsync<Login>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        return (login, CookieValue(cookie), res);
    }

    private async Task<HttpStatusCode> GetStatusAsync(string url, string? token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await NewClient().SendAsync(req)).StatusCode;
    }

    [Fact]
    public async Task Login_Succeeds_RefreshTokenOnlyInHttpOnlyCookie()
    {
        var user = await factory.CreateUserAsync(Roles.Requester);
        var (login, refresh, res) = await LoginAsync(user.Email!);

        Assert.False(string.IsNullOrEmpty(login.AccessToken));
        var cookie = RefreshCookie(res)!.ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=lax", cookie);
        Assert.Contains("path=/api/v1/auth", cookie);
        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain(refresh, body);
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_ReturnSameGenericError()
    {
        var user = await factory.CreateUserAsync(Roles.Requester);
        var wrong = await NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "Errada!12345" });
        var unknown = await NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = "ninguem@teste.example", password = "Errada!12345" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_InvalidPayload_Returns400ProblemDetails()
    {
        var res = await NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = "nao-e-email", password = "" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Login_InactiveUser_IsRejected()
    {
        var user = await factory.CreateUserAsync(Roles.Agent, active: false);
        var res = await NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = ApiFactory.TestPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Login_LocksOutAfterFiveFailures_EvenWithCorrectPassword()
    {
        var user = await factory.CreateUserAsync(Roles.Requester);
        for (var i = 0; i < 5; i++)
            await NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "Errada!12345" });

        var res = await NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = ApiFactory.TestPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/api/v1/auth/me", null));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/api/v1/users", null));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithGarbageToken_Returns401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync("/api/v1/auth/me", "abc.def.ghi"));

    [Theory]
    [InlineData(Roles.Requester, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Agent, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    public async Task UsersEndpoint_EnforcesRolePolicy(string role, HttpStatusCode expected)
    {
        var user = await factory.CreateUserAsync(role);
        var (login, _, _) = await LoginAsync(user.Email!);

        Assert.Equal(expected, await GetStatusAsync("/api/v1/users", login.AccessToken));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/api/v1/auth/me", login.AccessToken));
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndReuseRevokesWholeFamily()
    {
        var user = await factory.CreateUserAsync(Roles.Requester);
        var (_, first, _) = await LoginAsync(user.Email!);

        var r1 = await NewClient().SendAsync(Post("/api/v1/auth/refresh", first));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var second = CookieValue(RefreshCookie(r1)!);
        Assert.NotEqual(first, second);

        // Reapresentar o token antigo = reutilização: deve falhar e derrubar o token novo também.
        var reuse = await NewClient().SendAsync(Post("/api/v1/auth/refresh", first));
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var afterReuse = await NewClient().SendAsync(Post("/api/v1/auth/refresh", second));
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutCsrfHeader_IsRejected()
    {
        var user = await factory.CreateUserAsync(Roles.Requester);
        var (_, refresh, _) = await LoginAsync(user.Email!);

        var res = await NewClient().SendAsync(Post("/api/v1/auth/refresh", refresh, csrf: false));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutCookie_Returns401()
    {
        var res = await NewClient().SendAsync(Post("/api/v1/auth/refresh"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var user = await factory.CreateUserAsync(Roles.Requester);
        var (_, refresh, _) = await LoginAsync(user.Email!);

        var logout = await NewClient().SendAsync(Post("/api/v1/auth/logout", refresh));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var res = await NewClient().SendAsync(Post("/api/v1/auth/refresh", refresh));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task LogoutAll_RevokesEverySession()
    {
        var user = await factory.CreateUserAsync(Roles.Requester);
        var (loginA, refreshA, _) = await LoginAsync(user.Email!);
        var (_, refreshB, _) = await LoginAsync(user.Email!);

        var req = Post("/api/v1/auth/logout-all");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginA.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await NewClient().SendAsync(req)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await NewClient().SendAsync(Post("/api/v1/auth/refresh", refreshA))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await NewClient().SendAsync(Post("/api/v1/auth/refresh", refreshB))).StatusCode);
    }

    [Fact]
    public async Task Refresh_ForDeactivatedUser_Fails()
    {
        var user = await factory.CreateUserAsync(Roles.Agent);
        var (_, refresh, _) = await LoginAsync(user.Email!);

        await factory.SetActiveAsync(user.Id, false);

        var res = await NewClient().SendAsync(Post("/api/v1/auth/refresh", refresh));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_IsStoredOnlyAsHash()
    {
        var user = await factory.CreateUserAsync(Roles.Requester);
        var (_, refresh, _) = await LoginAsync(user.Email!);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.TokenHash == refresh));
        Assert.True(await db.RefreshTokens.AnyAsync(t => t.TokenHash == AuthService.Hash(refresh)));
    }

    [Fact]
    public async Task PasswordReset_DemoFlow_ChangesPasswordAndRevokesSessions()
    {
        var user = await factory.CreateUserAsync(Roles.Requester);
        var (_, refresh, _) = await LoginAsync(user.Email!);

        var forgot = await NewClient().PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = user.Email });
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        var token = (await forgot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resetToken").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        const string newPassword = "NovaSenha!2026x";
        var reset = await NewClient().PostAsJsonAsync("/api/v1/auth/reset-password", new { email = user.Email, token, newPassword });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await NewClient().SendAsync(Post("/api/v1/auth/refresh", refresh))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = ApiFactory.TestPassword })).StatusCode);
        await LoginAsync(user.Email!, newPassword);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_DoesNotRevealExistence()
    {
        var res = await NewClient().PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "fantasma@teste.example" });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("resetToken").ValueKind);
    }

    [Fact]
    public async Task ResetPassword_WithInvalidToken_Fails()
    {
        var user = await factory.CreateUserAsync(Roles.Requester);
        var res = await NewClient().PostAsJsonAsync("/api/v1/auth/reset-password",
            new { email = user.Email, token = "token-invalido", newPassword = "NovaSenha!2026x" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Cors_AllowsOnlyConfiguredFrontendOrigin()
    {
        async Task<HttpResponseMessage> Preflight(string origin)
        {
            var req = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
            req.Headers.Add("Origin", origin);
            req.Headers.Add("Access-Control-Request-Method", "POST");
            return await NewClient().SendAsync(req);
        }

        var ok = await Preflight("http://localhost:5173");
        Assert.Equal("http://localhost:5173", ok.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", ok.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        var evil = await Preflight("http://evil.example");
        Assert.False(evil.Headers.Contains("Access-Control-Allow-Origin"));
    }
}

public sealed class RateLimitTests
{
    [Fact]
    public async Task AuthEndpoints_AreRateLimitedPerIp()
    {
        // Sem seed: o banco de teste já existe (criado pelos outros testes) e rodar em paralelo causaria corrida.
        await using var limited = new ApiFactory { AuthPermitPerMinute = 3 };
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "x@teste.example", password = "Errada!12345" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
