using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Infrastructure.Auth;
using HelpDeskPro.Infrastructure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HelpDeskPro.IntegrationTests;

public enum Access { Anonymous, Authenticated, Staff, Admin }

[Collection(ApiCollection.Name)]
public sealed class PermissionMatrixTests(ApiFactory factory) : ApiTestBase(factory)
{
    /// <summary>
    /// Inventário de TODOS os endpoints e o acesso esperado. Um endpoint novo sem classificação quebra este teste:
    /// ninguém publica uma rota sem decidir quem pode usá-la. "Authenticated" = qualquer usuário logado; as regras
    /// por recurso (IDOR, papel dentro do serviço) são cobertas pelos testes de cada área.
    /// </summary>
    private static readonly Dictionary<string, Access> Matrix = new()
    {
        ["POST api/v1/auth/login"] = Access.Anonymous,
        ["POST api/v1/auth/refresh"] = Access.Anonymous,
        ["POST api/v1/auth/logout"] = Access.Anonymous,
        ["POST api/v1/auth/forgot-password"] = Access.Anonymous,
        ["POST api/v1/auth/reset-password"] = Access.Anonymous,
        ["POST api/v1/auth/logout-all"] = Access.Authenticated,
        ["GET api/v1/auth/me"] = Access.Authenticated,
        ["PATCH api/v1/auth/me"] = Access.Authenticated,
        ["POST api/v1/auth/change-password"] = Access.Authenticated,

        ["GET api/v1/users"] = Access.Admin,
        ["POST api/v1/users"] = Access.Admin,
        ["PUT api/v1/users/{id:guid}"] = Access.Admin,
        ["PATCH api/v1/users/{id:guid}/active"] = Access.Admin,
        ["GET api/v1/users/agents"] = Access.Staff,

        ["GET api/v1/teams"] = Access.Staff,
        ["POST api/v1/teams"] = Access.Admin,
        ["PUT api/v1/teams/{id:guid}"] = Access.Admin,
        ["PATCH api/v1/teams/{id:guid}/active"] = Access.Admin,
        ["GET api/v1/teams/{id:guid}/members"] = Access.Staff,
        ["POST api/v1/teams/{id:guid}/members"] = Access.Admin,
        ["DELETE api/v1/teams/{id:guid}/members/{userId:guid}"] = Access.Admin,

        ["GET api/v1/categories"] = Access.Authenticated,
        ["POST api/v1/categories"] = Access.Admin,
        ["PUT api/v1/categories/{id:guid}"] = Access.Admin,
        ["PATCH api/v1/categories/{id:guid}/active"] = Access.Admin,
        ["GET api/v1/priorities"] = Access.Authenticated,
        ["POST api/v1/priorities"] = Access.Admin,
        ["PUT api/v1/priorities/{id:guid}"] = Access.Admin,
        ["PATCH api/v1/priorities/{id:guid}/active"] = Access.Admin,

        ["GET api/v1/tickets"] = Access.Authenticated,
        ["POST api/v1/tickets"] = Access.Authenticated,
        ["GET api/v1/tickets/{id:guid}"] = Access.Authenticated,
        ["PATCH api/v1/tickets/{id:guid}"] = Access.Authenticated,
        ["PATCH api/v1/tickets/{id:guid}/status"] = Access.Authenticated,
        ["PATCH api/v1/tickets/{id:guid}/assignment"] = Access.Authenticated,
        ["GET api/v1/tickets/{id:guid}/history"] = Access.Authenticated,
        ["GET api/v1/tickets/{ticketId:guid}/messages"] = Access.Authenticated,
        ["POST api/v1/tickets/{ticketId:guid}/messages"] = Access.Authenticated,
        ["GET api/v1/tickets/{ticketId:guid}/attachments"] = Access.Authenticated,
        ["POST api/v1/tickets/{ticketId:guid}/attachments"] = Access.Authenticated,
        ["GET api/v1/tickets/{ticketId:guid}/attachments/{attachmentId:guid}"] = Access.Authenticated,

        ["GET api/v1/notifications"] = Access.Authenticated,
        ["GET api/v1/notifications/unread-count"] = Access.Authenticated,
        ["PATCH api/v1/notifications/{id:guid}/read"] = Access.Authenticated,
        ["POST api/v1/notifications/read-all"] = Access.Authenticated,

        ["GET api/v1/dashboard/summary"] = Access.Authenticated,
        ["GET api/v1/reports/sla"] = Access.Admin,
        ["GET api/v1/reports/agents"] = Access.Admin,
        ["GET api/v1/reports/tickets/export"] = Access.Admin,
        ["GET api/v1/audit"] = Access.Admin,
        ["GET api/v1/settings"] = Access.Admin,
    };

