using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Domain.Exceptions;

namespace HelpDeskPro.Domain.Entities;

public class Ticket
{
    private static readonly Dictionary<TicketStatus, TicketStatus[]> Transitions = new()
    {
        [TicketStatus.New] = [TicketStatus.InProgress, TicketStatus.Cancelled],
        [TicketStatus.InProgress] = [TicketStatus.WaitingForUser, TicketStatus.Resolved, TicketStatus.Cancelled],
        [TicketStatus.WaitingForUser] = [TicketStatus.InProgress, TicketStatus.Cancelled],
        [TicketStatus.Resolved] = [TicketStatus.InProgress, TicketStatus.Closed],
        [TicketStatus.Closed] = [],
        [TicketStatus.Cancelled] = [],
    };

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string TicketNumber { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public TicketStatus Status { get; private set; } = TicketStatus.New;
    public Guid RequesterId { get; private set; }
    public Guid? AssignedToId { get; private set; }
    public Guid? TeamId { get; private set; }
    public Guid CategoryId { get; private set; }
    public Guid PriorityId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? FirstRespondedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    public TicketSla? Sla { get; private set; }
    public List<TicketMessage> Messages { get; private set; } = [];
    public List<TicketHistory> History { get; private set; } = [];
    public List<TicketAttachment> Attachments { get; private set; } = [];

    private Ticket() { }

    public static Ticket Create(string ticketNumber, string title, string description, Guid requesterId,
        Guid categoryId, Priority priority, Guid? teamId, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Título obrigatório.");
        if (string.IsNullOrWhiteSpace(description)) throw new DomainException("Descrição obrigatória.");

        var ticket = new Ticket
        {
            TicketNumber = ticketNumber,
            Title = title.Trim(),
            Description = description.Trim(),
            RequesterId = requesterId,
            CategoryId = categoryId,
            PriorityId = priority.Id,
            TeamId = teamId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ticket.Sla = TicketSla.Create(ticket.Id, priority, now);
        ticket.AddHistory(HistoryAction.Created, requesterId, null, TicketStatus.New.ToString(), now);
        return ticket;
    }

    public static bool CanTransition(TicketStatus from, TicketStatus to) => Transitions[from].Contains(to);

    public bool IsFinal => Status is TicketStatus.Closed or TicketStatus.Cancelled;

    public void ChangeStatus(TicketStatus to, Guid actorId, DateTime now)
    {
        if (!CanTransition(Status, to)) throw new InvalidStatusTransitionException(Status, to);
        if (Status == TicketStatus.New && to == TicketStatus.InProgress && AssignedToId is null)
            throw new DomainException("Atribua um responsável antes de iniciar o atendimento.");

        var from = Status;
        Status = to;
        UpdatedAt = now;

        var action = to switch
        {
            TicketStatus.Resolved => HistoryAction.Resolved,
            TicketStatus.Closed => HistoryAction.Closed,
            TicketStatus.Cancelled => HistoryAction.Cancelled,
            TicketStatus.InProgress when from == TicketStatus.Resolved => HistoryAction.Reopened,
            _ => HistoryAction.StatusChanged,
        };

        if (to == TicketStatus.Resolved) ResolvedAt = now;
        if (to == TicketStatus.Closed) ClosedAt = now;
        if (action == HistoryAction.Reopened) ResolvedAt = null;

        AddHistory(action, actorId, from.ToString(), to.ToString(), now);

        // Resolução fora do prazo é registrada como violação mesmo que o monitor ainda não tenha rodado.
        if (to == TicketStatus.Resolved && Sla is not null && now > Sla.ResolutionDueAt && Sla.TryMarkResolutionBreached(now))
            AddHistory(HistoryAction.SlaBreached, null, null, SlaBreachKind.Resolution, now);
    }

    /// <summary>
    /// Avalia prazos de um chamado ativo no instante informado, registrando avisos e violações (uma vez cada).
    /// Chamado pelo monitor periódico; histórico de violações fica com autor nulo (sistema).
    /// </summary>
    public IReadOnlyList<SlaEvent> EvaluateSla(DateTime now)
    {
        var events = new List<SlaEvent>();
        if (!IsActive || Sla is null) return events;

        if (FirstRespondedAt is null)
        {
            if (now > Sla.FirstResponseDueAt)
            {
                if (Sla.TryMarkFirstResponseBreached(now))
                {
                    AddHistory(HistoryAction.SlaBreached, null, null, SlaBreachKind.FirstResponse, now);
                    events.Add(SlaEvent.FirstResponseBreached);
                }
            }
            else if (Sla.IsWarningDue(now, CreatedAt, Sla.FirstResponseDueAt) && Sla.TryMarkFirstResponseWarned(now))
            {
                events.Add(SlaEvent.FirstResponseWarning);
            }
        }

        if (now > Sla.ResolutionDueAt)
        {
            if (Sla.TryMarkResolutionBreached(now))
            {
                AddHistory(HistoryAction.SlaBreached, null, null, SlaBreachKind.Resolution, now);
                events.Add(SlaEvent.ResolutionBreached);
            }
        }
        else if (Sla.IsWarningDue(now, CreatedAt, Sla.ResolutionDueAt) && Sla.TryMarkResolutionWarned(now))
        {
            events.Add(SlaEvent.ResolutionWarning);
        }

        return events;
    }

    public SlaState GetSlaState(DateTime now)
    {
        if (Sla is null || Status == TicketStatus.Cancelled) return SlaState.NotApplicable;
        if (!IsActive) return Sla.BreachedAt is null ? SlaState.Met : SlaState.Missed;

        if (Sla.IsFirstResponseOverdue(now) || now > Sla.ResolutionDueAt) return SlaState.Overdue;

        var firstAtRisk = FirstRespondedAt is null && Sla.IsWarningDue(now, CreatedAt, Sla.FirstResponseDueAt);
        return firstAtRisk || Sla.IsWarningDue(now, CreatedAt, Sla.ResolutionDueAt) ? SlaState.AtRisk : SlaState.OnTrack;
    }

    public bool IsActive => Status is TicketStatus.New or TicketStatus.InProgress or TicketStatus.WaitingForUser;

    public void UpdateDetails(string title, string description, Guid categoryId, DateTime now)
    {
        if (IsFinal) throw new DomainException("Chamado finalizado não pode ser editado.");
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Título obrigatório.");
        if (string.IsNullOrWhiteSpace(description)) throw new DomainException("Descrição obrigatória.");
        Title = title.Trim();
        Description = description.Trim();
        CategoryId = categoryId;
        UpdatedAt = now;
    }

    public void Assign(Guid? agentId, Guid actorId, DateTime now)
    {
        if (IsFinal) throw new DomainException("Chamado finalizado não pode ser atribuído.");
        var old = AssignedToId;
        AssignedToId = agentId;
        UpdatedAt = now;
        AddHistory(HistoryAction.Assigned, actorId, old?.ToString(), agentId?.ToString(), now);

        if (agentId is not null && Status == TicketStatus.New)
            ChangeStatus(TicketStatus.InProgress, actorId, now);
    }

    public void ChangeTeam(Guid? teamId, Guid actorId, DateTime now)
    {
        if (IsFinal) throw new DomainException("Chamado finalizado não pode mudar de equipe.");
        var old = TeamId;
        TeamId = teamId;
        UpdatedAt = now;
        AddHistory(HistoryAction.TeamChanged, actorId, old?.ToString(), teamId?.ToString(), now);
    }

    public void ChangePriority(Priority priority, Guid actorId, DateTime now)
    {
        if (IsFinal) throw new DomainException("Chamado finalizado não pode mudar de prioridade.");
        var old = PriorityId;
        PriorityId = priority.Id;
        UpdatedAt = now;
        Sla!.Recalculate(priority, CreatedAt);
        AddHistory(HistoryAction.PriorityChanged, actorId, old.ToString(), priority.Id.ToString(), now);
    }

    public TicketMessage AddMessage(Guid authorId, string content, bool isInternal, bool authorIsAgent, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new DomainException("Mensagem vazia.");
        if (IsFinal) throw new DomainException("Chamado finalizado não aceita mensagens.");
        if (isInternal && !authorIsAgent) throw new DomainException("Apenas agentes criam notas internas.");

        var message = new TicketMessage(Id, authorId, content.Trim(), isInternal, now);
        Messages.Add(message);
        UpdatedAt = now;

        if (!isInternal && authorIsAgent && FirstRespondedAt is null)
        {
            FirstRespondedAt = now;
            Sla!.RegisterFirstResponse(now);
            if (now > Sla.FirstResponseDueAt && Sla.TryMarkFirstResponseBreached(now))
                AddHistory(HistoryAction.SlaBreached, null, null, SlaBreachKind.FirstResponse, now);
        }
        return message;
    }

    private DateTime _lastHistoryAt;

    // Entradas da mesma operação ganham instantes estritamente crescentes (1 µs, a precisão do PostgreSQL),
    // garantindo ordem cronológica estável sem depender da ordem de INSERT.
    private void AddHistory(HistoryAction action, Guid? actorId, string? oldValue, string? newValue, DateTime now)
    {
        var at = now > _lastHistoryAt ? now : _lastHistoryAt.AddTicks(10);
        _lastHistoryAt = at;
        History.Add(new TicketHistory(Id, actorId, action, oldValue, newValue, at));
    }
}

public class TicketMessage
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TicketId { get; private set; }
    public Guid AuthorId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public bool IsInternal { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private TicketMessage() { }

    internal TicketMessage(Guid ticketId, Guid authorId, string content, bool isInternal, DateTime now)
    {
        TicketId = ticketId;
        AuthorId = authorId;
        Content = content;
        IsInternal = isInternal;
        CreatedAt = now;
    }
}

public class TicketHistory
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    /// <summary>Ordem estável de inserção (gerada pelo banco), usada para desempate de datas iguais.</summary>
    public long Sequence { get; private set; }
    public Guid TicketId { get; private set; }
    /// <summary>Nulo quando o autor é o sistema (ex.: monitor de SLA).</summary>
    public Guid? ActorId { get; private set; }
    public HistoryAction Action { get; private set; }
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private TicketHistory() { }

    internal TicketHistory(Guid ticketId, Guid? actorId, HistoryAction action, string? oldValue, string? newValue, DateTime now)
    {
        TicketId = ticketId;
        ActorId = actorId;
        Action = action;
        OldValue = oldValue;
        NewValue = newValue;
        CreatedAt = now;
    }
}

public class TicketAttachment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TicketId { get; private set; }
    public Guid UploadedById { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StoredFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private TicketAttachment() { }

    public TicketAttachment(Guid ticketId, Guid uploadedById, string originalFileName, string storedFileName,
        string contentType, long sizeBytes, DateTime now)
    {
        TicketId = ticketId;
        UploadedById = uploadedById;
        OriginalFileName = originalFileName;
        StoredFileName = storedFileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        CreatedAt = now;
    }
}

public static class SlaBreachKind
{
    public const string FirstResponse = "FIRST_RESPONSE";
    public const string Resolution = "RESOLUTION";
}
