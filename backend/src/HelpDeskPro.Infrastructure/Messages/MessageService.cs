using HelpDeskPro.Application.Common;
using HelpDeskPro.Application.Messages;
using HelpDeskPro.Application.Notifications;
using HelpDeskPro.Application.Tickets;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Infrastructure.Persistence;
using HelpDeskPro.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Infrastructure.Messages;

public sealed class MessageService(
    AppDbContext db, ICurrentUser me, TicketScope scope, ITicketEvents events, TimeProvider clock) : IMessageService
{
    public async Task<IReadOnlyList<MessageDto>> ListAsync(Guid ticketId, CancellationToken ct)
    {
        await scope.EnsureVisibleAsync(ticketId, ct);

        var query = db.TicketMessages.AsNoTracking().Where(m => m.TicketId == ticketId);
        if (me.IsRequester) query = query.Where(m => !m.IsInternal); // regra de confidencialidade, aplicada no banco

        var rows = await (from m in query
                          join u in db.Users on m.AuthorId equals u.Id
                          orderby m.CreatedAt, m.Id
                          select new { m, u.FullName }).ToListAsync(ct);

        var authorIds = rows.Select(r => r.m.AuthorId).Distinct().ToList();
        var roles = await RolesAsync(authorIds, ct);

        return rows.Select(r => new MessageDto(r.m.Id, r.m.TicketId, r.m.AuthorId, r.FullName,
            roles.GetValueOrDefault(r.m.AuthorId, string.Empty), r.m.Content, r.m.IsInternal, r.m.CreatedAt)).ToList();
    }

    public async Task<MessageDto> CreateAsync(Guid ticketId, CreateMessageRequest request, CancellationToken ct)
    {
        var ticket = await scope.LoadAsync(ticketId, ct);
        var denied = TicketAccess.CheckMessage(me.Role, ticket.AssignedToId == me.Id, request.IsInternal);
        if (denied is not null) throw new ForbiddenException(denied);

        var now = clock.GetUtcNow().UtcDateTime;
        var message = ticket.AddMessage(me.Id, request.Content, request.IsInternal, authorIsAgent: me.IsStaff, now);

        // Resposta do solicitante a um chamado aguardando retorno o devolve ao atendimento.
        var previous = ticket.Status;
        var reopened = me.IsRequester && !request.IsInternal && previous == TicketStatus.WaitingForUser;
        if (reopened) ticket.ChangeStatus(TicketStatus.InProgress, me.Id, now);

        await db.SaveChangesAsync(ct);

        await events.MessageAddedAsync(ticket, message, me.Id, ct);
        if (reopened) await events.StatusChangedAsync(ticket, previous, me.Id, ct);

        var authorName = await db.Users.Where(u => u.Id == me.Id).Select(u => u.FullName).FirstAsync(ct);
        return new MessageDto(message.Id, ticketId, me.Id, authorName, me.Role, message.Content, message.IsInternal, message.CreatedAt);
    }

    private async Task<Dictionary<Guid, string>> RolesAsync(List<Guid> userIds, CancellationToken ct) =>
        await (from ur in db.UserRoles
               join r in db.Roles on ur.RoleId equals r.Id
               where userIds.Contains(ur.UserId)
               select new { ur.UserId, r.Name }).ToDictionaryAsync(x => x.UserId, x => x.Name!, ct);
}
