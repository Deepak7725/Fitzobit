using Fitzobit.Application.Imports;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Imports;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests.Imports;

public class ImportValidationTests
{
    private const string Regular6 = "Regular Fit Box - 6 Days";

    [Fact]
    public async Task Rows_are_validated_individually_and_partial_files_are_kept()
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);
        await using (var db = test.CreateContext())
        {
            var existing = new Customer { CustomerId = "CUST900", CustomerName = "Existing Person" };
            existing.SetPhoneNumber("9811111111");
            existing.SetLocation("Anna Nagar, Chennai - 600040");
            db.Customers.Add(existing);
            await db.SaveChangesAsync();
        }

        using var file = TestWorkbook.Create(TestWorkbook.StandardHeaders,
            ["CUST001", "Valid Person", "9876543210", "RS Puram, Coimbatore - 641002", Regular6],                 // 2 valid
            [null, "No Id", "9876543211", "Thanjavur 613001", Regular6],                                         // 3 error: id missing
            ["CUST003", "Dup One", "9876543212", "Thanjavur 613001", Regular6],                                  // 4 error: duplicate
            ["cust003", "Dup Two", "9876543213", "Thanjavur 613001", Regular6],                                  // 5 error: duplicate (case)
            ["CUST005", "Bad Phone", "12345", "Thanjavur 613001", Regular6],                                     // 6 error: phone
            ["CUST006", "No Pin", "9876543214", "Near bus stand, Kumbakonam", Regular6],                         // 7 warning: pincode
            ["CUST007", "Odd Plan", "9876543215", "Thanjavur 613001", "Gold Membership"],                        // 8 error: unknown plan
            ["CUST900", "Existing Person", "9811111111", "Anna Nagar, Chennai - 600040", Regular6],              // 9 warning: existing
            ["CUST009", "Same Phone", "9876543214", "Madurai 625001", Regular6],                                 // 10 warning: possible duplicate of row 7
            ["SAMPLE-001", "Sample Customer A", "9000000001", "Chennai 600001", Regular6]);                      // 11 error: sample row

        var batchId = await harness.UploadAsync(file);
        var rows = (await harness.RowsAsync(batchId)).ToDictionary(r => r.RowNumber);

        Assert.Equal("Valid", rows[2].Status);
        Assert.Equal("641002", rows[2].Pincode);
        Assert.Equal("RS Puram, Coimbatore - 641002", rows[2].Location);
        AssertIssue(rows[3], ImportIssueCodes.CustomerIdMissing, "Error", "CustomerID is missing");
        AssertIssue(rows[4], ImportIssueCodes.CustomerIdDuplicate, "Error", "Duplicate CustomerID");
        AssertIssue(rows[5], ImportIssueCodes.CustomerIdDuplicate, "Error", "Duplicate CustomerID");
        AssertIssue(rows[6], ImportIssueCodes.PhoneInvalid, "Error", "Invalid phone number");
        AssertIssue(rows[7], ImportIssueCodes.PincodeNotDetected, "Warning", "Pincode not detected");
        AssertIssue(rows[8], ImportIssueCodes.PlanUnknown, "Error", "Unknown Fitzobit plan");
        AssertIssue(rows[9], ImportIssueCodes.ExistingCustomer, "Warning", "Existing Customer");
        Assert.True(rows[9].IsExistingCustomer);
        AssertIssue(rows[10], ImportIssueCodes.PotentialDuplicate, "Warning", "same phone as CUST006 on row 7");
        Assert.Contains(rows[7].Issues, i => i.Code == ImportIssueCodes.PotentialDuplicate);
        AssertIssue(rows[11], ImportIssueCodes.SampleRow, "Error", "Sample row");

        await using var check = test.CreateContext();
        var summary = await new ImportQueryService(check).GetSummaryAsync(batchId);
        Assert.Equal(10, summary.TotalRows);
        Assert.Equal(1, summary.ValidRows);
        Assert.Equal(3, summary.WarningRows);
        Assert.Equal(6, summary.ErrorRows);
        Assert.Equal(4, summary.ToImport);
        Assert.Equal(3, summary.NewCustomers);
        Assert.Equal(1, summary.ExistingCustomers);
        Assert.Equal(1, summary.UnresolvedPlanMappings);

        // Validation never touches the customer tables.
        Assert.Equal(1, await check.Customers.CountAsync());
    }

    [Fact]
    public async Task Unreadable_files_are_recorded_as_failed_imports()
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);
        await using var db = test.CreateContext();
        using var file = TestWorkbook.Create(["CustomerID", "Name", "Phone"], ["C1", "Some Person", "9000000001"]);

        var result = await harness.Upload(db).UploadAsync(file, "missing-columns.xlsx", file.Length);

        Assert.Equal("Failed", result.Status);
        Assert.NotEmpty(result.Errors);
        var batch = await db.ImportBatches.SingleAsync();
        Assert.Equal(ImportBatchStatus.Failed, batch.Status);
        Assert.Contains("Missing required column", batch.FailureReason);
    }

    [Theory]
    [InlineData("customers.csv", "Only Excel workbooks")]
    [InlineData("customers.xlsm", "not accepted")]
    [InlineData("customers.xls", "not accepted")]
    public async Task Wrong_file_types_are_rejected(string name, string message)
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);
        await using var db = test.CreateContext();
        using var file = TestWorkbook.Create(TestWorkbook.StandardHeaders, ["C1", "A B", "9000000001", "Chennai 600001", Regular6]);

        var result = await harness.Upload(db).UploadAsync(file, name, file.Length);

        Assert.Equal("Failed", result.Status);
        Assert.Contains(result.Errors, e => e.Contains(message));
    }

    [Fact]
    public async Task Mapping_an_unknown_plan_fixes_its_rows()
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);
        using var file = TestWorkbook.Create(TestWorkbook.StandardHeaders,
            ["C1", "Person One", "9876500001", "Thanjavur 613001", "Medium Fit Box"],
            ["C2", "Person Two", "9876500002", "Thanjavur 613001", "medium fit box"]);
        var batchId = await harness.UploadAsync(file);

        Assert.All(await harness.RowsAsync(batchId), r => Assert.Contains(r.Issues, i => i.Code == ImportIssueCodes.PlanAmbiguous));

        await using (var db = test.CreateContext())
        {
            var mapping = Assert.Single(await harness.Queries(db).GetPlanMappingsAsync(batchId));
            Assert.Equal(2, mapping.RowCount);
            Assert.True(mapping.CandidatePlanIds.Count >= 2);
            var plan = await db.Plans.SingleAsync(p => p.Name == "Medium Fit Box - 6 Days");
            var veg = await db.CataloguePricingTiers.SingleAsync(t => t.CatalogueItemId == plan.CatalogueItemId && t.TierName == "Veg Plan");
            await harness.Workflow(db).UpdatePlanMappingAsync(batchId, mapping.Id, plan.Id, veg.Id);
        }

        var rows = await harness.RowsAsync(batchId);
        Assert.All(rows, r =>
        {
            Assert.Equal("Valid", r.Status);
            Assert.Equal("Medium Fit Box - 6 Days", r.PlanName);
            Assert.Equal("Veg Plan", r.TierName);
            Assert.Equal("Medium Fit Box", r.Plan, ignoreCase: true);
        });
    }

    private static void AssertIssue(ImportRowDto row, string code, string status, string messagePart)
    {
        Assert.Equal(status, row.Status);
        var issue = Assert.Single(row.Issues, i => i.Code == code);
        Assert.Contains(messagePart, issue.Message, StringComparison.OrdinalIgnoreCase);
    }
}
