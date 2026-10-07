using Fitzobit.Domain.Catalogue;
using Fitzobit.Domain.Common;

namespace Fitzobit.Domain.Plans;

/// <summary>
/// A plan a customer can be subscribed to. Database-driven; may be linked to the catalogue item it was derived from.
/// </summary>
public class Plan : ITimestamped
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Description { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;

    public int? CatalogueItemId { get; set; }
    public CatalogueItem? CatalogueItem { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
