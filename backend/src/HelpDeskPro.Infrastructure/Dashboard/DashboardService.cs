using HelpDeskPro.Application.Common;
using HelpDeskPro.Application.Dashboard;
using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Infrastructure.Persistence;
using HelpDeskPro.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Infrastructure.Dashboard;

/// <summary>Indicadores calculados no banco, sempre dentro do escopo do usuário (mesma regra da listagem).</summary>
public sealed class DashboardService(AppDbContext db, ICurrentUser me, TicketScope scope, TimeProvider clock) : IDashboardService
{
    public async Task<DashboardSummaryDto> SummaryAsync(DashboardQuery query, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var teamIds = await scope.MyTeamIdsAsync(ct);
        var scoped = scope.Apply(db.Tickets.AsNoTracking(), teamIds);

        // ---- Retrato atual ----
        var byStatus = await scoped.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(TicketStatus s) => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0;

        var overdue = await scoped
            .Where(t => t.Status == TicketStatus.New || t.Status == TicketStatus.InProgress || t.Status == TicketStatus.WaitingForUser)
            .CountAsync(t => db.TicketSlas.Any(s => s.TicketId == t.Id
                && ((t.FirstRespondedAt == null && s.FirstResponseDueAt < now) || s.ResolutionDueAt < now)), ct);

        // ---- Janela de análise (fuso de apresentação) ----
        var today = PresentationTime.ToLocal(now).Date;
        var firstDay = today.AddDays(-(query.Days - 1));
        var fromUtc = PresentationTime.LocalMidnightToUtc(firstDay);
        var inPeriod = scoped.Where(t => t.CreatedAt >= fromUtc);

        var times = await inPeriod.Select(t => new { t.CreatedAt, t.FirstRespondedAt, t.ResolvedAt }).ToListAsync(ct);
        double? Avg(IEnumerable<double> values) => values.Any() ? Math.Round(values.Average(), 1) : null;
        var avgFirst = Avg(times.Where(x => x.FirstRespondedAt != null).Select(x => (x.FirstRespondedAt!.Value - x.CreatedAt).TotalMinutes));
        var avgResolution = Avg(times.Where(x => x.ResolvedAt != null).Select(x => (x.ResolvedAt!.Value - x.CreatedAt).TotalMinutes));

        var byPriority = await (from t in inPeriod
                                join p in db.Priorities on t.PriorityId equals p.Id
                                group t by new { p.Id, p.Name, p.Level } into g
                                orderby g.Key.Level
                                select new NamedCount(g.Key.Id, g.Key.Name, g.Count())).ToListAsync(ct);

        var byCategory = await (from t in inPeriod
                                join c in db.Categories on t.CategoryId equals c.Id
                                group t by new { c.Id, c.Name } into g
                                orderby g.Count() descending, g.Key.Name
                                select new NamedCount(g.Key.Id, g.Key.Name, g.Count())).ToListAsync(ct);

        var byAgent = me.IsRequester ? [] : await AgentWorkloadAsync(inPeriod, now, ct);

        // ---- Linha do tempo por dia local ----
        var resolvedDates = await scoped.Where(t => t.ResolvedAt != null && t.ResolvedAt >= fromUtc)
            .Select(t => t.ResolvedAt!.Value).ToListAsync(ct);
        var createdByDay = times.GroupBy(x => PresentationTime.ToLocal(x.CreatedAt).Date).ToDictionary(g => g.Key, g => g.Count());
        var resolvedByDay = resolvedDates.GroupBy(d => PresentationTime.ToLocal(d).Date).ToDictionary(g => g.Key, g => g.Count());
        var timeline = Enumerable.Range(0, query.Days).Select(i => firstDay.AddDays(i))
            .Select(day => new TimelinePoint(day.ToString("yyyy-MM-dd"), createdByDay.GetValueOrDefault(day), resolvedByDay.GetValueOrDefault(day)))
            .ToList();

        return new DashboardSummaryDto(
            query.Days, now,
            byStatus.Sum(x => x.Count), Count(TicketStatus.New), Count(TicketStatus.InProgress), Count(TicketStatus.WaitingForUser),
            Count(TicketStatus.Resolved), Count(TicketStatus.Closed), Count(TicketStatus.Cancelled), overdue,
            avgFirst, avgResolution, byPriority, byCategory, byAgent, timeline);
    }

    private async Task<List<AgentWorkload>> AgentWorkloadAsync(IQueryable<Ticket> inPeriod, DateTime now, CancellationToken ct)
    {
        var rows = await (from t in inPeriod
                          where t.AssignedToId != null
                          join u in db.Users on t.AssignedToId equals u.Id
                          join s in db.TicketSlas on t.Id equals s.TicketId
                          select new
                          {
                              AgentId = u.Id,
                              u.FullName,
                              t.Status,
                              t.FirstRespondedAt,
                              s.FirstResponseDueAt,
                              s.ResolutionDueAt,
                          }).ToListAsync(ct);

        static bool IsActive(TicketStatus s) => s is TicketStatus.New or TicketStatus.InProgress or TicketStatus.WaitingForUser;

        return rows.GroupBy(r => new { r.AgentId, r.FullName })
            .Select(g => new AgentWorkload(g.Key.AgentId, g.Key.FullName, g.Count(),
                g.Count(r => IsActive(r.Status)),
                g.Count(r => r.Status is TicketStatus.Resolved or TicketStatus.Closed),
                g.Count(r => IsActive(r.Status)
                    && ((r.FirstRespondedAt == null && r.FirstResponseDueAt < now) || r.ResolutionDueAt < now))))
            .OrderByDescending(a => a.Total).ThenBy(a => a.Name).ToList();
    }
}
