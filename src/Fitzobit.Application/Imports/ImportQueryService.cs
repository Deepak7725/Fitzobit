using System.Text.Json;
using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Imports;

/// <summary>Read side of the import workflow. Aggregates are computed in the database or over narrow projections.</summary>
public sealed class ImportQueryService(IFitzobitDbContext db)
{
    public const int MaxPageSize = 500;

    private static readonly Dictionary<string, string> IssueLabels = new()
    {
        [ImportIssueCodes.CustomerIdMissing] = "CustomerID is missing",
        [ImportIssueCodes.CustomerIdTooLong] = "CustomerID too long",
        [ImportIssueCodes.CustomerIdDuplicate] = "Duplicate CustomerID",
        [ImportIssueCodes.SampleRow] = "Template sample row",
        [ImportIssueCodes.ExistingCustomer] = "Existing Customer",
        [ImportIssueCodes.NameMissing] = "CustomerName is missing",
        [ImportIssueCodes.NameInvalid] = "Invalid CustomerName",
        [ImportIssueCodes.NameTooLong] = "CustomerName too long",
        [ImportIssueCodes.NameHasDigits] = "Name contains digits",
        [ImportIssueCodes.PhoneMissing] = "PhoneNumber is missing",
        [ImportIssueCodes.PhoneInvalid] = "Invalid phone number",
        [ImportIssueCodes.PhoneScientific] = "Phone in scientific notation",
        [ImportIssueCodes.PhoneLandline] = "Landline number",
        [ImportIssueCodes.PotentialDuplicate] = "Possible duplicate (same phone)",
        [ImportIssueCodes.LocationMissing] = "Location is missing",
        [ImportIssueCodes.PincodeNotDetected] = "Pincode not detected",
        [ImportIssueCodes.PincodeMultiple] = "Several pincodes found",
        [ImportIssueCodes.PlanMissing] = "Plan is missing",
        [ImportIssueCodes.PlanUnknown] = "Unknown Fitzobit plan",
        [ImportIssueCodes.PlanAmbiguous] = "Ambiguous plan",
        [ImportIssueCodes.PlanKeywordMatch] = "Plan matched by keywords",
        [ImportIssueCodes.PlanVariantUnclear] = "Plan variant unclear",
        [ImportIssueCodes.CellFormula] = "Formula cell",
    };

    public static string IssueLabel(string code) => IssueLabels.GetValueOrDefault(code, code);

