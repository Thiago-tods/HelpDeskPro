using System.Security.Claims;
using FluentValidation;
using FluentValidation.Results;
using HelpDeskPro.Api.Infrastructure;
using HelpDeskPro.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HelpDeskPro.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService auth, IWebHostEnvironment env, IConfiguration config) : ControllerBase
{
    private const string RefreshCookie = "hdp_refresh";
    private const string CookiePath = "/api/v1/auth";

    public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, UserDto User);

    [AllowAnonymous, HttpPost("login"), EnableRateLimiting(Policies.AuthRateLimit)]
    public async Task<IActionResult> Login(LoginRequest request, IValidator<LoginRequest> validator, CancellationToken ct)
    {
        var invalid = await ValidateAsync(validator, request, ct);
        if (invalid is not null) return invalid;

        var session = await auth.LoginAsync(request, ct);
        if (session is null) return InvalidCredentials();

        SetRefreshCookie(session);
        return Ok(new LoginResponse(session.AccessToken, session.AccessTokenExpiresAt, session.User));
    }

    [AllowAnonymous, HttpPost("refresh"), EnableRateLimiting(Policies.AuthRateLimit)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        if (!HasCsrfHeader()) return CsrfRejected();

        var token = Request.Cookies[RefreshCookie];
        var session = string.IsNullOrEmpty(token) ? null : await auth.RefreshAsync(token, ct);
        if (session is null)
        {
            ClearRefreshCookie();
            return Unauthorized(new ProblemDetails { Status = 401, Title = "Sessão inválida ou expirada" });
        }

        SetRefreshCookie(session);
        return Ok(new LoginResponse(session.AccessToken, session.AccessTokenExpiresAt, session.User));
    }

    [AllowAnonymous, HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!HasCsrfHeader()) return CsrfRejected();

        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        ClearRefreshCookie();
        return NoContent();
    }

    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        if (!HasCsrfHeader()) return CsrfRejected();

        await auth.LogoutAllAsync(CurrentUserId(), ct);
        ClearRefreshCookie();
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await auth.GetUserAsync(CurrentUserId(), ct);
        return user is null ? Unauthorized() : Ok(user);
    }

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await auth.UpdateProfileAsync(CurrentUserId(), request, ct);
        return user is null ? Unauthorized() : Ok(user);
    }

    /// <summary>Troca a senha, derruba as demais sessões e devolve uma sessão nova.</summary>
    [HttpPost("change-password"), EnableRateLimiting(Policies.AuthRateLimit)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (!HasCsrfHeader()) return CsrfRejected();

        var result = await auth.ChangePasswordAsync(CurrentUserId(), request, ct);
        if (result.Session is null)
        {
            ModelState.AddModelError(nameof(request.NewPassword), string.Join(" ", result.Errors));
            return ValidationProblem(ModelState);
        }

        SetRefreshCookie(result.Session);
        return Ok(new LoginResponse(result.Session.AccessToken, result.Session.AccessTokenExpiresAt, result.Session.User));
    }

    /// <summary>Modo local demonstrativo: sem e-mail, o token só é devolvido em Development e com a opção ligada.</summary>
    [AllowAnonymous, HttpPost("forgot-password"), EnableRateLimiting(Policies.AuthRateLimit)]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request, IValidator<ForgotPasswordRequest> validator, CancellationToken ct)
    {
        var invalid = await ValidateAsync(validator, request, ct);
        if (invalid is not null) return invalid;

        var token = await auth.CreatePasswordResetTokenAsync(request.Email, ct);
        var expose = env.IsDevelopment() && config.GetValue("Auth:ExposeResetToken", true);
        return Accepted(new
        {
            message = "Se o e-mail existir, as instruções de redefinição foram geradas.",
            resetToken = expose ? token : null,
        });
    }

    [AllowAnonymous, HttpPost("reset-password"), EnableRateLimiting(Policies.AuthRateLimit)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request, IValidator<ResetPasswordRequest> validator, CancellationToken ct)
    {
        var invalid = await ValidateAsync(validator, request, ct);
        if (invalid is not null) return invalid;

        var errors = await auth.ResetPasswordAsync(request, ct);
        if (errors.Count == 0) return NoContent();

        ModelState.AddModelError(nameof(request.NewPassword), string.Join(" ", errors));
        return ValidationProblem(ModelState);
    }

    private async Task<IActionResult?> ValidateAsync<T>(IValidator<T> validator, T model, CancellationToken ct)
    {
        ValidationResult result = await validator.ValidateAsync(model, ct);
        if (result.IsValid) return null;
        foreach (var e in result.Errors) ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
        return ValidationProblem(ModelState);
    }

    private IActionResult InvalidCredentials() =>
        Unauthorized(new ProblemDetails { Status = 401, Title = "Credenciais inválidas" });

    private IActionResult CsrfRejected() =>
        BadRequest(new ProblemDetails { Status = 400, Title = "Cabeçalho X-Requested-With obrigatório" });

    private bool HasCsrfHeader() => Request.Headers.ContainsKey("X-Requested-With");

    private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue("sub")!);

    private void SetRefreshCookie(AuthSession session) =>
        Response.Cookies.Append(RefreshCookie, session.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = CookiePath,
            Expires = session.RefreshTokenExpiresAt,
            IsEssential = true,
        });

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = CookiePath, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax });
}
