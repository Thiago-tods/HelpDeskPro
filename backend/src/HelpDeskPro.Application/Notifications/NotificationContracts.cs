using HelpDeskPro.Application.Common;
using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Enums;

namespace HelpDeskPro.Application.Notifications;

public sealed record NotificationDto(
    Guid Id, string Type, string Message, Guid? TicketId, bool IsRead, DateTime CreatedAt);

public sealed class NotificationQuery
{
    public bool UnreadOnly { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public interface INotificationService
{
    Task<PagedResult<NotificationDto>> ListAsync(NotificationQuery query, CancellationToken ct);
    Task<int> UnreadCountAsync(CancellationToken ct);
    Task MarkReadAsync(Guid id, CancellationToken ct);
    Task MarkAllReadAsync(CancellationToken ct);
}

/// <summary>Quem recebe eventos em tempo real. Os ids vêm do servidor, nunca do cliente.</summary>
public sealed record RealtimeAudience(IReadOnlyCollection<Guid> Users, IReadOnlyCollection<Guid> Teams, bool Admins);

/// <summary>Evento sem conteúdo sensível: o cliente recarrega os dados via HTTP, que aplica a autorização.</summary>
public sealed record TicketEventPayload(
    string Type, Guid TicketId, string TicketNumber, Guid? MessageId = null, bool IsInternal = false);

public interface IRealtimePublisher
{
    Task PublishTicketEventAsync(TicketEventPayload payload, RealtimeAudience audience, CancellationToken ct);
    Task PublishNotificationAsync(Guid userId, NotificationDto notification, CancellationToken ct);
}

/// <summary>Efeitos colaterais de mudanças em chamados: notificações persistidas e eventos em tempo real.</summary>
public interface ITicketEvents
{
    Task CreatedAsync(Ticket ticket, Guid actorId, CancellationToken ct);
    Task StatusChangedAsync(Ticket ticket, TicketStatus from, Guid actorId, CancellationToken ct);
    Task AssignedAsync(Ticket ticket, Guid actorId, CancellationToken ct);
    Task UpdatedAsync(Ticket ticket, CancellationToken ct);
    Task MessageAddedAsync(Ticket ticket, TicketMessage message, Guid actorId, CancellationToken ct);
    Task AttachmentAddedAsync(Ticket ticket, CancellationToken ct);
    Task SlaAlertAsync(Ticket ticket, SlaEvent slaEvent, CancellationToken ct);
}

public sealed record SlaRunResult(int Evaluated, int Warnings, int Breaches);

/// <summary>Varredura de prazos: avisa proximidade do vencimento e registra violações (uma vez cada).</summary>
public interface ISlaMonitor
{
    Task<SlaRunResult> RunOnceAsync(DateTime now, CancellationToken ct);
}

public static class StatusLabels
{
    public static string Of(TicketStatus status) => status switch
    {
        TicketStatus.New => "Novo",
        TicketStatus.InProgress => "Em atendimento",
        TicketStatus.WaitingForUser => "Aguardando usuário",
        TicketStatus.Resolved => "Resolvido",
        TicketStatus.Closed => "Fechado",
        TicketStatus.Cancelled => "Cancelado",
        _ => status.ToString(),
    };
}
