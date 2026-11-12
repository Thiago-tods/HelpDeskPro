namespace HelpDeskPro.Domain.Enums;

public enum TicketStatus
{
    New = 0,
    InProgress = 1,
    WaitingForUser = 2,
    Resolved = 3,
    Closed = 4,
    Cancelled = 5,
}

public enum HistoryAction
{
    Created = 0,
    StatusChanged = 1,
    PriorityChanged = 2,
    Assigned = 3,
    TeamChanged = 4,
    Resolved = 5,
    Reopened = 6,
    Closed = 7,
    Cancelled = 8,
    SlaBreached = 9,
}

/// <summary>Situação do chamado frente ao SLA, calculada no backend.</summary>
public enum SlaState
{
    OnTrack = 0,
    AtRisk = 1,
    Overdue = 2,
    Met = 3,
    Missed = 4,
    NotApplicable = 5,
}

public enum SlaEvent
{
    FirstResponseWarning = 0,
    FirstResponseBreached = 1,
    ResolutionWarning = 2,
    ResolutionBreached = 3,
}

public enum NotificationType
{
    NewTicket = 0,
    TicketAssigned = 1,
    NewMessage = 2,
    StatusChanged = 3,
    SlaNearDue = 4,
    SlaBreached = 5,
}