    private IEnumerable<(string Method, string Pattern)> Endpoints() =>
        Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("api/", StringComparison.Ordinal))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (m, e.RoutePattern.RawText!)));

    private static string Concrete(string pattern) =>
        "/" + Regex.Replace(pattern, @"\{[^}]+\}", _ => Guid.NewGuid().ToString());

    private static HttpRequestMessage Request(string method, string pattern)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), Concrete(pattern));
        if (method is "POST" or "PUT" or "PATCH") req.Content = JsonContent.Create(new { });
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return req;
    }

    [Fact]
    public void EveryEndpoint_IsClassifiedInTheMatrix_AndNoStaleEntriesRemain()
    {
        var actual = Endpoints().Select(e => $"{e.Method} {e.Pattern}").ToHashSet();
        var expected = Matrix.Keys.ToHashSet();

        Assert.True(actual.SetEquals(expected),
            $"Sem classificação: [{string.Join(" | ", actual.Except(expected))}]  Obsoletas: [{string.Join(" | ", expected.Except(actual))}]");
        Assert.True(actual.Count >= 50);
    }

    [Fact]
    public async Task Anonymous_IsRejectedOnEveryProtectedEndpoint()
    {
        var anonymous = Factory.CreateClient();
        var failures = new List<string>();

        foreach (var (method, pattern) in Endpoints())
        {
            if (Matrix[$"{method} {pattern}"] == Access.Anonymous) continue;
            var status = (await anonymous.SendAsync(Request(method, pattern))).StatusCode;
            if (status != HttpStatusCode.Unauthorized) failures.Add($"{method} {pattern} -> {(int)status}");
        }
        Assert.Empty(failures);
    }

    [Fact]
    public async Task AdminOnlyEndpoints_RejectRequestersAndAgents_StaffEndpointsRejectRequesters()
    {
        var requester = await As(Seeded.Ana);
        var agent = await As(Seeded.Camila);
        var failures = new List<string>();

        foreach (var (method, pattern) in Endpoints())
        {
            var access = Matrix[$"{method} {pattern}"];
            if (access is Access.Admin)
            {
                foreach (var (name, client) in new[] { ("solicitante", requester), ("agente", agent) })
                {
                    var status = (await client.SendAsync(Request(method, pattern))).StatusCode;
                    if (status != HttpStatusCode.Forbidden) failures.Add($"{name}: {method} {pattern} -> {(int)status}");
                }
            }
            else if (access is Access.Staff)
            {
                var status = (await requester.SendAsync(Request(method, pattern))).StatusCode;
                if (status != HttpStatusCode.Forbidden) failures.Add($"solicitante: {method} {pattern} -> {(int)status}");
            }
        }
        Assert.Empty(failures);
    }

    [Fact]
    public async Task AdminCanReachAdminEndpoints_NoneAreAccidentallyLockedForEveryone()
    {
        var admin = await As(Seeded.Admin);
        foreach (var url in new[] { "/api/v1/users", "/api/v1/teams", "/api/v1/reports/sla", "/api/v1/reports/agents", "/api/v1/audit", "/api/v1/settings", "/api/v1/dashboard/summary" })
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
    }
}

[Collection(ApiCollection.Name)]
public sealed class TokenSecurityTests(ApiFactory factory) : ApiTestBase(factory)
{
    private JwtOptions Jwt => Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

