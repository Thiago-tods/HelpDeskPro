using HelpDeskPro.Application.Common;
using HelpDeskPro.Application.Notifications;
using HelpDeskPro.Application.Tickets;
using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Domain.Exceptions;
using HelpDeskPro.Infrastructure.Identity;
using HelpDeskPro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Infrastructure.Tickets;

public sealed class TicketService(AppDbContext db, ICurrentUser me, TicketScope scope, ITicketEvents events, TimeProvider clock) : ITicketService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ---------- Consulta ----------

    public async Task<PagedResult<TicketListItemDto>> ListAsync(TicketQuery q, CancellationToken ct)
    {
        var teamIds = await MyTeamIdsAsync(ct);
        var tickets = ScopeTo(db.Tickets.AsNoTracking(), teamIds);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = $"%{EscapeLike(q.Search.Trim())}%";
            tickets = tickets.Where(t => EF.Functions.ILike(t.Title, like)
                || EF.Functions.ILike(t.TicketNumber, like)
                || EF.Functions.ILike(t.Description, like));
        }

        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            var statuses = q.Status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => EnumCodes.TryParse<TicketStatus>(s, out var v) ? v : (TicketStatus?)null)
                .Where(s => s is not null).Select(s => s!.Value).ToList();
            tickets = tickets.Where(t => statuses.Contains(t.Status));
        }

        if (q.PriorityId is { } pid) tickets = tickets.Where(t => t.PriorityId == pid);
        if (q.CategoryId is { } cid) tickets = tickets.Where(t => t.CategoryId == cid);
        if (q.TeamId is { } tid) tickets = tickets.Where(t => t.TeamId == tid);
        if (q.CreatedFrom is { } from) tickets = tickets.Where(t => t.CreatedAt >= from.ToUniversalTime());
        if (q.CreatedTo is { } to) tickets = tickets.Where(t => t.CreatedAt <= to.ToUniversalTime());

        if (me.IsStaff && q.RequesterId is { } rid) tickets = tickets.Where(t => t.RequesterId == rid);
        var meId = me.Id;
        if (q.Unassigned) tickets = tickets.Where(t => t.AssignedToId == null);
        else if (q.Mine) tickets = tickets.Where(t => t.AssignedToId == meId);
        else if (q.AssignedToId is { } aid) tickets = tickets.Where(t => t.AssignedToId == aid);

        if (q.Overdue)
        {
            var nowUtc = Now;
            tickets = tickets.Where(t => (t.Status == TicketStatus.New || t.Status == TicketStatus.InProgress || t.Status == TicketStatus.WaitingForUser)
                && db.TicketSlas.Any(s => s.TicketId == t.Id
                    && ((t.FirstRespondedAt == null && s.FirstResponseDueAt < nowUtc) || s.ResolutionDueAt < nowUtc)));
        }

        tickets = Order(tickets, q.SortBy, q.SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase));

        var total = await tickets.CountAsync(ct);
        var page = await tickets.Include(t => t.Sla).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);

        var priorityIds = page.Select(t => t.PriorityId).Distinct().ToList();
        var categoryIds = page.Select(t => t.CategoryId).Distinct().ToList();
        var teamIdsOnPage = page.Where(t => t.TeamId != null).Select(t => t.TeamId!.Value).Distinct().ToList();
        var userIds = page.SelectMany(t => new[] { t.RequesterId, t.AssignedToId ?? default }).Where(g => g != default).Distinct().ToList();
        var now = Now;

        var priorities = await db.Priorities.AsNoTracking().Where(p => priorityIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var categories = await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var teams = await db.Teams.AsNoTracking().Where(t => teamIdsOnPage.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var items = page.Select(t =>
        {
            var p = priorities[t.PriorityId];
            var sla = t.Sla!;
            return new TicketListItemDto(
                t.Id, t.TicketNumber, t.Title, EnumCodes.ToCode(t.Status),
                p.Id, p.Name, p.Level, t.CategoryId, categories[t.CategoryId],
                t.TeamId, t.TeamId is { } team ? teams[team] : null,
                t.RequesterId, users[t.RequesterId],
                t.AssignedToId, t.AssignedToId is { } agent ? users[agent] : null,
                t.CreatedAt, t.UpdatedAt, sla.FirstResponseDueAt, sla.ResolutionDueAt, sla.BreachedAt,
                EnumCodes.ToCode(t.GetSlaState(now)));
        }).ToList();

        return new PagedResult<TicketListItemDto>(items, q.Page, q.PageSize, total);
    }

    public async Task<TicketDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var teamIds = await MyTeamIdsAsync(ct);
        var row = await Rows(ScopeTo(db.Tickets.AsNoTracking(), teamIds).Where(t => t.Id == id))
            .Select(r => new
            {
                r.T.Id,
                PriorityName = r.P.Name,
                r.P.Level,
                CategoryName = r.C.Name,
                TeamName = r.Tm == null ? null : r.Tm.Name,
                RequesterName = r.R.FullName,
                AssignedName = r.A == null ? null : r.A.FullName,
            })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Chamado não encontrado.");

        var t = await db.Tickets.AsNoTracking().Include(x => x.Sla).SingleAsync(x => x.Id == id, ct);
        var sla = t.Sla!;
        var isOwner = t.RequesterId == me.Id;
        var isAssignee = t.AssignedToId == me.Id;
        var allowed = TicketAccess.AllowedStatusTargets(me.Role, isOwner, isAssignee, t.Status)
            .Select(s => EnumCodes.ToCode(s)).ToList();

        var canAssignToMe = me.IsAgent && t.AssignedToId is null && !t.IsFinal
            && t.TeamId is { } team && teamIds.Contains(team);
        var canEdit = !t.IsFinal && (me.IsAdmin
            || (me.IsAgent && isAssignee)
            || (me.IsRequester && TicketAccess.CanEditAsRequester(isOwner, t.Status)));

        return new TicketDetailDto(
            t.Id, t.TicketNumber, t.Title, t.Description, EnumCodes.ToCode(t.Status),
            t.PriorityId, row.PriorityName, row.Level, t.CategoryId, row.CategoryName,
            t.TeamId, row.TeamName, t.RequesterId, row.RequesterName, t.AssignedToId, row.AssignedName,
            t.CreatedAt, t.UpdatedAt, t.FirstRespondedAt, t.ResolvedAt, t.ClosedAt,
            new TicketSlaDto(sla.FirstResponseDueAt, sla.ResolutionDueAt, sla.FirstRespondedAt, sla.BreachedAt,
                sla.FirstResponseBreachedAt, sla.ResolutionBreachedAt),
            EnumCodes.ToCode(t.GetSlaState(Now)),
            allowed, canAssignToMe, canEdit);
    }

    public async Task<IReadOnlyList<TicketHistoryDto>> GetHistoryAsync(Guid id, CancellationToken ct)
    {
        var teamIds = await MyTeamIdsAsync(ct);
        if (!await ScopeTo(db.Tickets.AsNoTracking(), teamIds).AnyAsync(t => t.Id == id, ct))
            throw new NotFoundException("Chamado não encontrado.");

        var entries = await (from h in db.TicketHistory.AsNoTracking()
                             join u in db.Users on h.ActorId equals u.Id into actors
                             from u in actors.DefaultIfEmpty()
                             where h.TicketId == id
                             orderby h.CreatedAt, h.Sequence
                             select new { h, ActorName = u == null ? null : u.FullName }).ToListAsync(ct);

        var names = await HistoryFormatter.ResolveNamesAsync(db, entries.Select(e => e.h), ct);
        return entries.Select(e => new TicketHistoryDto(
            e.h.Id, EnumCodes.ToCode(e.h.Action), e.h.ActorId, e.ActorName ?? HistoryFormatter.SystemActor,
            HistoryFormatter.Display(e.h.Action, e.h.OldValue, names), HistoryFormatter.Display(e.h.Action, e.h.NewValue, names),
            e.h.CreatedAt)).ToList();
    }

    // ---------- Comandos ----------

    public async Task<TicketDetailDto> CreateAsync(CreateTicketRequest r, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == r.CategoryId && c.IsActive, ct)
            ?? throw new DomainException("Categoria inválida ou inativa.");
        var priority = await db.Priorities.FirstOrDefaultAsync(p => p.Id == r.PriorityId && p.IsActive, ct)
            ?? throw new DomainException("Prioridade inválida ou inativa.");

        var requesterId = me.Id;
        if (r.RequesterId is { } other && other != me.Id)
        {
            if (!me.IsStaff) throw new ForbiddenException("Você só pode abrir chamados em seu nome.");
            if (!await db.Users.AnyAsync(u => u.Id == other && u.IsActive, ct))
                throw new DomainException("Solicitante inválido.");
            requesterId = other;
        }

        var teamId = category.DefaultTeamId;
        if (r.TeamId is { } requestedTeam)
        {
            if (!me.IsStaff) throw new ForbiddenException("Apenas a equipe de atendimento escolhe a equipe.");
            await EnsureActiveTeamAsync(requestedTeam, ct);
            teamId = requestedTeam;
        }

        var number = await db.Database.SqlQuery<long>($"SELECT nextval('ticket_number_seq') AS \"Value\"").SingleAsync(ct);
        var now = Now;
        var ticket = Ticket.Create($"HDP-{now.Year}-{number:D6}", r.Title, r.Description, requesterId,
            category.Id, priority, teamId, now);

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        await events.CreatedAsync(ticket, me.Id, ct);
        return await GetAsync(ticket.Id, ct);
    }

    public async Task<TicketDetailDto> UpdateAsync(Guid id, UpdateTicketRequest r, CancellationToken ct)
    {
        var ticket = await LoadAsync(id, ct);
        var isOwner = ticket.RequesterId == me.Id;
        var now = Now;

        if (me.IsRequester)
        {
            if (!TicketAccess.CanEditAsRequester(isOwner, ticket.Status))
                throw new ForbiddenException("Você só pode editar seu chamado enquanto ele estiver como Novo.");
            if (r.PriorityId is not null || r.TeamId is not null)
                throw new ForbiddenException("Prioridade e equipe só podem ser alteradas pela equipe de atendimento.");
        }
        else if (me.IsAgent)
        {
            if (ticket.AssignedToId != me.Id) throw new ForbiddenException("Assuma o chamado antes de editá-lo.");
            if (r.TeamId is not null) throw new ForbiddenException("Apenas administradores mudam a equipe.");
        }

        if (r.CategoryId is { } categoryId && !await db.Categories.AnyAsync(c => c.Id == categoryId && c.IsActive, ct))
            throw new DomainException("Categoria inválida ou inativa.");

        if (r.Title is not null || r.Description is not null || r.CategoryId is not null)
            ticket.UpdateDetails(r.Title ?? ticket.Title, r.Description ?? ticket.Description,
                r.CategoryId ?? ticket.CategoryId, now);

        if (r.PriorityId is { } priorityId && priorityId != ticket.PriorityId)
        {
            var priority = await db.Priorities.FirstOrDefaultAsync(p => p.Id == priorityId && p.IsActive, ct)
                ?? throw new DomainException("Prioridade inválida ou inativa.");
            ticket.ChangePriority(priority, me.Id, now);
        }

        if (r.TeamId is { } teamId && teamId != ticket.TeamId)
        {
            await EnsureActiveTeamAsync(teamId, ct);
            ticket.ChangeTeam(teamId, me.Id, now);
        }

        await db.SaveChangesAsync(ct);
        await events.UpdatedAsync(ticket, ct);
        return await GetAsync(id, ct);
    }

    public async Task<TicketDetailDto> ChangeStatusAsync(Guid id, ChangeStatusRequest r, CancellationToken ct)
    {
        var ticket = await LoadAsync(id, ct);
        EnumCodes.TryParse<TicketStatus>(r.Status, out var target);

        var allowed = TicketAccess.AllowedStatusTargets(
            me.Role, ticket.RequesterId == me.Id, ticket.AssignedToId == me.Id, ticket.Status);

        if (!allowed.Contains(target))
        {
            // Transição inexistente no domínio vira 422; existente mas sem permissão vira 403.
            if (!Ticket.CanTransition(ticket.Status, target) || (ticket.Status == TicketStatus.New && target == TicketStatus.InProgress))
                ticket.ChangeStatus(target, me.Id, Now); // lança a exceção de domínio apropriada
            throw new ForbiddenException("Você não pode realizar esta mudança de status neste chamado.");
        }

        var previous = ticket.Status;
        ticket.ChangeStatus(target, me.Id, Now);
        await db.SaveChangesAsync(ct);
        await events.StatusChangedAsync(ticket, previous, me.Id, ct);
        return await GetAsync(id, ct);
    }

    public async Task<TicketDetailDto> AssignAsync(Guid id, AssignTicketRequest r, CancellationToken ct)
    {
        var ticket = await LoadAsync(id, ct);
        var teamIds = await MyTeamIdsAsync(ct);
        var inTeam = ticket.TeamId is { } t && teamIds.Contains(t);

        var denied = TicketAccess.CheckAssignment(me.Role, me.Id, ticket.AssignedToId, r.AssignedToId, inTeam);
        if (denied is not null) throw new ForbiddenException(denied);

        if (r.AssignedToId is { } agentId)
        {
            var isAgent = await (from u in db.Users
                                 join ur in db.UserRoles on u.Id equals ur.UserId
                                 join role in db.Roles on ur.RoleId equals role.Id
                                 where u.Id == agentId && u.IsActive && role.Name == Roles.Agent
                                 select u.Id).AnyAsync(ct);
            if (!isAgent) throw new DomainException("O responsável deve ser um agente ativo.");

            if (ticket.TeamId is { } ticketTeam
                && !await db.TeamMembers.AnyAsync(m => m.TeamId == ticketTeam && m.UserId == agentId, ct))
                throw new DomainException("O agente não pertence à equipe do chamado.");
        }

        ticket.Assign(r.AssignedToId, me.Id, Now);
        await db.SaveChangesAsync(ct);
        await events.AssignedAsync(ticket, me.Id, ct);
        return await GetAsync(id, ct);
    }

    // ---------- Escopo e consulta ----------

    private Task<Guid[]> MyTeamIdsAsync(CancellationToken ct) => scope.MyTeamIdsAsync(ct);

    private IQueryable<Ticket> ScopeTo(IQueryable<Ticket> query, Guid[] teamIds) => scope.Apply(query, teamIds);

    private Task<Ticket> LoadAsync(Guid id, CancellationToken ct) => scope.LoadAsync(id, ct);
    private IQueryable<Row> Rows(IQueryable<Ticket> tickets) =>
        from t in tickets
        join p in db.Priorities on t.PriorityId equals p.Id
        join c in db.Categories on t.CategoryId equals c.Id
        join r in db.Users on t.RequesterId equals r.Id
        join tm in db.Teams on t.TeamId equals tm.Id into tms
        from tm in tms.DefaultIfEmpty()
        join a in db.Users on t.AssignedToId equals a.Id into ass
        from a in ass.DefaultIfEmpty()
        select new Row(t, p, c, r, tm, a);

    private IQueryable<Ticket> Order(IQueryable<Ticket> q, string sortBy, bool desc)
    {
        var levels = db.Priorities;
        return (sortBy.ToLowerInvariant(), desc) switch
        {
            ("updatedat", true) => q.OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.Id),
            ("updatedat", false) => q.OrderBy(t => t.UpdatedAt).ThenBy(t => t.Id),
            ("ticketnumber", true) => q.OrderByDescending(t => t.TicketNumber),
            ("ticketnumber", false) => q.OrderBy(t => t.TicketNumber),
            ("status", true) => q.OrderByDescending(t => t.Status).ThenByDescending(t => t.Id),
            ("status", false) => q.OrderBy(t => t.Status).ThenBy(t => t.Id),
            ("priority", true) => q.OrderByDescending(t => levels.Where(p => p.Id == t.PriorityId).Select(p => p.Level).FirstOrDefault()).ThenByDescending(t => t.CreatedAt),
            ("priority", false) => q.OrderBy(t => levels.Where(p => p.Id == t.PriorityId).Select(p => p.Level).FirstOrDefault()).ThenBy(t => t.CreatedAt),
            ("title", true) => q.OrderByDescending(t => t.Title).ThenByDescending(t => t.Id),
            ("title", false) => q.OrderBy(t => t.Title).ThenBy(t => t.Id),
            (_, true) => q.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id),
            _ => q.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id),
        };
    }
    private async Task EnsureActiveTeamAsync(Guid teamId, CancellationToken ct)
    {
        if (!await db.Teams.AnyAsync(t => t.Id == teamId && t.IsActive, ct))
            throw new DomainException("Equipe inválida ou inativa.");
    }

    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private sealed record Row(Ticket T, Priority P, Category C, ApplicationUser R, Team? Tm, ApplicationUser? A);
}
