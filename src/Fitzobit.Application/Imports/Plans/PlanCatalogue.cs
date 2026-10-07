using Fitzobit.Application.Abstractions;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Imports.Plans;

/// <summary>Loads the active plans (with their catalogue context and tiers) and saved aliases from the database.</summary>
public sealed class PlanCatalogue(IFitzobitDbContext db)
{
    public async Task<IReadOnlyList<PlanInfo>> LoadPlansAsync(CancellationToken ct = default)
    {
        var plans = await db.Plans.AsNoTracking()
            .Where(p => p.Status == RecordStatus.Active)
            .Select(p => new
            {
                p.Id, p.Name, p.Code, p.Category,
                Term = p.CatalogueItem != null ? p.CatalogueItem.Term : null,
                Period = p.CatalogueItem != null ? (Domain.Catalogue.BillingPeriod?)p.CatalogueItem.BillingPeriod : null,
                Duration = p.CatalogueItem != null ? p.CatalogueItem.DurationText : null,
                Tiers = p.CatalogueItem != null
                    ? p.CatalogueItem.PricingTiers.Select(t => new { t.Id, t.TierName, t.BillingPeriod }).ToList()
                    : null
            })
            .ToListAsync(ct);

        return plans.Select(p => new PlanInfo(
                p.Id, p.Name, p.Code, p.Category, p.Term,
                p.Period is null or Domain.Catalogue.BillingPeriod.Unspecified ? null : p.Period.ToString(),
                p.Duration,
                (p.Tiers ?? []).Select(t => new PlanTierInfo(t.Id, t.TierName, t.BillingPeriod.ToString())).ToList()))
            .ToList();
    }

    public async Task<PlanMatcher> CreateMatcherAsync(CancellationToken ct = default)
    {
        var plans = await LoadPlansAsync(ct);
        var aliases = await db.PlanAliases.AsNoTracking()
            .Select(a => new SavedPlanAlias(a.AliasKey, a.PlanId, a.PricingTierId))
            .ToListAsync(ct);
        return new PlanMatcher(plans, aliases);
    }
}

/// <summary>The row-level issues implied by a plan mapping.</summary>
public static class PlanIssues
{
    public static IEnumerable<ImportIssue> For(string? rawPlan, ImportPlanMapping? mapping, Func<int, string> planName)
    {
        if (string.IsNullOrWhiteSpace(rawPlan))
        {
            yield return new(ImportIssueCodes.PlanMissing, IssueSeverity.Error, "Plan is missing", "Plan");
            yield break;
        }

        if (mapping is null || mapping.Status == PlanMappingStatus.Unknown)
        {
            yield return new(ImportIssueCodes.PlanUnknown, IssueSeverity.Error,
                $"Unknown Fitzobit plan \"{rawPlan.Trim()}\". Map it to a plan in the Classify step", "Plan");
            yield break;
        }

        if (mapping.Status == PlanMappingStatus.Ambiguous)
        {
            yield return new(ImportIssueCodes.PlanAmbiguous, IssueSeverity.Error,
                $"Plan \"{rawPlan.Trim()}\" matches several Fitzobit plans. Choose the correct one in the Classify step", "Plan");
            yield break;
        }

        if (mapping.Method == PlanMatchMethod.Keywords)
            yield return new(ImportIssueCodes.PlanKeywordMatch, IssueSeverity.Warning,
                $"Plan matched by keywords to \"{planName(mapping.PlanId!.Value)}\". Confirm it in the Classify step", "Plan");

        if (mapping.Note is not null && mapping.Method != PlanMatchMethod.Manual)
            yield return new(ImportIssueCodes.PlanVariantUnclear, IssueSeverity.Warning, mapping.Note, "Plan");
    }
}
