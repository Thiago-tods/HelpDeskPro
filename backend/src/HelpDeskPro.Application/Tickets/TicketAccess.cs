using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Enums;

namespace HelpDeskPro.Application.Tickets;

/// <summary>Regras de autorização sobre chamados, puras e testáveis. A máquina de estados continua no domínio.</summary>
public static class TicketAccess
{
    public static IReadOnlyList<TicketStatus> AllowedStatusTargets(
        string role, bool isOwner, bool isAssignee, TicketStatus current)
    {
        var targets = Enum.GetValues<TicketStatus>()
            .Where(s => Ticket.CanTransition(current, s))
            // Iniciar atendimento acontece pela atribuição.
            .Where(s => !(current == TicketStatus.New && s == TicketStatus.InProgress))
            .ToList();

        return role switch
        {
            Roles.Admin => targets,
            Roles.Agent when isAssignee => targets,
            Roles.Requester when isOwner => targets.Where(s => RequesterMay(current, s)).ToList(),
            _ => [],
        };
    }

    public static bool RequesterMay(TicketStatus from, TicketStatus to) =>
        to == TicketStatus.Cancelled
        || (from == TicketStatus.Resolved && to is TicketStatus.InProgress or TicketStatus.Closed)
        || (from == TicketStatus.WaitingForUser && to == TicketStatus.InProgress);

    /// <returns>Mensagem de negação, ou null quando permitido.</returns>
    public static string? CheckAssignment(
        string role, Guid actorId, Guid? currentAssigneeId, Guid? newAssigneeId, bool actorInTicketTeam)
    {
        switch (role)
        {
            case Roles.Admin:
                return null;
            case Roles.Agent:
                var assumes = newAssigneeId == actorId && currentAssigneeId is null && actorInTicketTeam;
                var releases = newAssigneeId is null && currentAssigneeId == actorId;
                return assumes || releases
                    ? null
                    : "Agentes só podem assumir chamados livres da própria equipe ou liberar os seus.";
            default:
                return "Apenas a equipe de atendimento pode atribuir chamados.";
        }
    }

    /// <returns>Mensagem de negação, ou null quando o usuário pode postar. O escopo já foi verificado.</returns>
    public static string? CheckMessage(string role, bool isAssignee, bool isInternal) => role switch
    {
        Roles.Admin => null,
        Roles.Agent when isInternal => null,
        Roles.Agent when isAssignee => null,
        Roles.Agent => "Assuma o chamado para responder ao solicitante.",
        _ when isInternal => "Solicitantes não podem criar notas internas.",
        _ => null,
    };

    public static bool CanEditAsRequester(bool isOwner, TicketStatus status) => isOwner && status == TicketStatus.New;
}
