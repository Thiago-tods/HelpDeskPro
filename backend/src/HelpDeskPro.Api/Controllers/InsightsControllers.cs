using HelpDeskPro.Api.Infrastructure;
using HelpDeskPro.Application.Common;
using HelpDeskPro.Application.Dashboard;
using HelpDeskPro.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HelpDeskPro.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public sealed class DashboardController(IDashboardService dashboard) : ControllerBase
{
    [HttpGet("summary")]
    public Task<DashboardSummaryDto> Summary([FromQuery] DashboardQuery query, CancellationToken ct) =>
        dashboard.SummaryAsync(query, ct);
}

[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = Policies.Admin)]
public sealed class ReportsController(IReportService reports) : ControllerBase
{
    [HttpGet("sla")]
    public Task<SlaReportDto> Sla([FromQuery] ReportQuery query, CancellationToken ct) => reports.SlaAsync(query, ct);

    [HttpGet("agents")]
    public Task<AgentReportDto> Agents([FromQuery] ReportQuery query, CancellationToken ct) => reports.AgentsAsync(query, ct);

    [HttpGet("tickets/export")]
    public async Task<IActionResult> Export([FromQuery] ReportQuery query, CancellationToken ct)
    {
        var csv = await reports.ExportTicketsAsync(query, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(csv.Content, "text/csv; charset=utf-8", csv.FileName);
    }
}

[ApiController]
[Route("api/v1/audit")]
[Authorize(Policy = Policies.Admin)]
public sealed class AuditController(IReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AuditEntryDto>> List([FromQuery] AuditQuery query, CancellationToken ct) =>
        reports.AuditAsync(query, ct);
}
