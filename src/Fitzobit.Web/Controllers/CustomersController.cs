using Fitzobit.Application.Customers;
using Fitzobit.Application.DeliveryGroups;
using Fitzobit.Application.Geocoding;
using Fitzobit.Application.Common;
using Fitzobit.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitzobit.Web.Controllers;

/// <param name="GroupId">Target delivery group, or null to remove the customers from their group.</param>
public sealed record ChangeGroupRequest(List<int> CustomerIds, int? GroupId, string? Reason);

/// <param name="Ids">Customers to look up again; empty = every customer whose location is unavailable.</param>
public sealed record LocateRequest(List<int>? Ids);

/// <summary>Customers: server-side search/filter/sort/paging, details, create and edit, packages, notes, pauses, group changes and Excel/PDF export.</summary>
[ApiController]
[Route("api/customers")]
[Authorize(Policy = Policies.Operate)]
public sealed class CustomersController(
    CustomerQueryService queries,
    CustomerCommandService commands,
    CustomerPauseService pauses,
    CustomerPackageService packages,
    CustomerNoteService notes,
    DeliveryScheduleService schedule,
    CustomerExportService export,
    DeliveryGroupService groups,
    DeliveryGroupAssignmentService assignments) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CustomerListItemDto>> List([FromQuery] CustomerFilter filter, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default) => queries.ListAsync(filter, page, pageSize, ct);

    [HttpGet("filter-options")]
    public Task<CustomerFilterOptionsDto> FilterOptions(CancellationToken ct) => queries.GetFilterOptionsAsync(ct);

    [HttpGet("{id:int}")]
    public Task<CustomerDetailDto> Detail(int id, CancellationToken ct) => queries.GetDetailAsync(id, ct);

    [HttpPost]
    public Task<CreateCustomerResult> Create(CreateCustomerRequest request, CancellationToken ct) => commands.CreateAsync(request, ct);

    [HttpPut("{id:int}")]
    public Task<UpdateCustomerResult> Update(int id, UpdateCustomerRequest request, CancellationToken ct) => commands.UpdateAsync(id, request, ct);

    /// <summary>The end date a package would have: the start date plus the frequency's delivery days (Sundays excluded).</summary>
    [HttpGet("schedule")]
    public Task<SchedulePreviewDto> Schedule([FromQuery] int frequencyId, [FromQuery] DateOnly startDate, CancellationToken ct) =>
        schedule.PreviewAsync(frequencyId, startDate, ct);

    /// <summary>Pauses the chosen delivery days of every pausable package running on them; each paused day extends its end date.</summary>
    [HttpPost("{id:int}/pauses")]
    public Task<PauseResultDto> Pause(int id, PauseCustomerRequest request, CancellationToken ct) => pauses.PauseAsync(id, request, ct);

    /// <summary>What pausing these days would do (packages, new end dates, which days extend them), without saving.</summary>
    [HttpPost("{id:int}/pauses/preview")]
    public Task<PauseResultDto> PausePreview(int id, PauseCustomerRequest request, CancellationToken ct) => pauses.PreviewAsync(id, request, ct);

    /// <summary>Packages whose renewal is due (all customers, or one with ?customerId=), with ready-to-send WhatsApp links.</summary>
    [HttpGet("renewals")]
    public Task<RenewalListDto> Renewals([FromQuery] int? customerId, [FromServices] RenewalReminderService renewals, CancellationToken ct) =>
        renewals.ListDueAsync(customerId, ct);

    /// <summary>Records that a renewal reminder was sent for the package ("Send reminder" pressed).</summary>
    [HttpPost("packages/{packageId:int}/renewal-reminder")]
    public Task<ReminderRecordedDto> RenewalReminder(int packageId, [FromServices] RenewalReminderService renewals, CancellationToken ct) =>
        renewals.RecordReminderAsync(packageId, ct);

    [HttpPost("{id:int}/packages")]
    public Task<PackageSavedDto> AddPackage(int id, SavePackageRequest request, CancellationToken ct) => packages.AddAsync(id, request, ct);

    [HttpPut("packages/{packageId:int}")]
    public Task<PackageSavedDto> UpdatePackage(int packageId, UpdatePackageRequest request, CancellationToken ct) =>
        packages.UpdateAsync(packageId, request, ct);

    /// <summary>Only before the package's first delivery day.</summary>
    [HttpDelete("packages/{packageId:int}")]
    public async Task<IActionResult> DeletePackage(int packageId, CancellationToken ct)
    {
        await packages.DeleteAsync(packageId, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/notes")]
    public async Task<object> AddNote(int id, SaveNoteRequest request, CancellationToken ct) => new { Id = await notes.AddAsync(id, request, ct) };

    /// <summary>The writer of the note or an Admin.</summary>
    [HttpPut("notes/{noteId:int}")]
    public async Task<IActionResult> UpdateNote(int noteId, SaveNoteRequest request, CancellationToken ct)
    {
        await notes.UpdateAsync(noteId, request, ct);
        return NoContent();
    }

    [HttpDelete("notes/{noteId:int}")]
    public async Task<IActionResult> DeleteNote(int noteId, CancellationToken ct)
    {
        await notes.DeleteAsync(noteId, ct);
        return NoContent();
    }

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

    /// <summary>Queues a new location lookup (the background geocoder handles it within seconds).</summary>
    [HttpPost("locate")]
    public async Task<object> Locate(LocateRequest request, [FromServices] CustomerGeocodingService geocoding, CancellationToken ct) =>
        new { Queued = await geocoding.RequestAsync(request.Ids, ct) };

    /// <summary>Excel or PDF with exactly the requested rows (all / filtered / selected / current page) and columns.</summary>
    [HttpPost("export")]
    public async Task<IActionResult> Export(CustomerExportRequest request, CancellationToken ct)
    {
        var file = await export.ExportAsync(request, ct);
        Response.Headers["X-Export-Rows"] = file.RowCount.ToString();
        return File(file.Content, file.ContentType, file.FileName);
    }
}
