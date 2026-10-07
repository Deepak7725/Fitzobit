using Fitzobit.Application.Common;
using Fitzobit.Application.Imports;
using Fitzobit.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitzobit.Web.Controllers;

public sealed record UpdatePlanMappingRequest(int? PlanId, int? PricingTierId);
public sealed record ExclusionRequest(List<int> RowIds, bool Excluded);
public sealed record DraftGroupRequest(string Name, string? Description);
public sealed record AssignRequest(ImportRowSelection Selection, string? Target);
public sealed record ApplySuggestionsRequest(List<int>? RowIds, bool OnlyUnassigned = true);

[ApiController]
[Route("api/imports")]
[Authorize(Policy = Policies.Operate)]
public sealed class ImportsController(
    ImportBatchQueryService batches,
    ImportUploadService upload,
    ImportQueryService queries,
    ImportWorkflowService workflow,
    ImportCommitService commit,
    ImportTemplateService template) : ControllerBase
{
    private const long MaxRequestBytes = 12 * 1024 * 1024;

    [HttpGet]
    public Task<IReadOnlyList<ImportBatchDto>> List(CancellationToken ct) => batches.ListAsync(null, ct);

    [HttpGet("template")]
    public async Task<IActionResult> Template(CancellationToken ct) =>
        File(await template.CreateAsync(ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ImportTemplateService.FileName);

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<ImportUploadResult>> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null) throw new BusinessRuleException("Choose an Excel file to upload.");
        await using var stream = file.OpenReadStream();
        return await upload.UploadAsync(stream, file.FileName, file.Length, ct);
    }

    [HttpGet("{id:int}")]
    public Task<ImportBatchDetailDto> Detail(int id, CancellationToken ct) => queries.GetDetailAsync(id, ct);

    [HttpGet("{id:int}/rows")]
    public Task<PagedResult<ImportRowDto>> Rows(int id, [FromQuery] ImportRowFilter filter, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default) => queries.GetRowsAsync(id, filter, page, pageSize, ct);

    [HttpGet("{id:int}/classification")]
    public Task<ImportClassificationDto> Classification(int id, CancellationToken ct) => queries.GetClassificationAsync(id, ct);

    [HttpGet("{id:int}/plan-mappings")]
    public Task<IReadOnlyList<PlanMappingDto>> PlanMappings(int id, CancellationToken ct) => queries.GetPlanMappingsAsync(id, ct);

    [HttpPut("{id:int}/plan-mappings/{mappingId:int}")]
    public async Task<IActionResult> UpdatePlanMapping(int id, int mappingId, UpdatePlanMappingRequest request, CancellationToken ct)
    {
        await workflow.UpdatePlanMappingAsync(id, mappingId, request.PlanId, request.PricingTierId, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/rows/exclusion")]
    public async Task<object> SetExclusion(int id, ExclusionRequest request, CancellationToken ct) =>
        new { updated = await workflow.SetExcludedAsync(id, request.RowIds, request.Excluded, ct) };

    [HttpGet("{id:int}/groups")]
    public Task<IReadOnlyList<ImportGroupSummaryDto>> Groups(int id, CancellationToken ct) => queries.GetGroupsAsync(id, ct);

    [HttpPost("{id:int}/groups")]
    public async Task<object> CreateGroup(int id, DraftGroupRequest request, CancellationToken ct)
    {
        var group = await workflow.CreateDraftGroupAsync(id, request.Name, request.Description, ct);
        return new { key = $"d:{group.Id}", group.Name };
    }

    [HttpPut("{id:int}/groups/{draftId:int}")]
    public async Task<IActionResult> UpdateGroup(int id, int draftId, DraftGroupRequest request, CancellationToken ct)
    {
        await workflow.UpdateDraftGroupAsync(id, draftId, request.Name, request.Description, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}/groups/{draftId:int}")]
    public async Task<object> DeleteGroup(int id, int draftId, CancellationToken ct) =>
        new { unassigned = await workflow.DeleteDraftGroupAsync(id, draftId, ct) };

    [HttpPost("{id:int}/assignments/preview")]
    public Task<AssignmentPreviewDto> PreviewAssignment(int id, AssignRequest request, CancellationToken ct) =>
        workflow.PreviewAssignmentAsync(id, request.Selection, request.Target, ct);

    [HttpPost("{id:int}/assignments")]
    public async Task<object> Assign(int id, AssignRequest request, CancellationToken ct) =>
        new { updated = await workflow.AssignAsync(id, request.Selection, request.Target, ct) };

    [HttpPost("{id:int}/suggestions")]
    public Task<SuggestionResultDto> Suggest(int id, CancellationToken ct) => workflow.GenerateSuggestionsAsync(id, ct);

    [HttpPost("{id:int}/suggestions/preview")]
    public Task<AssignmentPreviewDto> PreviewSuggestions(int id, ApplySuggestionsRequest request, CancellationToken ct) =>
        workflow.PreviewSuggestionsAsync(id, request.RowIds, request.OnlyUnassigned, ct);

    [HttpPost("{id:int}/suggestions/apply")]
    public async Task<object> ApplySuggestions(int id, ApplySuggestionsRequest request, CancellationToken ct) =>
        new { updated = await workflow.ApplySuggestionsAsync(id, request.RowIds, request.OnlyUnassigned, ct) };

    [HttpGet("{id:int}/review")]
    public Task<ImportReviewDto> Review(int id, CancellationToken ct) => queries.GetReviewAsync(id, ct);

    [HttpPost("{id:int}/commit")]
    public Task<ImportCommitResultDto> Commit(int id, CancellationToken ct) => commit.CommitAsync(id, ct);

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        await workflow.CancelAsync(id, ct);
        return NoContent();
    }
}
