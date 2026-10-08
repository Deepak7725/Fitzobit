using System.Text.Json;
using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Common;
using Fitzobit.Application.Imports;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Customers;

/// <summary>View Data filters. All optional; they combine with AND.</summary>
public sealed class CustomerFilter
{
    /// <summary>Matches CustomerID, name, phone, location, pincode, plan name or delivery group name.</summary>
    public string? Search { get; set; }

    /// <summary>A delivery group id, or "none" for customers without an active group.</summary>
    public string? Group { get; set; }

    public int? PlanId { get; set; }

    /// <summary>Exact pincode.</summary>
    public string? Pincode { get; set; }

    /// <summary>Text contained in the location.</summary>
    public string? Location { get; set; }

    /// <summary>Active | Inactive | Unassigned</summary>
    public string? Status { get; set; }

    public int? ImportBatchId { get; set; }

    /// <summary>customerId | customerName | location | pincode | plan | group | createdAt | updatedAt</summary>
    public string? Sort { get; set; }

    public bool Desc { get; set; }
}

public sealed record CustomerListItemDto(
    int Id,
    string CustomerId,
    string CustomerName,
    string PhoneNumber,
    string? Location,
    string? Pincode,
    int? PlanId,
    string? PlanName,
    int? GroupId,
    string? GroupName,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record AssignmentHistoryDto(DateTime ChangedAt, string? ChangedBy, int? PreviousGroupId, string? PreviousGroupName,
    int? NewGroupId, string? NewGroupName, string? Reason);

public sealed record AuditEntryDto(long Id, DateTime OccurredAt, string Action, string EntityType, string? EntityKey, string Summary,
    string? PerformedBy, JsonElement? OldValues, JsonElement? NewValues, JsonElement? Details);

public sealed record FilterOptionDto(int Id, string Name, bool IsActive);

public sealed record ImportFilterOptionDto(int Id, string FileName, DateTime UploadedAt, int ImportedCount);

public sealed record CustomerFilterOptionsDto(
    IReadOnlyList<FilterOptionDto> Groups,
    IReadOnlyList<FilterOptionDto> Plans,
    IReadOnlyList<ImportFilterOptionDto> Imports,
    IReadOnlyList<int> PageSizes);

public sealed record CustomerImportInfoDto(int Id, string FileName, DateTime UploadedAt, string? UploadedBy);

public sealed record CustomerDetailDto(
    int Id,
    string CustomerId,
    string CustomerName,
    string PhoneNumber,
    string? NormalizedPhoneNumber,
    string? Location,
    string? NormalizedLocation,
    string? Pincode,
    string? Locality,
    string? City,
    int? PlanId,
    string? PlanName,
    string? PlanCategory,
    int? PricingTierId,
    string? TierName,
    string? OriginalPlan,
    string Status,
    int? GroupId,
    string? GroupName,
    DateTime? AssignedAt,
    string? AssignedBy,
    IReadOnlyList<AssignmentHistoryDto> History,
    CustomerImportInfoDto? ImportBatch,
    IReadOnlyList<AuditEntryDto> Audit,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime UpdatedAt,
    string? UpdatedBy);

/// <summary>Flat, composable view of a customer with its plan and current group (server-side filter/sort/page).</summary>
public sealed class CustomerRow
{
    public int Id { get; init; }
    public string CustomerId { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string PhoneNumber { get; init; } = "";
    public string? NormalizedPhoneNumber { get; init; }
    public string? Location { get; init; }
    public string? Pincode { get; init; }
    public int? PlanId { get; init; }
    public string? PlanName { get; init; }
    public int? GroupId { get; init; }
    public string? GroupName { get; init; }
    public CustomerStatus Status { get; init; }
    public int? SourceImportBatchId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed class CustomerQueryService(IFitzobitDbContext db)
{
    public static readonly int[] PageSizes = [10, 25, 50, 100];

    /// <summary>Customers with plan and current group. Each lookup is an indexed per-row subquery; nothing is loaded into memory.</summary>
    public IQueryable<CustomerRow> Rows() =>
        db.Customers.AsNoTracking().Select(c => new CustomerRow
        {
            Id = c.Id,
            CustomerId = c.CustomerId,
            CustomerName = c.CustomerName,
            PhoneNumber = c.PhoneNumber,
            NormalizedPhoneNumber = c.NormalizedPhoneNumber,
            Location = c.Location,
            Pincode = c.Pincode,
            PlanId = c.PlanId,
            PlanName = c.Plan != null ? c.Plan.Name : null,
            GroupId = c.GroupAssignments.Where(a => a.IsActive).Select(a => (int?)a.DeliveryGroupId).FirstOrDefault(),
            GroupName = c.GroupAssignments.Where(a => a.IsActive).Select(a => a.DeliveryGroup!.Name).FirstOrDefault(),
            Status = c.Status,
            SourceImportBatchId = c.SourceImportBatchId,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        });

    public static IQueryable<CustomerRow> ApplyFilter(IQueryable<CustomerRow> rows, CustomerFilter f)
    {
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim().ToLower();
            var digits = new string(s.Where(char.IsDigit).ToArray());
            rows = rows.Where(r =>
                r.CustomerId.ToLower().Contains(s) ||
                r.CustomerName.ToLower().Contains(s) ||
                r.PhoneNumber.Contains(s) ||
                (digits.Length >= 3 && r.NormalizedPhoneNumber != null && r.NormalizedPhoneNumber.Contains(digits)) ||
                (r.Location != null && r.Location.ToLower().Contains(s)) ||
                (r.Pincode != null && r.Pincode.Contains(s)) ||
                (r.PlanName != null && r.PlanName.ToLower().Contains(s)) ||
                (r.GroupName != null && r.GroupName.ToLower().Contains(s)));
        }

        if (f.Group == "none") rows = rows.Where(r => r.GroupId == null);
        else if (int.TryParse(f.Group, out var groupId)) rows = rows.Where(r => r.GroupId == groupId);

        if (f.PlanId is { } planId) rows = rows.Where(r => r.PlanId == planId);
        if (!string.IsNullOrWhiteSpace(f.Pincode)) { var pin = f.Pincode.Trim(); rows = rows.Where(r => r.Pincode == pin); }
        if (!string.IsNullOrWhiteSpace(f.Location))
        {
            var loc = f.Location.Trim().ToLower();
            rows = rows.Where(r => r.Location != null && r.Location.ToLower().Contains(loc));
        }

        rows = f.Status switch
        {
            "Active" => rows.Where(r => r.Status == CustomerStatus.Active),
            "Inactive" => rows.Where(r => r.Status == CustomerStatus.Inactive),
            "Unassigned" => rows.Where(r => r.GroupId == null),
            _ => rows
        };

        if (f.ImportBatchId is { } batchId) rows = rows.Where(r => r.SourceImportBatchId == batchId);
        return rows;
    }

    /// <summary>
    /// Server-side sort. Empty values (no location, pincode, plan or group) always come last in both directions;
    /// SQLite and PostgreSQL would otherwise disagree on where NULLs go.
    /// </summary>
    public static IQueryable<CustomerRow> ApplySort(IQueryable<CustomerRow> rows, string? sort, bool desc)
    {
        IOrderedQueryable<CustomerRow> ordered = (sort ?? "customerId").ToLowerInvariant() switch
        {
            "customername" => desc ? rows.OrderByDescending(r => r.CustomerName) : rows.OrderBy(r => r.CustomerName),
            "location" => EmptyLast(rows, r => r.Location, desc),
            "pincode" => EmptyLast(rows, r => r.Pincode, desc),
            "plan" => EmptyLast(rows, r => r.PlanName, desc),
            "group" => EmptyLast(rows, r => r.GroupName, desc),
            "createdat" => desc ? rows.OrderByDescending(r => r.CreatedAt) : rows.OrderBy(r => r.CreatedAt),
            "updatedat" => desc ? rows.OrderByDescending(r => r.UpdatedAt) : rows.OrderBy(r => r.UpdatedAt),
            _ => desc ? rows.OrderByDescending(r => r.CustomerId) : rows.OrderBy(r => r.CustomerId),
        };
        return ordered.ThenBy(r => r.Id);
    }

    private static IOrderedQueryable<CustomerRow> EmptyLast(IQueryable<CustomerRow> rows,
        System.Linq.Expressions.Expression<Func<CustomerRow, string?>> key, bool desc)
    {
        var isEmpty = System.Linq.Expressions.Expression.Lambda<Func<CustomerRow, bool>>(
            System.Linq.Expressions.Expression.Equal(key.Body, System.Linq.Expressions.Expression.Constant(null, typeof(string))), key.Parameters);
        var emptyLast = rows.OrderBy(isEmpty);
        return desc ? emptyLast.ThenByDescending(key) : emptyLast.ThenBy(key);
    }

    public async Task<PagedResult<CustomerListItemDto>> ListAsync(CustomerFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        if (!PageSizes.Contains(pageSize)) pageSize = 25;
        page = Math.Max(1, page);

        var rows = ApplyFilter(Rows(), filter);
        var total = await rows.CountAsync(ct);
        var items = await ApplySort(rows, filter.Sort, filter.Desc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new CustomerListItemDto(r.Id, r.CustomerId, r.CustomerName, r.PhoneNumber, r.Location, r.Pincode, r.PlanId,
                r.PlanName, r.GroupId, r.GroupName, r.Status.ToString(), r.CreatedAt, r.UpdatedAt))
            .ToListAsync(ct);
        return new PagedResult<CustomerListItemDto>(items, total, page, pageSize);
    }

    /// <summary>Choices for the View Data filter bar: groups, plans and completed imports.</summary>
    public async Task<CustomerFilterOptionsDto> GetFilterOptionsAsync(CancellationToken ct = default)
    {
        var groups = await db.DeliveryGroups.AsNoTracking().OrderBy(g => g.Name)
            .Select(g => new FilterOptionDto(g.Id, g.Name, g.Status == RecordStatus.Active)).ToListAsync(ct);
        var plans = await db.Plans.AsNoTracking().OrderBy(p => p.Name)
            .Select(p => new FilterOptionDto(p.Id, p.Name, p.Status == RecordStatus.Active)).ToListAsync(ct);
        var imports = await db.ImportBatches.AsNoTracking().Where(b => b.Status == ImportBatchStatus.Completed)
            .OrderByDescending(b => b.UploadedAt).ThenByDescending(b => b.Id)
            .Select(b => new ImportFilterOptionDto(b.Id, b.FileName, b.UploadedAt, b.ImportedCount)).ToListAsync(ct);
        return new CustomerFilterOptionsDto(groups, plans, imports, PageSizes);
    }

    public async Task<CustomerDetailDto> GetDetailAsync(int id, CancellationToken ct = default)
    {
        var c = await db.Customers.AsNoTracking()
                    .Include(x => x.Plan).Include(x => x.PricingTier)
                    .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException($"Customer {id} was not found.");

        var current = await db.CustomerDeliveryGroupAssignments.AsNoTracking()
            .Where(a => a.CustomerId == id && a.IsActive)
            .Select(a => new { a.DeliveryGroupId, a.DeliveryGroup!.Name, a.AssignedAt, a.AssignedBy })
            .FirstOrDefaultAsync(ct);

        var history = await db.CustomerDeliveryGroupAssignmentHistory.AsNoTracking()
            .Where(h => h.CustomerId == id)
            .OrderByDescending(h => h.ChangedAt).ThenByDescending(h => h.Id)
            .Select(h => new AssignmentHistoryDto(h.ChangedAt, h.ChangedBy, h.PreviousGroupId,
                h.PreviousGroup != null ? h.PreviousGroup.Name : null, h.NewGroupId, h.NewGroup != null ? h.NewGroup.Name : null, h.Reason))
            .ToListAsync(ct);

        var batch = c.SourceImportBatchId is { } bid
            ? await db.ImportBatches.AsNoTracking().Where(b => b.Id == bid)
                .Select(b => new CustomerImportInfoDto(b.Id, b.FileName, b.UploadedAt, b.UploadedBy)).FirstOrDefaultAsync(ct)
            : null;

        var audit = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(Customer) && a.EntityKey == c.CustomerId)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(50)
            .ToListAsync(ct);

        return new CustomerDetailDto(c.Id, c.CustomerId, c.CustomerName, c.PhoneNumber, c.NormalizedPhoneNumber, c.Location,
            c.NormalizedLocation, c.Pincode, c.Locality, c.City, c.PlanId, c.Plan?.Name, c.Plan?.Category, c.PricingTierId,
            c.PricingTier?.TierName, c.OriginalPlan, c.Status.ToString(), current?.DeliveryGroupId, current?.Name, current?.AssignedAt,
            current?.AssignedBy, history, batch, audit.Select(ToDto).ToList(), c.CreatedAt, c.CreatedBy, c.UpdatedAt, c.UpdatedBy);
    }

    public static AuditEntryDto ToDto(Domain.Auditing.AuditLog a) =>
        new(a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityKey, a.Summary, a.PerformedBy, Json(a.OldValues), Json(a.NewValues), Json(a.Details));

    private static JsonElement? Json(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return JsonDocument.Parse(value).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }
}
