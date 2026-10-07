using Fitzobit.Application.Abstractions;
using Fitzobit.Domain.Catalogue;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Catalogue;

public sealed record CataloguePricingTierDto(
    int Id, string TierName, decimal Price, string Currency, string BillingPeriod, int? MealCount, string? Description, string? Source);

public sealed record CatalogueItemDto(
    int Id,
    string Name,
    string? Category,
    string? Term,
    string? Description,
    decimal DisplayPrice,
    string Currency,
    string BillingPeriod,
    string? DurationText,
    int? DurationDays,
    int? MealCount,
    string? IncludedItems,
    string? Availability,
    int DisplayOrder,
    string? Source,
    string? SourceNotes,
    string? PlanCode,
    IReadOnlyList<CataloguePricingTierDto> PricingTiers,
    IReadOnlyList<CatalogueWarning> Warnings);

public sealed record CatalogueCategorySummaryDto(string Name, int ItemCount, int WarningCount);

public sealed record CatalogueOverviewDto(
    IReadOnlyList<CatalogueItemDto> Items,
    IReadOnlyList<CatalogueCategorySummaryDto> Categories,
    int TotalItems,
    int TotalPricingTiers,
    int ItemsWithWarnings,
    string? Source,
    DateTime? LastUpdatedAt);

public sealed record PlanDto(
    int Id, string Name, string Code, string? Category, string? Description, string Status, int? CatalogueItemId, int CustomerCount);

public sealed class CatalogueService(IFitzobitDbContext db)
{
    public const string Uncategorised = "Uncategorised";

    public async Task<CatalogueOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var items = await db.CatalogueItems.AsNoTracking()
            .Include(i => i.PricingTiers)
            .ToListAsync(cancellationToken);
        items = items.OrderBy(i => i.DisplayOrder).ThenBy(i => i.Name).ToList();

        var planCodes = await db.Plans.AsNoTracking()
            .Where(p => p.CatalogueItemId != null)
            .Select(p => new { p.CatalogueItemId, p.Code })
            .ToListAsync(cancellationToken);
        var planCodeByItem = planCodes
            .GroupBy(p => p.CatalogueItemId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Code);

        var dtos = items.Select(i => ToDto(i, items, planCodeByItem.GetValueOrDefault(i.Id))).ToList();

        var categories = dtos
            .GroupBy(d => d.Category ?? Uncategorised)
            .Select(g => new CatalogueCategorySummaryDto(g.Key, g.Count(), g.Count(d => d.Warnings.Count > 0)))
            .ToList();

        return new CatalogueOverviewDto(
            dtos,
            categories,
            dtos.Count,
            dtos.Sum(d => d.PricingTiers.Count),
            dtos.Count(d => d.Warnings.Count > 0),
            items.Select(i => i.Source).FirstOrDefault(s => s != null),
            items.Count == 0 ? null : items.Max(i => i.UpdatedAt));
    }

    public async Task<IReadOnlyList<PlanDto>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = await db.Plans.AsNoTracking()
            .Select(p => new PlanDto(p.Id, p.Name, p.Code, p.Category, p.Description, p.Status.ToString(), p.CatalogueItemId,
                db.Customers.Count(c => c.PlanId == p.Id)))
            .ToListAsync(cancellationToken);
        return plans.OrderBy(p => p.Category).ThenBy(p => p.Name).ToList();
    }

    private static CatalogueItemDto ToDto(CatalogueItem item, IReadOnlyList<CatalogueItem> all, string? planCode) =>
        new(item.Id, item.Name, item.Category, item.Term, item.Description, item.DisplayPrice, item.Currency,
            item.BillingPeriod.ToString(), item.DurationText, item.DurationDays, item.MealCount, item.IncludedItems,
            item.Availability, item.DisplayOrder, item.Source, item.SourceNotes, planCode,
            item.PricingTiers.OrderBy(t => t.BillingPeriod == BillingPeriod.Monthly ? 0 : 1).ThenBy(t => t.Id)
                .Select(t => new CataloguePricingTierDto(t.Id, t.TierName, t.Price, t.Currency, t.BillingPeriod.ToString(),
                    t.MealCount, t.Description, t.Source))
                .ToList(),
            CatalogueWarningEvaluator.Evaluate(item, all));
}
