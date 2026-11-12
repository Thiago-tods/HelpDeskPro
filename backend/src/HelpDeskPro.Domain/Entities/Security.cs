using HelpDeskPro.Domain.Enums;

namespace HelpDeskPro.Domain.Entities;

public class Notification
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public Guid? TicketId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public bool IsRead { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Notification() { }

    public Notification(Guid userId, Guid? ticketId, NotificationType type, string message, DateTime now)
    {
        UserId = userId;
        TicketId = ticketId;
        Type = type;
        Message = message;
        CreatedAt = now;
    }

    public void MarkAsRead() => IsRead = true;
}

public class RefreshToken
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }

    private RefreshToken() { }

    public RefreshToken(Guid userId, string tokenHash, DateTime expiresAt, DateTime now)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTime now, Guid? replacedBy = null)
    {
        RevokedAt ??= now;
        ReplacedByTokenId = replacedBy;
    }
}
