using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Administration;
using Fitzobit.Application.Catalogue;
using Fitzobit.Application.Customers;
using Fitzobit.Application.Imports;
using Fitzobit.Application.Imports.Locations;
using Fitzobit.Application.Messaging;
using Fitzobit.Application.Users;
using Fitzobit.Domain.Users;
using Fitzobit.Infrastructure.Catalogue;
using Fitzobit.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Fitzobit.Web.Controllers;

public sealed record CreateUserRequest(string UserName, string DisplayName, UserRole Role, string Password);
public sealed record UpdateUserRequest(string DisplayName, UserRole Role, bool IsActive);
public sealed record ResetPasswordRequest(string NewPassword);
public sealed record PlanStatusRequest(bool Active);

public sealed record ImportSettingsDto(long MaxFileSizeBytes, int MaxRows, string AcceptedFileType);
public sealed record ExportSettingsDto(int MaxRows, string TimeZone, IReadOnlyList<string> Columns);
public sealed record LocationSettingsDto(IReadOnlyList<string> KnownCities, IReadOnlyDictionary<string, string> CityAliases, IReadOnlyList<string> RegionWords);
public sealed record CatalogueSourceDto(string FileName, bool Exists, bool SyncOnStartup);
public sealed record SecuritySettingsDto(int SessionHours, int PasswordMinLength, int MaxFailedAttempts, int LockoutMinutes);

public sealed record SettingsDto(
    ImportSettingsDto Import,
    ExportSettingsDto Export,
    LocationSettingsDto Locations,
    CatalogueSourceDto Catalogue,
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
    IOptions<ImportOptions> import,
    IOptions<ExportOptions> export,
    IOptions<LocationClassificationOptions> locations,
    IOptions<CatalogueSeedOptions> catalogue,
    IConfiguration configuration,
    WhatsAppStatusService whatsApp) : ControllerBase
{
    [HttpGet]
    public async Task<SettingsDto> Get(CancellationToken ct)
    {
        var cataloguePath = catalogue.Value.ResolveFilePath();
        return new SettingsDto(
            new ImportSettingsDto(import.Value.MaxFileSizeBytes, import.Value.MaxRows, ".xlsx"),
            new ExportSettingsDto(export.Value.MaxRows, export.Value.TimeZone,
                ["CustomerID", "CustomerName", "PhoneNumber", "Location", "Pincode", "Plan", "DeliveryGroup", "Status", "CreatedAt", "UpdatedAt"]),
            new LocationSettingsDto(locations.Value.KnownCities, locations.Value.CityAliases, locations.Value.RegionWords),
            new CatalogueSourceDto(Path.GetFileName(cataloguePath), System.IO.File.Exists(cataloguePath), catalogue.Value.Enabled),
            new SecuritySettingsDto(configuration.GetValue("Auth:SessionHours", 10), PasswordPolicy.MinLength, AuthService.MaxFailedAttempts,
                (int)AuthService.LockoutDuration.TotalMinutes),
            await whatsApp.GetAsync(ct));
    }

    [HttpGet("whatsapp")]
    public Task<WhatsAppStatusDto> WhatsApp(CancellationToken ct) => whatsApp.GetAsync(ct);
}

/// <summary>Administrative catalogue actions: plan availability, remembered plan mappings and re-sync from the workbook.</summary>
[ApiController]
[Route("api/catalogue")]
[Authorize(Policy = Policies.Admin)]
public sealed class CatalogueAdminController(PlanAdminService plans, CatalogueSeeder seeder) : ControllerBase
{
    [HttpPut("plans/{id:int}/status")]
    public async Task<IActionResult> SetPlanStatus(int id, PlanStatusRequest request, CancellationToken ct)
    {
        await plans.SetStatusAsync(id, request.Active, ct);
        return NoContent();
    }

    [HttpGet("plan-aliases")]
    public Task<IReadOnlyList<PlanAliasDto>> PlanAliases(CancellationToken ct) => plans.ListAliasesAsync(ct);

    [HttpDelete("plan-aliases/{id:int}")]
    public async Task<IActionResult> DeletePlanAlias(int id, CancellationToken ct)
    {
        await plans.DeleteAliasAsync(id, ct);
        return NoContent();
    }

    [HttpPost("resync")]
    public Task<CatalogueSeedResult> Resync(CancellationToken ct) => seeder.SeedAsync(ct);
}
