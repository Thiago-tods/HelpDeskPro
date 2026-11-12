using HelpDeskPro.Api.Infrastructure;
using HelpDeskPro.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HelpDeskPro.Api.Controllers;

[ApiController]
[Route("api/v1/categories")]
public sealed class CategoriesController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CategoryDto>> List(CancellationToken ct) => catalog.ListCategoriesAsync(ct);

    [HttpPost, Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Create(SaveCategoryRequest request, CancellationToken ct) =>
        StatusCode(201, await catalog.CreateCategoryAsync(request, ct));

    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Admin)]
    public Task<CategoryDto> Update(Guid id, SaveCategoryRequest request, CancellationToken ct) =>
        catalog.UpdateCategoryAsync(id, request, ct);

    [HttpPatch("{id:guid}/active"), Authorize(Policy = Policies.Admin)]
    public Task<CategoryDto> SetActive(Guid id, SetActiveRequest request, CancellationToken ct) =>
        catalog.SetCategoryActiveAsync(id, request.IsActive, ct);
}

[ApiController]
[Route("api/v1/priorities")]
public sealed class PrioritiesController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PriorityDto>> List(CancellationToken ct) => catalog.ListPrioritiesAsync(ct);

    [HttpPost, Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Create(SavePriorityRequest request, CancellationToken ct) =>
        StatusCode(201, await catalog.CreatePriorityAsync(request, ct));

    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Admin)]
    public Task<PriorityDto> Update(Guid id, SavePriorityRequest request, CancellationToken ct) =>
        catalog.UpdatePriorityAsync(id, request, ct);

    [HttpPatch("{id:guid}/active"), Authorize(Policy = Policies.Admin)]
    public Task<PriorityDto> SetActive(Guid id, SetActiveRequest request, CancellationToken ct) =>
        catalog.SetPriorityActiveAsync(id, request.IsActive, ct);
}

[ApiController]
[Route("api/v1/teams")]
public sealed class TeamsController(ICatalogService catalog) : ControllerBase
{
    [HttpGet, Authorize(Policy = Policies.Staff)]
    public Task<IReadOnlyList<TeamDto>> List(CancellationToken ct) => catalog.ListTeamsAsync(ct);

    [HttpPost, Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Create(SaveTeamRequest request, CancellationToken ct) =>
        StatusCode(201, await catalog.CreateTeamAsync(request, ct));

    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Admin)]
    public Task<TeamDto> Update(Guid id, SaveTeamRequest request, CancellationToken ct) =>
        catalog.UpdateTeamAsync(id, request, ct);

    [HttpPatch("{id:guid}/active"), Authorize(Policy = Policies.Admin)]
    public Task<TeamDto> SetActive(Guid id, SetActiveRequest request, CancellationToken ct) =>
        catalog.SetTeamActiveAsync(id, request.IsActive, ct);

    [HttpGet("{id:guid}/members"), Authorize(Policy = Policies.Staff)]
    public Task<IReadOnlyList<TeamMemberDto>> Members(Guid id, CancellationToken ct) =>
        catalog.ListTeamMembersAsync(id, ct);

    [HttpPost("{id:guid}/members"), Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> AddMember(Guid id, AddTeamMemberRequest request, CancellationToken ct)
    {
        await catalog.AddTeamMemberAsync(id, request.UserId, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/members/{userId:guid}"), Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken ct)
    {
        await catalog.RemoveTeamMemberAsync(id, userId, ct);
        return NoContent();
    }
}
