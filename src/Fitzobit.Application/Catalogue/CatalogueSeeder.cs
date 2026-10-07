using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Catalogue;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fitzobit.Application.Catalogue;

public sealed record CatalogueSeedResult(
    bool Skipped,
    string? SkipReason,
    int ItemsCreated,
    int ItemsUpdated,
    int ItemsUnchanged,
    int TiersCreated,
    int TiersUpdated,
    int TiersRemoved,
    int PlansCreated,
    int ItemsNotInSource)
{
    public static CatalogueSeedResult SkippedBecause(string reason) => new(true, reason, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Synchronises the catalogue tables with the source snapshot. Idempotent: items are matched by their exact
/// name and tiers by (item, tier name), so running it repeatedly never creates duplicates. Source values are
/// copied as-is, including apparent inconsistencies; warnings are computed separately and never "fix" data.
/// Items that disappear from the source are kept (and counted) rather than deleted.
/// </summary>
public sealed class CatalogueSeeder(
    IFitzobitDbContext db,
    ICatalogueSource source,
    IAuditLogger audit,
    ILogger<CatalogueSeeder> logger)
{
    public async Task<CatalogueSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await source.LoadAsync(cancellationToken);
        if (snapshot is null)
        {
            logger.LogWarning("Catalogue seed skipped: no catalogue source available.");
            return CatalogueSeedResult.SkippedBecause("No catalogue source available");
        }

        var existing = await db.CatalogueItems.Include(i => i.PricingTiers).ToListAsync(cancellationToken);
        var byName = existing.ToDictionary(i => i.Name, StringComparer.Ordinal);

        int created = 0, updated = 0, unchanged = 0, tiersCreated = 0, tiersUpdated = 0, tiersRemoved = 0;
        var seededItems = new List<CatalogueItem>();

        foreach (var src in snapshot.Items)
        {
            if (!byName.TryGetValue(src.Name, out var item))
            {
                item = new CatalogueItem { Name = src.Name };
                Apply(item, src, snapshot.Source);
                foreach (var tierSrc in src.Tiers)
                {
                    item.PricingTiers.Add(NewTier(tierSrc, item.BillingPeriod));
                    tiersCreated++;
                }
                db.CatalogueItems.Add(item);
                byName[item.Name] = item;
                created++;
                audit.Record(AuditActions.CatalogueItemCreated, nameof(CatalogueItem), item.Name,
                    $"Catalogue item imported: {item.Name}",
                    new { item.DisplayPrice, item.Currency, Tiers = src.Tiers.Count, Source = snapshot.Source });
            }
            else
            {
                var changedFields = Apply(item, src, snapshot.Source);
                var (tc, tu, tr) = SyncTiers(item, src.Tiers);
                tiersCreated += tc; tiersUpdated += tu; tiersRemoved += tr;

                if (changedFields.Count > 0 || tc + tu + tr > 0)
                {
                    updated++;
                    audit.Record(AuditActions.CatalogueItemChanged, nameof(CatalogueItem), item.Name,
                        $"Catalogue item updated from source: {item.Name}",
                        new { ChangedFields = changedFields, TiersCreated = tc, TiersUpdated = tu, TiersRemoved = tr, Source = snapshot.Source });
                }
                else
                {
                    unchanged++;
                }
            }

            seededItems.Add(item);
        }

        var plansCreated = await EnsurePlansAsync(seededItems, cancellationToken);
        var notInSource = existing.Count(e => snapshot.Items.All(s => s.Name != e.Name));

        await db.SaveChangesAsync(cancellationToken);

        var result = new CatalogueSeedResult(false, null, created, updated, unchanged, tiersCreated, tiersUpdated, tiersRemoved, plansCreated, notInSource);
        logger.LogInformation("Catalogue seed from {Source}: {@Result}", snapshot.Source, result);
        return result;
    }

    /// <summary>Copies source values onto the entity and returns the names of fields that actually changed.</summary>
    private static List<string> Apply(CatalogueItem item, CatalogueSnapshotItem src, string source)
    {
        var changed = new List<string>();
        void Set<T>(string field, T current, T value, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return;
            assign(value);
            changed.Add(field);
        }

        var period = CatalogueTextParser.ParseItemPeriod(src.DurationText, src.Term, src.Name);
        Set(nameof(item.Category), item.Category, src.Category, v => item.Category = v);
        Set(nameof(item.Term), item.Term, src.Term, v => item.Term = v);
        Set(nameof(item.Description), item.Description, src.Description, v => item.Description = v);
        Set(nameof(item.DisplayPrice), item.DisplayPrice, src.DisplayPrice, v => item.DisplayPrice = v);
        Set(nameof(item.Currency), item.Currency, src.Currency, v => item.Currency = v);
        Set(nameof(item.BillingPeriod), item.BillingPeriod, period, v => item.BillingPeriod = v);
        Set(nameof(item.DurationText), item.DurationText, src.DurationText, v => item.DurationText = v);
        Set(nameof(item.DurationDays), item.DurationDays, CatalogueTextParser.ParseDays(src.DurationText), v => item.DurationDays = v);
        Set(nameof(item.MealCount), item.MealCount, CatalogueTextParser.ParseMeals(src.DurationText), v => item.MealCount = v);
        Set(nameof(item.IncludedItems), item.IncludedItems, src.IncludedItems, v => item.IncludedItems = v);
        Set(nameof(item.Availability), item.Availability, src.Availability, v => item.Availability = v);
        Set(nameof(item.DisplayOrder), item.DisplayOrder, src.DisplayOrder, v => item.DisplayOrder = v);
        Set(nameof(item.Source), item.Source, source, v => item.Source = v);
        Set(nameof(item.SourceNotes), item.SourceNotes, src.SourceNotes, v => item.SourceNotes = v);
        return changed;
    }

    private static CataloguePricingTier NewTier(CatalogueSnapshotTier src, BillingPeriod parentPeriod)
    {
        var tier = new CataloguePricingTier { TierName = src.TierName };
        ApplyTier(tier, src, parentPeriod);
        return tier;
    }

    private static bool ApplyTier(CataloguePricingTier tier, CatalogueSnapshotTier src, BillingPeriod parentPeriod)
    {
        var period = CatalogueTextParser.ParseTierPeriod(src.PeriodText, src.TierName, parentPeriod);
        var meals = CatalogueTextParser.ParseMeals(src.MealsText);
        var changed = tier.Price != src.Price || tier.Currency != src.Currency || tier.BillingPeriod != period
                      || tier.MealCount != meals || tier.Description != src.Description || tier.Source != src.Source;
        tier.Price = src.Price;
        tier.Currency = src.Currency;
        tier.BillingPeriod = period;
        tier.MealCount = meals;
        tier.Description = src.Description;
        tier.Source = src.Source;
        return changed;
    }

    private (int Created, int Updated, int Removed) SyncTiers(CatalogueItem item, IReadOnlyList<CatalogueSnapshotTier> sourceTiers)
    {
        int created = 0, updated = 0, removed = 0;
        var byTierName = item.PricingTiers.ToDictionary(t => t.TierName, StringComparer.Ordinal);

        foreach (var src in sourceTiers)
        {
            if (byTierName.TryGetValue(src.TierName, out var tier))
            {
                if (ApplyTier(tier, src, item.BillingPeriod)) updated++;
            }
            else
            {
                item.PricingTiers.Add(NewTier(src, item.BillingPeriod));
                created++;
            }
        }

        foreach (var stale in item.PricingTiers.Where(t => sourceTiers.All(s => s.TierName != t.TierName)).ToList())
        {
            item.PricingTiers.Remove(stale);
            db.CataloguePricingTiers.Remove(stale);
            removed++;
        }

        return (created, updated, removed);
    }

    /// <summary>Creates one plan per catalogue item that has none yet. Existing plans are never overwritten.</summary>
    private async Task<int> EnsurePlansAsync(IReadOnlyList<CatalogueItem> items, CancellationToken cancellationToken)
    {
        var plans = await db.Plans.ToListAsync(cancellationToken);
        var linkedItemIds = plans.Where(p => p.CatalogueItemId != null).Select(p => p.CatalogueItemId!.Value).ToHashSet();
        var usedCodes = plans.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedNames = plans.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var createdCount = 0;
        foreach (var item in items)
        {
            if (item.Id != 0 && linkedItemIds.Contains(item.Id)) continue;
            if (usedNames.Contains(item.Name)) continue;

            var code = Normalization.ToCode(item.Name);
            for (var n = 2; usedCodes.Contains(code); n++) code = $"{Normalization.ToCode(item.Name)}-{n}";

            db.Plans.Add(new Plan
            {
                Name = item.Name,
                Code = code,
                Category = item.Category,
                Description = FirstLine(item.Description),
                Status = RecordStatus.Active,
                CatalogueItem = item
            });
            usedCodes.Add(code);
            usedNames.Add(item.Name);
            createdCount++;
            audit.Record(AuditActions.PlanCreated, nameof(Plan), code, $"Plan created from catalogue item: {item.Name}");
        }

        return createdCount;
    }

    private static string? FirstLine(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Split('\n', 2)[0].Trim();
}
