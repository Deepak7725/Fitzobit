using Fitzobit.Domain.Imports;

namespace Fitzobit.Application.Imports;

/// <summary>Provider-neutral query building blocks for staged import rows.</summary>
public static class ImportRowQueries
{
    /// <summary>Rows that will be imported: no errors and not excluded by the manager.</summary>
    public static IQueryable<ImportRow> Included(this IQueryable<ImportRow> rows) =>
        rows.Where(r => r.Status != ImportRowStatus.Error && !r.ExcludedByManager);

    public static IQueryable<ImportRow> ApplyFilter(this IQueryable<ImportRow> rows, ImportRowFilter? f)
    {
        if (f is null) return rows;

        rows = f.Status switch
        {
            "Valid" => rows.Where(r => r.Status == ImportRowStatus.Valid),
            "Warning" => rows.Where(r => r.Status == ImportRowStatus.Warning),
            "Error" => rows.Where(r => r.Status == ImportRowStatus.Error),
            "Included" => rows.Included(),
            "Excluded" => rows.Where(r => r.Status == ImportRowStatus.Error || r.ExcludedByManager),
            _ => rows
        };

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim().ToLower();
            rows = rows.Where(r =>
                (r.CustomerId != null && r.CustomerId.ToLower().Contains(s)) ||
                (r.CustomerName != null && r.CustomerName.ToLower().Contains(s)) ||
                (r.RawPhoneNumber != null && r.RawPhoneNumber.Contains(s)) ||
                (r.NormalizedPhoneNumber != null && r.NormalizedPhoneNumber.Contains(s)) ||
                (r.RawLocation != null && r.RawLocation.ToLower().Contains(s)) ||
                (r.RawPlan != null && r.RawPlan.ToLower().Contains(s)));
        }

        if (f.Pincode == "none") rows = rows.Where(r => r.Pincode == null);
        else if (!string.IsNullOrWhiteSpace(f.Pincode)) rows = rows.Where(r => r.Pincode == f.Pincode);

        if (!string.IsNullOrWhiteSpace(f.Locality))
        {
            var locality = f.Locality.ToLower();
            rows = rows.Where(r => r.Locality != null && r.Locality.ToLower() == locality);
        }

        if (!string.IsNullOrWhiteSpace(f.City))
        {
            var city = f.City.ToLower();
            rows = rows.Where(r => r.City != null && r.City.ToLower() == city);
        }

        if (!string.IsNullOrWhiteSpace(f.Location)) rows = rows.Where(r => r.NormalizedLocation == f.Location);

        if (f.Group == "none")
            rows = rows.Where(r => r.TargetDeliveryGroupId == null && r.TargetDraftGroupId == null);
        else if (GroupKey.Parse(f.Group) is { } key)
            rows = key.DeliveryGroupId is { } g
                ? rows.Where(r => r.TargetDeliveryGroupId == g)
                : rows.Where(r => r.TargetDraftGroupId == key.DraftGroupId);

        if (f.PlanId is { } planId) rows = rows.Where(r => r.PlanId == planId);

        if (!string.IsNullOrWhiteSpace(f.IssueCode))
        {
            var token = ";" + f.IssueCode + ";";
            rows = rows.Where(r => r.IssueCodes.Contains(token));
        }

        if (f.Existing is { } existing) rows = rows.Where(r => r.IsExistingCustomer == existing);
        if (f.HasSuggestion == true) rows = rows.Where(r => r.SuggestedDeliveryGroupId != null || r.SuggestedDraftGroupId != null);

        return rows;
    }

    /// <summary>Included rows targeted by a selection (explicit ids are still restricted to included rows of the batch).</summary>
    public static IQueryable<ImportRow> Select(this IQueryable<ImportRow> batchRows, ImportRowSelection selection)
    {
        var rows = batchRows.Included();
        if (selection.RowIds is { Count: > 0 } ids) return rows.Where(r => ids.Contains(r.Id));
        if (selection.Filter is not null) return rows.ApplyFilter(selection.Filter);
        throw new Common.BusinessRuleException("Select at least one customer.");
    }
}