    private string Forge(Guid sub, string role, string? key = null, string? issuer = null, string? audience = null, DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Jwt.Issuer,
            Audience = audience ?? Jwt.Audience,
            IssuedAt = expires is null ? now : expires.Value.AddMinutes(-15),
            NotBefore = expires is null ? now : expires.Value.AddMinutes(-15),
            Expires = expires ?? now.AddMinutes(10),
            Claims = new Dictionary<string, object> { ["sub"] = sub.ToString(), ["role"] = role, ["jti"] = Guid.NewGuid().ToString() },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? Jwt.Key)), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private async Task<HttpStatusCode> StatusWith(string token, string url = "/api/v1/auth/me")
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await Factory.CreateClient().SendAsync(req)).StatusCode;
    }

    private static string B64(object value) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task ControlToken_SignedWithTheRealKey_IsAccepted()
    {
        var ana = await UserId(Seeded.Ana);
        Assert.Equal(HttpStatusCode.OK, await StatusWith(Forge(ana, Roles.Requester)));
    }

    [Fact]
    public async Task ForgedTokens_AreRejected()
    {
        var ana = await UserId(Seeded.Ana);
        var wrongKey = Convert.ToBase64String(new byte[48].Select((_, i) => (byte)(i + 7)).ToArray());

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWith(Forge(ana, Roles.Admin, key: wrongKey)));                      // chave errada, papel elevado
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWith(Forge(ana, Roles.Requester, issuer: "outro-emissor")));         // emissor errado
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWith(Forge(ana, Roles.Requester, audience: "outra-audiencia")));    // audiência errada
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWith(Forge(ana, Roles.Requester, expires: DateTime.UtcNow.AddHours(-2)))); // expirado
    }

    [Fact]
    public async Task AlgNone_AndTamperedPayload_AreRejected()
    {
        var ana = await UserId(Seeded.Ana);
        var header = B64(new { alg = "none", typ = "JWT" });
        var payload = B64(new { sub = ana, role = Roles.Admin, iss = Jwt.Issuer, aud = Jwt.Audience, exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() });
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWith($"{header}.{payload}."));

        // Adultera o papel dentro de um token legítimo, mantendo a assinatura antiga.
        var parts = Forge(ana, Roles.Requester).Split('.');
        var tampered = B64(new { sub = ana, role = Roles.Admin, iss = Jwt.Issuer, aud = Jwt.Audience, exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() });
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWith($"{parts[0]}.{tampered}.{parts[2]}"));
    }

    [Fact]
    public async Task TokenWithAnotherRoleThanTheAccount_IsRejected_EvenWhenProperlySigned()
    {
        // Simula um token antigo de quem foi rebaixado: assinatura válida, papel diferente do cadastro atual.
        var ana = await UserId(Seeded.Ana);
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWith(Forge(ana, Roles.Admin), "/api/v1/users"));
    }

    [Fact]
    public async Task TokenForUnknownUser_IsRejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWith(Forge(Guid.NewGuid(), Roles.Admin)));

    [Fact]
    public async Task DeactivatedUser_LosesAccessImmediately_NotOnlyAtTokenExpiry()
    {
        var user = await Factory.CreateUserAsync(Roles.Agent);
        var token = await LoginToken(user.Email!);
        Assert.Equal(HttpStatusCode.OK, await StatusWith(token));

        await Factory.SetActiveAsync(user.Id, false);
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWith(token));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWith(token, "/api/v1/tickets"));

        await Factory.SetActiveAsync(user.Id, true);
        Assert.Equal(HttpStatusCode.OK, await StatusWith(token));
    }

    [Fact]
    public async Task RoleChange_InvalidatesTheOldToken_UntilANewLogin()
    {
        var user = await Factory.CreateUserAsync(Roles.Admin);
        var oldToken = await LoginToken(user.Email!);
        Assert.Equal(HttpStatusCode.OK, await StatusWith(oldToken, "/api/v1/users"));

        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var entity = (await users.FindByIdAsync(user.Id.ToString()))!;
            await users.RemoveFromRoleAsync(entity, Roles.Admin);
            await users.AddToRoleAsync(entity, Roles.Requester);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusWith(oldToken, "/api/v1/users"));
        var fresh = await LoginToken(user.Email!);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusWith(fresh, "/api/v1/users")); // novo token reflete o novo papel
    }

    [Fact]
    public async Task RealtimeHub_RejectsTokensOfDeactivatedUsers()
    {
        var user = await Factory.CreateUserAsync(Roles.Requester);
        var token = await LoginToken(user.Email!);
        await Factory.SetActiveAsync(user.Id, false);

        var res = await Factory.CreateClient().PostAsync($"/hubs/tickets/negotiate?negotiateVersion=1&access_token={token}", null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    private async Task<string> LoginToken(string email)
    {
        var res = await Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = ApiFactory.TestPassword });
        return (await res.Json()).Str("accessToken");
    }
}

