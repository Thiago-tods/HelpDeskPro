using HelpDeskPro.Application.Notifications;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HelpDeskPro.Infrastructure.Sla;

public sealed class SlaOptions
{
    public const string Section = "Sla";

    /// <summary>Intervalo da varredura automática em segundos. 0 desliga o worker (ex.: testes).</summary>
    public int CheckIntervalSeconds { get; set; } = 60;
}

public sealed class SlaMonitor(AppDbContext db, ITicketEvents events, ILogger<SlaMonitor> logger) : ISlaMonitor
{
    public async Task<SlaRunResult> RunOnceAsync(DateTime now, CancellationToken ct)
    {
        // Em escala de demonstração, avalia em memória todos os chamados ativos ainda sem violação completa.
        var active = await db.Tickets.Include(t => t.Sla)
            .Where(t => t.Status == TicketStatus.New || t.Status == TicketStatus.InProgress || t.Status == TicketStatus.WaitingForUser)
            .Where(t => t.Sla!.ResolutionBreachedAt == null
                || (t.FirstRespondedAt == null && t.Sla.FirstResponseBreachedAt == null))
            .ToListAsync(ct);

        var pending = new List<(Domain.Entities.Ticket Ticket, IReadOnlyList<SlaEvent> Events)>();
        foreach (var ticket in active)
        {
            var found = ticket.EvaluateSla(now);
            if (found.Count > 0) pending.Add((ticket, found));
        }

        if (pending.Count > 0) await db.SaveChangesAsync(ct);

        int warnings = 0, breaches = 0;
        foreach (var (ticket, found) in pending)
            foreach (var evt in found)
            {
                if (evt is SlaEvent.FirstResponseWarning or SlaEvent.ResolutionWarning) warnings++; else breaches++;
                await events.SlaAlertAsync(ticket, evt, ct);
            }

        if (warnings + breaches > 0)
            logger.LogInformation("Monitor de SLA: {Warnings} avisos e {Breaches} violações.", warnings, breaches);
        return new SlaRunResult(active.Count, warnings, breaches);
    }
}

public sealed class SlaBackgroundService(
    IServiceScopeFactory scopes, IOptions<SlaOptions> options, TimeProvider clock, ILogger<SlaBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = options.Value.CheckIntervalSeconds;
        if (seconds <= 0)
        {
            logger.LogInformation("Monitor de SLA desativado (Sla:CheckIntervalSeconds = 0).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISlaMonitor>()
                    .RunOnceAsync(clock.GetUtcNow().UtcDateTime, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha na varredura de SLA; nova tentativa no próximo ciclo.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
