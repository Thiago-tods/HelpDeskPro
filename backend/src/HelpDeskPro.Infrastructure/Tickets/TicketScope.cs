using HelpDeskPro.Application.Common;
using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Infrastructure.Tickets;

/// <summary>Define quais chamados o usuário atual pode enxergar (proteção IDOR).</summary>
public sealed class TicketScope(AppDbContext db, ICurrentUser me)
{
    public async Task<Guid[]> MyTeamIdsAsync(CancellationToken ct) =>
        me.IsAgent
            ? await db.TeamMembers.Where(m => m.UserId == me.Id).Select(m => m.TeamId).ToArrayAsync(ct)
            : [];

    /// <summary>ADMIN: tudo. AGENT: equipes que integra ou atribuídos a si. REQUESTER: apenas os próprios.</summary>
    public IQueryable<Ticket> Apply(IQueryable<Ticket> query, Guid[] teamIds)
    {
        var userId = me.Id;
        return me.Role switch
        {
            Roles.Admin => query,
            Roles.Agent => query.Where(t => t.AssignedToId == userId || (t.TeamId != null && teamIds.Contains(t.TeamId.Value))),
            _ => query.Where(t => t.RequesterId == userId),
        };
    }

    /// <summary>Carrega o chamado (rastreado, com SLA) se estiver no escopo; senão 404.</summary>
    public async Task<Ticket> LoadAsync(Guid id, CancellationToken ct)
    {
        var teamIds = await MyTeamIdsAsync(ct);
        return await Apply(db.Tickets.Include(t => t.Sla), teamIds).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Chamado não encontrado.");
    }

    public async Task EnsureVisibleAsync(Guid id, CancellationToken ct)
    {
        var teamIds = await MyTeamIdsAsync(ct);
        if (!await Apply(db.Tickets.AsNoTracking(), teamIds).AnyAsync(t => t.Id == id, ct))
            throw new NotFoundException("Chamado não encontrado.");
    }
}
