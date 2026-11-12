using HelpDeskPro.Application.Auth;
using HelpDeskPro.Application.Catalog;
using HelpDeskPro.Application.Common;
using HelpDeskPro.Domain.Entities;
using HelpDeskPro.Domain.Enums;
using HelpDeskPro.Domain.Exceptions;
using HelpDeskPro.Infrastructure.Identity;
using HelpDeskPro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskPro.Infrastructure.Catalog;

public sealed class CatalogService(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    ICurrentUser me,
    TimeProvider clock) : ICatalogService
{
    // ---------- Categorias ----------

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct)
    {
        var query = db.Categories.AsNoTracking();
        if (!me.IsAdmin) query = query.Where(c => c.IsActive);
        return await query.OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.IsActive, c.DefaultTeamId))
            .ToListAsync(ct);
    }

    public async Task<CategoryDto> CreateCategoryAsync(SaveCategoryRequest request, CancellationToken ct)
    {
        await EnsureCategoryNameFreeAsync(request.Name, null, ct);
        await EnsureTeamExistsAsync(request.DefaultTeamId, ct);
        var category = new Category(request.Name.Trim(), request.Description?.Trim(), request.DefaultTeamId);
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return ToDto(category);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(Guid id, SaveCategoryRequest request, CancellationToken ct)
    {
        var category = await db.Categories.FindAsync([id], ct) ?? throw new NotFoundException("Categoria não encontrada.");
        await EnsureCategoryNameFreeAsync(request.Name, id, ct);
        await EnsureTeamExistsAsync(request.DefaultTeamId, ct);
        category.Update(request.Name.Trim(), request.Description?.Trim(), request.DefaultTeamId);
        await db.SaveChangesAsync(ct);
        return ToDto(category);
    }

    public async Task<CategoryDto> SetCategoryActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var category = await db.Categories.FindAsync([id], ct) ?? throw new NotFoundException("Categoria não encontrada.");
        category.SetActive(active);
        await db.SaveChangesAsync(ct);
        return ToDto(category);
    }

    // ---------- Prioridades ----------

    public async Task<IReadOnlyList<PriorityDto>> ListPrioritiesAsync(CancellationToken ct)
    {
        var query = db.Priorities.AsNoTracking();
        if (!me.IsAdmin) query = query.Where(p => p.IsActive);
        return await query.OrderBy(p => p.Level)
            .Select(p => new PriorityDto(p.Id, p.Name, p.Level, p.FirstResponseMinutes, p.ResolutionMinutes, p.IsActive))
            .ToListAsync(ct);
    }

    public async Task<PriorityDto> CreatePriorityAsync(SavePriorityRequest request, CancellationToken ct)
    {
        await EnsurePriorityUniqueAsync(request, null, ct);
        var priority = new Priority(request.Name.Trim(), request.Level, request.FirstResponseMinutes, request.ResolutionMinutes);
        db.Priorities.Add(priority);
        await db.SaveChangesAsync(ct);
        return ToDto(priority);
    }

    /// <summary>Alterar prazos afeta apenas chamados criados depois (o SLA dos existentes não é recalculado).</summary>
    public async Task<PriorityDto> UpdatePriorityAsync(Guid id, SavePriorityRequest request, CancellationToken ct)
    {
        var priority = await db.Priorities.FindAsync([id], ct) ?? throw new NotFoundException("Prioridade não encontrada.");
        await EnsurePriorityUniqueAsync(request, id, ct);
        priority.Update(request.Name.Trim(), request.Level, request.FirstResponseMinutes, request.ResolutionMinutes);
        await db.SaveChangesAsync(ct);
        return ToDto(priority);
    }

    public async Task<PriorityDto> SetPriorityActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var priority = await db.Priorities.FindAsync([id], ct) ?? throw new NotFoundException("Prioridade não encontrada.");
        priority.SetActive(active);
        await db.SaveChangesAsync(ct);
        return ToDto(priority);
    }

    // ---------- Equipes ----------

    public async Task<IReadOnlyList<TeamDto>> ListTeamsAsync(CancellationToken ct)
    {
        var query = db.Teams.AsNoTracking();
        if (!me.IsAdmin) query = query.Where(t => t.IsActive);
        return await query.OrderBy(t => t.Name)
            .Select(t => new TeamDto(t.Id, t.Name, t.Description, t.IsActive, t.Members.Count))
            .ToListAsync(ct);
    }

    public async Task<TeamDto> CreateTeamAsync(SaveTeamRequest request, CancellationToken ct)
    {
        await EnsureTeamNameFreeAsync(request.Name, null, ct);
        var team = new Team(request.Name.Trim(), request.Description?.Trim(), clock.GetUtcNow().UtcDateTime);
        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);
        return new TeamDto(team.Id, team.Name, team.Description, team.IsActive, 0);
    }

    public async Task<TeamDto> UpdateTeamAsync(Guid id, SaveTeamRequest request, CancellationToken ct)
    {
        var team = await db.Teams.FindAsync([id], ct) ?? throw new NotFoundException("Equipe não encontrada.");
        await EnsureTeamNameFreeAsync(request.Name, id, ct);
        team.Update(request.Name.Trim(), request.Description?.Trim());
        await db.SaveChangesAsync(ct);
        return await GetTeamDtoAsync(id, ct);
    }

    public async Task<TeamDto> SetTeamActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var team = await db.Teams.FindAsync([id], ct) ?? throw new NotFoundException("Equipe não encontrada.");
        team.SetActive(active);
        await db.SaveChangesAsync(ct);
        return await GetTeamDtoAsync(id, ct);
    }

    public async Task<IReadOnlyList<TeamMemberDto>> ListTeamMembersAsync(Guid teamId, CancellationToken ct)
    {
        if (!await db.Teams.AnyAsync(t => t.Id == teamId, ct)) throw new NotFoundException("Equipe não encontrada.");

        // Agente só enxerga membros das próprias equipes.
        if (me.IsAgent && !await db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == me.Id, ct))
            throw new ForbiddenException("Você não pertence a esta equipe.");

        return await (from m in db.TeamMembers.AsNoTracking()
                      join u in db.Users on m.UserId equals u.Id
                      where m.TeamId == teamId
                      orderby u.FullName
                      select new TeamMemberDto(u.Id, u.FullName, u.Email!, m.JoinedAt))
            .ToListAsync(ct);
    }

    public async Task AddTeamMemberAsync(Guid teamId, Guid userId, CancellationToken ct)
    {
        var team = await db.Teams.FindAsync([teamId], ct) ?? throw new NotFoundException("Equipe não encontrada.");
        if (!team.IsActive) throw new DomainException("Equipe desativada.");

        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("Usuário não encontrado.");
        if (!user.IsActive || !await users.IsInRoleAsync(user, Roles.Agent))
            throw new DomainException("Apenas agentes ativos podem ser membros de equipe.");

        if (await db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == userId, ct))
            throw new DomainException("Usuário já é membro da equipe.");

        db.TeamMembers.Add(new TeamMember(teamId, userId, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveTeamMemberAsync(Guid teamId, Guid userId, CancellationToken ct)
    {
        var member = await db.TeamMembers.FindAsync([teamId, userId], ct)
            ?? throw new NotFoundException("Vínculo não encontrado.");
        db.TeamMembers.Remove(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<UserDto>> ListAgentsAsync(CancellationToken ct)
    {
        var agents = await users.GetUsersInRoleAsync(Roles.Agent);
        return agents.Where(a => a.IsActive).OrderBy(a => a.FullName)
            .Select(a => new UserDto(a.Id, a.FullName, a.Email!, Roles.Agent, true))
            .ToList();
    }

    // ---------- Helpers ----------

    private static CategoryDto ToDto(Category c) => new(c.Id, c.Name, c.Description, c.IsActive, c.DefaultTeamId);
    private static PriorityDto ToDto(Priority p) => new(p.Id, p.Name, p.Level, p.FirstResponseMinutes, p.ResolutionMinutes, p.IsActive);

    private Task<TeamDto> GetTeamDtoAsync(Guid id, CancellationToken ct) =>
        db.Teams.AsNoTracking().Where(t => t.Id == id)
            .Select(t => new TeamDto(t.Id, t.Name, t.Description, t.IsActive, t.Members.Count))
            .SingleAsync(ct);

    private async Task EnsureCategoryNameFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var n = name.Trim().ToLower();
        if (await db.Categories.AnyAsync(c => c.Name.ToLower() == n && c.Id != exceptId, ct))
            throw new DomainException("Já existe uma categoria com este nome.");
    }

    private async Task EnsureTeamNameFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var n = name.Trim().ToLower();
        if (await db.Teams.AnyAsync(t => t.Name.ToLower() == n && t.Id != exceptId, ct))
            throw new DomainException("Já existe uma equipe com este nome.");
    }

    private async Task EnsurePriorityUniqueAsync(SavePriorityRequest r, Guid? exceptId, CancellationToken ct)
    {
        var n = r.Name.Trim().ToLower();
        if (await db.Priorities.AnyAsync(p => (p.Name.ToLower() == n || p.Level == r.Level) && p.Id != exceptId, ct))
            throw new DomainException("Já existe uma prioridade com este nome ou nível.");
    }

    private async Task EnsureTeamExistsAsync(Guid? teamId, CancellationToken ct)
    {
        if (teamId is not null && !await db.Teams.AnyAsync(t => t.Id == teamId && t.IsActive, ct))
            throw new DomainException("Equipe padrão inválida.");
    }
}
