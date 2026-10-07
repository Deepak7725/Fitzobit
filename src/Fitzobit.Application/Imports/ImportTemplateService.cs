namespace Fitzobit.Application.Imports;

/// <summary>Builds the customer upload template, with the plan list taken from the database.</summary>
public sealed class ImportTemplateService(ImportQueryService queries, ICustomerTemplateGenerator generator)
{
    public const string FileName = "Fitzobit_Customer_Upload_Template.xlsx";

    public async Task<byte[]> CreateAsync(CancellationToken ct = default)
    {
        var plans = await queries.GetPlanOptionsAsync(ct);
        return generator.Generate(plans
            .Select(p => new TemplatePlan(p.Name, p.Category,
                p.Tiers.Count == 0 ? null : string.Join(", ", p.Tiers.Select(t => t.Name).Distinct())))
            .ToList());
    }
}
