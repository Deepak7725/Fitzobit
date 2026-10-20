using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Application.DeliveryGroups;
using Fitzobit.Application.Geocoding;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Domain.Geography;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Classification;

/// <summary>A located customer on the map (X = longitude, Y = latitude).</summary>
public sealed record MapPointDto(int Id, string CustomerId, string Name, string? Address, string? Pincode, string? Area, double Lat, double Lng,
    bool Approximate, string Status, string StatusCode, string? MealType, int? GroupId, int? GroupNumber);

/// <summary>A customer in scope whose location is unavailable (not drawn; can still be assigned by hand).</summary>
public sealed record UnlocatedCustomerDto(int Id, string CustomerId, string Name, string? Address, string? Pincode, string GeocodeStatus,
    string? Message, int? GroupId, int? GroupNumber);

/// <param name="Kind">none | point | line | polygon</param>
/// <param name="Points">[latitude, longitude] pairs in drawing order.</param>
public sealed record BoundaryDto(string Kind, IReadOnlyList<double[]> Points);

/// <summary>An area (locality from the address, or the pincode when no locality is known) and how many of a group's customers live there.</summary>
public sealed record AreaCountDto(string Name, int Count);

/// <param name="Id">Null for a group that would be created when a suggestion is saved.</param>
/// <param name="Areas">The group's areas, most customers first (e.g. RS Puram 5, Gandhipuram 4, Avinashi Road 3).</param>
public sealed record MapGroupDto(int? Id, int Number, bool IsNew, bool IsActive, int CustomerCount, int LocatedCount, BoundaryDto Boundary,
    double? CenterLat, double? CenterLng, IReadOnlyList<AreaCountDto> Areas);

public sealed record MapDataDto(
    IReadOnlyList<MapPointDto> Points,
    IReadOnlyList<UnlocatedCustomerDto> Unlocated,
    int UnlocatedTotal,
    IReadOnlyList<MapGroupDto> Groups,
    int InScope,
    int Located,
    int Approximate,
    int Unassigned,
    IReadOnlyList<string> Statuses,
    ServiceAreaDto Area);

public sealed class SuggestRequest
{
    /// <summary>How many delivery groups the manager wants. Ignored when <see cref="Auto"/> is set.</summary>
    public int GroupCount { get; set; }

    /// <summary>Find the natural number of location clusters instead of using <see cref="GroupCount"/>.</summary>
    public bool Auto { get; set; }

    /// <summary>Customer status codes to classify (default Active and Paused).</summary>
    public List<string>? Statuses { get; set; }

    /// <summary>Even out group sizes (no group above about 15% over the average).</summary>
    public bool Balance { get; set; }

    /// <summary>Customers without a location join the group most customers with the same pincode were put in.</summary>
    public bool UsePincodeForUnlocated { get; set; }
}

/// <param name="Basis">location | pincode | unchanged (no location: keeps its current group or none)</param>
public sealed record SuggestedAssignmentDto(int Id, int? GroupNumber, string Basis);

public sealed record ChangeSummaryDto(int Assigned, int Moved, int Removed, int Unchanged, IReadOnlyList<int> NewGroupNumbers, IReadOnlyList<string> Messages);

public sealed record SuggestionDto(int RequestedGroups, int GroupsUsed, IReadOnlyList<SuggestedAssignmentDto> Assignments, IReadOnlyList<MapGroupDto> Groups,
    ChangeSummaryDto Changes, IReadOnlyList<string> Notes);

public sealed class DraftAssignment
{
    public int CustomerId { get; set; }

    /// <summary>Target group number, or null for "no group".</summary>
    public int? GroupNumber { get; set; }
}

public sealed class DraftRequest
{
    public List<DraftAssignment> Assignments { get; set; } = [];
    public string? Reason { get; set; }
}

public sealed record DraftEvaluationDto(IReadOnlyList<MapGroupDto> Groups, ChangeSummaryDto Changes);

public sealed record ApplyResultDto(int Assigned, int Moved, int Removed, int Unchanged, IReadOnlyList<int> CreatedGroupNumbers);

