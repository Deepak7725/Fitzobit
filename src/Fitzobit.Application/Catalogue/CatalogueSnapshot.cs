namespace Fitzobit.Application.Catalogue;

/// <summary>A point-in-time copy of the external catalogue (currently the WhatsApp catalogue Excel export).</summary>
public sealed record CatalogueSnapshot(string Source, IReadOnlyList<CatalogueSnapshotItem> Items);

public sealed record CatalogueSnapshotItem(
    int DisplayOrder,
    string Name,
    string? Category,
    string? Term,
    string? Description,
    decimal DisplayPrice,
    string Currency,
    string? DurationText,
    string? IncludedItems,
    string? Availability,
    string? SourceNotes,
    IReadOnlyList<CatalogueSnapshotTier> Tiers);

public sealed record CatalogueSnapshotTier(
    string TierName,
    decimal Price,
    string Currency,
    string? PeriodText,
    string? MealsText,
    string? Description,
    string? Source);

/// <summary>Reads the catalogue snapshot from wherever it lives. Returns null when no source is available.</summary>
public interface ICatalogueSource
{
    Task<CatalogueSnapshot?> LoadAsync(CancellationToken cancellationToken = default);
}
