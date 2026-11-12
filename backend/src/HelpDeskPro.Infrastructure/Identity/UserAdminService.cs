using HelpDeskPro.Application.Auth;
using HelpDeskPro.Application.Common;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Domain.Exceptions;
using HelpDeskPro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Infrastructure.Identity;

public sealed class UserAdminService(
    UserManager<ApplicationUser> users, AppDbContext db, ICurrentUser me, TimeProvider clock) : IUserAdminService
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct)
    {
        var all = await users.Users.AsNoTracking().OrderBy(u => u.FullName).ToListAsync(ct);
        var roles = await (from ur in db.UserRoles
                           join r in db.Roles on ur.RoleId equals r.Id
                           select new { ur.UserId, r.Name }).ToDictionaryAsync(x => x.UserId, x => x.Name!, ct);
        return all.Select(u => new UserDto(u.Id, u.FullName, u.Email!, roles.GetValueOrDefault(u.Id, string.Empty), u.IsActive)).ToList();
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        if (await users.FindByEmailAsync(request.Email) is not null)
            throw new DomainException("Já existe um usuário com este e-mail.");

        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            EmailConfirmed = true,
            FullName = request.FullName.Trim(),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) throw new DomainException(string.Join(" ", result.Errors.Select(e => e.Description)));

        await users.AddToRoleAsync(user, request.Role);
        return new UserDto(user.Id, user.FullName, user.Email!, request.Role, true);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("Usuário não encontrado.");
        var currentRole = (await users.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;
        var roleChanged = currentRole != request.Role;

        if (roleChanged && id == me.Id)
            throw new DomainException("Você não pode alterar o seu próprio papel.");

        user.FullName = request.FullName.Trim();
        await users.UpdateAsync(user);

        if (roleChanged)
        {
            if (currentRole.Length > 0) await users.RemoveFromRoleAsync(user, currentRole);
            await users.AddToRoleAsync(user, request.Role);

            // Apenas agentes integram equipes; as sessões são renovadas para refletir o novo papel.
            if (currentRole == Roles.Agent)
                await db.TeamMembers.Where(m => m.UserId == id).ExecuteDeleteAsync(ct);
            await RevokeSessionsAsync(id, ct);
        }

        return new UserDto(user.Id, user.FullName, user.Email!, request.Role, user.IsActive);
    }

    public async Task<UserDto> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        if (id == me.Id && !active) throw new DomainException("Você não pode desativar a sua própria conta.");

        var user = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("Usuário não encontrado.");
        user.IsActive = active;
        await users.UpdateAsync(user);
        if (!active) await RevokeSessionsAsync(id, ct);

        var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;
        return new UserDto(user.Id, user.FullName, user.Email!, role, active);
    }

    private async Task RevokeSessionsAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }
}