[Collection(ApiCollection.Name)]
public sealed class InputHardeningTests(ApiFactory factory) : CommunicationTestBase(factory)
{
    [Fact]
    public async Task OverPosting_ExtraFieldsInPatchAndPost_AreIgnored()
    {
        var ana = await As(Seeded.Ana);
        var carlos = await UserId(Seeded.Carlos);
        var camila = await UserId(Seeded.Camila);
        var ticket = await OpenTicket(ana, "Hardware");

        var patched = await ana.PatchAsync(Url(ticket), JsonContent.Create(new
        {
            title = "Título novo legítimo",
            requesterId = carlos,
            status = "RESOLVED",
            assignedToId = camila,
            ticketNumber = "HDP-0000-000000",
            closedAt = DateTime.UtcNow,
        }));
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        var detail = await patched.Json();
        Assert.Equal("Título novo legítimo", detail.Str("title"));
        Assert.Equal(ticket.Guid("requesterId"), detail.Guid("requesterId"));
        Assert.Equal("NEW", detail.Str("status"));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("assignedToId").ValueKind);
        Assert.Equal(ticket.Str("ticketNumber"), detail.Str("ticketNumber"));

        var created = await ana.PostAsJsonAsync("/api/v1/tickets", new
        {
            title = "Criado com campos extras",
            description = "descrição válida",
            categoryId = await CategoryId("Hardware"),
            priorityId = await PriorityId("Baixa"),
            status = "CLOSED",
            assignedToId = camila,
            firstRespondedAt = DateTime.UtcNow,
            id = Guid.NewGuid(),
        });
        var body = await created.Json();
        Assert.Equal("NEW", body.Str("status"));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("assignedToId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("firstRespondedAt").ValueKind);
    }

    [Theory]
    [InlineData("' OR 1=1 --")]
    [InlineData("'; DROP TABLE \"Tickets\"; --")]
    [InlineData("\" OR \"\"=\"")]
    [InlineData("' UNION SELECT NULL--")]
    public async Task SqlInjectionPayloads_AreTreatedAsPlainText(string payload)
    {
        var admin = await As(Seeded.Admin);
        var res = await admin.GetAsync($"/api/v1/tickets?search={Uri.EscapeDataString(payload)}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(0, (await res.Json()).GetProperty("totalCount").GetInt32());

        var audit = await admin.GetAsync($"/api/v1/audit?ticketNumber={Uri.EscapeDataString(payload)}");
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);

        // A tabela continua íntegra.
        Assert.True((await (await admin.GetAsync("/api/v1/tickets?pageSize=1")).Json()).GetProperty("totalCount").GetInt32() > 0);
    }

