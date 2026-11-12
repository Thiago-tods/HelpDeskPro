using FluentValidation;
using HelpDeskPro.Application.Auth;

namespace HelpDeskPro.Application.Catalog;

public sealed record CategoryDto(Guid Id, string Name, string? Description, bool IsActive, Guid? DefaultTeamId);
public sealed record SaveCategoryRequest(string Name, string? Description, Guid? DefaultTeamId);

public sealed record PriorityDto(Guid Id, string Name, int Level, int FirstResponseMinutes, int ResolutionMinutes, bool IsActive);
public sealed record SavePriorityRequest(string Name, int Level, int FirstResponseMinutes, int ResolutionMinutes);

public sealed record TeamDto(Guid Id, string Name, string? Description, bool IsActive, int MemberCount);
public sealed record SaveTeamRequest(string Name, string? Description);
public sealed record TeamMemberDto(Guid UserId, string FullName, string Email, DateTime JoinedAt);
public sealed record AddTeamMemberRequest(Guid UserId);

public sealed record SetActiveRequest(bool IsActive);

public interface ICatalogService
{
    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct);
    Task<CategoryDto> CreateCategoryAsync(SaveCategoryRequest request, CancellationToken ct);
    Task<CategoryDto> UpdateCategoryAsync(Guid id, SaveCategoryRequest request, CancellationToken ct);
    Task<CategoryDto> SetCategoryActiveAsync(Guid id, bool active, CancellationToken ct);

    Task<IReadOnlyList<PriorityDto>> ListPrioritiesAsync(CancellationToken ct);
    Task<PriorityDto> CreatePriorityAsync(SavePriorityRequest request, CancellationToken ct);
    Task<PriorityDto> UpdatePriorityAsync(Guid id, SavePriorityRequest request, CancellationToken ct);
    Task<PriorityDto> SetPriorityActiveAsync(Guid id, bool active, CancellationToken ct);

    Task<IReadOnlyList<TeamDto>> ListTeamsAsync(CancellationToken ct);
    Task<TeamDto> CreateTeamAsync(SaveTeamRequest request, CancellationToken ct);
    Task<TeamDto> UpdateTeamAsync(Guid id, SaveTeamRequest request, CancellationToken ct);
    Task<TeamDto> SetTeamActiveAsync(Guid id, bool active, CancellationToken ct);
    Task<IReadOnlyList<TeamMemberDto>> ListTeamMembersAsync(Guid teamId, CancellationToken ct);
    Task AddTeamMemberAsync(Guid teamId, Guid userId, CancellationToken ct);
    Task RemoveTeamMemberAsync(Guid teamId, Guid userId, CancellationToken ct);

    Task<IReadOnlyList<UserDto>> ListAgentsAsync(CancellationToken ct);
}

public sealed class SaveCategoryRequestValidator : AbstractValidator<SaveCategoryRequest>
{
    public SaveCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class SavePriorityRequestValidator : AbstractValidator<SavePriorityRequest>
{
    public SavePriorityRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Level).InclusiveBetween(1, 10);
        RuleFor(x => x.FirstResponseMinutes).GreaterThan(0);
        RuleFor(x => x.ResolutionMinutes).GreaterThanOrEqualTo(x => x.FirstResponseMinutes)
            .WithMessage("A resolução não pode ser menor que a primeira resposta.");
    }
}

public sealed class SaveTeamRequestValidator : AbstractValidator<SaveTeamRequest>
{
    public SaveTeamRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class AddTeamMemberRequestValidator : AbstractValidator<AddTeamMemberRequest>
{
    public AddTeamMemberRequestValidator() => RuleFor(x => x.UserId).NotEmpty();
}
