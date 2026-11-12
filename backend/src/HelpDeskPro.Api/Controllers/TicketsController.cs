using HelpDeskPro.Application.Common;
using HelpDeskPro.Application.Tickets;
using Microsoft.AspNetCore.Mvc;

namespace HelpDeskPro.Api.Controllers;

[ApiController]
[Route("api/v1/tickets")]
public sealed class TicketsController(ITicketService tickets) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<TicketListItemDto>> List([FromQuery] TicketQuery query, CancellationToken ct) =>
        tickets.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<TicketDetailDto> Get(Guid id, CancellationToken ct) => tickets.GetAsync(id, ct);

    [HttpPost]
    public async Task<IActionResult> Create(CreateTicketRequest request, CancellationToken ct)
    {
        var created = await tickets.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPatch("{id:guid}")]
    public Task<TicketDetailDto> Update(Guid id, UpdateTicketRequest request, CancellationToken ct) =>
        tickets.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/status")]
    public Task<TicketDetailDto> ChangeStatus(Guid id, ChangeStatusRequest request, CancellationToken ct) =>
        tickets.ChangeStatusAsync(id, request, ct);

    [HttpPatch("{id:guid}/assignment")]
    public Task<TicketDetailDto> Assign(Guid id, AssignTicketRequest request, CancellationToken ct) =>
        tickets.AssignAsync(id, request, ct);

    [HttpGet("{id:guid}/history")]
    public Task<IReadOnlyList<TicketHistoryDto>> History(Guid id, CancellationToken ct) =>
        tickets.GetHistoryAsync(id, ct);
}
