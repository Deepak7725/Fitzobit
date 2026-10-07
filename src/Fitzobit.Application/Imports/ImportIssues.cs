using System.Text.Json;
using Fitzobit.Domain.Imports;

namespace Fitzobit.Application.Imports;

public static class ImportIssueCodes
{
    public const string CustomerIdMissing = "CUSTOMER_ID_MISSING";
    public const string CustomerIdTooLong = "CUSTOMER_ID_TOO_LONG";
    public const string CustomerIdDuplicate = "CUSTOMER_ID_DUPLICATE";
    public const string SampleRow = "SAMPLE_ROW";
    public const string ExistingCustomer = "EXISTING_CUSTOMER";
    public const string NameMissing = "NAME_MISSING";
    public const string NameInvalid = "NAME_INVALID";
    public const string NameTooLong = "NAME_TOO_LONG";
    public const string NameHasDigits = "NAME_HAS_DIGITS";
    public const string PhoneMissing = "PHONE_MISSING";
    public const string PhoneInvalid = "PHONE_INVALID";
    public const string PhoneScientific = "PHONE_SCIENTIFIC_NOTATION";
    public const string PhoneLandline = "PHONE_LANDLINE";
    public const string PotentialDuplicate = "POTENTIAL_DUPLICATE";
    public const string LocationMissing = "LOCATION_MISSING";
    public const string PincodeNotDetected = "PINCODE_NOT_DETECTED";
    public const string PincodeMultiple = "PINCODE_MULTIPLE";
    public const string PlanMissing = "PLAN_MISSING";
    public const string PlanUnknown = "PLAN_UNKNOWN";
    public const string PlanAmbiguous = "PLAN_AMBIGUOUS";
    public const string PlanKeywordMatch = "PLAN_KEYWORD_MATCH";
    public const string PlanVariantUnclear = "PLAN_VARIANT_UNCLEAR";
    public const string CellFormula = "CELL_FORMULA";

    public const string PlanPrefix = "PLAN_";
}

/// <summary>Stores a row's issues and derives its status: any error → Error, else any warning → Warning, else Valid.</summary>
public static class ImportRowIssues
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static List<ImportIssue> Read(ImportRow row) =>
        JsonSerializer.Deserialize<List<ImportIssue>>(row.IssuesJson, Json) ?? [];

    public static List<ImportIssue> Parse(string issuesJson) =>
        JsonSerializer.Deserialize<List<ImportIssue>>(issuesJson, Json) ?? [];

    public static void Write(ImportRow row, IReadOnlyCollection<ImportIssue> issues)
    {
        row.IssuesJson = JsonSerializer.Serialize(issues, Json);
        row.IssueCodes = ";" + string.Concat(issues.Select(i => i.Code + ";"));
        row.Status = issues.Any(i => i.Severity == IssueSeverity.Error) ? ImportRowStatus.Error
            : issues.Count > 0 ? ImportRowStatus.Warning
            : ImportRowStatus.Valid;
    }

    /// <summary>Replaces the plan-related issues of a row (used when the manager changes a plan mapping).</summary>
    public static void ReplacePlanIssues(ImportRow row, IEnumerable<ImportIssue> planIssues)
    {
        var issues = Read(row).Where(i => !i.Code.StartsWith(ImportIssueCodes.PlanPrefix, StringComparison.Ordinal)).ToList();
        issues.AddRange(planIssues);
        Write(row, issues);
    }
}
