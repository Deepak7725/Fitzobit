using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Deliveries;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Dictionaries;

/// <summary>An active drop-down value. <see cref="NumericValue"/> is the delivery-day count for frequencies; <see cref="CanPause"/>
/// whether packages of that frequency may pause.</summary>
public sealed record DictionaryOptionDto(int Id, string Code, string DisplayName, int SortOrder, int? NumericValue, bool CanPause);

/// <summary>Every customer drop-down, active values only, in display order.</summary>
public sealed record DictionaryOptionsDto(
    IReadOnlyList<DictionaryOptionDto> Preference,
    IReadOnlyList<DictionaryOptionDto> MealType,
    IReadOnlyList<DictionaryOptionDto> Frequency,
    IReadOnlyList<DictionaryOptionDto> CustomerStatus);

/// <summary>A value as the Settings screen shows it, with how many customers use it.</summary>
public sealed record DictionaryEntryDto(
    int Id,
    string DictionaryType,
    string Code,
    string DisplayName,
    int SortOrder,
    bool IsActive,
    bool IsSystem,
    int? NumericValue,
    bool CanPause,
    int CustomerCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? UpdatedBy);

public sealed class SaveDictionaryEntryRequest
{
    public string DisplayName { get; set; } = "";

    /// <summary>Frequency: delivery days per package. Ignored for other types.</summary>
    public int? NumericValue { get; set; }

