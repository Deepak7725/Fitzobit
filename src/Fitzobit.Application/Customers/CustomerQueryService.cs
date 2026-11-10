using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Text.Json;
using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Deliveries;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Customers;

/// <summary>Customers list filters. All optional; they combine with AND.</summary>
public sealed class CustomerFilter
{
    /// <summary>Matches CustomerID, name, phone, address, pincode, a package's meal type, or a group number ("3", "Group 3", "G3").</summary>
    public string? Search { get; set; }

    /// <summary>A delivery group id, or "none" for customers without an active group.</summary>
    public string? Group { get; set; }

    /// <summary>Customers having at least one package with this preference / meal type / frequency.</summary>
    public int? PreferenceId { get; set; }
    public int? MealTypeId { get; set; }
    public int? FrequencyId { get; set; }

    public int? StatusId { get; set; }

    /// <summary>Exact pincode.</summary>
    public string? Pincode { get; set; }

    /// <summary>Text contained in the address.</summary>
    public string? Address { get; set; }

    /// <summary>Only customers that never had a package, or still miss a preferred delivery time or a pincode.</summary>
    public bool Incomplete { get; set; }

    /// <summary>located (any coordinates) | approximate (pincode area only) | missing (location unavailable)</summary>
    public string? Geo { get; set; }

    /// <summary>customerId | customerName | address | pincode | deliveryTime | group | startDate | endDate | paused | status | latitude | longitude | createdAt | updatedAt</summary>
    public string? Sort { get; set; }

    public bool Desc { get; set; }
}

/// <summary>A package as the customers list shows it.</summary>
public sealed record PackageSummaryDto(int Id, string Preference, string MealType, int Quantity, string Frequency, DateOnly StartDate, DateOnly EndDate)
{
    /// <summary>"Veg Medium ×1 · Monthly (12 Oct 2026 – 10 Nov 2026)"</summary>
    public string Text => CustomerPackageService.Text(Preference, MealType, Quantity, Frequency, StartDate, EndDate);
}

/// <summary>A note as the customers list and exports show it; <see cref="CreatedBy"/> is the writer's display name.</summary>
public sealed record NoteSummaryDto(int Id, string Text, DateTime CreatedAt, string? CreatedBy);

/// <param name="StartDate">The earliest start of the customer's packages.</param>
/// <param name="EndDate">The latest end of the customer's packages.</param>
/// <param name="Notes">All notes, newest first.</param>
/// <param name="PausedDays">Paused days of the current packages (each day once, whichever packages it paused).</param>
/// <param name="OwnPausedDays">Of those, days the customer paused; the rest (<see cref="FestivePausedDays"/>) were paused for everyone.</param>
public sealed record CustomerListItemDto(
    int Id,
    string CustomerId,
    string CustomerName,
    string PhoneNumber,
    string? AlternatePhoneNumber,
    string? Address,
    string? Pincode,
    string? GoogleMapUrl,
    TimeOnly? PreferredDeliveryTime,
    IReadOnlyList<PackageSummaryDto> Packages,
    IReadOnlyList<NoteSummaryDto> Notes,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Status,
    string StatusCode,
    int? GroupId,
    int? GroupNumber,
    decimal? Latitude,
    decimal? Longitude,
    string GeocodeStatus,
    string? GeocodeMatchLevel,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int PausedDays,
    int OwnPausedDays)
{
    public int FestivePausedDays => PausedDays - OwnPausedDays;

    /// <summary>"5 (2 own + 3 for everyone)"</summary>
    public string PausedText => CustomerPauseService.PausedDaysText(OwnPausedDays, FestivePausedDays);
}

public sealed record AssignmentHistoryDto(DateTime ChangedAt, string? ChangedBy, int? PreviousGroupId, int? PreviousGroupNumber,
    int? NewGroupId, int? NewGroupNumber, string? Reason);

public sealed record AuditEntryDto(long Id, DateTime OccurredAt, string Action, string EntityType, string? EntityKey, string Summary,
    string? PerformedBy, JsonElement? OldValues, JsonElement? NewValues, JsonElement? Details);

public sealed record FilterOptionDto(int Id, string Name, bool IsActive);

/// <summary>Choices for the customers filter bar. Drop-down values come from the dictionary tables (inactive ones included, flagged).</summary>
public sealed record CustomerFilterOptionsDto(
    IReadOnlyList<FilterOptionDto> Groups,
    IReadOnlyList<FilterOptionDto> Preferences,
    IReadOnlyList<FilterOptionDto> MealTypes,
    IReadOnlyList<FilterOptionDto> Frequencies,
    IReadOnlyList<FilterOptionDto> Statuses,
    IReadOnlyList<int> PageSizes);

