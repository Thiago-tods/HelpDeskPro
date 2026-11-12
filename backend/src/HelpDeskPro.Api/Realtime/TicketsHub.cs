using HelpDeskPro.Application.Notifications;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Api.Realtime;

public static class RealtimeGroups
{
    public const string Admins = "admins";
    public static string User(Guid id) => $"user:{id}";
    public static string Team(Guid id) => $"team:{id}";
}

/// <summary>
/// Os grupos são derivados exclusivamente do token e do banco: o cliente não escolhe a quais grupos entra.
/// Eventos carregam só ids; os dados são relidos via HTTP, onde a autorização por recurso é aplicada.
/// </summary>
[Authorize]
public sealed class TicketsHub(AppDbContext db) : Hub
{
    public const string TicketEvent = "ticketEvent";
    public const string Notification = "notification";

    public override async Task OnConnectedAsync()
    {
        var principal = Context.User!;
        var userId = Guid.Parse(principal.FindFirst("sub")!.Value);
        var role = principal.FindFirst("role")?.Value;

        if (!await db.Users.AnyAsync(u => u.Id == userId && u.IsActive))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.User(userId));

        if (role == Roles.Admin)
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Admins);

        if (role == Roles.Agent)
        {
            var teams = await db.TeamMembers.Where(m => m.UserId == userId).Select(m => m.TeamId).ToListAsync();
            foreach (var team in teams)
                await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Team(team));
        }

        await base.OnConnectedAsync();
    }
}

public sealed class SignalRRealtimePublisher(IHubContext<TicketsHub> hub, ILogger<SignalRRealtimePublisher> logger)
    : IRealtimePublisher
{
    public async Task PublishTicketEventAsync(TicketEventPayload payload, RealtimeAudience audience, CancellationToken ct)
    {
        var groups = audience.Users.Select(RealtimeGroups.User)
            .Concat(audience.Teams.Select(RealtimeGroups.Team))
            .Concat(audience.Admins ? [RealtimeGroups.Admins] : [])
            .Distinct().ToList();
        if (groups.Count == 0) return;

        try
        {
            await hub.Clients.Groups(groups).SendAsync(TicketsHub.TicketEvent, payload, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao publicar evento em tempo real.");
        }
    }

    public async Task PublishNotificationAsync(Guid userId, NotificationDto notification, CancellationToken ct)
    {
        try
        {
            await hub.Clients.Group(RealtimeGroups.User(userId)).SendAsync(TicketsHub.Notification, notification, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao publicar notificação em tempo real.");
        }
    }
}
