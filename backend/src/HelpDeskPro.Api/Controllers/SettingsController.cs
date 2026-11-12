using HelpDeskPro.Api.Infrastructure;
using HelpDeskPro.Application.Attachments;
using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Infrastructure.Auth;
using HelpDeskPro.Infrastructure.Sla;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HelpDeskPro.Api.Controllers;

public sealed record SystemSettingsDto(
    string Environment,
    AttachmentSettings Attachments,
    SlaSettings Sla,
    AuthSettings Auth,
    IReadOnlyList<string> CorsAllowedOrigins,
    int AuthRateLimitPerMinute);

public sealed record AttachmentSettings(long MaxBytes, int MaxPerTicket, IReadOnlyList<string> AllowedExtensions);
public sealed record SlaSettings(int CheckIntervalSeconds, int WarningPercent, string Calendar);
public sealed record AuthSettings(int AccessTokenMinutes, int RefreshTokenDays, int PasswordMinLength, int LockoutMaxFailures);

/// <summary>
/// Configurações de execução em modo somente leitura. Valores sensíveis (chaves, senhas, connection string)
/// nunca são expostos; a alteração é feita por User Secrets/variáveis de ambiente.
/// </summary>
[ApiController]
[Route("api/v1/settings")]
[Authorize(Policy = Policies.Admin)]
public sealed class SettingsController(
    IOptions<AttachmentOptions> attachments, IOptions<SlaOptions> sla, IOptions<JwtOptions> jwt,
    IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    [HttpGet]
    public SystemSettingsDto Get() => new(
        env.EnvironmentName,
        new AttachmentSettings(attachments.Value.MaxBytes, attachments.Value.MaxPerTicket, FileInspector.AllowedExtensions),
        new SlaSettings(sla.Value.CheckIntervalSeconds, (int)(TicketSla.WarningFraction * 100), "Horas corridas, sem feriados"),
        new AuthSettings(jwt.Value.AccessTokenMinutes, jwt.Value.RefreshTokenDays, 10, 5),
        config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"],
        config.GetValue("RateLimiting:AuthPermitPerMinute", 10));
}
