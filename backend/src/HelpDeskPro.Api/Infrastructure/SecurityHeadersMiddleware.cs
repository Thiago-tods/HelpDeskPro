namespace HelpDeskPro.Api.Infrastructure;

/// <summary>
/// Cabeçalhos defensivos em todas as respostas. A API só devolve JSON/arquivos baixados, então a CSP é restritiva
/// (exceto no Swagger UI, que precisa de scripts e estilos próprios e só existe em Development).
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Resource-Policy"] = "same-site";

            var path = context.Request.Path;
            if (!path.StartsWithSegments("/swagger"))
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

            // Respostas de autenticação e dados pessoais nunca devem ficar em cache de navegador/proxy.
            if (path.StartsWithSegments("/api/v1/auth") && !headers.ContainsKey("Cache-Control"))
            {
                headers["Cache-Control"] = "no-store";
                headers["Pragma"] = "no-cache";
            }
            return Task.CompletedTask;
        });
        return next(context);
    }
}
