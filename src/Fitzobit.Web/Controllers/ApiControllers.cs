using Fitzobit.Application.Dashboard;
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
[Route("api/system")]
[Authorize(Policy = Policies.Operate)]
public sealed class SystemController(SystemInfoService systemInfo) : ControllerBase
{
    [HttpGet]
    public Task<SystemInfoDto> Get(CancellationToken ct) => systemInfo.GetAsync(ct);
}
