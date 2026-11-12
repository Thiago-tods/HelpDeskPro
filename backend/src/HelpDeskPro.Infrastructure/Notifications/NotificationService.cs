using HelpDeskPro.Application.Common;
using HelpDeskPro.Application.Notifications;
using HelpDeskPro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Infrastructure.Notifications;

public sealed class NotificationService(AppDbContext db, ICurrentUser me) : INotificationService
{
    public async Task<PagedResult<NotificationDto>> ListAsync(NotificationQuery q, CancellationToken ct)
    {
        var userId = me.Id;
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (q.UnreadOnly) query = query.Where(n => !n.IsRead);

        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);

        var items = page.Select(n => new NotificationDto(
            n.Id, EnumCodes.ToCode(n.Type), n.Message, n.TicketId, n.IsRead, n.CreatedAt)).ToList();
        return new PagedResult<NotificationDto>(items, q.Page, q.PageSize, total);
    }

    public Task<int> UnreadCountAsync(CancellationToken ct)
    {
        var userId = me.Id;
        return db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var userId = me.Id;
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct)
            ?? throw new NotFoundException("Notificação não encontrada.");
        notification.MarkAsRead();
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct)
    {
        var userId = me.Id;
        await db.Notifications.Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }
}
