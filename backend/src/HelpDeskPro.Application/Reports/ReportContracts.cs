using System.Text;
using FluentValidation;
using HelpDeskPro.Application.Common;

namespace HelpDeskPro.Application.Reports;

public sealed class ReportQuery
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}

public sealed class AuditQuery
{
    public string? TicketNumber { get; init; }
    public Guid? ActorId { get; init; }
    public string? Action { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record SlaReportRow(
    Guid PriorityId, string PriorityName, int Total,
    int FirstResponseMet, int FirstResponseMissed, int ResolutionMet, int ResolutionMissed,
    double? AvgFirstResponseMinutes, double? AvgResolutionMinutes, double? ComplianceRate);

public sealed record SlaReportDto(DateTime From, DateTime To, double? OverallComplianceRate, IReadOnlyList<SlaReportRow> Rows);

public sealed record AgentReportRow(
    Guid AgentId, string AgentName, int Assigned, int Active, int Resolved, int Overdue, double? AvgResolutionMinutes);

public sealed record AgentReportDto(DateTime From, DateTime To, IReadOnlyList<AgentReportRow> Rows);

public sealed record AuditEntryDto(
    Guid Id, Guid TicketId, string TicketNumber, string Action, Guid? ActorId, string ActorName,
    string? OldValue, string? NewValue, DateTime CreatedAt);

public sealed record CsvExport(byte[] Content, string FileName);

public interface IReportService
{
    Task<SlaReportDto> SlaAsync(ReportQuery query, CancellationToken ct);
    Task<AgentReportDto> AgentsAsync(ReportQuery query, CancellationToken ct);
    Task<CsvExport> ExportTicketsAsync(ReportQuery query, CancellationToken ct);
    Task<PagedResult<AuditEntryDto>> AuditAsync(AuditQuery query, CancellationToken ct);
}

public sealed class ReportQueryValidator : AbstractValidator<ReportQuery>
{
    public ReportQueryValidator() =>
        RuleFor(x => x).Must(x => x.From is null || x.To is null || x.From <= x.To).WithMessage("Período inválido.");
}

public sealed class AuditQueryValidator : AbstractValidator<AuditQuery>
{
    public AuditQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.TicketNumber).MaximumLength(30);
        RuleFor(x => x.Action).Must(a => EnumCodes.TryParse<Domain.Enums.HistoryAction>(a, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Action)).WithMessage("Ação inválida.");
        RuleFor(x => x).Must(x => x.From is null || x.To is null || x.From <= x.To).WithMessage("Período inválido.");
    }
}

public static class Csv
{
    public const char Separator = ';';

    /// <summary>Escapa aspas e neutraliza injeção de fórmulas (=, +, -, @) abrindo a planilha no Excel.</summary>
    public static string Cell(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    public static string Row(IEnumerable<string?> cells) => string.Join(Separator, cells.Select(Cell));

    /// <summary>UTF-8 com BOM, para o Excel reconhecer a acentuação.</summary>
    public static byte[] Encode(IEnumerable<string> lines) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n")];
}
