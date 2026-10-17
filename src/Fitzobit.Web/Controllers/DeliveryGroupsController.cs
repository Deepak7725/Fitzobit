using Fitzobit.Application.Classification;
using Fitzobit.Application.Common;
using Fitzobit.Application.DeliveryGroups;
using Fitzobit.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitzobit.Web.Controllers;

/// <param name="Count">How many new groups to create (numbers continue after the highest existing one).</param>
public sealed record CreateGroupsRequest(int Count, string? Notes);
public sealed record GroupNotesRequest(string? Notes);
public sealed record MoveCustomersRequest(List<int> CustomerIds, int TargetGroupId, string? Reason);

[ApiController]
[Route("api/delivery-groups")]
[Authorize(Policy = Policies.Operate)]
public sealed class DeliveryGroupsController(DeliveryGroupQueryService queries, DeliveryGroupService groups) : ControllerBase
{
    [HttpGet]
    public Task<DeliveryGroupOverviewDto> List(CancellationToken ct) => queries.GetOverviewAsync(ct);

    [HttpPost]
    public Task<IReadOnlyList<CreatedGroupDto>> Create(CreateGroupsRequest request, CancellationToken ct) =>
        groups.CreateGroupsAsync(request.Count, request.Notes, ct);

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, GroupNotesRequest request, CancellationToken ct)
    {
        await groups.UpdateNotesAsync(id, request.Notes, ct);
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

/// <summary>
/// Data classification: customers on a longitude/latitude plot by delivery group, a deterministic suggestion for a chosen
/// number of groups, evaluation of a manager-edited draft, and saving it (all changes recorded in the assignment history).
/// </summary>
[ApiController]
[Route("api/classification")]
[Authorize(Policy = Policies.Operate)]
public sealed class ClassificationController(ClassificationService classification) : ControllerBase
{
    /// <param name="statuses">Customer status codes, comma separated (default ACTIVE,PAUSED).</param>
    [HttpGet("map")]
    public Task<MapDataDto> Map([FromQuery] string? statuses, CancellationToken ct) => classification.GetMapAsync(Split(statuses), ct);

    [HttpPost("suggest")]
    public Task<SuggestionDto> Suggest(SuggestRequest request, CancellationToken ct) => classification.SuggestAsync(request, ct);

    [HttpPost("evaluate")]
    public Task<DraftEvaluationDto> Evaluate(DraftRequest request, CancellationToken ct) => classification.EvaluateAsync(request, ct);

    [HttpPost("apply")]
    public Task<ApplyResultDto> Apply(DraftRequest request, CancellationToken ct) => classification.ApplyAsync(request, ct);

    private static List<string>? Split(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
