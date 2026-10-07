using Fitzobit.Domain.DeliveryGroups;

namespace Fitzobit.Domain.Imports;

/// <summary>
/// One data row of an uploaded file, staged for review. Raw* fields hold the cell text exactly as read and are
/// never modified; the other fields hold validated/normalized values and the manager's decisions.
/// </summary>
public class ImportRow
{
    public int Id { get; set; }
    public int ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }

    /// <summary>Excel row number (1-based, as shown in Excel).</summary>
    public int RowNumber { get; set; }

    public string? RawCustomerId { get; set; }
    public string? RawCustomerName { get; set; }
    public string? RawPhoneNumber { get; set; }
    public string? RawLocation { get; set; }
    public string? RawPlan { get; set; }

    public string? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? NormalizedPhoneNumber { get; set; }
    public string? NormalizedLocation { get; set; }
    public string? Pincode { get; set; }
    public string? Locality { get; set; }
    public string? City { get; set; }

    /// <summary>Normalized plan text; links the row to its <see cref="ImportPlanMapping"/>.</summary>
    public string? PlanKey { get; set; }
    public int? PlanId { get; set; }
    public int? PricingTierId { get; set; }

    public ImportRowStatus Status { get; set; }

    /// <summary>JSON array of <see cref="ImportIssue"/>.</summary>
    public string IssuesJson { get; set; } = "[]";

    /// <summary>";"-delimited issue codes for filtering, e.g. ";PINCODE_NOT_DETECTED;EXISTING_CUSTOMER;".</summary>
    public string IssueCodes { get; set; } = ";";

    public bool IsExistingCustomer { get; set; }
    public int? ExistingCustomerDbId { get; set; }

    /// <summary>The existing customer's active group when the file was validated.</summary>
    public int? ExistingGroupId { get; set; }

    public bool IsPotentialDuplicate { get; set; }

    /// <summary>Manager chose to leave this (non-error) row out of the import.</summary>
    public bool ExcludedByManager { get; set; }

    /// <summary>Target group: an existing delivery group or a group drafted in this import (at most one is set).</summary>
    public int? TargetDeliveryGroupId { get; set; }
    public DeliveryGroup? TargetDeliveryGroup { get; set; }
    public int? TargetDraftGroupId { get; set; }
    public ImportDraftGroup? TargetDraftGroup { get; set; }

    public int? SuggestedDeliveryGroupId { get; set; }
    public int? SuggestedDraftGroupId { get; set; }
    public string? SuggestionReason { get; set; }
}

public enum ImportRowStatus
{
    Valid,
    Warning,
    Error
}

public enum IssueSeverity
{
    Warning,
    Error
}

/// <summary>A validation finding for one row.</summary>
public sealed record ImportIssue(string Code, IssueSeverity Severity, string Message, string? Field = null);

/// <summary>A delivery group drafted during an import. It becomes a real <see cref="DeliveryGroup"/> when the import is saved.</summary>
public class ImportDraftGroup
{
    public int Id { get; set; }
    public int ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Set when the import is saved.</summary>
    public int? CreatedDeliveryGroupId { get; set; }
}

/// <summary>How one distinct plan text in a file maps to a Fitzobit plan (and optionally a pricing tier).</summary>
public class ImportPlanMapping
{
    public int Id { get; set; }
    public int ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }

    public string PlanKey { get; set; } = string.Empty;
    public string ExampleText { get; set; } = string.Empty;
    public int RowCount { get; set; }

    public int? PlanId { get; set; }
    public int? PricingTierId { get; set; }

    public PlanMatchMethod Method { get; set; }
    public PlanMappingStatus Status { get; set; }

    /// <summary>JSON array of candidate plan ids when the text was ambiguous.</summary>
    public string? CandidatePlanIdsJson { get; set; }

    /// <summary>Human-readable note, e.g. why the tier could not be determined.</summary>
    public string? Note { get; set; }
}

public enum PlanMatchMethod
{
    None,
    SavedAlias,
    ExactName,
    Keywords,
    Manual
}

public enum PlanMappingStatus
{
    Unknown,
    Ambiguous,
    Mapped
}
