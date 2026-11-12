using System.Security.Cryptography;
using HelpDeskPro.Application.Attachments;
using HelpDeskPro.Application.Common;
using HelpDeskPro.Application.Notifications;
using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Exceptions;
using HelpDeskPro.Infrastructure.Persistence;
using HelpDeskPro.Infrastructure.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HelpDeskPro.Infrastructure.Attachments;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<AttachmentOptions> options)
    {
        var configured = options.Value.StoragePath;
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HelpDeskPro", "attachments")
            : configured;
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(byte[] content, CancellationToken ct)
    {
        // Nome aleatório, sem extensão: o arquivo nunca é executável nem previsível.
        var storedName = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        await File.WriteAllBytesAsync(Resolve(storedName), content, ct);
        return storedName;
    }

    public Stream OpenRead(string storedName) =>
        new FileStream(Resolve(storedName), FileMode.Open, FileAccess.Read, FileShare.Read);

    private string Resolve(string storedName)
    {
        if (storedName.Length == 0 || !storedName.All(Uri.IsHexDigit))
            throw new DomainException("Nome de armazenamento inválido.");

        var full = Path.GetFullPath(Path.Combine(_root, storedName));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Caminho inválido."); // proteção contra path traversal
        return full;
    }
}

public sealed class AttachmentService(
    AppDbContext db, ICurrentUser me, TicketScope scope, IFileStorage storage,
    IOptions<AttachmentOptions> options, ITicketEvents events, TimeProvider clock) : IAttachmentService
{
    public async Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid ticketId, CancellationToken ct)
    {
        await scope.EnsureVisibleAsync(ticketId, ct);
        var rows = await (from a in db.TicketAttachments.AsNoTracking()
                          join u in db.Users on a.UploadedById equals u.Id
                          where a.TicketId == ticketId
                          orderby a.CreatedAt, a.Id
                          select new { a, u.FullName }).ToListAsync(ct);
        return rows.Select(r => new AttachmentDto(r.a.Id, r.a.TicketId, r.a.OriginalFileName, r.a.ContentType,
            r.a.SizeBytes, r.a.UploadedById, r.FullName, r.a.CreatedAt)).ToList();
    }

    public async Task<AttachmentDto> UploadAsync(
        Guid ticketId, string fileName, string? declaredContentType, byte[] content, CancellationToken ct)
    {
        var ticket = await scope.LoadAsync(ticketId, ct);
        if (ticket.IsFinal) throw new DomainException("Chamado finalizado não aceita anexos.");
        if (me.IsAgent && ticket.AssignedToId != me.Id)
            throw new ForbiddenException("Assuma o chamado para anexar arquivos.");

        var opts = options.Value;
        if (content.LongLength > opts.MaxBytes)
            throw new DomainException($"Arquivo excede o limite de {opts.MaxBytes / (1024 * 1024)} MB.");
        if (await db.TicketAttachments.CountAsync(a => a.TicketId == ticketId, ct) >= opts.MaxPerTicket)
            throw new DomainException($"Limite de {opts.MaxPerTicket} anexos por chamado atingido.");

        var inspection = FileInspector.Inspect(fileName, declaredContentType, content);
        if (!inspection.IsValid) throw new DomainException(inspection.Error!);

        var storedName = await storage.SaveAsync(content, ct);
        var attachment = new TicketAttachment(ticketId, me.Id, FileInspector.SanitizeFileName(fileName), storedName,
            inspection.ContentType!, content.LongLength, clock.GetUtcNow().UtcDateTime);
        db.TicketAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        await events.AttachmentAddedAsync(ticket, ct);

        var uploader = await db.Users.Where(u => u.Id == me.Id).Select(u => u.FullName).FirstAsync(ct);
        return new AttachmentDto(attachment.Id, ticketId, attachment.OriginalFileName, attachment.ContentType,
            attachment.SizeBytes, me.Id, uploader, attachment.CreatedAt);
    }

    public async Task<AttachmentDownload> DownloadAsync(Guid ticketId, Guid attachmentId, CancellationToken ct)
    {
        await scope.EnsureVisibleAsync(ticketId, ct);
        var attachment = await db.TicketAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.TicketId == ticketId, ct)
            ?? throw new NotFoundException("Anexo não encontrado.");

        return new AttachmentDownload(storage.OpenRead(attachment.StoredFileName), attachment.ContentType, attachment.OriginalFileName);
    }
}
