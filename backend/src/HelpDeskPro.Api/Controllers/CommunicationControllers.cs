using HelpDeskPro.Application.Attachments;
using HelpDeskPro.Application.Common;
using HelpDeskPro.Application.Messages;
using HelpDeskPro.Application.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace HelpDeskPro.Api.Controllers;

[ApiController]
[Route("api/v1/tickets/{ticketId:guid}/messages")]
public sealed class MessagesController(IMessageService messages) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<MessageDto>> List(Guid ticketId, CancellationToken ct) => messages.ListAsync(ticketId, ct);

    [HttpPost]
    public async Task<IActionResult> Create(Guid ticketId, CreateMessageRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await messages.CreateAsync(ticketId, request, ct));
}

[ApiController]
[Route("api/v1/tickets/{ticketId:guid}/attachments")]
public sealed class AttachmentsController(IAttachmentService attachments) : ControllerBase
{
    private const long RequestLimit = 12L * 1024 * 1024;

    [HttpGet]
    public Task<IReadOnlyList<AttachmentDto>> List(Guid ticketId, CancellationToken ct) => attachments.ListAsync(ticketId, ct);

    [HttpPost, RequestSizeLimit(RequestLimit), RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    public async Task<IActionResult> Upload(Guid ticketId, IFormFile file, CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var created = await attachments.UploadAsync(ticketId, file.FileName, file.ContentType, buffer.ToArray(), ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("{attachmentId:guid}")]
    public async Task<IActionResult> Download(Guid ticketId, Guid attachmentId, CancellationToken ct)
    {
        var download = await attachments.DownloadAsync(ticketId, attachmentId, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        // fileDownloadName força Content-Disposition: attachment (nunca renderiza inline).
        return File(download.Content, download.ContentType, download.FileName);
    }
}

[ApiController]
[Route("api/v1/notifications")]
public sealed class NotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<NotificationDto>> List([FromQuery] NotificationQuery query, CancellationToken ct) =>
        notifications.ListAsync(query, ct);

    [HttpGet("unread-count")]
    public async Task<object> UnreadCount(CancellationToken ct) => new { count = await notifications.UnreadCountAsync(ct) };

    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}