/// <summary>
/// Data classification for delivery groups: the map data, a deterministic geographic suggestion for a chosen number of
/// groups, evaluation of a manager-edited draft, and saving it. The manager decides; nothing changes until a draft is saved,
/// and every change is recorded in the assignment history.
/// </summary>
public sealed class ClassificationService(
    IFitzobitDbContext db,
    DeliveryGroupService groups,
    DeliveryGroupAssignmentService assignments,
    ServiceAreaService serviceArea,
    IAuditLogger audit)
{
    public const int MaxGroups = 50;
    public const int UnlocatedListLimit = 200;
    public const int MaxAreasPerGroup = 12;
    public static readonly string[] DefaultStatuses = [CustomerStatusCodes.Active, CustomerStatusCodes.Paused];

    private sealed record Row(int Id, string CustomerId, string Name, string? Address, string? Pincode, string? Locality, decimal? Lat, decimal? Lng,
        GeoMatchLevel? Level, GeocodeStatus GeocodeStatus, string? GeocodeMessage, string Status, string StatusCode, string? MealType, int? GroupId,
        int? GroupNumber)
    {
        public GeoPoint? Point => Lat is { } lat && Lng is { } lng ? new GeoPoint((double)lat, (double)lng) : null;

        /// <summary>Where the customer lives, in words: the locality read from the address, else the pincode.</summary>
        public string? Area => !string.IsNullOrWhiteSpace(Locality) ? Locality.Trim() : Pincode is { } pin ? $"PIN {pin}" : null;
    }

    private sealed record ActiveGroup(int Id, int Number, bool IsActive);

    public async Task<MapDataDto> GetMapAsync(IReadOnlyCollection<string>? statuses, CancellationToken ct = default)
    {
        var codes = Codes(statuses);
        var rows = await RowsAsync(codes, null, ct);
        var groupList = await GroupsAsync(ct);
        var current = rows.ToDictionary(r => r.Id, r => r.GroupNumber);
        var unlocated = rows.Where(r => r.Point is null).ToList();
        return new MapDataDto(
            rows.Where(r => r.Point is not null).Select(ToPoint).ToList(),
            unlocated.Take(UnlocatedListLimit).Select(ToUnlocated).ToList(),
            unlocated.Count,
            Summaries(rows, current, groupList),
            rows.Count,
            rows.Count(r => r.Point is not null),
            rows.Count(r => r.Point is not null && r.Level == GeoMatchLevel.Pincode),
            rows.Count(r => r.GroupNumber is null),
            codes,
            await serviceArea.GetAsync(ct));
    }

    /// <summary>
    /// Suggests <see cref="SuggestRequest.GroupCount"/> groups from customer locations (deterministic K-Means). Suggested
    /// groups reuse the existing group numbers they overlap most with, so as few customers as possible change group.
    /// Customers without a location are not moved (unless the pincode option is on).
    /// </summary>
    public async Task<SuggestionDto> SuggestAsync(SuggestRequest request, CancellationToken ct = default)
    {
        if (!request.Auto && request.GroupCount is < 1 or > MaxGroups) throw new BusinessRuleException($"Choose between 1 and {MaxGroups} groups.");
        var codes = Codes(request.Statuses);
        var rows = await RowsAsync(codes, null, ct);
        var groupList = await GroupsAsync(ct);
        var notes = new List<string>();

        var located = rows.Where(r => r.Point is not null).ToList();
        var inputs = located.Select(r => new ClusterInput(r.Id, r.Point!.Value)).ToList();
        var groupCount = request.GroupCount;
        if (request.Auto)
        {
            var (natural, silhouette) = GeoClustering.ChooseGroupCount(inputs, maxGroups: 15);
            groupCount = natural;
            if (located.Count > 0)
                notes.Add($"Auto: the customer locations form {natural} natural group(s) (separation score {silhouette:0.00}; above 0.5 means clearly separate areas). Change the number to override.");
        }
        var clustering = GeoClustering.KMeans(inputs, Math.Max(1, groupCount));
        if (located.Count == 0)
            notes.Add("No customer in scope has a location yet, so no groups could be suggested. Set the service city and let the locations be looked up, or assign customers by hand.");
        else if (clustering.ClusterCount < groupCount)
            notes.Add($"Only {clustering.ClusterCount} distinct location(s) are known, so {clustering.ClusterCount} group(s) were suggested instead of {groupCount}.");
        if (request.Balance && clustering.ClusterCount > 1)
        {
            clustering = GeoClustering.Balance(inputs, clustering);
            notes.Add($"Group sizes were balanced (at most {(int)Math.Ceiling(located.Count / (double)clustering.ClusterCount * 1.15)} customers each).");
        }

        var numberOfCluster = NumberClusters(clustering, located, groupList);
        var suggested = new Dictionary<int, (int? Number, string Basis)>();
        foreach (var r in located) suggested[r.Id] = (numberOfCluster[clustering.ClusterOf[r.Id]], "location");

        var unlocated = rows.Where(r => r.Point is null).ToList();
        var byPincode = request.UsePincodeForUnlocated
            ? located.Where(r => r.Pincode != null).GroupBy(r => r.Pincode!)
                .ToDictionary(g => g.Key, g => g.GroupBy(r => suggested[r.Id].Number).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key)
            : new Dictionary<string, int?>();
        var byPincodeCount = 0;
        foreach (var r in unlocated)
        {
            if (r.Pincode != null && byPincode.TryGetValue(r.Pincode, out var number)) { suggested[r.Id] = (number, "pincode"); byPincodeCount++; }
            else suggested[r.Id] = (r.GroupNumber, "unchanged");
        }
        if (unlocated.Count > 0)
            notes.Add(byPincodeCount > 0
                ? $"{unlocated.Count} customer(s) have no location: {byPincodeCount} were placed by pincode; the rest keep their current group (or stay unassigned)."
                : $"{unlocated.Count} customer(s) have no location and keep their current group (or stay unassigned). Assign them by hand if needed.");

        var draft = suggested.ToDictionary(x => x.Key, x => x.Value.Number);
        return new SuggestionDto(groupCount, clustering.ClusterCount,
            suggested.OrderBy(x => x.Key).Select(x => new SuggestedAssignmentDto(x.Key, x.Value.Number, x.Value.Basis)).ToList(),
            Summaries(rows, draft, groupList), Changes(rows, draft, groupList), notes);
    }

    /// <summary>Boundaries, sizes and the list of changes for a draft the manager edited on the map.</summary>
    public async Task<DraftEvaluationDto> EvaluateAsync(DraftRequest request, CancellationToken ct = default)
    {
        var draft = Draft(request);
        var rows = await RowsAsync(null, draft.Keys.ToList(), ct);
        var groupList = await GroupsAsync(ct);
        return new DraftEvaluationDto(Summaries(rows, draft, groupList), Changes(rows, draft, groupList));
    }

    /// <summary>
    /// Saves a reviewed draft in one transaction: creates groups for new numbers, then assigns, moves or removes customers.
    /// Every change is recorded in the assignment history with the reason.
    /// </summary>
    public async Task<ApplyResultDto> ApplyAsync(DraftRequest request, CancellationToken ct = default)
    {
        var draft = Draft(request);
        var existingNumbers = (await GroupsAsync(ct)).Select(g => g.Number).ToHashSet();
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Delivery groups reclassified" : request.Reason.Trim();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var ids = await groups.EnsureGroupsAsync(draft.Values.Where(n => n != null).Select(n => n!.Value).ToList(), ct);
        var result = await assignments.ApplyAsync(draft.ToDictionary(x => x.Key, x => x.Value is { } n ? (int?)ids[n] : null), reason, ct);
        var created = ids.Keys.Where(n => !existingNumbers.Contains(n)).Order().ToList();
        if (result.Changed > 0 || created.Count > 0)
        {
            audit.Record(AuditActions.GroupsReclassified, nameof(DeliveryGroup), null,
                $"Delivery groups saved: {result.Assigned} assigned, {result.Moved} moved, {result.Removed} removed",
                new { result.Assigned, result.Moved, result.Removed, CreatedGroups = created, Reason = reason });
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return new ApplyResultDto(result.Assigned, result.Moved, result.Removed, result.Unchanged, created);
    }

    /// <summary>
    /// Gives each cluster a group number: first the existing group it shares most customers with (largest overlaps first),
    /// then unused existing active groups, then new numbers after the highest number in use.
    /// </summary>
    private static Dictionary<int, int> NumberClusters(ClusteringResult clustering, IReadOnlyList<Row> located, IReadOnlyList<ActiveGroup> groupList)
    {
        var active = groupList.Where(g => g.IsActive).ToList();
        var overlaps = located.Where(r => r.GroupNumber != null && active.Any(g => g.Number == r.GroupNumber))
            .GroupBy(r => (Cluster: clustering.ClusterOf[r.Id], Number: r.GroupNumber!.Value))
            .Select(g => (g.Key.Cluster, g.Key.Number, Count: g.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Number).ThenBy(x => x.Cluster);

        var result = new Dictionary<int, int>();
        var usedNumbers = new HashSet<int>();
        foreach (var (cluster, number, _) in overlaps)
        {
            if (result.ContainsKey(cluster) || usedNumbers.Contains(number)) continue;
            result[cluster] = number;
            usedNumbers.Add(number);
        }

        var spare = new Queue<int>(active.Select(g => g.Number).Where(n => !usedNumbers.Contains(n)).Order());
        var next = (groupList.Count == 0 ? 0 : groupList.Max(g => g.Number)) + 1;
        for (var cluster = 0; cluster < clustering.ClusterCount; cluster++)
            if (!result.ContainsKey(cluster)) result[cluster] = spare.Count > 0 ? spare.Dequeue() : next++;
        return result;
    }

    /// <summary>Per-group sizes and boundaries for an assignment (customer id → group number); existing active groups are always listed.</summary>
    private static List<MapGroupDto> Summaries(IReadOnlyList<Row> rows, IReadOnlyDictionary<int, int?> numberOf, IReadOnlyList<ActiveGroup> groupList)
    {
        var byNumber = groupList.ToDictionary(g => g.Number);
        var members = rows.Where(r => numberOf.GetValueOrDefault(r.Id) != null).GroupBy(r => numberOf[r.Id]!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var numbers = groupList.Where(g => g.IsActive).Select(g => g.Number).Concat(members.Keys).Distinct().Order();
        return numbers.Select(n =>
        {
            var list = members.GetValueOrDefault(n) ?? [];
            var points = list.Where(r => r.Point is not null).Select(r => r.Point!.Value).ToList();
            var boundary = GroupBoundaries.For(points);
            var known = byNumber.GetValueOrDefault(n);
            return new MapGroupDto(known?.Id, n, known is null, known?.IsActive ?? true, list.Count, points.Count,
                new BoundaryDto(boundary.Kind.ToString().ToLowerInvariant(), boundary.Points.Select(p => new[] { p.Latitude, p.Longitude }).ToList()),
                points.Count > 0 ? points.Average(p => p.Latitude) : null, points.Count > 0 ? points.Average(p => p.Longitude) : null, Areas(list));
        }).ToList();
    }

    /// <summary>
    /// Areas of a group's customers, most customers first. Spellings that differ only in case, spaces or dots ("RS Puram",
    /// "R.S. Puram") count as one area, shown with the most common spelling.
    /// </summary>
    private static List<AreaCountDto> Areas(IEnumerable<Row> members) =>
        members.Where(r => r.Area != null)
            .GroupBy(r => new string(r.Area!.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
            .Select(g => new AreaCountDto(g.GroupBy(r => r.Area!).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key, g.Count()))
            .OrderByDescending(a => a.Count).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxAreasPerGroup)
            .ToList();

    private static ChangeSummaryDto Changes(IReadOnlyList<Row> rows, IReadOnlyDictionary<int, int?> target, IReadOnlyList<ActiveGroup> groupList)
    {
        int assigned = 0, moved = 0, removed = 0, unchanged = 0;
        var flows = new Dictionary<(int? From, int? To), int>();
        foreach (var r in rows)
        {
            if (!target.TryGetValue(r.Id, out var to)) continue;
            var from = r.GroupNumber;
            if (from == to) { unchanged++; continue; }
            if (from is null) assigned++;
            else if (to is null) removed++;
            else moved++;
            flows[(from, to)] = flows.GetValueOrDefault((from, to)) + 1;
        }
        var existing = groupList.Select(g => g.Number).ToHashSet();
        var newNumbers = target.Values.Where(n => n != null && !existing.Contains(n.Value)).Select(n => n!.Value).Distinct().Order().ToList();
        string Name(int n) => existing.Contains(n) ? DeliveryGroup.Label(n) : $"{DeliveryGroup.Label(n)} (new)";
        var messages = flows.OrderByDescending(f => f.Value).ThenBy(f => f.Key.To ?? int.MaxValue).ThenBy(f => f.Key.From ?? 0)
            .Select(f => (f.Key.From, f.Key.To) switch
            {
                (null, { } to) => $"{Customers(f.Value)} assigned to {Name(to)}",
                ({ } from, null) => $"{Customers(f.Value)} removed from {DeliveryGroup.Label(from)}",
                var (from, to) => $"{Customers(f.Value)} moved from {DeliveryGroup.Label(from!.Value)} to {Name(to!.Value)}"
            }).ToList();
        return new ChangeSummaryDto(assigned, moved, removed, unchanged, newNumbers, messages);
    }

    private async Task<List<Row>> RowsAsync(IReadOnlyCollection<string>? statusCodes, IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        var query = db.Customers.AsNoTracking();
        if (statusCodes is not null) query = query.Where(c => statusCodes.Contains(c.Status!.Code));
        if (ids is not null) query = query.Where(c => ids.Contains(c.Id));
        var found = await query.OrderBy(c => c.Id).Select(c => new
            {
                c.Id, c.CustomerId, c.CustomerName, c.Address, c.Pincode, c.Locality, c.Latitude, c.Longitude, c.GeocodeMatchLevel, c.GeocodeStatus,
                c.GeocodeMessage, Status = c.Status!.DisplayName, StatusCode = c.Status.Code,
                MealTypes = c.Packages.Where(p => p.ArchivedAt == null).OrderBy(p => p.StartDate).Select(p => p.MealType!.DisplayName).ToList(),
                GroupId = c.GroupAssignments.Where(a => a.IsActive).Select(a => (int?)a.DeliveryGroupId).FirstOrDefault(),
                GroupNumber = c.GroupAssignments.Where(a => a.IsActive).Select(a => (int?)a.DeliveryGroup!.GroupNumber).FirstOrDefault()
            })
            .ToListAsync(ct);
        return found.Select(c => new Row(c.Id, c.CustomerId, c.CustomerName, c.Address, c.Pincode, c.Locality, c.Latitude, c.Longitude,
            c.GeocodeMatchLevel, c.GeocodeStatus, c.GeocodeMessage, c.Status, c.StatusCode,
            c.MealTypes.Count > 0 ? string.Join(", ", c.MealTypes.Distinct()) : null, c.GroupId, c.GroupNumber)).ToList();
    }

    private async Task<List<ActiveGroup>> GroupsAsync(CancellationToken ct) =>
        await db.DeliveryGroups.AsNoTracking().OrderBy(g => g.GroupNumber)
            .Select(g => new ActiveGroup(g.Id, g.GroupNumber, g.Status == RecordStatus.Active)).ToListAsync(ct);

    private static Dictionary<int, int?> Draft(DraftRequest request)
    {
        if (request.Assignments is not { Count: > 0 }) throw new BusinessRuleException("The draft has no customers.");
        if (request.Assignments.Any(a => a.GroupNumber is < 1)) throw new BusinessRuleException("Group numbers start at 1.");
        if (request.Assignments.Select(a => a.CustomerId).Distinct().Count() != request.Assignments.Count)
            throw new BusinessRuleException("A customer appears more than once in the draft.");
        return request.Assignments.ToDictionary(a => a.CustomerId, a => a.GroupNumber);
    }

    private static List<string> Codes(IReadOnlyCollection<string>? statuses) =>
        statuses is { Count: > 0 } ? statuses.Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList() : [.. DefaultStatuses];

    private static MapPointDto ToPoint(Row r) =>
        new(r.Id, r.CustomerId, r.Name, r.Address, r.Pincode, r.Area, (double)r.Lat!.Value, (double)r.Lng!.Value, r.Level == GeoMatchLevel.Pincode, r.Status,
            r.StatusCode, r.MealType, r.GroupId, r.GroupNumber);

    private static UnlocatedCustomerDto ToUnlocated(Row r) =>
        new(r.Id, r.CustomerId, r.Name, r.Address, r.Pincode, r.GeocodeStatus.ToString(), r.GeocodeMessage, r.GroupId, r.GroupNumber);

    private static string Customers(int count) => count == 1 ? "1 customer" : $"{count:N0} customers";
}
