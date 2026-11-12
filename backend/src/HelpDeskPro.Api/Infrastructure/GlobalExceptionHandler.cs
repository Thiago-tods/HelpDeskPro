using HelpDeskPro.Application.Common;
using HelpDeskPro.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HelpDeskPro.Api.Infrastructure;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var problem = exception switch
        {
            NotFoundException e => new ProblemDetails { Status = 404, Title = "Não encontrado", Detail = e.Message },
            ForbiddenException e => new ProblemDetails { Status = 403, Title = "Acesso negado", Detail = e.Message },
            DomainException e => new ProblemDetails { Status = 422, Title = "Regra de negócio violada", Detail = e.Message },
            _ => null,
        };

        if (problem is null)
        {
            logger.LogError(exception, "Erro não tratado em {Path}", context.Request.Path);
            problem = new ProblemDetails { Status = 500, Title = "Erro inesperado" };
        }

        context.Response.StatusCode = problem.Status!.Value;
        await context.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}
