using HelpDeskPro.Application.Common;

namespace HelpDeskPro.Api.Infrastructure;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal Principal =>
        accessor.HttpContext?.User ?? throw new InvalidOperationException("Sem contexto HTTP.");

    public Guid Id => Guid.Parse(Principal.FindFirst("sub")?.Value ?? throw new ForbiddenException());

    public string Role => Principal.FindFirst("role")?.Value ?? string.Empty;
}