/// <summary>A package in the customer panel.</summary>
/// <param name="DeliveryDaysLeft">Delivery days still to come (today included), paused days not counted; null when the package has ended.</param>
/// <param name="CanDelete">The first delivery day has not come yet.</param>
public sealed record CustomerPackageDto(
    int Id,
    int PreferenceId,
    string Preference,
    int MealTypeId,
    string MealType,
    int Quantity,
    int FrequencyId,
    string Frequency,
    int? FrequencyDeliveryDays,
    bool CanPause,
    DateOnly StartDate,
    DateOnly EndDate,
    int? DeliveryDaysLeft,
    int PausedCount,
    IReadOnlyList<PackagePauseDto> Pauses,
    bool CanDelete,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime UpdatedAt,
    string? UpdatedBy,
    /// <summary>The package price in rupees; only sent to Manager accounts (null for everyone else).</summary>
    decimal? Price = null);

public sealed record CustomerDetailDto(
    int Id,
    string CustomerId,
    string CustomerName,
    string PhoneNumber,
    string? NormalizedPhoneNumber,
    string? AlternatePhoneNumber,
    string? Address,
    string? Pincode,
    string? GoogleMapUrl,
    string? Locality,
    string? City,
    TimeOnly? PreferredDeliveryTime,
    IReadOnlyList<CustomerPackageDto> Packages,
    /// <summary>Finished packages moved aside by the daily 12 pm run, newest first; read-only.</summary>
    IReadOnlyList<CustomerPackageDto> PastPackages,
    IReadOnlyList<CustomerNoteDto> Notes,
    int StatusId,
    string Status,
    string StatusCode,
    decimal? Latitude,
    decimal? Longitude,
    string GeocodeStatus,
    string? GeocodeMatchLevel,
    string? GeocodeMessage,
    string? GeocodeSource,
    DateTime? GeocodedAt,
    DateTime? GeocodeNextAttemptAt,
    string? LegacyPlan,
    int? GroupId,
    int? GroupNumber,
    DateTime? AssignedAt,
    string? AssignedBy,
    IReadOnlyList<AssignmentHistoryDto> History,
    IReadOnlyList<AuditEntryDto> Audit,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime UpdatedAt,
    string? UpdatedBy);

