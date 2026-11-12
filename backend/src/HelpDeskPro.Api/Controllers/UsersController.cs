using HelpDeskPro.Api.Infrastructure;
using HelpDeskPro.Application.Auth;
using HelpDeskPro.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HelpDeskPro.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(IUserAdminService admin, ICatalogService catalog) : ControllerBase
{
    [HttpGet, Authorize(Policy = Policies.Admin)]
    public Task<IReadOnlyList<UserDto>> List(CancellationToken ct) => admin.ListAsync(ct);

    [HttpPost, Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await admin.CreateAsync(request, ct));

    [HttpPut("{id:guid}"), Authorize(Policy = Policies.Admin)]
    public Task<UserDto> Update(Guid id, UpdateUserRequest request, CancellationToken ct) =>
        admin.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/active"), Authorize(Policy = Policies.Admin)]
    public Task<UserDto> SetActive(Guid id, SetActiveRequest request, CancellationToken ct) =>
        admin.SetActiveAsync(id, request.IsActive, ct);

    /// <summary>Agentes ativos, para seleção de responsável.</summary>
    [HttpGet("agents"), Authorize(Policy = Policies.Staff)]
    public Task<IReadOnlyList<UserDto>> Agents(CancellationToken ct) => catalog.ListAgentsAsync(ct);
}
