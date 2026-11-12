using HelpDeskPro.Application.Common;
using HelpDeskPro.Application.Dashboard;
using HelpDeskPro.Application.Notifications;
using HelpDeskPro.Application.Reports;
using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Infrastructure.Persistence;
using HelpDeskPro.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Infrastructure.Reports;

/// <summary>Relatórios e auditoria globais. A restrição a administradores é imposta no controller (política Admin).</summary>
public sealed class ReportService(AppDbContext db, TimeProvider clock) : IReportService
{
    private const int MaxExportRows = 10_000;

    public async Task<SlaReportDto> SlaAsync(ReportQuery query, CancellationToken ct)
    {
        var (from, to) = Period(query);
        var tickets = await PeriodTickets(from, to).Where(t => t.Status != TicketStatus.Cancelled).ToListAsync(ct);
        var priorities = await db.Priorities.AsNoTracking().OrderBy(p => p.Level).ToListAsync(ct);

        var rows = new List<SlaReportRow>();
        foreach (var p in priorities)
        {
            var group = tickets.Where(t => t.PriorityId == p.Id).ToList();
            if (group.Count == 0) continue;

            rows.Add(new SlaReportRow(p.Id, p.Name, group.Count,
                group.Count(t => t.FirstRespondedAt != null && t.Sla!.FirstResponseBreachedAt == null),
                group.Count(t => t.Sla!.FirstResponseBreachedAt != null),
                group.Count(t => t.ResolvedAt != null && t.Sla!.ResolutionBreachedAt == null),
                group.Count(t => t.Sla!.ResolutionBreachedAt != null),
                Average(group.Where(t => t.FirstRespondedAt != null).Select(t => (t.FirstRespondedAt!.Value - t.CreatedAt).TotalMinutes)),
                Average(group.Where(t => t.ResolvedAt != null).Select(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalMinutes)),
                Rate(group.Count(t => t.Sla!.BreachedAt == null), group.Count)));
        }

        return new SlaReportDto(from, to, Rate(tickets.Count(t => t.Sla!.BreachedAt == null), tickets.Count), rows);
    }

    public async Task<AgentReportDto> AgentsAsync(ReportQuery query, CancellationToken ct)
    {
        var (from, to) = Period(query);
        var now = clock.GetUtcNow().UtcDateTime;
        var tickets = await PeriodTickets(from, to).Where(t => t.AssignedToId != null).ToListAsync(ct);
        var names = await db.Users.AsNoTracking().Select(u => new { u.Id, u.FullName }).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var rows = tickets.GroupBy(t => t.AssignedToId!.Value)
            .Select(g => new AgentReportRow(g.Key, names.GetValueOrDefault(g.Key, "—"), g.Count(),
                g.Count(t => t.IsActive),
                g.Count(t => t.Status is TicketStatus.Resolved or TicketStatus.Closed),
                g.Count(t => t.GetSlaState(now) == SlaState.Overdue),
                Average(g.Where(t => t.ResolvedAt != null).Select(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalMinutes))))
            .OrderByDescending(r => r.Assigned).ThenBy(r => r.AgentName).ToList();

        return new AgentReportDto(from, to, rows);
    }

    public async Task<CsvExport> ExportTicketsAsync(ReportQuery query, CancellationToken ct)
    {
        var (from, to) = Period(query);
        var now = clock.GetUtcNow().UtcDateTime;

        var tickets = await PeriodTickets(from, to).OrderBy(t => t.CreatedAt).Take(MaxExportRows).ToListAsync(ct);
        var priorities = await db.Priorities.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var teams = await db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var users = await db.Users.AsNoTracking().Select(u => new { u.Id, u.FullName }).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        static string Local(DateTime? utc) =>
            utc is null ? string.Empty : PresentationTime.ToLocal(utc.Value).ToString("dd/MM/yyyy HH:mm");

        var lines = new List<string>
        {
            Csv.Row(["Número", "Título", "Status", "Prioridade", "Categoria", "Equipe", "Solicitante", "Responsável",
                "Criado em", "Primeira resposta", "Resolvido em", "Situação do SLA"]),
        };
        lines.AddRange(tickets.Select(t => Csv.Row([
            t.TicketNumber, t.Title, StatusLabels.Of(t.Status), priorities.GetValueOrDefault(t.PriorityId),
            categories.GetValueOrDefault(t.CategoryId), t.TeamId is { } team ? teams.GetValueOrDefault(team) : null,
            users.GetValueOrDefault(t.RequesterId), t.AssignedToId is { } a ? users.GetValueOrDefault(a) : null,
            Local(t.CreatedAt), Local(t.FirstRespondedAt), Local(t.ResolvedAt), EnumCodes.ToCode(t.GetSlaState(now)),
        ])));

        var stamp = PresentationTime.ToLocal(now).ToString("yyyyMMdd-HHmm");
        return new CsvExport(Csv.Encode(lines), $"chamados-{stamp}.csv");
    }

    public async Task<PagedResult<AuditEntryDto>> AuditAsync(AuditQuery q, CancellationToken ct)
    {
        var entries = from h in db.TicketHistory.AsNoTracking()
                      join t in db.Tickets on h.TicketId equals t.Id
                      select new { h, t.TicketNumber };

        if (!string.IsNullOrWhiteSpace(q.TicketNumber))
        {
            var like = $"%{q.TicketNumber.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            entries = entries.Where(e => EF.Functions.ILike(e.TicketNumber, like));
        }
        if (q.ActorId is { } actor) entries = entries.Where(e => e.h.ActorId == actor);
        if (EnumCodes.TryParse<HistoryAction>(q.Action, out var action)) entries = entries.Where(e => e.h.Action == action);
        if (q.From is { } from) entries = entries.Where(e => e.h.CreatedAt >= from.ToUniversalTime());
        if (q.To is { } to) entries = entries.Where(e => e.h.CreatedAt <= to.ToUniversalTime());

        var total = await entries.CountAsync(ct);
        var page = await entries.OrderByDescending(e => e.h.CreatedAt).ThenByDescending(e => e.h.Sequence)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);

        var actorIds = page.Where(e => e.h.ActorId != null).Select(e => e.h.ActorId!.Value).Distinct().ToList();
        var actors = await db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var names = await HistoryFormatter.ResolveNamesAsync(db, page.Select(e => e.h), ct);

        var items = page.Select(e => new AuditEntryDto(
            e.h.Id, e.h.TicketId, e.TicketNumber, EnumCodes.ToCode(e.h.Action), e.h.ActorId,
            e.h.ActorId is { } id ? actors.GetValueOrDefault(id, "—") : HistoryFormatter.SystemActor,
            HistoryFormatter.Display(e.h.Action, e.h.OldValue, names), HistoryFormatter.Display(e.h.Action, e.h.NewValue, names),
            e.h.CreatedAt)).ToList();

        return new PagedResult<AuditEntryDto>(items, q.Page, q.PageSize, total);
    }

    // ---------- Helpers ----------

    private (DateTime From, DateTime To) Period(ReportQuery q)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var to = q.To?.ToUniversalTime() ?? now;
        var from = q.From?.ToUniversalTime() ?? to.AddDays(-30);
        return (from, to);
    }

    private IQueryable<Ticket> PeriodTickets(DateTime from, DateTime to) =>
        db.Tickets.AsNoTracking().Include(t => t.Sla).Where(t => t.CreatedAt >= from && t.CreatedAt <= to);

    private static double? Average(IEnumerable<double> values) => values.Any() ? Math.Round(values.Average(), 1) : null;

    private static double? Rate(int part, int total) => total == 0 ? null : Math.Round(part * 100.0 / total, 1);
}
