using HelpDeskPro.Application.Notifications;
using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HelpDeskPro.Infrastructure.Notifications;

/// <summary>
/// Gera notificações persistidas e eventos em tempo real. Falhas aqui nunca derrubam a operação principal:
/// o sistema continua funcionando por HTTP quando o canal em tempo real está indisponível.
/// </summary>
public sealed class TicketEventService(
    AppDbContext db,
    IRealtimePublisher realtime,
    TimeProvider clock,
    ILogger<TicketEventService> logger) : ITicketEvents
{
    public Task CreatedAsync(Ticket t, Guid actorId, CancellationToken ct) => SafeAsync(async () =>
    {
        var recipients = await TeamAgentsAsync(t, ct);
        if (recipients.Count == 0) recipients = await AdminsAsync(ct);
        await NotifyAsync(recipients.Where(r => r != actorId), t, NotificationType.NewTicket,
            $"Novo chamado {t.TicketNumber}: {t.Title}", ct);
        await PublishAsync("ticket.created", t, includeRequester: true, ct: ct);
    });

    public Task StatusChangedAsync(Ticket t, TicketStatus from, Guid actorId, CancellationToken ct) => SafeAsync(async () =>
    {
        var message = $"Chamado {t.TicketNumber} mudou para {StatusLabels.Of(t.Status)}";
        await NotifyAsync(new[] { t.RequesterId, t.AssignedToId ?? default }.Where(r => r != default && r != actorId),
            t, NotificationType.StatusChanged, message, ct);
        await PublishAsync("ticket.status", t, includeRequester: true, ct: ct);
    });

    public Task AssignedAsync(Ticket t, Guid actorId, CancellationToken ct) => SafeAsync(async () =>
    {
        if (t.AssignedToId is { } assignee)
        {
            var name = await db.Users.Where(u => u.Id == assignee).Select(u => u.FullName).FirstAsync(ct);
            await NotifyAsync(new[] { assignee }.Where(r => r != actorId), t, NotificationType.TicketAssigned,
                $"O chamado {t.TicketNumber} foi atribuído a você", ct);
            await NotifyAsync(new[] { t.RequesterId }.Where(r => r != actorId), t, NotificationType.TicketAssigned,
                $"Seu chamado {t.TicketNumber} foi atribuído a {name}", ct);
        }
        await PublishAsync("ticket.assigned", t, includeRequester: true, ct: ct);
    });

    public Task SlaAlertAsync(Ticket t, SlaEvent slaEvent, CancellationToken ct) => SafeAsync(async () =>
    {
        var firstResponse = slaEvent is SlaEvent.FirstResponseWarning or SlaEvent.FirstResponseBreached;
        var kind = firstResponse ? "primeira resposta" : "resolução";
        var breached = slaEvent is SlaEvent.FirstResponseBreached or SlaEvent.ResolutionBreached;

        var recipients = t.AssignedToId is { } assignee ? new List<Guid> { assignee } : await TeamAgentsAsync(t, ct);
        if (breached) recipients = recipients.Concat(await AdminsAsync(ct)).ToList();

        await NotifyAsync(recipients, t,
            breached ? NotificationType.SlaBreached : NotificationType.SlaNearDue,
            breached
                ? $"SLA de {kind} violado no chamado {t.TicketNumber}"
                : $"SLA de {kind} do chamado {t.TicketNumber} está próximo do vencimento", ct);
        await PublishAsync("ticket.sla", t, includeRequester: false, ct: ct);
    });

    public Task UpdatedAsync(Ticket t, CancellationToken ct) => SafeAsync(() =>
        PublishAsync("ticket.updated", t, includeRequester: true, ct: ct));

    public Task AttachmentAddedAsync(Ticket t, CancellationToken ct) => SafeAsync(() =>
        PublishAsync("ticket.attachment", t, includeRequester: true, ct: ct));

    public Task MessageAddedAsync(Ticket t, TicketMessage m, Guid actorId, CancellationToken ct) => SafeAsync(async () =>
    {
        IEnumerable<Guid> recipients;
        var text = $"Nova mensagem no chamado {t.TicketNumber}";

        if (m.IsInternal)
        {
            // Notas internas nunca chegam ao solicitante.
            recipients = new[] { t.AssignedToId ?? default }.Where(r => r != default);
            text = $"Nova nota interna no chamado {t.TicketNumber}";
        }
        else if (m.AuthorId == t.RequesterId)
        {
            recipients = t.AssignedToId is { } assignee ? new List<Guid> { assignee } : await TeamAgentsAsync(t, ct);
        }
        else
        {
            recipients = new[] { t.RequesterId };
        }

        await NotifyAsync(recipients.Where(r => r != actorId), t, NotificationType.NewMessage, text, ct);
        await PublishAsync("ticket.message", t, includeRequester: !m.IsInternal, m.Id, m.IsInternal, ct);
    });

    // ---------- Helpers ----------

    private async Task NotifyAsync(IEnumerable<Guid> userIds, Ticket t, NotificationType type, string message, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var created = userIds.Distinct().Select(id => new Notification(id, t.Id, type, message, now)).ToList();
        if (created.Count == 0) return;

        db.Notifications.AddRange(created);
        await db.SaveChangesAsync(ct);

        foreach (var n in created)
            await realtime.PublishNotificationAsync(n.UserId,
                new NotificationDto(n.Id, Application.Common.EnumCodes.ToCode(n.Type), n.Message, n.TicketId, n.IsRead, n.CreatedAt), ct);
    }

    private Task PublishAsync(string type, Ticket t, bool includeRequester, Guid? messageId = null, bool isInternal = false, CancellationToken ct = default)
    {
        var users = new List<Guid>();
        if (t.AssignedToId is { } assignee) users.Add(assignee);
        if (includeRequester) users.Add(t.RequesterId);
        var teams = t.TeamId is { } team ? new[] { team } : Array.Empty<Guid>();

        return realtime.PublishTicketEventAsync(
            new TicketEventPayload(type, t.Id, t.TicketNumber, messageId, isInternal),
            new RealtimeAudience(users, teams, Admins: true), ct);
    }

    private async Task<List<Guid>> TeamAgentsAsync(Ticket t, CancellationToken ct) =>
        t.TeamId is { } team
            ? await db.TeamMembers.Where(m => m.TeamId == team).Select(m => m.UserId).ToListAsync(ct)
            : [];

    private async Task<List<Guid>> AdminsAsync(CancellationToken ct) =>
        await (from u in db.Users
               join ur in db.UserRoles on u.Id equals ur.UserId
               join r in db.Roles on ur.RoleId equals r.Id
               where u.IsActive && r.Name == Roles.Admin
               select u.Id).ToListAsync(ct);

    private async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao gerar notificações/eventos em tempo real; operação principal mantida.");
        }
    }
}
