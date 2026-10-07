using Fitzobit.Domain.Imports;

namespace Fitzobit.Application.Imports;

/// <summary>
/// Identifies an assignment target inside an import: an existing delivery group ("g:12") or a group drafted in
/// this import ("d:3").
/// </summary>
public sealed record GroupKey(int? DeliveryGroupId, int? DraftGroupId)
{
    public static GroupKey? Parse(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key == "none") return null;
        var parts = key.Split(':', 2);
        if (parts.Length == 2 && int.TryParse(parts[1], out var id))
        {
            if (parts[0] == "g") return new GroupKey(id, null);
            if (parts[0] == "d") return new GroupKey(null, id);
        }
        throw new Common.BusinessRuleException($"\"{key}\" is not a valid group reference.");
    }

    public static string? Format(int? deliveryGroupId, int? draftGroupId) =>
        deliveryGroupId is { } g ? $"g:{g}" : draftGroupId is { } d ? $"d:{d}" : null;

    public override string ToString() => Format(DeliveryGroupId, DraftGroupId)!;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record GroupRefDto(string Key, string Name, bool IsNew);

/// <summary>Row filters. All are optional and combine with AND.</summary>
public sealed class ImportRowFilter
{
    /// <summary>All | Valid | Warning | Error | Included | Excluded</summary>
    public string? Status { get; set; }
    public string? Search { get; set; }
    public string? Pincode { get; set; }
    public string? Locality { get; set; }
    public string? City { get; set; }
    public string? Location { get; set; }
    /// <summary>A group key, or "none" for rows without a group.</summary>
    public string? Group { get; set; }
    public int? PlanId { get; set; }
    public string? IssueCode { get; set; }
    public bool? Existing { get; set; }
    public bool? HasSuggestion { get; set; }
}

/// <summary>Which rows an action applies to: explicit row ids, or every included row matching a filter.</summary>
public sealed class ImportRowSelection
{
    public List<int>? RowIds { get; set; }
    public ImportRowFilter? Filter { get; set; }
}

public sealed record ImportRowDto(
    int Id,
    int RowNumber,
    string? CustomerId,
    string? CustomerName,
    string? PhoneNumber,
    string? NormalizedPhoneNumber,
    string? Location,
    string? NormalizedLocation,
    string? Pincode,
    string? Locality,
    string? City,
    string? Plan,
    int? PlanId,
    string? PlanName,
    string? TierName,
    string Status,
    IReadOnlyList<ImportIssue> Issues,
    bool IsExistingCustomer,
    bool IsPotentialDuplicate,
    bool Included,
    bool ExcludedByManager,
    GroupRefDto? Group,
    string? ExistingGroupName,
    GroupRefDto? SuggestedGroup,
    string? SuggestionReason);

public sealed record ImportSummaryDto(
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int ErrorRows,
    int NewCustomers,
    int ExistingCustomers,
    int Duplicates,
    int Locations,
    int Pincodes,
    int Plans,
    int ToImport,
    int ExcludedByManager,
    int Unassigned,
    int KeepingCurrentGroup,
    int MissingPincode,
    int UnresolvedPlanMappings);

public sealed record ImportBatchDetailDto(
    int Id,
    string FileName,
    long FileSizeBytes,
    string? SheetName,
    DateTime UploadedAt,
    string? UploadedBy,
    string Status,
    string? FailureReason,
    IReadOnlyList<string> FileMessages,
    int EmptyRowsSkipped,
    long ValidationDurationMs,
    long SaveDurationMs,
    DateTime? CompletedAt,
    int ImportedCount,
    int CustomersCreated,
    int ExistingCustomersMatched,
    int GroupsCreated,
    int AssignmentsChanged,
    ImportSummaryDto? Summary);

public sealed record CountItemDto(string Key, string Label, int Count);

public sealed record ImportClassificationDto(
    int IncludedRows,
    int NewCustomers,
    int ExistingCustomers,
    int WithoutPincode,
    int WithoutGroup,
    int PotentialDuplicates,
    IReadOnlyList<CountItemDto> ByLocation,
    IReadOnlyList<CountItemDto> ByLocality,
    IReadOnlyList<CountItemDto> ByCity,
    IReadOnlyList<CountItemDto> ByPincode,
    IReadOnlyList<CountItemDto> ByPlan,
    IReadOnlyList<CountItemDto> ByCategory);

public sealed record PlanOptionTierDto(int Id, string Name, decimal Price, string BillingPeriod);

public sealed record PlanOptionDto(int Id, string Name, string Code, string? Category, IReadOnlyList<PlanOptionTierDto> Tiers);

public sealed record PlanMappingDto(
    int Id,
    string PlanKey,
    string ExampleText,
    int RowCount,
    string Status,
    string Method,
    int? PlanId,
    string? PlanName,
    int? PricingTierId,
    string? TierName,
    IReadOnlyList<int> CandidatePlanIds,
    string? Note);

public sealed record ImportGroupSummaryDto(
    string Key,
    string Name,
    string? Description,
    bool IsNew,
    string Status,
    int CustomerCount,
    int NewCustomerCount,
    int ExistingMembersOutsideImport,
    int PincodeCount,
    IReadOnlyList<string> Pincodes,
    int LocationCount,
    IReadOnlyList<string> Localities,
    int PlanCount,
    IReadOnlyList<string> Plans);

public sealed record ReviewIssueCountDto(string Code, string Label, int Count);

public sealed record ImportReviewDto(
    ImportSummaryDto Summary,
    IReadOnlyList<ImportGroupSummaryDto> Groups,
    int Unassigned,
    int UnknownPlan,
    int MissingPincode,
    int GroupChangesForExistingCustomers,
    IReadOnlyList<ReviewIssueCountDto> Warnings,
    IReadOnlyList<ReviewIssueCountDto> ExcludedErrors,
    bool CanSave,
    IReadOnlyList<string> Blockers);

public sealed record AssignmentMoveDto(string? FromKey, string FromName, int Count);

public sealed record AssignmentPreviewDto(
    int SelectedRows,
    int WillChange,
    int AlreadyInTarget,
    string? TargetName,
    IReadOnlyList<AssignmentMoveDto> Moves,
    IReadOnlyList<string> Messages);

public sealed record SuggestionResultDto(int RowsWithSuggestion, int DifferentFromCurrent, IReadOnlyList<CountItemDto> ByRule);

public sealed record ImportCommitResultDto(
    int BatchId,
    int CustomersCreated,
    int ExistingCustomersMatched,
    int GroupsCreated,
    int AssignmentsChanged,
    int ImportedCount,
    int SkippedRows,
    long DurationMs);
