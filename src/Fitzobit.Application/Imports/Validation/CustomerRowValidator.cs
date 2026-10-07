using Fitzobit.Application.Imports.Locations;
using Fitzobit.Application.Imports.Plans;
using Fitzobit.Domain.Imports;

namespace Fitzobit.Application.Imports.Validation;

/// <summary>An existing customer, as needed for matching uploaded rows.</summary>
public sealed record ExistingCustomerInfo(
    int Id,
    string CustomerId,
    string CustomerName,
    string? NormalizedPhoneNumber,
    string? NormalizedLocation,
    int? PlanId,
    int? ActiveGroupId);

/// <summary>Validation context shared by every row of one file.</summary>
public sealed class RowValidationContext
{
    public required IReadOnlyDictionary<string, ExistingCustomerInfo> ExistingById { get; init; }
    public required ILookup<string, ExistingCustomerInfo> ExistingByPhone { get; init; }

    /// <summary>CustomerID (case-insensitive) → Excel row numbers where it appears.</summary>
    public required IReadOnlyDictionary<string, List<int>> RowsByCustomerId { get; init; }

    /// <summary>Normalized phone → (row number, CustomerID) for every row with a usable phone.</summary>
    public required ILookup<string, (int RowNumber, string? CustomerId)> RowsByPhone { get; init; }

    public required IReadOnlyDictionary<string, ImportPlanMapping> PlanMappings { get; init; }
    public required Func<int, string> PlanName { get; init; }
}

/// <summary>Validates one row and fills the normalized fields of its <see cref="ImportRow"/>.</summary>
public sealed class CustomerRowValidator(ILocationClassificationService locations)
{
    public const int MaxCustomerIdLength = 50;
    public const int MaxNameLength = 150;
    public const string SamplePrefix = "SAMPLE-";

    private static readonly HashSet<string> PlaceholderNames =
        new(["na", "n/a", "nil", "null", "none", "test", "xxx", "-", "--", "unknown", "customer", "name"], StringComparer.OrdinalIgnoreCase);

    public ImportRow Validate(RawCustomerRow raw, RowValidationContext context)
    {
        var row = new ImportRow
        {
            RowNumber = raw.RowNumber,
            RawCustomerId = raw.CustomerId,
            RawCustomerName = raw.CustomerName,
            RawPhoneNumber = raw.PhoneNumber,
            RawLocation = raw.Location,
            RawPlan = raw.Plan
        };
        var issues = new List<ImportIssue>();

        ValidateCustomerId(raw, row, context, issues);
        ValidateName(raw, row, issues);
        ValidatePhone(raw, row, context, issues);
        ValidateLocation(raw, row, issues);

        row.PlanKey = string.IsNullOrWhiteSpace(raw.Plan) ? null : Plans.PlanTextNormalizer.Key(raw.Plan);
        var mapping = row.PlanKey is null ? null : context.PlanMappings.GetValueOrDefault(row.PlanKey);
        if (mapping?.Status == PlanMappingStatus.Mapped)
        {
            row.PlanId = mapping.PlanId;
            row.PricingTierId = mapping.PricingTierId;
        }
        issues.AddRange(PlanIssues.For(raw.Plan, mapping, context.PlanName));

        if (row.IsExistingCustomer)
            issues.Add(ExistingCustomerIssue(row, context.ExistingById[row.CustomerId!]));

        foreach (var note in raw.CellNotes)
            issues.Add(new(ImportIssueCodes.CellFormula, IssueSeverity.Warning, note));

        ImportRowIssues.Write(row, issues);
        return row;
    }

    private static void ValidateCustomerId(RawCustomerRow raw, ImportRow row, RowValidationContext context, List<ImportIssue> issues)
    {
        var id = raw.CustomerId?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            issues.Add(new(ImportIssueCodes.CustomerIdMissing, IssueSeverity.Error, "CustomerID is missing", "CustomerID"));
            return;
        }

        row.CustomerId = id;
        if (id.Length > MaxCustomerIdLength)
            issues.Add(new(ImportIssueCodes.CustomerIdTooLong, IssueSeverity.Error,
                $"CustomerID is longer than {MaxCustomerIdLength} characters", "CustomerID"));

        if (id.StartsWith(SamplePrefix, StringComparison.OrdinalIgnoreCase))
            issues.Add(new(ImportIssueCodes.SampleRow, IssueSeverity.Error,
                "Sample row from the template. Delete it before uploading", "CustomerID"));

        if (context.RowsByCustomerId.TryGetValue(id, out var rows) && rows.Count > 1)
        {
            var others = string.Join(", ", rows.Where(r => r != raw.RowNumber));
            issues.Add(new(ImportIssueCodes.CustomerIdDuplicate, IssueSeverity.Error,
                $"Duplicate CustomerID (also on row {others})", "CustomerID"));
        }