    /// <summary>Frequency: whether its packages may pause delivery days. Null keeps the current setting (new values: no).</summary>
    public bool? CanPause { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Administrator-managed drop-down values. Values used by customers are never deleted (deactivate them instead), the last
/// active value of a type cannot be deactivated, and system statuses stay active.
/// </summary>
public sealed class DictionaryService(IFitzobitDbContext db, IAuditLogger audit)
{
    public const int MaxDisplayNameLength = 100;

    public async Task<DictionaryOptionsDto> GetActiveOptionsAsync(CancellationToken ct = default)
    {
        var entries = await db.DictionaryEntries.AsNoTracking().Where(e => e.IsActive)
            .OrderBy(e => e.SortOrder).ThenBy(e => e.DisplayName)
            .Select(e => new { e.DictionaryType, Option = new DictionaryOptionDto(e.Id, e.Code, e.DisplayName, e.SortOrder, e.NumericValue, e.CanPause) })
            .ToListAsync(ct);
        List<DictionaryOptionDto> Of(DictionaryType type) => entries.Where(e => e.DictionaryType == type).Select(e => e.Option).ToList();
        return new DictionaryOptionsDto(Of(DictionaryType.Preference), Of(DictionaryType.MealType), Of(DictionaryType.Frequency),
            Of(DictionaryType.CustomerStatus));
    }

    public async Task<IReadOnlyList<DictionaryEntryDto>> ListAsync(DictionaryType type, CancellationToken ct = default)
    {
        var entries = await db.DictionaryEntries.AsNoTracking().Where(e => e.DictionaryType == type)
            .OrderBy(e => e.SortOrder).ThenBy(e => e.DisplayName).ToListAsync(ct);
        var usage = await UsageAsync(type, ct);
        return entries.Select(e => ToDto(e, usage.GetValueOrDefault(e.Id))).ToList();
    }

    public async Task<DictionaryEntryDto> CreateAsync(DictionaryType type, SaveDictionaryEntryRequest request, CancellationToken ct = default)
    {
        var name = await ValidateNameAsync(type, request.DisplayName, null, ct);
        var code = Normalization.ToCode(name);
        if (code.Length == 0) throw new BusinessRuleException("The name must contain letters or digits.");
        if (await db.DictionaryEntries.AnyAsync(e => e.DictionaryType == type && e.Code == code, ct))
            throw new BusinessRuleException($"A {Describe(type)} with the code {code} already exists (it may be inactive). Reactivate or rename it instead.");

        var maxOrder = await db.DictionaryEntries.Where(e => e.DictionaryType == type).MaxAsync(e => (int?)e.SortOrder, ct) ?? 0;
        var entry = new DictionaryEntry
        {
            DictionaryType = type,
            Code = code,
            DisplayName = name,
            SortOrder = maxOrder + 10,
            IsActive = request.IsActive,
            NumericValue = ValidateNumber(type, request.NumericValue),
            CanPause = type == DictionaryType.Frequency && request.CanPause == true
        };
        db.DictionaryEntries.Add(entry);
        audit.RecordChange(AuditActions.DictionaryEntryCreated, nameof(DictionaryEntry), $"{type}:{code}", $"{Title(type)} added: {name}",
            new Dictionary<string, object?>(), Snapshot(entry));
        await db.SaveChangesAsync(ct);
        return ToDto(entry, 0);
    }

    public async Task<DictionaryEntryDto> UpdateAsync(int id, SaveDictionaryEntryRequest request, CancellationToken ct = default)
    {
        var entry = await FindAsync(id, ct);
        var name = await ValidateNameAsync(entry.DictionaryType, request.DisplayName, id, ct);
        var number = ValidateNumber(entry.DictionaryType, request.NumericValue);
        var canPause = entry.DictionaryType == DictionaryType.Frequency && (request.CanPause ?? entry.CanPause);
        if (entry.IsActive && !request.IsActive) await EnsureCanDeactivateAsync(entry, ct);

        var changes = new ChangeSet();
        changes.Track("DisplayName", entry.DisplayName, name);
        changes.Track("NumericValue", entry.NumericValue, number);
        changes.Track("CanPause", entry.CanPause, canPause);
        changes.Track("IsActive", entry.IsActive, request.IsActive);
        if (changes.HasChanges)
        {
            entry.DisplayName = name;
            entry.NumericValue = number;
            entry.CanPause = canPause;
            entry.IsActive = request.IsActive;
            audit.RecordChange(AuditActions.DictionaryEntryUpdated, nameof(DictionaryEntry), $"{entry.DictionaryType}:{entry.Code}",
                $"{Title(entry.DictionaryType)} updated: {name} ({string.Join(", ", changes.New.Keys)})", changes.Old, changes.New);
            await db.SaveChangesAsync(ct);
        }
        return ToDto(entry, await CountCustomersAsync(entry, ct));
    }

    /// <summary>Sets the display order of a type's values to the order of <paramref name="orderedIds"/> (all of the type's ids).</summary>
    public async Task ReorderAsync(DictionaryType type, IReadOnlyList<int> orderedIds, CancellationToken ct = default)
    {
        var entries = await db.DictionaryEntries.Where(e => e.DictionaryType == type).ToListAsync(ct);
        if (orderedIds.Count != entries.Count || orderedIds.Distinct().Count() != entries.Count || entries.Any(e => !orderedIds.Contains(e.Id)))
            throw new BusinessRuleException("The new order must list every value of this drop-down exactly once. Reload and try again.");

        var changed = false;
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var entry = entries.Single(e => e.Id == orderedIds[i]);
            var order = (i + 1) * 10;
            if (entry.SortOrder == order) continue;
            entry.SortOrder = order;
            changed = true;
        }
        if (!changed) return;
        audit.Record(AuditActions.DictionaryEntryUpdated, nameof(DictionaryEntry), type.ToString(), $"{Title(type)} order changed",
            new { Order = orderedIds.Select(id => entries.Single(e => e.Id == id).DisplayName).ToList() });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes a value that no customer has ever used. Used values can only be deactivated.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entry = await FindAsync(id, ct);
        if (entry.IsSystem) throw new BusinessRuleException($"\"{entry.DisplayName}\" is required by the application and cannot be deleted. You can rename it.");
        var used = await CountCustomersAsync(entry, ct);
        if (used > 0)
            throw new BusinessRuleException($"\"{entry.DisplayName}\" is used by {Users(entry.DictionaryType, used)} and cannot be deleted. Deactivate it instead.");
        if (entry.IsActive) await EnsureCanDeactivateAsync(entry, ct);

        // Its prices on the Money page go with it (a price list entry is not a use by a customer).
        db.PackagePrices.RemoveRange(await db.PackagePrices
            .Where(p => p.MealTypeId == entry.Id || p.PreferenceId == entry.Id || p.FrequencyId == entry.Id).ToListAsync(ct));
        db.DictionaryEntries.Remove(entry);
        audit.RecordChange(AuditActions.DictionaryEntryDeleted, nameof(DictionaryEntry), $"{entry.DictionaryType}:{entry.Code}",
            $"{Title(entry.DictionaryType)} deleted: {entry.DisplayName}", Snapshot(entry), new Dictionary<string, object?>());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Checks a customer's choice: it must exist and be of <paramref name="type"/>, and be active unless the customer already has it
    /// (<paramref name="currentId"/>), so deactivating a value never forces existing customers to change.
    /// </summary>
    public async Task<DictionaryEntry> ResolveSelectionAsync(DictionaryType type, int id, int? currentId, CancellationToken ct = default)
    {
        var entry = await db.DictionaryEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null || entry.DictionaryType != type) throw new BusinessRuleException($"The selected {Describe(type)} does not exist.");
        if (!entry.IsActive && id != currentId) throw new BusinessRuleException($"The {Describe(type)} \"{entry.DisplayName}\" is no longer available.");
        return entry;
    }

    public async Task<DictionaryEntry> FindByCodeAsync(DictionaryType type, string code, CancellationToken ct = default) =>
        await db.DictionaryEntries.AsNoTracking().FirstOrDefaultAsync(e => e.DictionaryType == type && e.Code == code, ct)
        ?? throw new BusinessRuleException($"The {Describe(type)} {code} is missing. Check Settings → Dropdown values.");

    public static string Describe(DictionaryType type) => type switch
    {
        DictionaryType.MealType => "meal type",
        DictionaryType.CustomerStatus => "status",
        _ => type.ToString().ToLowerInvariant()
    };

    private static string Title(DictionaryType type) => type switch
    {
        DictionaryType.MealType => "Meal type",
        DictionaryType.CustomerStatus => "Customer status",
        _ => type.ToString()
    };

    private async Task EnsureCanDeactivateAsync(DictionaryEntry entry, CancellationToken ct)
    {
        if (entry.IsSystem)
            throw new BusinessRuleException($"\"{entry.DisplayName}\" is required by the application and must stay active. You can rename it.");
        if (!await db.DictionaryEntries.AnyAsync(e => e.DictionaryType == entry.DictionaryType && e.IsActive && e.Id != entry.Id, ct))
            throw new BusinessRuleException($"\"{entry.DisplayName}\" is the only active {Describe(entry.DictionaryType)}. Add or activate another one first.");
    }

    private async Task<string> ValidateNameAsync(DictionaryType type, string? displayName, int? exceptId, CancellationToken ct)
    {
        var name = string.Join(' ', (displayName ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0) throw new BusinessRuleException("A name is required.");
        if (name.Length > MaxDisplayNameLength) throw new BusinessRuleException($"The name must be at most {MaxDisplayNameLength} characters.");
        var lower = name.ToLower();
        if (await db.DictionaryEntries.AnyAsync(e => e.DictionaryType == type && e.Id != exceptId && e.DisplayName.ToLower() == lower, ct))
            throw new BusinessRuleException($"A {Describe(type)} named \"{name}\" already exists.");
        return name;
    }

    private static int? ValidateNumber(DictionaryType type, int? value)
    {
        if (type != DictionaryType.Frequency) return null;
        if (value is not { } days || days < 1 || days > DeliveryCalendarService.MaxDeliveryDays)
            throw new BusinessRuleException($"Enter the number of delivery days for this frequency (1–{DeliveryCalendarService.MaxDeliveryDays}).");
        return days;
    }

    private async Task<DictionaryEntry> FindAsync(int id, CancellationToken ct) =>
        await db.DictionaryEntries.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException($"Value {id} was not found.");

    /// <summary>Where a value is used: packages for preference, meal type and frequency; customers for status.</summary>
    private IQueryable<int?> ReferenceColumn(DictionaryType type) => type switch
    {
        DictionaryType.Preference => db.CustomerPackages.Select(p => (int?)p.PreferenceId),
        DictionaryType.MealType => db.CustomerPackages.Select(p => (int?)p.MealTypeId),
        DictionaryType.Frequency => db.CustomerPackages.Select(p => (int?)p.FrequencyId),
        _ => db.Customers.Select(c => (int?)c.StatusId)
    };

    private async Task<Dictionary<int, int>> UsageAsync(DictionaryType type, CancellationToken ct) =>
        await ReferenceColumn(type).Where(id => id != null).GroupBy(id => id!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    private Task<int> CountCustomersAsync(DictionaryEntry entry, CancellationToken ct) =>
        ReferenceColumn(entry.DictionaryType).CountAsync(id => id == entry.Id, ct);

    private static Dictionary<string, object?> Snapshot(DictionaryEntry e) => new()
    {
        ["DisplayName"] = e.DisplayName,
        ["Code"] = e.Code,
        ["NumericValue"] = e.NumericValue,
        ["CanPause"] = e.CanPause,
        ["IsActive"] = e.IsActive
    };

    private static DictionaryEntryDto ToDto(DictionaryEntry e, int customers) =>
        new(e.Id, e.DictionaryType.ToString(), e.Code, e.DisplayName, e.SortOrder, e.IsActive, e.IsSystem, e.NumericValue, e.CanPause, customers,
            e.CreatedAt, e.UpdatedAt, e.UpdatedBy);

    private static string Users(DictionaryType type, int count) =>
        (type == DictionaryType.CustomerStatus ? "customer" : "package") is var noun && count == 1 ? $"1 {noun}" : $"{count:N0} {noun}s";
}
