using System.Security.Cryptography;
using System.Text;
using HelpDeskPro.Application.Auth;
using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Infrastructure.Identity;
using HelpDeskPro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HelpDeskPro.Infrastructure.Auth;

public sealed class AuthService(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider clock) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<AuthSession?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive) return null;
        if (await users.IsLockedOutAsync(user)) return null;

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            return null;
        }

        await users.ResetAccessFailedCountAsync(user);
        return await IssueSessionAsync(user, ct);
    }

    public async Task<AuthSession?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return null;

        if (stored.RevokedAt is not null)
        {
            // Reutilização de token já rotacionado: possível roubo. Derruba todas as sessões.
            if (stored.ReplacedByTokenId is not null) await RevokeAllAsync(stored.UserId, now, ct);
            return null;
        }

        if (stored.ExpiresAt <= now) return null;

        var user = await users.FindByIdAsync(stored.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            stored.Revoke(now);
            await db.SaveChangesAsync(ct);
            return null;
        }

        return await IssueSessionAsync(user, ct, stored);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return;
        stored.Revoke(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
    }

    public Task LogoutAllAsync(Guid userId, CancellationToken ct) =>
        RevokeAllAsync(userId, clock.GetUtcNow().UtcDateTime, ct);

    public async Task<UserDto?> GetUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null || !user.IsActive ? null : await ToDtoAsync(user);
    }

    public async Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive) return null;
        user.FullName = request.FullName.Trim();
        await users.UpdateAsync(user);
        return await ToDtoAsync(user);
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive) return new ChangePasswordResult(null, ["Usuário inválido."]);

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return new ChangePasswordResult(null, result.Errors
                .Select(e => e.Code == "PasswordMismatch" ? "Senha atual incorreta." : e.Description).ToList());

        // Todas as sessões anteriores caem; o chamador recebe uma sessão nova.
        await RevokeAllAsync(user.Id, clock.GetUtcNow().UtcDateTime, ct);
        return new ChangePasswordResult(await IssueSessionAsync(user, ct), []);
    }

    public async Task<string?> CreatePasswordResetTokenAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null || !user.IsActive) return null;
        return await users.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<IReadOnlyList<string>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive) return ["Token inválido ou expirado."];

        var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded) return result.Errors.Select(e => e.Description).ToList();

        await users.ResetAccessFailedCountAsync(user);
        await RevokeAllAsync(user.Id, clock.GetUtcNow().UtcDateTime, ct);
        return [];
    }

    private async Task<AuthSession> IssueSessionAsync(ApplicationUser user, CancellationToken ct, RefreshToken? rotating = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var dto = await ToDtoAsync(user);

        var accessExpires = now.AddMinutes(_jwt.AccessTokenMinutes);
        var accessToken = CreateAccessToken(dto, accessExpires, now);

        var rawRefresh = Base64Url(RandomNumberGenerator.GetBytes(64));
        var refreshExpires = now.AddDays(_jwt.RefreshTokenDays);
        var newToken = new RefreshToken(user.Id, Hash(rawRefresh), refreshExpires, now);
        db.RefreshTokens.Add(newToken);
        rotating?.Revoke(now, newToken.Id);
        await db.SaveChangesAsync(ct);

        return new AuthSession(accessToken, accessExpires, dto, rawRefresh, refreshExpires);
    }

    private string CreateAccessToken(UserDto user, DateTime expires, DateTime now)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email,
                [JwtRegisteredClaimNames.Name] = user.FullName,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                ["role"] = user.Role,
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key)), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private async Task<UserDto> ToDtoAsync(ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        return new UserDto(user.Id, user.FullName, user.Email!, roles.FirstOrDefault() ?? string.Empty, user.IsActive);
    }

    private async Task RevokeAllAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in active) token.Revoke(now);
        await db.SaveChangesAsync(ct);
    }

    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
