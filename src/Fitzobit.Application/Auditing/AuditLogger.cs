using System.Text.Json;
using Fitzobit.Application.Abstractions;
using Fitzobit.Domain.Auditing;

namespace Fitzobit.Application.Auditing;

public interface IAuditLogger
{
    /// <summary>Stages an audit entry; it is written by the caller's next SaveChanges, in the same transaction as the change.</summary>
    void Record(string action, string entityType, string? entityKey, string summary, object? details = null);

    /// <summary>Stages an audit entry that records the old and new values of the fields that changed.</summary>
    void RecordChange(string action, string entityType, string? entityKey, string summary,
        IReadOnlyDictionary<string, object?> oldValues, IReadOnlyDictionary<string, object?> newValues, object? details = null);

    /// <summary>Like <see cref="Record"/>, for actions whose actor is not yet the signed-in user (sign-in, first-run setup).</summary>
    void RecordAs(string performedBy, string action, string entityType, string? entityKey, string summary, object? details = null);
}

public sealed class AuditLogger(IFitzobitDbContext db, ICurrentUser currentUser, IClock clock) : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public void Record(string action, string entityType, string? entityKey, string summary, object? details = null) =>
        Add(action, entityType, entityKey, summary, null, null, details);

    public void RecordChange(string action, string entityType, string? entityKey, string summary,
        IReadOnlyDictionary<string, object?> oldValues, IReadOnlyDictionary<string, object?> newValues, object? details = null) =>
        Add(action, entityType, entityKey, summary, oldValues, newValues, details);

    public void RecordAs(string performedBy, string action, string entityType, string? entityKey, string summary, object? details = null) =>
        Add(action, entityType, entityKey, summary, null, null, details, performedBy);

    private void Add(string action, string entityType, string? entityKey, string summary, object? oldValues, object? newValues, object? details,
        string? performedBy = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            OccurredAt = clock.UtcNow,
            Action = action,
            EntityType = entityType,
            EntityKey = entityKey,
            Summary = summary,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues, JsonOptions),
            NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues, JsonOptions),
            Details = details is null ? null : JsonSerializer.Serialize(details, JsonOptions),
            PerformedBy = performedBy ?? currentUser.UserName
        });
    }
}

/// <summary>Collects field changes as "before" and "after" dictionaries for <see cref="IAuditLogger.RecordChange"/>.</summary>
public sealed class ChangeSet
{
    public Dictionary<string, object?> Old { get; } = new();
    public Dictionary<string, object?> New { get; } = new();
    public bool HasChanges => New.Count > 0;

    /// <summary>Records the change only when the value actually differs; returns true in that case.</summary>
    public bool Track<T>(string field, T oldValue, T newValue)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue)) return false;
        Old[field] = oldValue;
        New[field] = newValue;
        return true;
    }
}