/// <summary>Flat, composable view of a customer with its current group and package dates (server-side filter/sort/page).</summary>
public sealed class CustomerRow
{
    public int Id { get; init; }
    public string CustomerId { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string PhoneNumber { get; init; } = "";
    public string? NormalizedPhoneNumber { get; init; }
    public string? AlternatePhoneNumber { get; init; }
    public string? Address { get; init; }
    public string? Pincode { get; init; }
    public string? GoogleMapUrl { get; init; }
    public TimeOnly? PreferredDeliveryTime { get; init; }
    /// <summary>Current packages (past packages are not counted).</summary>
    public int PackageCount { get; init; }
    /// <summary>The customer has (or had) at least one package, current or past.</summary>
    public bool HasAnyPackage { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    /// <summary>Distinct paused days of the current packages, and how many of them the customer paused (the rest: for everyone).</summary>
    public int PausedDays { get; init; }
    public int OwnPausedDays { get; init; }
    public int StatusId { get; init; }
    public string Status { get; init; } = "";
    public string StatusCode { get; init; } = "";
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public GeocodeStatus GeocodeStatus { get; init; }
    public GeoMatchLevel? GeocodeMatchLevel { get; init; }
    public int? GroupId { get; init; }
    public int? GroupNumber { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed partial class CustomerQueryService(IFitzobitDbContext db, IDeliveryCalendarService calendar, IClock clock, ICurrentUser currentUser)
{
    public static readonly int[] PageSizes = [10, 25, 50, 100];

    /// <summary>Customers with their current group and package dates, as one SQL query; nothing is loaded into memory.</summary>
    public IQueryable<CustomerRow> Rows() =>
        db.Customers.AsNoTracking().Select(c => new CustomerRow
        {
            Id = c.Id,
            CustomerId = c.CustomerId,
            CustomerName = c.CustomerName,
            PhoneNumber = c.PhoneNumber,
            NormalizedPhoneNumber = c.NormalizedPhoneNumber,
            AlternatePhoneNumber = c.AlternatePhoneNumber,
            Address = c.Address,
            Pincode = c.Pincode,
            GoogleMapUrl = c.GoogleMapUrl,
            PreferredDeliveryTime = c.PreferredDeliveryTime,
            PackageCount = c.Packages.Count(p => p.ArchivedAt == null),
            HasAnyPackage = c.Packages.Any(),
            StartDate = c.Packages.Where(p => p.ArchivedAt == null).Min(p => (DateOnly?)p.StartDate),
            EndDate = c.Packages.Where(p => p.ArchivedAt == null).Max(p => (DateOnly?)p.EndDate),
            PausedDays = c.Packages.Where(p => p.ArchivedAt == null).SelectMany(p => p.Pauses).Select(x => x.PausedDate).Distinct().Count(),
            OwnPausedDays = c.Packages.Where(p => p.ArchivedAt == null).SelectMany(p => p.Pauses).Where(x => x.FestivePauseId == null)
                .Select(x => x.PausedDate).Distinct().Count(),
            StatusId = c.StatusId,
            Status = c.Status!.DisplayName,
            StatusCode = c.Status.Code,
            Latitude = c.Latitude,
            Longitude = c.Longitude,
            GeocodeStatus = c.GeocodeStatus,
            GeocodeMatchLevel = c.GeocodeMatchLevel,
            GroupId = c.GroupAssignments.Where(a => a.IsActive).Select(a => (int?)a.DeliveryGroupId).FirstOrDefault(),
            GroupNumber = c.GroupAssignments.Where(a => a.IsActive).Select(a => (int?)a.DeliveryGroup!.GroupNumber).FirstOrDefault(),
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        });

    /// <summary>The customers list filters, applied in SQL. Package filters match customers having any package with that value.</summary>
    public IQueryable<CustomerRow> ApplyFilter(IQueryable<CustomerRow> rows, CustomerFilter f)
    {
        var packages = db.CustomerPackages.AsNoTracking().Where(p => p.ArchivedAt == null);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim().ToLower();
            var digits = new string(s.Where(char.IsDigit).ToArray());
            var groupMatch = GroupSearch().Match(s);
            int? groupNumber = groupMatch.Success && int.TryParse(groupMatch.Groups[1].Value, out var n) ? n : null;
            rows = rows.Where(r =>
                (groupNumber != null && r.GroupNumber == groupNumber) ||
                r.CustomerId.ToLower().Contains(s) ||
                r.CustomerName.ToLower().Contains(s) ||
                r.PhoneNumber.Contains(s) ||
                (digits.Length >= 3 && r.NormalizedPhoneNumber != null && r.NormalizedPhoneNumber.Contains(digits)) ||
                (r.Address != null && r.Address.ToLower().Contains(s)) ||
                (r.Pincode != null && r.Pincode.Contains(s)) ||
                packages.Any(p => p.CustomerId == r.Id && p.MealType!.DisplayName.ToLower().Contains(s)));
        }

        if (f.Group == "none") rows = rows.Where(r => r.GroupId == null);
        else if (int.TryParse(f.Group, out var groupId)) rows = rows.Where(r => r.GroupId == groupId);

        if (f.PreferenceId is { } preferenceId) rows = rows.Where(r => packages.Any(p => p.CustomerId == r.Id && p.PreferenceId == preferenceId));
        if (f.MealTypeId is { } mealTypeId) rows = rows.Where(r => packages.Any(p => p.CustomerId == r.Id && p.MealTypeId == mealTypeId));
        if (f.FrequencyId is { } frequencyId) rows = rows.Where(r => packages.Any(p => p.CustomerId == r.Id && p.FrequencyId == frequencyId));
        if (f.StatusId is { } statusId) rows = rows.Where(r => r.StatusId == statusId);
        if (!string.IsNullOrWhiteSpace(f.Pincode)) { var pin = f.Pincode.Trim(); rows = rows.Where(r => r.Pincode == pin); }
        if (!string.IsNullOrWhiteSpace(f.Address))
        {
            var text = f.Address.Trim().ToLower();
            rows = rows.Where(r => r.Address != null && r.Address.ToLower().Contains(text));
        }
        rows = f.Geo?.ToLowerInvariant() switch
        {
            "located" => rows.Where(r => r.Latitude != null),
            "approximate" => rows.Where(r => r.Latitude != null && r.GeocodeMatchLevel == GeoMatchLevel.Pincode),
            "missing" => rows.Where(r => r.Latitude == null),
            _ => rows
        };
        if (f.Incomplete)
            rows = rows.Where(r => !r.HasAnyPackage || r.PreferredDeliveryTime == null || r.Pincode == null);
        return rows;
    }

    /// <summary>Server-side sort. Empty values (no address, pincode, time, group or package dates) always come last in both directions.</summary>
    public static IQueryable<CustomerRow> ApplySort(IQueryable<CustomerRow> rows, string? sort, bool desc)
    {
        IOrderedQueryable<CustomerRow> ordered = (sort ?? "customerId").ToLowerInvariant() switch
        {
            "customername" => desc ? rows.OrderByDescending(r => r.CustomerName) : rows.OrderBy(r => r.CustomerName),
            "address" => EmptyLast(rows, r => r.Address, desc),
            "pincode" => EmptyLast(rows, r => r.Pincode, desc),
            "deliverytime" => EmptyLast(rows, r => r.PreferredDeliveryTime, desc),
            "group" => EmptyLast(rows, r => r.GroupNumber, desc),
            "latitude" => EmptyLast(rows, r => r.Latitude, desc),
            "longitude" => EmptyLast(rows, r => r.Longitude, desc),
            "startdate" => EmptyLast(rows, r => r.StartDate, desc),
            "enddate" => EmptyLast(rows, r => r.EndDate, desc),
            "paused" => desc ? rows.OrderByDescending(r => r.PausedDays) : rows.OrderBy(r => r.PausedDays),
            "status" => desc ? rows.OrderByDescending(r => r.Status) : rows.OrderBy(r => r.Status),
            "createdat" => desc ? rows.OrderByDescending(r => r.CreatedAt) : rows.OrderBy(r => r.CreatedAt),
            "updatedat" => desc ? rows.OrderByDescending(r => r.UpdatedAt) : rows.OrderBy(r => r.UpdatedAt),
            _ => desc ? rows.OrderByDescending(r => r.CustomerId) : rows.OrderBy(r => r.CustomerId),
        };
        return ordered.ThenBy(r => r.Id);
    }

    private static IOrderedQueryable<CustomerRow> EmptyLast<TKey>(IQueryable<CustomerRow> rows, Expression<Func<CustomerRow, TKey>> key, bool desc)
    {
        var isEmpty = Expression.Lambda<Func<CustomerRow, bool>>(
            Expression.Equal(key.Body, Expression.Constant(null, typeof(TKey))), key.Parameters);
        var emptyLast = rows.OrderBy(isEmpty);
        return desc ? emptyLast.ThenByDescending(key) : emptyLast.ThenBy(key);
    }

    public async Task<PagedResult<CustomerListItemDto>> ListAsync(CustomerFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        if (!PageSizes.Contains(pageSize)) pageSize = 25;
        page = Math.Max(1, page);

        var rows = ApplyFilter(Rows(), filter);
        var total = await rows.CountAsync(ct);
        var pageRows = await ApplySort(rows, filter.Sort, filter.Desc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = pageRows.Select(r => r.Id).ToList();
        var packages = await PackageSummariesAsync(ids, ct);
        var notes = await NoteSummariesAsync(ids, ct);
        var items = pageRows.Select(r => new CustomerListItemDto(r.Id, r.CustomerId, r.CustomerName, r.PhoneNumber, r.AlternatePhoneNumber,
                r.Address, r.Pincode, r.GoogleMapUrl, r.PreferredDeliveryTime, packages.GetValueOrDefault(r.Id) ?? [], notes.GetValueOrDefault(r.Id) ?? [], r.StartDate, r.EndDate, r.Status, r.StatusCode, r.GroupId,
                r.GroupNumber, r.Latitude, r.Longitude, r.GeocodeStatus.ToString(), r.GeocodeMatchLevel?.ToString(), r.CreatedAt, r.UpdatedAt, r.PausedDays,
                r.OwnPausedDays))
            .ToList();
        return new PagedResult<CustomerListItemDto>(items, total, page, pageSize);
    }

    /// <summary>The current packages of the given customers (by customer id), oldest start first. Loaded in batches to keep queries small.</summary>
    public async Task<Dictionary<int, List<PackageSummaryDto>>> PackageSummariesAsync(IReadOnlyList<int> customerIds, CancellationToken ct = default)
    {
        var result = new Dictionary<int, List<PackageSummaryDto>>();
        foreach (var batch in customerIds.Distinct().Chunk(2000))
        {
            var ids = batch.ToList();
            var packages = await db.CustomerPackages.AsNoTracking()
                .Where(p => ids.Contains(p.CustomerId) && p.ArchivedAt == null)
                .OrderBy(p => p.StartDate).ThenBy(p => p.Id)
                .Select(p => new
                {
                    p.CustomerId,
                    Dto = new PackageSummaryDto(p.Id, p.Preference!.DisplayName, p.MealType!.DisplayName, p.Quantity, p.Frequency!.DisplayName,
                        p.StartDate, p.EndDate)
                })
                .ToListAsync(ct);
            foreach (var p in packages)
            {
                if (!result.TryGetValue(p.CustomerId, out var list)) result[p.CustomerId] = list = [];
                list.Add(p.Dto);
            }
        }
        return result;
    }

    /// <summary>The notes of the given customers (by customer id), newest first, with the writers' display names.</summary>
    public async Task<Dictionary<int, List<NoteSummaryDto>>> NoteSummariesAsync(IReadOnlyList<int> customerIds, CancellationToken ct = default)
    {
        var result = new Dictionary<int, List<NoteSummaryDto>>();
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.UserName, u => u.DisplayName, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var batch in customerIds.Distinct().Chunk(2000))
        {
            var ids = batch.ToList();
            var notes = await db.CustomerNotes.AsNoTracking()
                .Where(n => ids.Contains(n.CustomerId))
                .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
                .Select(n => new { n.CustomerId, n.Id, n.Text, n.CreatedAt, n.CreatedBy })
                .ToListAsync(ct);
            foreach (var n in notes)
            {
                if (!result.TryGetValue(n.CustomerId, out var list)) result[n.CustomerId] = list = [];
                list.Add(new NoteSummaryDto(n.Id, n.Text, n.CreatedAt, n.CreatedBy != null ? names.GetValueOrDefault(n.CreatedBy, n.CreatedBy) : null));
            }
        }
        return result;
    }

    public async Task<CustomerFilterOptionsDto> GetFilterOptionsAsync(CancellationToken ct = default)
    {
        var groups = (await db.DeliveryGroups.AsNoTracking().OrderBy(g => g.GroupNumber)
                .Select(g => new { g.Id, g.GroupNumber, Active = g.Status == RecordStatus.Active }).ToListAsync(ct))
            .Select(g => new FilterOptionDto(g.Id, DeliveryGroup.Label(g.GroupNumber), g.Active)).ToList();
        var entries = await db.DictionaryEntries.AsNoTracking().OrderBy(e => e.SortOrder).ThenBy(e => e.DisplayName)
            .Select(e => new { e.DictionaryType, Option = new FilterOptionDto(e.Id, e.DisplayName, e.IsActive) }).ToListAsync(ct);
        List<FilterOptionDto> Of(DictionaryType type) => entries.Where(e => e.DictionaryType == type).Select(e => e.Option).ToList();
        return new CustomerFilterOptionsDto(groups, Of(DictionaryType.Preference), Of(DictionaryType.MealType), Of(DictionaryType.Frequency),
            Of(DictionaryType.CustomerStatus), PageSizes);
    }

    public async Task<CustomerDetailDto> GetDetailAsync(int id, CancellationToken ct = default)
    {
        var c = await db.Customers.AsNoTracking().Include(x => x.Status).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException($"Customer {id} was not found.");

        var current = await db.CustomerDeliveryGroupAssignments.AsNoTracking()
            .Where(a => a.CustomerId == id && a.IsActive)
            .Select(a => new { a.DeliveryGroupId, a.DeliveryGroup!.GroupNumber, a.AssignedAt, a.AssignedBy })
            .FirstOrDefaultAsync(ct);

        var history = await db.CustomerDeliveryGroupAssignmentHistory.AsNoTracking()
            .Where(h => h.CustomerId == id)
            .OrderByDescending(h => h.ChangedAt).ThenByDescending(h => h.Id)
            .Select(h => new AssignmentHistoryDto(h.ChangedAt, h.ChangedBy, h.PreviousGroupId,
                h.PreviousGroup != null ? (int?)h.PreviousGroup.GroupNumber : null, h.NewGroupId, h.NewGroup != null ? (int?)h.NewGroup.GroupNumber : null,
                h.Reason))
            .ToListAsync(ct);

        var today = DeliveryScheduleService.Today(clock);
        var packages = await db.CustomerPackages.AsNoTracking().Where(p => p.CustomerId == id)
            .Include(p => p.Preference).Include(p => p.MealType).Include(p => p.Frequency).Include(p => p.Pauses)
            .OrderBy(p => p.StartDate).ThenBy(p => p.Id)
            .ToListAsync(ct);
        CustomerPackageDto ToPackageDto(CustomerPackage p)
        {
            var pauses = p.Pauses.OrderBy(x => x.PausedDate)
                .Select(x => new PackagePauseDto(x.PausedDate, x.PreviousEndDate, x.NewEndDate, x.ExtendedEndDate, x.FestivePauseId != null, x.Reason, x.CreatedAt,
                    x.CreatedBy)).ToList();
            // Delivery days still to come (today included), not counting days that are paused.
            var from = p.StartDate > today ? p.StartDate : today;
            int? daysLeft = p.EndDate < today ? null
                : calendar.CountDeliveryDays(from, p.EndDate) - pauses.Count(x => x.PausedDate >= from && x.PausedDate <= p.EndDate);
            return new CustomerPackageDto(p.Id, p.PreferenceId, p.Preference!.DisplayName, p.MealTypeId, p.MealType!.DisplayName, p.Quantity,
                p.FrequencyId, p.Frequency!.DisplayName, p.Frequency.NumericValue, p.Frequency.CanPause, p.StartDate, p.EndDate, daysLeft, p.PausedCount,
                pauses, CustomerPackageService.CanDelete(p, today), p.CreatedAt, p.CreatedBy, p.UpdatedAt, p.UpdatedBy,
                Money.MoneyService.CanSeeMoney(currentUser) ? p.Price : null);
        }
        var packageDtos = packages.Where(p => !p.IsPast).Select(ToPackageDto).ToList();
        var pastDtos = packages.Where(p => p.IsPast).OrderByDescending(p => p.EndDate).ThenByDescending(p => p.Id).Select(ToPackageDto).ToList();

        var notes = await db.CustomerNotes.AsNoTracking().Where(n => n.CustomerId == id)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).ToListAsync(ct);
        var writers = notes.Select(n => n.CreatedBy).Where(w => w != null).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => writers.Contains(u.UserName))
            .ToDictionaryAsync(u => u.UserName, u => u.DisplayName, StringComparer.OrdinalIgnoreCase, ct);
        var noteDtos = notes.Select(n => new CustomerNoteDto(n.Id, n.Text, n.CreatedAt, n.CreatedBy,
            n.CreatedBy != null ? names.GetValueOrDefault(n.CreatedBy, n.CreatedBy) : null, n.UpdatedAt, n.UpdatedBy,
            Math.Abs((n.UpdatedAt - n.CreatedAt).TotalSeconds) > 1, CustomerNoteService.CanEdit(n.CreatedBy, currentUser))).ToList();

        var audit = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(Customer) && a.EntityKey == c.CustomerId)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(50)
            .ToListAsync(ct);

        return new CustomerDetailDto(c.Id, c.CustomerId, c.CustomerName, c.PhoneNumber, c.NormalizedPhoneNumber, c.AlternatePhoneNumber, c.Address, c.Pincode, c.GoogleMapUrl, c.Locality,
            c.City, c.PreferredDeliveryTime, packageDtos, pastDtos, noteDtos, c.StatusId, c.Status!.DisplayName, c.Status.Code, c.Latitude, c.Longitude,
            c.GeocodeStatus.ToString(), c.GeocodeMatchLevel?.ToString(), c.GeocodeMessage, c.GeocodeSource, c.GeocodedAt, c.GeocodeNextAttemptAt,
            c.LegacyPlan, current?.DeliveryGroupId, current?.GroupNumber, current?.AssignedAt, current?.AssignedBy, history,
            audit.Select(ToDto).ToList(), c.CreatedAt, c.CreatedBy, c.UpdatedAt, c.UpdatedBy);
    }

    public static AuditEntryDto ToDto(Domain.Auditing.AuditLog a) =>
        new(a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityKey, a.Summary, a.PerformedBy, Json(a.OldValues), Json(a.NewValues), Json(a.Details));

    private static JsonElement? Json(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return JsonDocument.Parse(value).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    /// <summary>"3", "group 3", "grp 3", "g3", "#3".</summary>
    [GeneratedRegex(@"^(?:group|grp|g)?\s*#?\s*(\d{1,6})$")]
    private static partial Regex GroupSearch();
}
