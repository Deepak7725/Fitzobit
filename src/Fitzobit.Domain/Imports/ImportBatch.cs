namespace Fitzobit.Domain.Imports;

/// <summary>
/// One customer Excel upload and its lifecycle: Validated (draft, being reviewed) → Completed, or Cancelled.
/// Files that cannot be read at all are recorded as Failed.
/// </summary>
public class ImportBatch
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? SheetName { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }

    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public int WarningRows { get; set; }
    public int EmptyRowsSkipped { get; set; }

    /// <summary>Rows written as customers or used for assignments when the batch was saved.</summary>
    public int ImportedCount { get; set; }
    public int CustomersCreated { get; set; }
    public int ExistingCustomersMatched { get; set; }
    public int GroupsCreated { get; set; }
    public int AssignmentsChanged { get; set; }

    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Pending;

    /// <summary>File-level problems or the reason the last save attempt failed.</summary>
    public string? FailureReason { get; set; }

    /// <summary>File-level notices (e.g. ignored columns, skipped empty rows), one per line.</summary>
    public string? FileMessages { get; set; }

    public long ValidationDurationMs { get; set; }
    public long SaveDurationMs { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <remarks>A set, so EF Core adds thousands of rows in constant time per row (a list makes relationship fix-up quadratic).</remarks>
    public ICollection<ImportRow> Rows { get; set; } = new HashSet<ImportRow>();
    public ICollection<ImportDraftGroup> DraftGroups { get; set; } = new List<ImportDraftGroup>();
    public ICollection<ImportPlanMapping> PlanMappings { get; set; } = new List<ImportPlanMapping>();
}

public enum ImportBatchStatus
{
    Pending,
    Validated,
    Completed,
    Failed,
    Cancelled
}
