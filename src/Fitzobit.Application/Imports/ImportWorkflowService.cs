using System.Text.Json;
using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Common;
using Fitzobit.Application.Imports.Plans;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Imports;

/// <summary>
/// Steps 3–5 of the import: plan mapping, row inclusion, draft groups, assignment and suggestions.
/// Everything here edits the staged batch only; real customers/groups are written by <see cref="ImportCommitService"/>.
/// </summary>
public sealed class ImportWorkflowService(IFitzobitDbContext db, PlanCatalogue planCatalogue, IClock clock)
{
    public const int MaxGroupNameLength = 150;

    public const string RuleExisting = "Existing assignment";
    public const string RulePincode = "Same pincode";
    public const string RuleLocation = "Same location";
    public const string RuleArea = "Same area";
    public const string RuleCity = "Same city";

    // ---------- Plan mapping ----------

    /// <summary>
    /// Sets the plan (and optional tier) for one distinct plan text; every row using that text is re-checked.
    /// Passing no plan resets the mapping to the automatic result.
    /// </summary>
    public async Task UpdatePlanMappingAsync(int batchId, int mappingId, int? planId, int? pricingTierId, CancellationToken ct = default)
    {
        await RequireDraftAsync(batchId, ct);
        var mapping = await db.ImportPlanMappings.FirstOrDefaultAsync(m => m.Id == mappingId && m.ImportBatchId == batchId, ct)
                      ?? throw new NotFoundException($"Plan mapping {mappingId} was not found in import {batchId}.");

        if (planId is { } pid)
        {
            var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid && p.Status == RecordStatus.Active, ct)
                       ?? throw new BusinessRuleException($"Plan {pid} does not exist or is inactive.");
            if (pricingTierId is { } tid && !await db.CataloguePricingTiers.AnyAsync(t => t.Id == tid && t.CatalogueItemId == plan.CatalogueItemId, ct))
                throw new BusinessRuleException("The selected variant does not belong to the selected plan.");

            mapping.PlanId = pid;
            mapping.PricingTierId = pricingTierId;
            mapping.Method = PlanMatchMethod.Manual;
            mapping.Status = PlanMappingStatus.Mapped;
            mapping.Note = null;
        }
        else
        {
            var match = (await planCatalogue.CreateMatcherAsync(ct)).Match(mapping.ExampleText);
            mapping.PlanId = match.PlanId;
            mapping.PricingTierId = match.PricingTierId;
            mapping.Method = match.Method;
            mapping.Status = match.Status;
            mapping.Note = match.Note;
            mapping.CandidatePlanIdsJson = match.CandidatePlanIds.Count == 0 ? null : JsonSerializer.Serialize(match.CandidatePlanIds);
        }

        var planNames = await db.Plans.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var rows = await db.ImportRows.Where(r => r.ImportBatchId == batchId && r.PlanKey == mapping.PlanKey).ToListAsync(ct);
        foreach (var row in rows)
        {
            var mapped = mapping.Status == PlanMappingStatus.Mapped;
            row.PlanId = mapped ? mapping.PlanId : null;
            row.PricingTierId = mapped ? mapping.PricingTierId : null;
            ImportRowIssues.ReplacePlanIssues(row, PlanIssues.For(row.RawPlan, mapping, id => planNames.GetValueOrDefault(id, $"Plan #{id}")));
        }

