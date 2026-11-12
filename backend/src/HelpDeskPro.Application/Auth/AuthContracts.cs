using FluentValidation;

namespace HelpDeskPro.Application.Auth;

public sealed record LoginRequest(string Email, string Password);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record UserDto(Guid Id, string FullName, string Email, string Role, bool IsActive);

/// <summary>Sessão emitida. O RefreshToken nunca vai no corpo da resposta; apenas em cookie HttpOnly.</summary>
public sealed record AuthSession(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    UserDto User,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);

public interface IAuthService
{
    Task<AuthSession?> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthSession?> RefreshAsync(string refreshToken, CancellationToken ct);
    Task LogoutAsync(string? refreshToken, CancellationToken ct);
    Task LogoutAllAsync(Guid userId, CancellationToken ct);
    Task<UserDto?> GetUserAsync(Guid userId, CancellationToken ct);
    Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct);
    Task<ChangePasswordResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct);

    /// <returns>Token de redefinição (modo demonstrativo) ou null se o usuário não existir.</returns>
    Task<string?> CreatePasswordResetTokenAsync(string email, CancellationToken ct);

    /// <returns>Lista de erros; vazia quando concluído.</returns>
    Task<IReadOnlyList<string>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() =>
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Token).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10).MaximumLength(128);
    }
}