        if (context.ExistingById.TryGetValue(id, out var existing))
        {
            row.IsExistingCustomer = true;
            row.ExistingCustomerDbId = existing.Id;
            row.ExistingGroupId = existing.ActiveGroupId;
        }
    }

    private static void ValidateName(RawCustomerRow raw, ImportRow row, List<ImportIssue> issues)
    {
        var name = raw.CustomerName is null ? null : string.Join(' ', raw.CustomerName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrEmpty(name))
        {
            issues.Add(new(ImportIssueCodes.NameMissing, IssueSeverity.Error, "CustomerName is missing", "CustomerName"));
            return;
        }

        row.CustomerName = name;
        if (name.Length > MaxNameLength)
            issues.Add(new(ImportIssueCodes.NameTooLong, IssueSeverity.Error, $"CustomerName is longer than {MaxNameLength} characters", "CustomerName"));
        else if (name.Length < 2 || !name.Any(char.IsLetter) || PlaceholderNames.Contains(name))
            issues.Add(new(ImportIssueCodes.NameInvalid, IssueSeverity.Error, $"CustomerName \"{name}\" does not look like a name", "CustomerName"));
        else if (name.Any(char.IsDigit))
            issues.Add(new(ImportIssueCodes.NameHasDigits, IssueSeverity.Warning, "CustomerName contains digits", "CustomerName"));
    }

    private static void ValidatePhone(RawCustomerRow raw, ImportRow row, RowValidationContext context, List<ImportIssue> issues)
    {
        var result = PhoneNumberValidator.Validate(raw.PhoneNumber);
        switch (result.Check)
        {
            case PhoneCheck.Missing:
                issues.Add(new(ImportIssueCodes.PhoneMissing, IssueSeverity.Error, "PhoneNumber is missing", "PhoneNumber"));
                return;
            case PhoneCheck.ScientificNotation:
                issues.Add(new(ImportIssueCodes.PhoneScientific, IssueSeverity.Error,
                    "Invalid phone number: Excel converted it to scientific notation. Format the column as Text and re-enter it", "PhoneNumber"));
                return;
            case PhoneCheck.ContainsLetters:
            case PhoneCheck.Invalid:
                issues.Add(new(ImportIssueCodes.PhoneInvalid, IssueSeverity.Error,
                    "Invalid phone number: expected a 10-digit Indian mobile number", "PhoneNumber"));
                return;
            case PhoneCheck.Landline:
                issues.Add(new(ImportIssueCodes.PhoneLandline, IssueSeverity.Warning, "Looks like a landline, not a mobile number", "PhoneNumber"));
                break;
        }

        row.NormalizedPhoneNumber = result.Normalized;
        var id = row.CustomerId;

        var sameInFile = context.RowsByPhone[result.Normalized!]
            .Where(r => r.RowNumber != raw.RowNumber && !string.Equals(r.CustomerId, id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var sameInDb = context.ExistingByPhone[result.Normalized!]
            .Where(c => !string.Equals(c.CustomerId, id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sameInFile.Count + sameInDb.Count == 0) return;

        row.IsPotentialDuplicate = true;
        var parts = sameInFile.Select(r => $"{r.CustomerId ?? "(no ID)"} on row {r.RowNumber}")
            .Concat(sameInDb.Select(c => $"existing customer {c.CustomerId}"));
        issues.Add(new(ImportIssueCodes.PotentialDuplicate, IssueSeverity.Warning,
            $"Possible duplicate: same phone as {string.Join(", ", parts)}", "PhoneNumber"));
    }

    private void ValidateLocation(RawCustomerRow raw, ImportRow row, List<ImportIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(raw.Location))
        {
            issues.Add(new(ImportIssueCodes.LocationMissing, IssueSeverity.Error, "Location is missing", "Location"));
            return;
        }

        var c = locations.Classify(raw.Location);
        row.NormalizedLocation = c.NormalizedLocation;
        row.Pincode = c.Pincode;
        row.Locality = c.Locality;
        row.City = c.City;

        if (c.PincodeStatus == PincodeStatus.NotDetected)
            issues.Add(new(ImportIssueCodes.PincodeNotDetected, IssueSeverity.Warning, "Pincode not detected", "Location"));
        else if (c.PincodeStatus == PincodeStatus.Multiple)
            issues.Add(new(ImportIssueCodes.PincodeMultiple, IssueSeverity.Warning,
                $"Several possible pincodes ({string.Join(", ", c.PincodeCandidates)}); none was used", "Location"));
    }

    private static ImportIssue ExistingCustomerIssue(ImportRow row, ExistingCustomerInfo existing)
    {
        var differences = new List<string>();
        if (!string.Equals(existing.CustomerName, row.CustomerName, StringComparison.OrdinalIgnoreCase)) differences.Add("name");
        if (row.NormalizedPhoneNumber is not null && existing.NormalizedPhoneNumber != row.NormalizedPhoneNumber) differences.Add("phone");
        if (row.NormalizedLocation is not null && existing.NormalizedLocation != row.NormalizedLocation) differences.Add("location");
        if (row.PlanId is not null && existing.PlanId != row.PlanId) differences.Add("plan");

        var message = differences.Count == 0
            ? "Existing Customer. Saved details are kept"
            : $"Existing Customer. Saved details are kept; the file has a different {string.Join(", ", differences)}";
        return new(ImportIssueCodes.ExistingCustomer, IssueSeverity.Warning, message, "CustomerID");
    }
}
