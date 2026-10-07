namespace Fitzobit.Domain.Auditing;

/// <summary>Append-only record of an important change: who, what, when, old and new values. Never store passwords or secrets here.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityKey { get; set; }
    public string Summary { get; set; } = string.Empty;

    /// <summary>Field values before the change (JSON object), when the action changed existing data.</summary>
    public string? OldValues { get; set; }

    /// <summary>Field values after the change (JSON object).</summary>
    public string? NewValues { get; set; }

    /// <summary>Other context (JSON), e.g. import batch, filters used for an export.</summary>
    public string? Details { get; set; }

    public string? PerformedBy { get; set; }
}

/// <summary>Well-known audit action names.</summary>
public static class AuditActions
{
    public const string CustomerCreated = "CustomerCreated";
    public const string CustomerUpdated = "CustomerUpdated";
    public const string GroupCreated = "GroupCreated";
    public const string GroupUpdated = "GroupUpdated";
    public const string CustomerAssigned = "CustomerAssigned";
    public const string CustomerReassigned = "CustomerReassigned";
    public const string CustomerUnassigned = "CustomerUnassigned";
    public const string ImportPerformed = "ImportPerformed";
    public const string ExportPerformed = "ExportPerformed";
    public const string CatalogueItemCreated = "CatalogueItemCreated";
    public const string CatalogueItemChanged = "CatalogueItemChanged";
    public const string PlanCreated = "PlanCreated";
    public const string PlanUpdated = "PlanUpdated";
    public const string PlanAliasDeleted = "PlanAliasDeleted";
    public const string UserCreated = "UserCreated";
    public const string UserUpdated = "UserUpdated";
    public const string UserPasswordReset = "UserPasswordReset";
    public const string UserPasswordChanged = "UserPasswordChanged";
    public const string UserSignedIn = "UserSignedIn";
    public const string UserSignInFailed = "UserSignInFailed";
}
