using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Administration;
using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Application.Dictionaries;
using Fitzobit.Application.Geocoding;
using Fitzobit.Application.Messaging;
using Fitzobit.Application.Users;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Domain.Users;
using Fitzobit.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Fitzobit.Web.Controllers;

public sealed record CreateUserRequest(string UserName, string DisplayName, UserRole Role, string Password);
public sealed record UpdateUserRequest(string DisplayName, UserRole Role, bool IsActive);
public sealed record ResetPasswordRequest(string NewPassword);
public sealed record ReorderRequest(List<int> Ids);

public sealed record ExportSettingsDto(int MaxRows, int PdfMaxRows, string TimeZone, IReadOnlyList<string> Columns);
public sealed record LocationSettingsDto(IReadOnlyList<string> KnownCities, IReadOnlyDictionary<string, string> CityAliases, IReadOnlyList<string> RegionWords);
public sealed record SecuritySettingsDto(int SessionHours, int PasswordMinLength, int MaxFailedAttempts, int LockoutMinutes);

public sealed record SettingsDto(
    ExportSettingsDto Export,
    LocationSettingsDto Locations,
    SecuritySettingsDto Security,
    WhatsAppStatusDto WhatsApp);

[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.Admin)]
public sealed class UsersController(UserService users, IClock clock) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<UserDto>> List(CancellationToken ct) => users.ListAsync(clock.UtcNow, ct);

    [HttpPost]
    public Task<UserDto> Create(CreateUserRequest request, CancellationToken ct) =>
        users.CreateAsync(request.UserName, request.DisplayName, request.Role, request.Password, ct);

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest request, CancellationToken ct)
    {
        await users.UpdateAsync(id, request.DisplayName, request.Role, request.IsActive, User.UserId()!.Value, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest request, CancellationToken ct)
    {
        await users.ResetPasswordAsync(id, request.NewPassword, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/audit")]
[Authorize(Policy = Policies.Admin)]
public sealed class AuditController(AuditQueryService audit) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AuditEntryDto>> List([FromQuery] string? entityType, [FromQuery] string? action, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        audit.ListAsync(entityType, action, search, page, pageSize, ct);

    [HttpGet("actions")]
    public Task<IReadOnlyList<string>> Actions(CancellationToken ct) => audit.ActionsAsync(ct);
}

/// <summary>Read-only configuration summary for the Settings screen. Never includes secrets, tokens or connection strings.</summary>
[ApiController]
[Route("api/settings")]
[Authorize(Policy = Policies.Operate)]
public sealed class SettingsController(
    IOptions<ExportOptions> export,
    IOptions<LocationClassificationOptions> locations,
    IConfiguration configuration,
    WhatsAppStatusService whatsApp) : ControllerBase
{
    [HttpGet]
    public async Task<SettingsDto> Get(CancellationToken ct)
    {
        return new SettingsDto(
            new ExportSettingsDto(export.Value.MaxRows, export.Value.PdfMaxRows, export.Value.TimeZone, CustomerExportColumns.All.Select(c => c.Header).ToList()),
            new LocationSettingsDto(locations.Value.KnownCities, locations.Value.CityAliases, locations.Value.RegionWords),
            new SecuritySettingsDto(configuration.GetValue("Auth:SessionHours", 10), PasswordPolicy.MinLength, AuthService.MaxFailedAttempts,
                (int)AuthService.LockoutDuration.TotalMinutes),
            await whatsApp.GetAsync(ct));
    }

    [HttpGet("whatsapp")]
    public Task<WhatsAppStatusDto> WhatsApp(CancellationToken ct) => whatsApp.GetAsync(ct);

    /// <summary>The city Fitzobit delivers in (used to look up customer locations).</summary>
    [HttpGet("delivery-area")]
    public Task<ServiceAreaDto> DeliveryArea([FromServices] ServiceAreaService area, CancellationToken ct) => area.GetAsync(ct);

    [HttpPut("delivery-area")]
    [Authorize(Policy = Policies.Admin)]
    public Task<ServiceAreaDto> SaveDeliveryArea(SaveServiceAreaRequest request, [FromServices] ServiceAreaService area, CancellationToken ct) =>
        area.SaveAsync(request, ct);

    /// <summary>Renewal reminders: days before the end and the WhatsApp message text.</summary>
    [HttpGet("renewal")]
    public Task<RenewalSettingsDto> Renewal([FromServices] RenewalReminderService renewals, CancellationToken ct) => renewals.GetSettingsAsync(ct);

    [HttpPut("renewal")]
    [Authorize(Policy = Policies.Admin)]
    public Task<RenewalSettingsDto> SaveRenewal(SaveRenewalSettingsRequest request, [FromServices] RenewalReminderService renewals, CancellationToken ct) =>
        renewals.SaveSettingsAsync(request, ct);
}

/// <summary>
/// Customer drop-down values. Everyone who works with customers reads the active values; only administrators change them.
/// Types: Preference, MealType, Frequency, CustomerStatus.
/// </summary>
[ApiController]
[Route("api/dictionaries")]
[Authorize(Policy = Policies.Operate)]
public sealed class DictionariesController(DictionaryService dictionaries) : ControllerBase
{
    /// <summary>Active values of every drop-down, in display order (used by the Add/Edit customer screens).</summary>
    [HttpGet]
    public Task<DictionaryOptionsDto> Active(CancellationToken ct) => dictionaries.GetActiveOptionsAsync(ct);

    [HttpGet("{type}")]
    [Authorize(Policy = Policies.Admin)]
    public Task<IReadOnlyList<DictionaryEntryDto>> List(DictionaryType type, CancellationToken ct) => dictionaries.ListAsync(type, ct);

    [HttpPost("{type}")]
    [Authorize(Policy = Policies.Admin)]
    public Task<DictionaryEntryDto> Create(DictionaryType type, SaveDictionaryEntryRequest request, CancellationToken ct) =>
        dictionaries.CreateAsync(type, request, ct);

    [HttpPut("{type}/order")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Reorder(DictionaryType type, ReorderRequest request, CancellationToken ct)
    {
        await dictionaries.ReorderAsync(type, request.Ids ?? [], ct);
        return NoContent();
    }

    [HttpPut("entries/{id:int}")]
    [Authorize(Policy = Policies.Admin)]
    public Task<DictionaryEntryDto> Update(int id, SaveDictionaryEntryRequest request, CancellationToken ct) => dictionaries.UpdateAsync(id, request, ct);

    [HttpDelete("entries/{id:int}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await dictionaries.DeleteAsync(id, ct);
        return NoContent();
    }
}
