using HelpDeskPro.Application.Common;
using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Infrastructure.Tickets;

/// <summary>Converte valores brutos do histórico (ids, enums) em texto legível.</summary>
public static class HistoryFormatter
{
    public const string SystemActor = "Sistema";

    public static async Task<Dictionary<string, string>> ResolveNamesAsync(
        AppDbContext db, IEnumerable<TicketHistory> history, CancellationToken ct)
    {
        var ids = history
            .Where(h => h.Action is HistoryAction.Assigned or HistoryAction.TeamChanged or HistoryAction.PriorityChanged)
            .SelectMany(h => new[] { h.OldValue, h.NewValue })
            .Select(v => Guid.TryParse(v, out var g) ? g : (Guid?)null)
            .Where(g => g is not null).Select(g => g!.Value).Distinct().ToList();

        var map = new Dictionary<string, string>();
        if (ids.Count == 0) return map;

        foreach (var u in await db.Users.Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.FullName }).ToListAsync(ct))
            map[u.Id.ToString()] = u.FullName;
        foreach (var t in await db.Teams.Where(t => ids.Contains(t.Id)).Select(t => new { t.Id, t.Name }).ToListAsync(ct))
            map[t.Id.ToString()] = t.Name;
        foreach (var p in await db.Priorities.Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync(ct))
            map[p.Id.ToString()] = p.Name;
        return map;
    }

    public static string? Display(HistoryAction action, string? value, Dictionary<string, string> names)
    {
        if (value is null) return null;
        return action switch
        {
            HistoryAction.Assigned or HistoryAction.TeamChanged or HistoryAction.PriorityChanged =>
                names.GetValueOrDefault(value, value),
            _ => Enum.TryParse<TicketStatus>(value, out _) ? EnumCodes.ToCode(value) : value,
        };
    }
}