    [Fact]
    public async Task MalformedAndHostileBodies_ReturnClientErrors_NeverServerErrors()
    {
        var ana = await As(Seeded.Ana);

        async Task<HttpStatusCode> Post(HttpContent content) => (await ana.PostAsync("/api/v1/tickets", content)).StatusCode;
        HttpContent Json(string raw) => new StringContent(raw, Encoding.UTF8, "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, await Post(Json("{ não é json")));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(Json("""{"title": 123, "description": ["x"], "categoryId": "não-é-guid", "priorityId": null}""")));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(Json("""{"title": "%s", "description": "ok", "categoryId": "00000000-0000-0000-0000-000000000000", "priorityId": "00000000-0000-0000-0000-000000000000"}""".Replace("%s", new string('a', 5000)))));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, await Post(new StringContent("title=x", Encoding.UTF8, "application/x-www-form-urlencoded")));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(Json("null")));
    }

    [Fact]
    public async Task InvalidRouteIds_Return404_AndErrorsDoNotLeakInternals()
    {
        var ana = await As(Seeded.Ana);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync("/api/v1/tickets/não-é-guid")).StatusCode);

        var missing = await ana.GetAsync($"/api/v1/tickets/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var body = await missing.Content.ReadAsStringAsync();
        foreach (var leak in new[] { "Exception", "StackTrace", "   at ", "Npgsql", "EntityFramework", "HelpDeskPro." })
            Assert.DoesNotContain(leak, body);
    }

    [Fact]
    public async Task StoredScriptPayloads_AreStoredAsPlainText_NotExecutedOrAltered()
    {
        var ana = await As(Seeded.Ana);
        const string xss = "<img src=x onerror=alert(1)><script>alert(2)</script>";
        var ticket = await OpenTicket(ana, title: xss);
        Assert.Equal(xss, ticket.Str("title")); // o servidor guarda o texto; a interface escapa na renderização (coberto no E2E)

        var raw = await (await ana.GetAsync(Url(ticket))).Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>", raw); // o JSON escapa '<' como < (defesa em profundidade)
        Assert.Contains("Média", await (await ana.GetAsync(Url(ticket))).Content.ReadAsStringAsync()); // acentos seguem legíveis
    }

    [Fact]
    public async Task Responses_NeverExposePasswordHashesOrSecurityStamps()
    {
        var admin = await As(Seeded.Admin);
        foreach (var url in new[] { "/api/v1/users", "/api/v1/auth/me", "/api/v1/users/agents" })
        {
            var raw = await (await admin.GetAsync(url)).Content.ReadAsStringAsync();
            foreach (var leak in new[] { "passwordHash", "securityStamp", "concurrencyStamp", "normalizedEmail", "tokenHash" })
                Assert.DoesNotContain(leak, raw, StringComparison.OrdinalIgnoreCase);
        }
    }
}

[Collection(ApiCollection.Name)]
public sealed class TransportSecurityTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task ApiResponses_CarryDefensiveHeaders()
    {
        var res = await (await As(Seeded.Ana)).GetAsync("/api/v1/tickets");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", res.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("camera=()", res.Headers.GetValues("Permissions-Policy").Single());
    }

    [Fact]
    public async Task AuthResponses_AreNeverCached()
    {
        var res = await Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = "ninguem@teste.example", password = "Errada!12345" });
        Assert.Equal("no-store", res.Headers.GetValues("Cache-Control").Single());
    }

    [Fact]
    public async Task ErrorResponses_AlsoCarryDefensiveHeaders()
    {
        var res = await Factory.CreateClient().GetAsync("/api/v1/tickets");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Swagger_IsAvailableInDevelopment_WithBearerSecurityScheme()
    {
        var client = Factory.CreateClient();
        var ui = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.DoesNotContain("default-src 'none'", string.Join(';', ui.Headers.TryGetValues("Content-Security-Policy", out var csp) ? csp : []));

        var doc = await (await client.GetAsync("/openapi/v1.json")).Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(doc);
        Assert.Equal("HelpDesk Pro API", json.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("bearer", json.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
        Assert.True(json.RootElement.GetProperty("paths").EnumerateObject().Count() >= 40);
    }

    [Fact]
    public async Task RefreshCookie_IsScopedToAuthPath_AndNotReadableByScripts()
    {
        var user = await Factory.CreateUserAsync(Roles.Requester);
        var res = await Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = ApiFactory.TestPassword });
        var cookie = res.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("hdp_refresh=")).ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("path=/api/v1/auth", cookie);
        Assert.Contains("samesite=lax", cookie);
        Assert.DoesNotContain("domain=", cookie);
    }
}
