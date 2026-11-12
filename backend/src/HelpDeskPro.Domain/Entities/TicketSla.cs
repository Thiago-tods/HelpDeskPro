using HelpDeskPro.Domain.Enums;

namespace HelpDeskPro.Domain.Entities;

public class TicketSla
{
    /// <summary>Fração do prazo a partir da qual o chamado entra em risco (alerta de proximidade).</summary>
    public const double WarningFraction = 0.8;

    public Guid TicketId { get; private set; }
    public DateTime FirstResponseDueAt { get; private set; }
    public DateTime ResolutionDueAt { get; private set; }
    public DateTime? FirstRespondedAt { get; private set; }

    /// <summary>Instante da primeira violação registrada (de qualquer tipo).</summary>
    public DateTime? BreachedAt { get; private set; }
    public DateTime? FirstResponseBreachedAt { get; private set; }
    public DateTime? ResolutionBreachedAt { get; private set; }
    public DateTime? FirstResponseWarnedAt { get; private set; }
    public DateTime? ResolutionWarnedAt { get; private set; }

    private TicketSla() { }

    public static TicketSla Create(Guid ticketId, Priority priority, DateTime createdAt)
    {
        var sla = new TicketSla { TicketId = ticketId };
        sla.Recalculate(priority, createdAt);
        return sla;
    }

    // Horas corridas, sem calendário de feriados e sem pausa por "aguardando usuário".
    public void Recalculate(Priority priority, DateTime createdAt)
    {
        FirstResponseDueAt = createdAt.AddMinutes(priority.FirstResponseMinutes);
        ResolutionDueAt = createdAt.AddMinutes(priority.ResolutionMinutes);
        FirstResponseWarnedAt = null;
        ResolutionWarnedAt = null;
    }

    public void RegisterFirstResponse(DateTime now) => FirstRespondedAt ??= now;

    public bool IsFirstResponseOverdue(DateTime now) => FirstRespondedAt is null && now > FirstResponseDueAt;

    public bool IsWarningDue(DateTime now, DateTime createdAt, DateTime dueAt) =>
        now <= dueAt && now >= createdAt + (dueAt - createdAt) * WarningFraction;

    /// <summary>Registra violação de primeira resposta uma única vez.</summary>
    public bool TryMarkFirstResponseBreached(DateTime now)
    {
        if (FirstResponseBreachedAt is not null) return false;
        FirstResponseBreachedAt = now;
        BreachedAt ??= now;
        return true;
    }

    /// <summary>Registra violação de resolução uma única vez.</summary>
    public bool TryMarkResolutionBreached(DateTime now)
    {
        if (ResolutionBreachedAt is not null) return false;
        ResolutionBreachedAt = now;
        BreachedAt ??= now;
        return true;
    }

    public bool TryMarkFirstResponseWarned(DateTime now)
    {
        if (FirstResponseWarnedAt is not null) return false;
        FirstResponseWarnedAt = now;
        return true;
    }

    public bool TryMarkResolutionWarned(DateTime now)
    {
        if (ResolutionWarnedAt is not null) return false;
        ResolutionWarnedAt = now;
        return true;
    }
}
