using FluentValidation;
using HelpDeskPro.Application.Common;
using HelpDeskPro.Domain.Enums;

namespace HelpDeskPro.Application.Tickets;

public sealed class TicketQuery
{
    public static readonly string[] SortFields = ["createdAt", "updatedAt", "ticketNumber", "status", "priority", "title"];

    public string? Search { get; init; }
    /// <summary>Um ou mais status separados por vírgula (ex.: NEW,IN_PROGRESS).</summary>
    public string? Status { get; init; }
    public Guid? PriorityId { get; init; }
    public Guid? CategoryId { get; init; }
    public Guid? TeamId { get; init; }
    public Guid? AssignedToId { get; init; }
    public Guid? RequesterId { get; init; }
    public bool Unassigned { get; init; }
    public bool Mine { get; init; }
    /// <summary>Somente chamados ativos com prazo vencido.</summary>
    public bool Overdue { get; init; }
    public DateTime? CreatedFrom { get; init; }
    public DateTime? CreatedTo { get; init; }
    public string SortBy { get; init; } = "createdAt";
    public string SortDir { get; init; } = "desc";
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record TicketListItemDto(
    Guid Id, string TicketNumber, string Title, string Status,
    Guid PriorityId, string PriorityName, int PriorityLevel,
    Guid CategoryId, string CategoryName,
    Guid? TeamId, string? TeamName,
    Guid RequesterId, string RequesterName,
    Guid? AssignedToId, string? AssignedToName,
    DateTime CreatedAt, DateTime UpdatedAt,
    DateTime? FirstResponseDueAt, DateTime ResolutionDueAt, DateTime? BreachedAt, string SlaState);

public sealed record TicketSlaDto(
    DateTime FirstResponseDueAt, DateTime ResolutionDueAt, DateTime? FirstRespondedAt, DateTime? BreachedAt,
    DateTime? FirstResponseBreachedAt, DateTime? ResolutionBreachedAt);

public sealed record TicketDetailDto(
    Guid Id, string TicketNumber, string Title, string Description, string Status,
    Guid PriorityId, string PriorityName, int PriorityLevel,
    Guid CategoryId, string CategoryName,
    Guid? TeamId, string? TeamName,
    Guid RequesterId, string RequesterName,
    Guid? AssignedToId, string? AssignedToName,
    DateTime CreatedAt, DateTime UpdatedAt,
    DateTime? FirstRespondedAt, DateTime? ResolvedAt, DateTime? ClosedAt,
    TicketSlaDto Sla, string SlaState,
    IReadOnlyList<string> AllowedStatuses,
    bool CanAssignToMe, bool CanEdit);

public sealed record TicketHistoryDto(
    Guid Id, string Action, Guid? ActorId, string ActorName, string? OldValue, string? NewValue, DateTime CreatedAt);

public sealed record CreateTicketRequest(
    string Title, string Description, Guid CategoryId, Guid PriorityId, Guid? TeamId = null, Guid? RequesterId = null);

public sealed record UpdateTicketRequest(
    string? Title = null, string? Description = null, Guid? CategoryId = null, Guid? PriorityId = null, Guid? TeamId = null);

public sealed record ChangeStatusRequest(string Status);

public sealed record AssignTicketRequest(Guid? AssignedToId);

public interface ITicketService
{
    Task<PagedResult<TicketListItemDto>> ListAsync(TicketQuery query, CancellationToken ct);
    Task<TicketDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<TicketDetailDto> CreateAsync(CreateTicketRequest request, CancellationToken ct);
    Task<TicketDetailDto> UpdateAsync(Guid id, UpdateTicketRequest request, CancellationToken ct);
    Task<TicketDetailDto> ChangeStatusAsync(Guid id, ChangeStatusRequest request, CancellationToken ct);
    Task<TicketDetailDto> AssignAsync(Guid id, AssignTicketRequest request, CancellationToken ct);
    Task<IReadOnlyList<TicketHistoryDto>> GetHistoryAsync(Guid id, CancellationToken ct);
}

public sealed class TicketQueryValidator : AbstractValidator<TicketQuery>
{
    public TicketQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.SortBy).Must(s => TicketQuery.SortFields.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Ordenação permitida: {string.Join(", ", TicketQuery.SortFields)}.");
        RuleFor(x => x.SortDir).Must(s => s.Equals("asc", StringComparison.OrdinalIgnoreCase) || s.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Direção deve ser asc ou desc.");
        RuleFor(x => x.Status).Must(BeValidStatusList).When(x => !string.IsNullOrWhiteSpace(x.Status))
            .WithMessage("Status inválido.");
        RuleFor(x => x).Must(x => x.CreatedFrom is null || x.CreatedTo is null || x.CreatedFrom <= x.CreatedTo)
            .WithMessage("Período inválido.");
    }

    private static bool BeValidStatusList(string? list) =>
        list!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(s => EnumCodes.TryParse<TicketStatus>(s, out _));
}

public sealed class CreateTicketRequestValidator : AbstractValidator<CreateTicketRequest>
{
    public CreateTicketRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(10000);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.PriorityId).NotEmpty();
    }
}

public sealed class UpdateTicketRequestValidator : AbstractValidator<UpdateTicketRequest>
{
    public UpdateTicketRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).When(x => x.Title is not null);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(10000).When(x => x.Description is not null);
        RuleFor(x => x).Must(x => x.Title is not null || x.Description is not null || x.CategoryId is not null
            || x.PriorityId is not null || x.TeamId is not null).WithMessage("Informe ao menos um campo.");
    }
}

public sealed class ChangeStatusRequestValidator : AbstractValidator<ChangeStatusRequest>
{
    public ChangeStatusRequestValidator() =>
        RuleFor(x => x.Status).Must(s => EnumCodes.TryParse<TicketStatus>(s, out _)).WithMessage("Status inválido.");
}
