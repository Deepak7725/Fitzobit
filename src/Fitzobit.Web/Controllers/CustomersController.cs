using Fitzobit.Application.Customers;
using Fitzobit.Application.DeliveryGroups;
using Fitzobit.Application.Imports;
using Fitzobit.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitzobit.Web.Controllers;

/// <param name="GroupId">Target delivery group, or null to remove the customers from their group.</param>
public sealed record ChangeGroupRequest(List<int> CustomerIds, int? GroupId, string? Reason);

/// <param name="Filter">The View Data filters and sort (used for "Filtered", and for row order in every scope).</param>
/// <param name="Ids">Customer ids for "Selected".</param>
public sealed record ExportRequest(ExportScope Scope, CustomerFilter? Filter, List<int>? Ids);

/// <summary>View Data: server-side search/filter/sort/paging, customer details, edits, group changes and Excel export.</summary>
[ApiController]
[Route("api/customers")]
[Authorize(Policy = Policies.Operate)]
public sealed class CustomersController(
    CustomerQueryService queries,
    CustomerCommandService commands,
    CustomerExportService export,
    DeliveryGroupService groups,
    DeliveryGroupAssignmentService assignments) : ControllerBase
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet]
    public Task<PagedResult<CustomerListItemDto>> List([FromQuery] CustomerFilter filter, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default) => queries.ListAsync(filter, page, pageSize, ct);

    [HttpGet("filter-options")]
    public Task<CustomerFilterOptionsDto> FilterOptions(CancellationToken ct) => queries.GetFilterOptionsAsync(ct);

    [HttpGet("{id:int}")]
    public Task<CustomerDetailDto> Detail(int id, CancellationToken ct) => queries.GetDetailAsync(id, ct);

    [HttpPut("{id:int}")]
    public Task<UpdateCustomerResult> Update(int id, UpdateCustomerRequest request, CancellationToken ct) => commands.UpdateAsync(id, request, ct);

    [HttpPost("group/preview")]
    public Task<MovePreviewDto> PreviewGroupChange(ChangeGroupRequest request, CancellationToken ct) =>
        request.GroupId is { } groupId
            ? groups.PreviewMoveAsync(request.CustomerIds ?? [], groupId, ct)
            : groups.PreviewUnassignAsync(request.CustomerIds ?? [], ct);

    [HttpPost("group")]
    public Task<BulkAssignmentResult> ChangeGroup(ChangeGroupRequest request, CancellationToken ct) =>
        request.GroupId is { } groupId
            ? groups.MoveAsync(request.CustomerIds ?? [], groupId, request.Reason, ct)
            : assignments.UnassignManyAsync(request.CustomerIds ?? [],
                string.IsNullOrWhiteSpace(request.Reason) ? "Removed from group by manager" : request.Reason.Trim(), ct);

    [HttpPost("export")]
    public async Task<IActionResult> Export(ExportRequest request, CancellationToken ct)
    {
        var file = await export.ExportAsync(request.Scope, request.Filter ?? new CustomerFilter(), request.Ids, ct);
        Response.Headers["X-Export-Rows"] = file.RowCount.ToString();
        return File(file.Content, ExcelContentType, file.FileName);
    }
}
