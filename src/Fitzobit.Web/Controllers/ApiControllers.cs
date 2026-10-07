using Fitzobit.Application.Catalogue;
using Fitzobit.Application.Dashboard;
using Fitzobit.Application.Imports;
using Fitzobit.Infrastructure.Diagnostics;
using Fitzobit.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitzobit.Web.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = Policies.Operate)]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    [HttpGet]
    public Task<DashboardSummaryDto> Get(CancellationToken ct) => dashboard.GetSummaryAsync(ct);
}

[ApiController]
[Route("api/catalogue")]
[Authorize(Policy = Policies.Operate)]
public sealed class CatalogueController(CatalogueService catalogue, ImportQueryService imports) : ControllerBase
{
    [HttpGet]
    public Task<CatalogueOverviewDto> Get(CancellationToken ct) => catalogue.GetOverviewAsync(ct);

    [HttpGet("plans")]
    public Task<IReadOnlyList<PlanDto>> Plans(CancellationToken ct) => catalogue.GetPlansAsync(ct);

    /// <summary>Active plans with their pricing tiers, for plan-mapping drop-downs.</summary>
    [HttpGet("plan-options")]
    public Task<IReadOnlyList<PlanOptionDto>> PlanOptions(CancellationToken ct) => imports.GetPlanOptionsAsync(ct);
}

[ApiController]
[Route("api/system")]
[Authorize(Policy = Policies.Admin)]
public sealed class SystemController(SystemInfoService systemInfo, AppPaths paths) : ControllerBase
{
    [HttpGet]
    public Task<SystemInfoDto> Get(CancellationToken ct) => systemInfo.GetAsync(paths.BasePath, ct);
}
