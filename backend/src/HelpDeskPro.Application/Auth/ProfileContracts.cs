using FluentValidation;
using HelpDeskPro.Domain.Enums;

namespace HelpDeskPro.Application.Auth;

public sealed record UpdateProfileRequest(string FullName);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Sessão nova (as anteriores são revogadas) ou lista de erros.</summary>
public sealed record ChangePasswordResult(AuthSession? Session, IReadOnlyList<string> Errors);

public sealed record CreateUserRequest(string FullName, string Email, string Role, string Password);
public sealed record UpdateUserRequest(string FullName, string Role);

public interface IUserAdminService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct);
    Task<UserDto> SetActiveAsync(Guid id, bool active, CancellationToken ct);
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator() => RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10).MaximumLength(128);
        RuleFor(x => x).Must(x => x.CurrentPassword != x.NewPassword).WithMessage("A nova senha deve ser diferente da atual.");
    }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Role).Must(r => Roles.All.Contains(r)).WithMessage("Papel inválido.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).MaximumLength(128);
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Role).Must(r => Roles.All.Contains(r)).WithMessage("Papel inválido.");
    }
}
