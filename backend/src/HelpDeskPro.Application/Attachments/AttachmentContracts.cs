using System.IO.Compression;
using System.Text;

namespace HelpDeskPro.Application.Attachments;

public sealed record AttachmentDto(
    Guid Id, Guid TicketId, string FileName, string ContentType, long SizeBytes, Guid UploadedById, string UploadedByName, DateTime CreatedAt);

public sealed record AttachmentDownload(Stream Content, string ContentType, string FileName);

public interface IAttachmentService
{
    Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid ticketId, CancellationToken ct);
    Task<AttachmentDto> UploadAsync(Guid ticketId, string fileName, string? declaredContentType, byte[] content, CancellationToken ct);
    Task<AttachmentDownload> DownloadAsync(Guid ticketId, Guid attachmentId, CancellationToken ct);
}

/// <summary>Armazenamento fora da pasta pública; o nome gravado é sempre aleatório.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(byte[] content, CancellationToken ct);
    Stream OpenRead(string storedName);
}

public sealed class AttachmentOptions
{
    public const string Section = "Attachments";

    public string? StoragePath { get; set; }
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxPerTicket { get; set; } = 20;
}

public sealed record InspectionResult(string? ContentType, string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>Valida extensão, tipo declarado e o conteúdo real (assinatura/estrutura) do arquivo.</summary>
public static class FileInspector
{
    public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly Dictionary<string, string> ExtensionToType = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".docx"] = Docx,
        [".xlsx"] = Xlsx,
    };

    public static IReadOnlyList<string> AllowedExtensions => ExtensionToType.Keys.Order().ToList();

    public static IReadOnlyCollection<string> AllowedContentTypes => ExtensionToType.Values.Distinct().ToList();

    public static InspectionResult Inspect(string fileName, string? declaredContentType, byte[] data)
    {
        if (data.Length == 0) return Fail("Arquivo vazio.");

        var extension = Path.GetExtension(fileName);
        if (!ExtensionToType.TryGetValue(extension, out var expected))
            return Fail($"Extensão não permitida. Permitidas: {string.Join(", ", ExtensionToType.Keys.Order())}.");

        if (!string.IsNullOrWhiteSpace(declaredContentType))
        {
            var declared = declaredContentType.Split(';')[0].Trim().ToLowerInvariant();
            if (!AllowedContentTypes.Contains(declared) && declared != "image/jpg")
                return Fail("Tipo de conteúdo não permitido.");
        }

        var ok = expected switch
        {
            "image/png" => StartsWith(data, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            "image/jpeg" => StartsWith(data, 0xFF, 0xD8, 0xFF),
            "image/gif" => StartsWith(data, "GIF87a"u8) || StartsWith(data, "GIF89a"u8),
            "application/pdf" => StartsWith(data, "%PDF-"u8),
            "text/plain" or "text/csv" => IsPlainText(data),
            Docx => IsOpenXml(data, "word/"),
            Xlsx => IsOpenXml(data, "xl/"),
            _ => false,
        };

        return ok ? new InspectionResult(expected, null) : Fail("O conteúdo do arquivo não corresponde à extensão.");
    }

    /// <summary>Remove caminhos e caracteres inválidos do nome original (apenas para exibição/download).</summary>
    public static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim().TrimStart('.');
        if (clean.Length > 200) clean = clean[^200..];
        return clean.Length == 0 ? "arquivo" : clean;
    }

    private static InspectionResult Fail(string error) => new(null, error);

    private static bool StartsWith(byte[] data, params byte[] signature) =>
        data.Length >= signature.Length && data.AsSpan(0, signature.Length).SequenceEqual(signature);

    private static bool StartsWith(byte[] data, ReadOnlySpan<byte> signature) =>
        data.Length >= signature.Length && data.AsSpan(0, signature.Length).SequenceEqual(signature);

    private static bool IsPlainText(byte[] data)
    {
        try
        {
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data);
            return !text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n'));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsOpenXml(byte[] data, string folder)
    {
        if (!StartsWith(data, 0x50, 0x4B, 0x03, 0x04)) return false;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            const long maxUncompressed = 200L * 1024 * 1024;
            if (zip.Entries.Sum(e => e.Length) > maxUncompressed) return false;
            return zip.GetEntry("[Content_Types].xml") is not null
                && zip.Entries.Any(e => e.FullName.StartsWith(folder, StringComparison.Ordinal));
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