        await db.SaveChangesAsync(ct);
        await RefreshCountsAsync(batchId, ct);
    }

    // ---------- Row inclusion ----------

    public async Task<int> SetExcludedAsync(int batchId, IReadOnlyCollection<int> rowIds, bool excluded, CancellationToken ct = default)
    {
        await RequireDraftAsync(batchId, ct);
        if (rowIds.Count == 0) return 0;
        return await db.ImportRows
            .Where(r => r.ImportBatchId == batchId && rowIds.Contains(r.Id) && r.Status != ImportRowStatus.Error)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExcludedByManager, excluded), ct);
    }

    // ---------- Draft groups ----------

    public async Task<ImportDraftGroup> CreateDraftGroupAsync(int batchId, string name, string? description, CancellationToken ct = default)
    {
        await RequireDraftAsync(batchId, ct);
        var clean = await ValidateGroupNameAsync(batchId, name, null, ct);
        var group = new ImportDraftGroup
        {
            ImportBatchId = batchId,
            Name = clean,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedAt = clock.UtcNow
        };
        db.ImportDraftGroups.Add(group);
        await db.SaveChangesAsync(ct);
        return group;
    }

    public async Task UpdateDraftGroupAsync(int batchId, int draftId, string name, string? description, CancellationToken ct = default)
    {
        await RequireDraftAsync(batchId, ct);
        var group = await db.ImportDraftGroups.FirstOrDefaultAsync(d => d.Id == draftId && d.ImportBatchId == batchId, ct)
                    ?? throw new NotFoundException($"Group {draftId} was not found in import {batchId}.");
        group.Name = await ValidateGroupNameAsync(batchId, name, draftId, ct);
        group.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes a draft group; its customers become unassigned.</summary>
    public async Task<int> DeleteDraftGroupAsync(int batchId, int draftId, CancellationToken ct = default)
    {
        await RequireDraftAsync(batchId, ct);
        var group = await db.ImportDraftGroups.FirstOrDefaultAsync(d => d.Id == draftId && d.ImportBatchId == batchId, ct)
                    ?? throw new NotFoundException($"Group {draftId} was not found in import {batchId}.");

        var released = await db.ImportRows.Where(r => r.ImportBatchId == batchId && r.TargetDraftGroupId == draftId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.TargetDraftGroupId, (int?)null), ct);
        await db.ImportRows.Where(r => r.ImportBatchId == batchId && r.SuggestedDraftGroupId == draftId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.SuggestedDraftGroupId, (int?)null).SetProperty(r => r.SuggestionReason, (string?)null), ct);

        db.ImportDraftGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        return released;
    }

    private async Task<string> ValidateGroupNameAsync(int batchId, string name, int? exceptDraftId, CancellationToken ct)
    {
        var clean = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) throw new BusinessRuleException("Group name is required.");
        if (clean.Length > MaxGroupNameLength) throw new BusinessRuleException($"Group name must be at most {MaxGroupNameLength} characters.");

        var lower = clean.ToLower();
        if (await db.DeliveryGroups.AnyAsync(g => g.Name.ToLower() == lower, ct))
            throw new BusinessRuleException($"A delivery group named \"{clean}\" already exists. Assign customers to it instead.");
        if (await db.ImportDraftGroups.AnyAsync(d => d.ImportBatchId == batchId && d.Id != exceptDraftId && d.Name.ToLower() == lower, ct))
            throw new BusinessRuleException($"This import already has a group named \"{clean}\".");
        return clean;
    }

    // ---------- Assignment ----------

    public async Task<AssignmentPreviewDto> PreviewAssignmentAsync(int batchId, ImportRowSelection selection, string? targetKey, CancellationToken ct = default)
    {
        await RequireDraftAsync(batchId, ct);
        var target = await ResolveTargetAsync(batchId, targetKey, ct);
        var targetName = target is null ? null : await GroupNameAsync(target, ct);

        var current = await db.ImportRows.AsNoTracking().Where(r => r.ImportBatchId == batchId).Select(selection)
            .GroupBy(r => new { r.TargetDeliveryGroupId, r.TargetDraftGroupId })
            .Select(g => new { g.Key.TargetDeliveryGroupId, g.Key.TargetDraftGroupId, Count = g.Count() })
            .ToListAsync(ct);

        var selected = current.Sum(c => c.Count);
        var same = current.Where(c => c.TargetDeliveryGroupId == target?.DeliveryGroupId && c.TargetDraftGroupId == target?.DraftGroupId).Sum(c => c.Count);
        var moves = new List<AssignmentMoveDto>();
        foreach (var c in current.Where(c => !(c.TargetDeliveryGroupId == target?.DeliveryGroupId && c.TargetDraftGroupId == target?.DraftGroupId)))
        {
            var key = GroupKey.Format(c.TargetDeliveryGroupId, c.TargetDraftGroupId);
            var name = key is null ? "No group" : await GroupNameAsync(new GroupKey(c.TargetDeliveryGroupId, c.TargetDraftGroupId), ct);
            moves.Add(new AssignmentMoveDto(key, name, c.Count));
        }

        var messages = moves.OrderByDescending(m => m.Count).Select(m => (m.FromKey, target) switch
        {
            (null, null) => "",
            (null, _) => $"{Customers(m.Count)} will be assigned to {targetName}.",
            (_, null) => $"{Customers(m.Count)} will be removed from {m.FromName}.",
            _ => $"{Customers(m.Count)} will be moved from {m.FromName} to {targetName}."
        }).Where(s => s.Length > 0).ToList();
        if (same > 0) messages.Add($"{Customers(same)} already in {targetName ?? "no group"} (no change).");

        return new AssignmentPreviewDto(selected, selected - same, same, targetName, moves, messages);
    }

    /// <summary>Assigns the selected rows to a group (or removes their group when <paramref name="targetKey"/> is null).</summary>
    public async Task<int> AssignAsync(int batchId, ImportRowSelection selection, string? targetKey, CancellationToken ct = default)
    {
        await RequireDraftAsync(batchId, ct);
        var target = await ResolveTargetAsync(batchId, targetKey, ct);
        var rows = db.ImportRows.Where(r => r.ImportBatchId == batchId).Select(selection);

        if (target is null)
            return await rows.Where(r => r.TargetDeliveryGroupId != null || r.TargetDraftGroupId != null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.TargetDeliveryGroupId, (int?)null).SetProperty(r => r.TargetDraftGroupId, (int?)null), ct);

        return await rows.Where(r => r.TargetDeliveryGroupId != target.DeliveryGroupId || r.TargetDraftGroupId != target.DraftGroupId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.TargetDeliveryGroupId, target.DeliveryGroupId)
                .SetProperty(r => r.TargetDraftGroupId, target.DraftGroupId), ct);
    }

    // ---------- Suggestions ----------

    /// <summary>
    /// Proposes a group per row using deterministic rules, in order: existing assignment → same pincode → same location
    /// → same area → same city. A rule only fires when its evidence points to exactly one group. Suggestions are stored
    /// for review and never change an assignment by themselves.
    /// </summary>
    public async Task<SuggestionResultDto> GenerateSuggestionsAsync(int batchId, CancellationToken ct = default)
    {
        await RequireDraftAsync(batchId, ct);
        var batchRows = db.ImportRows.Where(r => r.ImportBatchId == batchId);
        await batchRows.ExecuteUpdateAsync(s => s
            .SetProperty(r => r.SuggestedDeliveryGroupId, (int?)null)
            .SetProperty(r => r.SuggestedDraftGroupId, (int?)null)
            .SetProperty(r => r.SuggestionReason, (string?)null), ct);

        var activeGroups = (await db.DeliveryGroups.AsNoTracking().Where(g => g.Status == RecordStatus.Active).Select(g => g.Id).ToListAsync(ct)).ToHashSet();
        var rows = await batchRows.AsNoTracking().Included()
            .Select(r => new { r.Id, r.ExistingGroupId, r.Pincode, r.NormalizedLocation, r.Locality, r.City, r.TargetDeliveryGroupId, r.TargetDraftGroupId })
            .ToListAsync(ct);

        var evidence = new Evidence();
        var saved = await db.CustomerDeliveryGroupAssignments.AsNoTracking()
            .Where(a => a.IsActive && a.DeliveryGroup!.Status == RecordStatus.Active)
            .Select(a => new { a.DeliveryGroupId, a.Customer!.Pincode, a.Customer.NormalizedLocation, a.Customer.Locality, a.Customer.City })
            .ToListAsync(ct);
        foreach (var s in saved)
            evidence.Add((s.DeliveryGroupId, null), s.Pincode, s.NormalizedLocation, s.Locality, s.City);
        foreach (var r in rows.Where(r => r.TargetDeliveryGroupId != null || r.TargetDraftGroupId != null))
            evidence.Add((r.TargetDeliveryGroupId, r.TargetDraftGroupId), r.Pincode, r.NormalizedLocation, r.Locality, r.City);

        var suggestions = new List<(int RowId, int? Group, int? Draft, string Rule, bool Differs)>();
        foreach (var r in rows)
        {
            (int? G, int? D)? pick = null;
            string? rule = null;
            if (r.ExistingGroupId is { } eg && activeGroups.Contains(eg)) { pick = (eg, null); rule = RuleExisting; }
            else if (evidence.Unique(evidence.ByPincode, r.Pincode) is { } p) { pick = p; rule = RulePincode; }
            else if (evidence.Unique(evidence.ByLocation, r.NormalizedLocation) is { } l) { pick = l; rule = RuleLocation; }
            else if (evidence.Unique(evidence.ByArea, Evidence.AreaKey(r.Locality, r.City)) is { } a) { pick = a; rule = RuleArea; }
            else if (evidence.Unique(evidence.ByCity, r.City) is { } c) { pick = c; rule = RuleCity; }

            if (pick is { } chosen)
                suggestions.Add((r.Id, chosen.G, chosen.D, rule!, chosen.G != r.TargetDeliveryGroupId || chosen.D != r.TargetDraftGroupId));
        }

        foreach (var group in suggestions.GroupBy(s => (s.Group, s.Draft, s.Rule)))
            foreach (var chunk in group.Select(s => s.RowId).Chunk(500))
            {
                var ids = chunk.ToList();
                await batchRows.Where(r => ids.Contains(r.Id)).ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.SuggestedDeliveryGroupId, group.Key.Group)
                    .SetProperty(r => r.SuggestedDraftGroupId, group.Key.Draft)
                    .SetProperty(r => r.SuggestionReason, group.Key.Rule), ct);
            }

        return new SuggestionResultDto(
            suggestions.Count,
            suggestions.Count(s => s.Differs),
            suggestions.GroupBy(s => s.Rule).Select(g => new CountItemDto(g.Key, g.Key, g.Count())).OrderByDescending(x => x.Count).ToList());
    }

    /// <summary>Copies stored suggestions into the assignment of the selected rows (by default only rows without a group).</summary>
    public async Task<int> ApplySuggestionsAsync(int batchId, IReadOnlyCollection<int>? rowIds, bool onlyUnassigned, CancellationToken ct = default)
    {
        await RequireDraftAsync(batchId, ct);
        var rows = SuggestionTargets(batchId, rowIds, onlyUnassigned);
        return await rows.ExecuteUpdateAsync(s => s
            .SetProperty(r => r.TargetDeliveryGroupId, r => r.SuggestedDeliveryGroupId)
            .SetProperty(r => r.TargetDraftGroupId, r => r.SuggestedDraftGroupId), ct);
    }

    public async Task<AssignmentPreviewDto> PreviewSuggestionsAsync(int batchId, IReadOnlyCollection<int>? rowIds, bool onlyUnassigned, CancellationToken ct = default)
    {
        await RequireDraftAsync(batchId, ct);
        var groups = await SuggestionTargets(batchId, rowIds, onlyUnassigned).AsNoTracking()
            .GroupBy(r => new { r.SuggestedDeliveryGroupId, r.SuggestedDraftGroupId, Assigned = r.TargetDeliveryGroupId != null || r.TargetDraftGroupId != null })
            .Select(g => new { g.Key.SuggestedDeliveryGroupId, g.Key.SuggestedDraftGroupId, g.Key.Assigned, Count = g.Count() })
            .ToListAsync(ct);

        var messages = new List<string>();
        var moves = new List<AssignmentMoveDto>();
        foreach (var g in groups.OrderByDescending(g => g.Count))
        {
            var name = await GroupNameAsync(new GroupKey(g.SuggestedDeliveryGroupId, g.SuggestedDraftGroupId), ct);
            messages.Add(g.Assigned
                ? $"{Customers(g.Count)} will be moved to {name} (replacing their current group)."
                : $"{Customers(g.Count)} will be assigned to {name}.");
            moves.Add(new AssignmentMoveDto(GroupKey.Format(g.SuggestedDeliveryGroupId, g.SuggestedDraftGroupId), name, g.Count));
        }
        var total = groups.Sum(g => g.Count);
        return new AssignmentPreviewDto(total, total, 0, null, moves, messages);
    }

    private IQueryable<ImportRow> SuggestionTargets(int batchId, IReadOnlyCollection<int>? rowIds, bool onlyUnassigned)
    {
        // EF Core applies C# null semantics, so nullable comparisons behave the same on every provider.
        var rows = db.ImportRows.Where(r => r.ImportBatchId == batchId).Included()
            .Where(r => r.SuggestedDeliveryGroupId != null || r.SuggestedDraftGroupId != null)
            .Where(r => r.SuggestedDeliveryGroupId != r.TargetDeliveryGroupId || r.SuggestedDraftGroupId != r.TargetDraftGroupId);
        if (rowIds is { Count: > 0 }) rows = rows.Where(r => rowIds.Contains(r.Id));
        if (onlyUnassigned) rows = rows.Where(r => r.TargetDeliveryGroupId == null && r.TargetDraftGroupId == null);
        return rows;
    }

    // ---------- Lifecycle ----------

    public async Task CancelAsync(int batchId, CancellationToken ct = default)
    {
        var batch = await RequireDraftAsync(batchId, ct);
        batch.Status = ImportBatchStatus.Cancelled;
        batch.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Keeps the batch's stored counts in line with its rows (they change when plan mappings change).</summary>
    public async Task RefreshCountsAsync(int batchId, CancellationToken ct = default)
    {
        var counts = await db.ImportRows.Where(r => r.ImportBatchId == batchId)
            .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var batch = await db.ImportBatches.FirstAsync(b => b.Id == batchId, ct);
        batch.ValidRows = counts.FirstOrDefault(c => c.Key == ImportRowStatus.Valid)?.Count ?? 0;
        batch.WarningRows = counts.FirstOrDefault(c => c.Key == ImportRowStatus.Warning)?.Count ?? 0;
        batch.InvalidRows = counts.FirstOrDefault(c => c.Key == ImportRowStatus.Error)?.Count ?? 0;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ImportBatch> RequireDraftAsync(int batchId, CancellationToken ct)
    {
        var batch = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct)
                    ?? throw new NotFoundException($"Import {batchId} was not found.");
        if (batch.Status != ImportBatchStatus.Validated)
            throw new BusinessRuleException($"Import {batchId} is {batch.Status.ToString().ToLowerInvariant()} and can no longer be changed.");
        return batch;
    }

    private async Task<GroupKey?> ResolveTargetAsync(int batchId, string? targetKey, CancellationToken ct)
    {
        var key = GroupKey.Parse(targetKey);
        if (key?.DeliveryGroupId is { } g && !await db.DeliveryGroups.AnyAsync(x => x.Id == g && x.Status == RecordStatus.Active, ct))
            throw new BusinessRuleException("The selected delivery group does not exist or is inactive.");
        if (key?.DraftGroupId is { } d && !await db.ImportDraftGroups.AnyAsync(x => x.Id == d && x.ImportBatchId == batchId, ct))
            throw new BusinessRuleException("The selected group does not belong to this import.");
        return key;
    }

    private async Task<string> GroupNameAsync(GroupKey key, CancellationToken ct) =>
        key.DeliveryGroupId is { } g
            ? await db.DeliveryGroups.Where(x => x.Id == g).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? $"Group #{g}"
            : await db.ImportDraftGroups.Where(x => x.Id == key.DraftGroupId).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "New group";

    private static string Customers(int count) => count == 1 ? "1 customer" : $"{count:N0} customers";

    /// <summary>Which groups each pincode / location / area / city already belongs to.</summary>
    private sealed class Evidence
    {
        public Dictionary<string, HashSet<(int?, int?)>> ByPincode { get; } = new();
        public Dictionary<string, HashSet<(int?, int?)>> ByLocation { get; } = new();
        public Dictionary<string, HashSet<(int?, int?)>> ByArea { get; } = new();
        public Dictionary<string, HashSet<(int?, int?)>> ByCity { get; } = new();

        public static string? AreaKey(string? locality, string? city) =>
            locality is null ? null : $"{locality.ToLowerInvariant()}|{city?.ToLowerInvariant()}";

        public void Add((int?, int?) group, string? pincode, string? location, string? locality, string? city)
        {
            Put(ByPincode, pincode, group);
            Put(ByLocation, location, group);
            Put(ByArea, AreaKey(locality, city), group);
            Put(ByCity, city?.ToLowerInvariant(), group);
        }

        public (int? G, int? D)? Unique(Dictionary<string, HashSet<(int?, int?)>> map, string? key)
        {
            if (key is null) return null;
            var k = map == ByCity ? key.ToLowerInvariant() : key;
            return map.TryGetValue(k, out var set) && set.Count == 1 ? set.First() : null;
        }

        private static void Put(Dictionary<string, HashSet<(int?, int?)>> map, string? key, (int?, int?) group)
        {
            if (key is null) return;
            if (!map.TryGetValue(key, out var set)) map[key] = set = [];
            set.Add(group);
        }
    }
}
