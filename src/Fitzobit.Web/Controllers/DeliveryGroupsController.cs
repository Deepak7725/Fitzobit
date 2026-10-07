using Fitzobit.Application.DeliveryGroups;
using Fitzobit.Application.Imports;
using Fitzobit.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitzobit.Web.Controllers;

public sealed record GroupRequest(string Name, string? Description);
public sealed record MoveCustomersRequest(List<int> CustomerIds, int TargetGroupId, string? Reason);

[ApiController]
[Route("api/delivery-groups")]
[Authorize(Policy = Policies.Operate)]
public sealed class DeliveryGroupsController(DeliveryGroupQueryService queries, DeliveryGroupService groups) : ControllerBase
{
    [HttpGet]
    public Task<DeliveryGroupOverviewDto> List(CancellationToken ct) => queries.GetOverviewAsync(ct);

    [HttpPost]
    public async Task<object> Create(GroupRequest request, CancellationToken ct)
    {
        var group = await groups.CreateAsync(request.Name, request.Description, ct);
        return new { group.Id, group.Name };
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, GroupRequest request, CancellationToken ct)
    {
        await groups.UpdateAsync(id, request.Name, request.Description, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        await groups.SetActiveAsync(id, false, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/activate")]
    public async Task<IActionResult> Activate(int id, CancellationToken ct)
    {
        await groups.SetActiveAsync(id, true, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await groups.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <param name="group">A group id, or "unassigned".</param>
    [HttpGet("{group}/customers")]
    public Task<PagedResult<GroupCustomerDto>> Customers(string group, [FromQuery] string? search, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, CancellationToken ct = default) => groups.GetCustomersAsync(group, search, page, pageSize, ct);

    [HttpPost("move/preview")]
    public Task<MovePreviewDto> PreviewMove(MoveCustomersRequest request, CancellationToken ct) =>
        groups.PreviewMoveAsync(request.CustomerIds, request.TargetGroupId, ct);

    [HttpPost("move")]
    public Task<BulkAssignmentResult> Move(MoveCustomersRequest request, CancellationToken ct) =>
        groups.MoveAsync(request.CustomerIds, request.TargetGroupId, request.Reason, ct);
}
