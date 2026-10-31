using Fitzobit.Application.Customers;
using Fitzobit.Application.DailyOrders;
using Fitzobit.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitzobit.Web.Controllers;

public sealed class DailyOrdersExportRequest
{
    public DateOnly? Date { get; set; }
    public ExportFormat Format { get; set; } = ExportFormat.Excel;
}

/// <summary>Daily Orders: the boxes to cook and pack for one day, in total and per delivery group.</summary>
[ApiController]
[Route("api/daily-orders")]
[Authorize(Policy = Policies.Operate)]
public sealed class DailyOrdersController(DailyOrdersService orders) : ControllerBase
{
    /// <summary>One day's orders; without a date, the next delivery day.</summary>
    [HttpGet]
    public Task<DailyOrdersDto> Get([FromQuery] DateOnly? date, CancellationToken ct) => orders.GetAsync(date ?? orders.DefaultDate(), ct);

    /// <summary>Excel (summary + sheet per group) or PDF (totals page + page per group).</summary>
    [HttpPost("export")]
    public async Task<IActionResult> Export(DailyOrdersExportRequest request, CancellationToken ct)
    {
        var file = await orders.ExportAsync(request.Date ?? orders.DefaultDate(), request.Format, ct);
        Response.Headers["X-Export-Rows"] = file.RowCount.ToString();
        return File(file.Content, file.ContentType, file.FileName);
    }
}