    public async Task<ImportBatch> GetBatchAsync(int batchId, CancellationToken ct) =>
        await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct)
        ?? throw new NotFoundException($"Import {batchId} was not found.");

    public async Task<ImportBatchDetailDto> GetDetailAsync(int batchId, CancellationToken ct = default)
    {
        var b = await GetBatchAsync(batchId, ct);
        var summary = b.Status is ImportBatchStatus.Failed ? null : await GetSummaryAsync(batchId, ct);
        return new ImportBatchDetailDto(b.Id, b.FileName, b.FileSizeBytes, b.SheetName, b.UploadedAt, b.UploadedBy,
            b.Status.ToString(), b.FailureReason,
            b.FileMessages?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [],
            b.EmptyRowsSkipped, b.ValidationDurationMs, b.SaveDurationMs, b.CompletedAt, b.ImportedCount,
            b.CustomersCreated, b.ExistingCustomersMatched, b.GroupsCreated, b.AssignmentsChanged, summary);
    }

    public async Task<ImportSummaryDto> GetSummaryAsync(int batchId, CancellationToken ct = default)
    {
        var rows = db.ImportRows.AsNoTracking().Where(r => r.ImportBatchId == batchId);
        var included = rows.Included();

        var statusCounts = await rows.GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(ImportRowStatus s) => statusCounts.FirstOrDefault(x => x.Key == s)?.Count ?? 0;

        var duplicateToken = ";" + ImportIssueCodes.CustomerIdDuplicate + ";";
        return new ImportSummaryDto(
            TotalRows: statusCounts.Sum(x => x.Count),
            ValidRows: Count(ImportRowStatus.Valid),
            WarningRows: Count(ImportRowStatus.Warning),
            ErrorRows: Count(ImportRowStatus.Error),
            NewCustomers: await included.CountAsync(r => !r.IsExistingCustomer, ct),
            ExistingCustomers: await rows.CountAsync(r => r.IsExistingCustomer, ct),
            Duplicates: await rows.CountAsync(r => r.IsPotentialDuplicate || r.IssueCodes.Contains(duplicateToken), ct),
            Locations: await rows.Where(r => r.NormalizedLocation != null).Select(r => r.NormalizedLocation).Distinct().CountAsync(ct),
            Pincodes: await rows.Where(r => r.Pincode != null).Select(r => r.Pincode).Distinct().CountAsync(ct),
            Plans: await rows.Where(r => r.PlanId != null).Select(r => r.PlanId).Distinct().CountAsync(ct),
            ToImport: await included.CountAsync(ct),
            ExcludedByManager: await rows.CountAsync(r => r.ExcludedByManager && r.Status != ImportRowStatus.Error, ct),
            // Existing customers with no new choice keep the group they already have; they are not "unassigned".
            Unassigned: await included.CountAsync(r => r.TargetDeliveryGroupId == null && r.TargetDraftGroupId == null
                                                       && !(r.IsExistingCustomer && r.ExistingGroupId != null), ct),
            KeepingCurrentGroup: await included.CountAsync(r => r.TargetDeliveryGroupId == null && r.TargetDraftGroupId == null
                                                                && r.IsExistingCustomer && r.ExistingGroupId != null, ct),
            MissingPincode: await included.CountAsync(r => r.Pincode == null, ct),
            UnresolvedPlanMappings: await db.ImportPlanMappings.CountAsync(m => m.ImportBatchId == batchId && m.Status != PlanMappingStatus.Mapped, ct));
    }

    public async Task<PagedResult<ImportRowDto>> GetRowsAsync(int batchId, ImportRowFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        await GetBatchAsync(batchId, ct);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.ImportRows.AsNoTracking().Where(r => r.ImportBatchId == batchId).ApplyFilter(filter);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(r => r.RowNumber).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var names = await LookupNamesAsync(batchId, rows, ct);
        var items = rows.Select(r => new ImportRowDto(
            r.Id, r.RowNumber, r.CustomerId ?? r.RawCustomerId, r.CustomerName ?? r.RawCustomerName,
            r.RawPhoneNumber, r.NormalizedPhoneNumber, r.RawLocation, r.NormalizedLocation, r.Pincode, r.Locality, r.City,
            r.RawPlan, r.PlanId,
            r.PlanId is { } p ? names.Plans.GetValueOrDefault(p) : null,
            r.PricingTierId is { } t ? names.Tiers.GetValueOrDefault(t) : null,
            r.Status.ToString(), ImportRowIssues.Parse(r.IssuesJson), r.IsExistingCustomer, r.IsPotentialDuplicate,
            r.Status != ImportRowStatus.Error && !r.ExcludedByManager, r.ExcludedByManager,
            names.Ref(r.TargetDeliveryGroupId, r.TargetDraftGroupId),
            r.ExistingGroupId is { } eg ? names.Groups.GetValueOrDefault(eg) : null,
            names.Ref(r.SuggestedDeliveryGroupId, r.SuggestedDraftGroupId),
            r.SuggestionReason)).ToList();

        return new PagedResult<ImportRowDto>(items, total, page, pageSize);
    }

    public async Task<ImportClassificationDto> GetClassificationAsync(int batchId, CancellationToken ct = default)
    {
        await GetBatchAsync(batchId, ct);
        var rows = db.ImportRows.AsNoTracking().Where(r => r.ImportBatchId == batchId).Included();

        var byPlan = await rows.GroupBy(r => r.PlanId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var plans = await db.Plans.AsNoTracking().Select(p => new { p.Id, p.Name, p.Category }).ToDictionaryAsync(p => p.Id, ct);

        return new ImportClassificationDto(
            IncludedRows: await rows.CountAsync(ct),
            NewCustomers: await rows.CountAsync(r => !r.IsExistingCustomer, ct),
            ExistingCustomers: await rows.CountAsync(r => r.IsExistingCustomer, ct),
            WithoutPincode: await rows.CountAsync(r => r.Pincode == null, ct),
            WithoutGroup: await rows.CountAsync(r => r.TargetDeliveryGroupId == null && r.TargetDraftGroupId == null
                                                     && !(r.IsExistingCustomer && r.ExistingGroupId != null), ct),
            PotentialDuplicates: await rows.CountAsync(r => r.IsPotentialDuplicate, ct),
            ByLocation: await TopAsync(rows.Where(r => r.NormalizedLocation != null).GroupBy(r => r.NormalizedLocation!), ct),
            ByLocality: await TopAsync(rows.Where(r => r.Locality != null).GroupBy(r => r.Locality!), ct),
            ByCity: await TopAsync(rows.Where(r => r.City != null).GroupBy(r => r.City!), ct),
            ByPincode: await TopAsync(rows.Where(r => r.Pincode != null).GroupBy(r => r.Pincode!), ct),
            ByPlan: byPlan.OrderByDescending(x => x.Count)
                .Select(x => new CountItemDto(x.Key?.ToString() ?? "none",
                    x.Key is { } id && plans.TryGetValue(id, out var p) ? p.Name : "No plan", x.Count))
                .ToList(),
            ByCategory: byPlan
                .GroupBy(x => x.Key is { } id && plans.TryGetValue(id, out var p) ? p.Category ?? "Uncategorised" : "No plan")
                .Select(g => new CountItemDto(g.Key, g.Key, g.Sum(x => x.Count)))
                .OrderByDescending(x => x.Count)
                .ToList());
    }

    private static async Task<List<CountItemDto>> TopAsync(IQueryable<IGrouping<string, ImportRow>> groups, CancellationToken ct, int take = 100)
    {
        var items = await groups.Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Key).Take(take).ToListAsync(ct);
        return items.Select(x => new CountItemDto(x.Key, x.Key, x.Count)).ToList();
    }

    public async Task<IReadOnlyList<PlanMappingDto>> GetPlanMappingsAsync(int batchId, CancellationToken ct = default)
    {
        await GetBatchAsync(batchId, ct);
        var mappings = await db.ImportPlanMappings.AsNoTracking().Where(m => m.ImportBatchId == batchId).ToListAsync(ct);
        var plans = await db.Plans.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var tierIds = mappings.Where(m => m.PricingTierId != null).Select(m => m.PricingTierId!.Value).ToList();
        var tiers = await db.CataloguePricingTiers.AsNoTracking().Where(t => tierIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.TierName, ct);

        return mappings
            .OrderBy(m => m.Status == PlanMappingStatus.Mapped ? 1 : 0).ThenByDescending(m => m.RowCount)
            .Select(m => new PlanMappingDto(m.Id, m.PlanKey, m.ExampleText, m.RowCount, m.Status.ToString(), m.Method.ToString(),
                m.PlanId, m.PlanId is { } p ? plans.GetValueOrDefault(p) : null,
                m.PricingTierId, m.PricingTierId is { } t ? tiers.GetValueOrDefault(t) : null,
                m.CandidatePlanIdsJson is null ? [] : JsonSerializer.Deserialize<List<int>>(m.CandidatePlanIdsJson) ?? [],
                m.Note))
            .ToList();
    }

    public async Task<IReadOnlyList<PlanOptionDto>> GetPlanOptionsAsync(CancellationToken ct = default)
    {
        var plans = await db.Plans.AsNoTracking()
            .Where(p => p.Status == RecordStatus.Active)
            .Select(p => new
            {
                p.Id, p.Name, p.Code, p.Category,
                Tiers = p.CatalogueItem != null
                    ? p.CatalogueItem.PricingTiers.Select(t => new { t.Id, t.TierName, t.Price, t.BillingPeriod }).ToList()
                    : null
            })
            .ToListAsync(ct);
        return plans.OrderBy(p => p.Category).ThenBy(p => p.Name)
            .Select(p => new PlanOptionDto(p.Id, p.Name, p.Code, p.Category,
                (p.Tiers ?? []).Select(t => new PlanOptionTierDto(t.Id, t.TierName, t.Price, t.BillingPeriod.ToString())).ToList()))
            .ToList();
    }

    /// <summary>Groups available in this import (drafts + active existing groups) with what the import would put in them.</summary>
    public async Task<IReadOnlyList<ImportGroupSummaryDto>> GetGroupsAsync(int batchId, CancellationToken ct = default)
    {
        await GetBatchAsync(batchId, ct);
        var drafts = await db.ImportDraftGroups.AsNoTracking().Where(d => d.ImportBatchId == batchId).OrderBy(d => d.Name).ToListAsync(ct);
        var existing = await db.DeliveryGroups.AsNoTracking().Where(g => g.Status == RecordStatus.Active).OrderBy(g => g.Name).ToListAsync(ct);

        var members = await db.ImportRows.AsNoTracking().Where(r => r.ImportBatchId == batchId).Included()
            .Where(r => r.TargetDeliveryGroupId != null || r.TargetDraftGroupId != null)
            .Select(r => new { r.Id, r.TargetDeliveryGroupId, r.TargetDraftGroupId, r.Pincode, r.NormalizedLocation, r.Locality, r.PlanId, r.IsExistingCustomer, r.ExistingCustomerDbId })
            .ToListAsync(ct);

        var existingCounts = await db.CustomerDeliveryGroupAssignments.AsNoTracking()
            .Where(a => a.IsActive)
            .GroupBy(a => a.DeliveryGroupId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        // Existing customers in this import already count towards their current group; avoid double counting them.
        var movingOut = await db.ImportRows.AsNoTracking().Where(r => r.ImportBatchId == batchId).Included()
            .Where(r => r.IsExistingCustomer && r.ExistingGroupId != null)
            .Select(r => r.ExistingGroupId!.Value)
            .ToListAsync(ct);
        var movingOutCounts = movingOut.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        var planNames = await db.Plans.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var result = new List<ImportGroupSummaryDto>();
        foreach (var d in drafts)
        {
            var m = members.Where(x => x.TargetDraftGroupId == d.Id).ToList();
            result.Add(Build($"d:{d.Id}", d.Name, d.Description, true, "New", 0,
                m.Count, m.Count(x => !x.IsExistingCustomer), m.Select(x => x.Pincode), m.Select(x => x.NormalizedLocation),
                m.Select(x => x.Locality), m.Select(x => x.PlanId), planNames));
        }
        foreach (var g in existing)
        {
            var m = members.Where(x => x.TargetDeliveryGroupId == g.Id).ToList();
            var outside = existingCounts.GetValueOrDefault(g.Id) - movingOutCounts.GetValueOrDefault(g.Id);
            result.Add(Build($"g:{g.Id}", g.Name, g.Description, false, g.Status.ToString(), Math.Max(0, outside),
                m.Count, m.Count(x => !x.IsExistingCustomer), m.Select(x => x.Pincode), m.Select(x => x.NormalizedLocation),
                m.Select(x => x.Locality), m.Select(x => x.PlanId), planNames));
        }
        return result;
    }

    private static ImportGroupSummaryDto Build(string key, string name, string? description, bool isNew, string status, int outside,
        int count, int newCount, IEnumerable<string?> pincodes, IEnumerable<string?> locations, IEnumerable<string?> localities,
        IEnumerable<int?> planIds, IReadOnlyDictionary<int, string> planNames)
    {
        var pins = pincodes.Where(p => p != null).Select(p => p!).Distinct().Order().ToList();
        var locs = localities.Where(l => l != null).Select(l => l!).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        var planList = planIds.Where(p => p != null).Select(p => planNames.GetValueOrDefault(p!.Value, $"Plan #{p}")).Distinct().Order().ToList();
        return new ImportGroupSummaryDto(key, name, description, isNew, status, count, newCount, outside,
            pins.Count, pins.Take(12).ToList(),
            locations.Where(l => l != null).Distinct().Count(), locs.Take(8).ToList(),
            planList.Count, planList.Take(6).ToList());
    }

    public async Task<ImportReviewDto> GetReviewAsync(int batchId, CancellationToken ct = default)
    {
        var batch = await GetBatchAsync(batchId, ct);
        var summary = await GetSummaryAsync(batchId, ct);
        var groups = (await GetGroupsAsync(batchId, ct)).Where(g => g.IsNew || g.CustomerCount > 0).ToList();

        var rows = db.ImportRows.AsNoTracking().Where(r => r.ImportBatchId == batchId);
        var included = rows.Included();

        var includedCodes = await included.Select(r => r.IssueCodes).ToListAsync(ct);
        var errorCodes = await rows.Where(r => r.Status == ImportRowStatus.Error).Select(r => r.IssuesJson).ToListAsync(ct);

        var warnings = CountCodes(includedCodes);
        var errors = errorCodes
            .SelectMany(json => ImportRowIssues.Parse(json).Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Code).Distinct())
            .GroupBy(c => c).Select(g => new ReviewIssueCountDto(g.Key, IssueLabel(g.Key), g.Count()))
            .OrderByDescending(x => x.Count).ToList();

        var unknownToken1 = ";" + ImportIssueCodes.PlanUnknown + ";";
        var unknownToken2 = ";" + ImportIssueCodes.PlanAmbiguous + ";";
        var unknownPlan = await rows.CountAsync(r => r.IssueCodes.Contains(unknownToken1) || r.IssueCodes.Contains(unknownToken2), ct);

        var groupChanges = await included.CountAsync(r => r.IsExistingCustomer &&
            ((r.TargetDeliveryGroupId != null && r.TargetDeliveryGroupId != r.ExistingGroupId) || r.TargetDraftGroupId != null), ct);

        var blockers = new List<string>();
        if (batch.Status != ImportBatchStatus.Validated) blockers.Add($"This import is {batch.Status.ToString().ToLowerInvariant()} and can no longer be saved.");
        if (summary.ToImport == 0) blockers.Add("There are no rows to import.");

        return new ImportReviewDto(summary, groups, summary.Unassigned, unknownPlan, summary.MissingPincode, groupChanges,
            warnings, errors, blockers.Count == 0, blockers);
    }

    private static List<ReviewIssueCountDto> CountCodes(IEnumerable<string> issueCodeStrings) =>
        issueCodeStrings
            .SelectMany(s => s.Split(';', StringSplitOptions.RemoveEmptyEntries).Distinct())
            .GroupBy(c => c)
            .Select(g => new ReviewIssueCountDto(g.Key, IssueLabel(g.Key), g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

    private sealed record NameLookup(
        IReadOnlyDictionary<int, string> Plans,
        IReadOnlyDictionary<int, string> Tiers,
        IReadOnlyDictionary<int, string> Groups,
        IReadOnlyDictionary<int, string> Drafts)
    {
        public GroupRefDto? Ref(int? deliveryGroupId, int? draftGroupId) =>
            deliveryGroupId is { } g ? new GroupRefDto($"g:{g}", Groups.GetValueOrDefault(g, $"Group #{g}"), false)
            : draftGroupId is { } d ? new GroupRefDto($"d:{d}", Drafts.GetValueOrDefault(d, $"Draft #{d}"), true)
            : null;
    }

    private async Task<NameLookup> LookupNamesAsync(int batchId, IReadOnlyCollection<ImportRow> rows, CancellationToken ct)
    {
        var tierIds = rows.Where(r => r.PricingTierId != null).Select(r => r.PricingTierId!.Value).Distinct().ToList();
        return new NameLookup(
            await db.Plans.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct),
            await db.CataloguePricingTiers.AsNoTracking().Where(t => tierIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.TierName, ct),
            await db.DeliveryGroups.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, ct),
            await db.ImportDraftGroups.AsNoTracking().Where(d => d.ImportBatchId == batchId).ToDictionaryAsync(d => d.Id, d => d.Name, ct));
    }
}
