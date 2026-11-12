using System.Text;
using System.Threading.RateLimiting;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Infrastructure.Auth;
using HelpDeskPro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HelpDeskPro.Api.Infrastructure;

public static class Policies
{
    public const string Admin = "Admin";
    public const string Staff = "Staff"; // ADMIN ou AGENT
    public const string AuthRateLimit = "auth";
    public const string CorsPolicy = "Frontend";
}

public static class SecurityExtensions
{
    public static IServiceCollection AddApiSecurity(this IServiceCollection services, IConfiguration config)
    {
        // Configuração lida de forma preguiçosa (via DI) para respeitar overrides do host.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((o, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                o.MapInboundClaims = false;
                // WebSockets não aceitam cabeçalho Authorization no navegador: o hub recebe o token na query.
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    },
                    // O token assinado não basta: conta desativada ou papel alterado invalidam o acesso imediatamente,
                    // sem esperar o token expirar (o cliente renova a sessão e recebe os dados atuais).
                    OnTokenValidated = async ctx =>
                    {
                        var principal = ctx.Principal;
                        if (principal?.FindFirst("sub")?.Value is not { } sub || !Guid.TryParse(sub, out var userId))
                        {
                            ctx.Fail("Token sem identificação do usuário.");
                            return;
                        }

                        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                        var current = await (from u in db.Users
                                             join ur in db.UserRoles on u.Id equals ur.UserId
                                             join r in db.Roles on ur.RoleId equals r.Id
                                             where u.Id == userId
                                             select new { u.IsActive, Role = r.Name }).FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);

                        if (current is null || !current.IsActive || current.Role != principal.FindFirst("role")?.Value)
                            ctx.Fail("Conta inativa ou papel alterado.");
                    },
                };
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                    RoleClaimType = "role",
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin))
            .AddPolicy(Policies.Staff, p => p.RequireRole(Roles.Admin, Roles.Agent));

        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IConfiguration>((o, cfg) =>
        {
            var origins = cfg.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"];
            o.AddPolicy(Policies.CorsPolicy, p => p
                .WithOrigins(origins)
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                .WithHeaders("Content-Type", "Authorization", "X-Requested-With", "X-SignalR-User-Agent")
                .AllowCredentials());
        });

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((o, cfg) =>
        {
            var authPermits = cfg.GetValue("RateLimiting:AuthPermitPerMinute", 10);
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Policies.AuthRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = authPermits,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await ctx.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = 429,
                    Title = "Muitas tentativas",
                    Detail = "Aguarde um minuto e tente novamente.",
                }, ct);
            };
        });

        return services;
    }
}
