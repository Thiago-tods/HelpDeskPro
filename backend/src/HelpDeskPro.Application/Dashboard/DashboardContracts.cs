using FluentValidation;
using HelpDeskPro.Application.Common;

namespace HelpDeskPro.Application.Dashboard;

public sealed class DashboardQuery
{
    /// <summary>Janela, em dias, para médias, quebras e linha do tempo (padrão 30).</summary>
    public int Days { get; init; } = 30;
}

public sealed record NamedCount(Guid Id, string Name, int Count);
public sealed record AgentWorkload(Guid Id, string Name, int Total, int Active, int Resolved, int Overdue);
public sealed record TimelinePoint(string Date, int Created, int Resolved);

/// <summary>
/// Totais por status e "Overdue" são o retrato atual do escopo do usuário.
/// Médias, quebras e linha do tempo consideram chamados criados nos últimos <see cref="Days"/> dias.
/// </summary>
public sealed record DashboardSummaryDto(
    int Days, DateTime GeneratedAt,
    int Total, int Open, int InProgress, int WaitingForUser, int Resolved, int Closed, int Cancelled, int Overdue,
    double? AvgFirstResponseMinutes, double? AvgResolutionMinutes,
    IReadOnlyList<NamedCount> ByPriority, IReadOnlyList<NamedCount> ByCategory,
    IReadOnlyList<AgentWorkload> ByAgent, IReadOnlyList<TimelinePoint> Timeline);

public interface IDashboardService
{
    Task<DashboardSummaryDto> SummaryAsync(DashboardQuery query, CancellationToken ct);
}

public sealed class DashboardQueryValidator : AbstractValidator<DashboardQuery>
{
    public DashboardQueryValidator() => RuleFor(x => x.Days).InclusiveBetween(1, 365);
}

public static class PresentationTime
{
    /// <summary>Fuso de apresentação (America/Sao_Paulo); o banco guarda UTC.</summary>
    public static TimeZoneInfo Zone { get; } = Resolve();

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília", "Brasília");
    }

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static DateTime LocalMidnightToUtc(DateTime localDate) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified), Zone);
}
