using Fitzobit.Application.Common;
using Fitzobit.Application.Imports;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.DeliveryGroups;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests.Imports;

/// <summary>Classification, draft groups, assignment (individual / bulk / by pincode / by area) and suggestions.</summary>
public class ImportWorkflowTests
{
    private const string Regular6 = "Regular Fit Box - 6 Days";
    private const string Large6 = "Large Fit Box - 6 Days";

    private static MemoryStream TenCustomers() => TestWorkbook.Create(TestWorkbook.StandardHeaders,
        ["C01", "Person One", "9876500001", "RS Puram, Coimbatore - 641002", Regular6],
        ["C02", "Person Two", "9876500002", "Gandhipuram, Coimbatore - 641012", Regular6],
        ["C03", "Person Three", "9876500003", "Saibaba Colony, Coimbatore - 641011", Large6],
        ["C04", "Person Four", "9876500004", "RS Puram, Coimbatore - 641002", Large6],
        ["C05", "Person Five", "9876500005", "Anna Nagar, Chennai - 600040", Regular6],
        ["C06", "Person Six", "9876500006", "T Nagar, Chennai - 600017", Regular6],
        ["C07", "Person Seven", "9876500007", "Medical College Road, Thanjavur - 613004", Large6],
        ["C08", "Person Eight", "9876500008", "Gandhi Road, Thanjavur - 613001", Large6],
        ["C09", "Person Nine", "9876500009", "Near bus stand, Kumbakonam", Regular6],
        ["C10", "Person Ten", "9876500010", "Medical College Road, Thanjavur - 613004", Regular6]);

    [Fact]
    public async Task Classification_counts_locations_pincodes_plans_and_categories()
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);
        var batchId = await harness.UploadAsync(TenCustomers());

        await using var db = test.CreateContext();
        var c = await harness.Queries(db).GetClassificationAsync(batchId);

        Assert.Equal(10, c.IncludedRows);
        Assert.Equal(10, c.NewCustomers);
        Assert.Equal(1, c.WithoutPincode);
        Assert.Equal(10, c.WithoutGroup);
        Assert.Equal(2, c.ByPincode.Single(p => p.Key == "641002").Count);
        Assert.Equal(2, c.ByPincode.Single(p => p.Key == "613004").Count);
        Assert.Equal(4, c.ByCity.Single(x => x.Key == "Coimbatore").Count);
        Assert.Equal(6, c.ByPlan.Single(p => p.Label == Regular6).Count);
        Assert.Equal(10, c.ByCategory.Single(x => x.Key == "6-Day Fit Box Trial").Count);
    }

    [Fact]
    public async Task Manager_decides_how_many_groups_and_assigns_individually_in_bulk_and_by_pincode()
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);
        var batchId = await harness.UploadAsync(TenCustomers());
        var rows = (await harness.RowsAsync(batchId)).ToDictionary(r => r.CustomerId!);

        string north, south;
        await using (var db = test.CreateContext())
        {
            var wf = harness.Workflow(db);
            north = $"d:{(await wf.CreateDraftGroupAsync(batchId, "Coimbatore North", null)).Id}";
            south = $"d:{(await wf.CreateDraftGroupAsync(batchId, "Coimbatore South", "Gandhipuram side")).Id}";
            await Assert.ThrowsAsync<BusinessRuleException>(() => wf.CreateDraftGroupAsync(batchId, "  coimbatore   NORTH ", null));

            // By pincode: both 641002 customers.
            Assert.Equal(2, await wf.AssignAsync(batchId, new ImportRowSelection { Filter = new ImportRowFilter { Pincode = "641002" } }, north));
            // Individual.
            Assert.Equal(1, await wf.AssignAsync(batchId, new ImportRowSelection { RowIds = [rows["C02"].Id] }, south));
            // Bulk by explicit selection: C02 is already in South, so only C03 (unassigned) and C01 (from North) change.
            Assert.Equal(2, await wf.AssignAsync(batchId, new ImportRowSelection { RowIds = [rows["C03"].Id, rows["C02"].Id, rows["C01"].Id] }, south));
        }

        var assigned = (await harness.RowsAsync(batchId)).ToDictionary(r => r.CustomerId!);
        Assert.Equal("Coimbatore South", assigned["C01"].Group!.Name);
        Assert.Equal("Coimbatore North", assigned["C04"].Group!.Name);
        Assert.Equal("Coimbatore South", assigned["C03"].Group!.Name);
        Assert.Null(assigned["C05"].Group);

        await using (var db = test.CreateContext())
        {
            var groups = (await harness.Queries(db).GetGroupsAsync(batchId)).Where(g => g.IsNew).ToDictionary(g => g.Name);
            Assert.Equal(1, groups["Coimbatore North"].CustomerCount);
            Assert.Equal(3, groups["Coimbatore South"].CustomerCount);
            Assert.Equal(3, groups["Coimbatore South"].PincodeCount);
        }
    }

    [Fact]
    public async Task Bulk_move_preview_describes_the_change()
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);
        var batchId = await harness.UploadAsync(TenCustomers());

        await using var db = test.CreateContext();
        var wf = harness.Workflow(db);
        var north = $"d:{(await wf.CreateDraftGroupAsync(batchId, "Coimbatore North", null)).Id}";
        var south = $"d:{(await wf.CreateDraftGroupAsync(batchId, "Coimbatore South", null)).Id}";
        var coimbatore = new ImportRowSelection { Filter = new ImportRowFilter { City = "Coimbatore" } };
        await wf.AssignAsync(batchId, coimbatore, north);

        var preview = await wf.PreviewAssignmentAsync(batchId, new ImportRowSelection { Filter = new ImportRowFilter { Status = "Included" } }, south);

        Assert.Equal(10, preview.SelectedRows);
        Assert.Contains("4 customers will be moved from Coimbatore North to Coimbatore South.", preview.Messages);
        Assert.Contains("6 customers will be assigned to Coimbatore South.", preview.Messages);
    }

    [Fact]
    public async Task Deleting_a_draft_group_unassigns_its_customers()
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);
        var batchId = await harness.UploadAsync(TenCustomers());

        await using var db = test.CreateContext();
        var wf = harness.Workflow(db);
        var group = await wf.CreateDraftGroupAsync(batchId, "Thanjavur", null);
        await wf.AssignAsync(batchId, new ImportRowSelection { Filter = new ImportRowFilter { City = "Thanjavur" } }, $"d:{group.Id}");

        Assert.Equal(3, await wf.DeleteDraftGroupAsync(batchId, group.Id));
        Assert.Equal(10, (await harness.Queries(db).GetSummaryAsync(batchId)).Unassigned);
    }

    [Fact]
    public async Task Suggestions_follow_the_deterministic_rules_and_need_approval()
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);

        int existingGroupId;
        await using (var db = test.CreateContext())
        {
            // A saved group that already serves pincode 600040, and an existing customer in it.
            var chennai = new DeliveryGroup { Name = "Chennai West" };
            var existing = new Customer { CustomerId = "OLD1", CustomerName = "Old Customer", Pincode = "600040" };
            existing.SetPhoneNumber("9811100000");
            existing.SetLocation("Anna Nagar, Chennai - 600040");
            db.AddRange(chennai, existing);
            await db.SaveChangesAsync();
            db.CustomerDeliveryGroupAssignments.Add(new CustomerDeliveryGroupAssignment
                { CustomerId = existing.Id, DeliveryGroupId = chennai.Id, AssignedAt = test.Clock.UtcNow });
            await db.SaveChangesAsync();
            existingGroupId = chennai.Id;
        }

        var batchId = await harness.UploadAsync(TenCustomers());
        await using (var db = test.CreateContext())
        {
            var wf = harness.Workflow(db);
            var tanjore = await wf.CreateDraftGroupAsync(batchId, "Thanjavur Central", null);
            var c07 = (await harness.RowsAsync(batchId)).Single(r => r.CustomerId == "C07");
            await wf.AssignAsync(batchId, new ImportRowSelection { RowIds = [c07.Id] }, $"d:{tanjore.Id}");

            var result = await wf.GenerateSuggestionsAsync(batchId);
            Assert.True(result.RowsWithSuggestion >= 3);
        }

        var rows = (await harness.RowsAsync(batchId)).ToDictionary(r => r.CustomerId!);
        Assert.Equal(($"g:{existingGroupId}", "Same pincode"), (rows["C05"].SuggestedGroup?.Key, rows["C05"].SuggestionReason)); // 600040
        Assert.Equal(("Thanjavur Central", "Same pincode"), (rows["C10"].SuggestedGroup?.Name, rows["C10"].SuggestionReason)); // 613004 like C07
        Assert.Equal(("Thanjavur Central", "Same city"), (rows["C08"].SuggestedGroup?.Name, rows["C08"].SuggestionReason));
        Assert.Null(rows["C01"].SuggestedGroup);
        Assert.Null(rows["C10"].Group); // suggestions never assign by themselves

        await using (var db = test.CreateContext())
        {
            var wf = harness.Workflow(db);
            var preview = await wf.PreviewSuggestionsAsync(batchId, null, onlyUnassigned: true);
            Assert.Contains(preview.Messages, m => m.Contains("will be assigned to Thanjavur Central"));
            var applied = await wf.ApplySuggestionsAsync(batchId, null, onlyUnassigned: true);
            Assert.Equal(preview.SelectedRows, applied);
        }

        var after = (await harness.RowsAsync(batchId)).ToDictionary(r => r.CustomerId!);
        Assert.Equal("Thanjavur Central", after["C10"].Group?.Name);
        Assert.Equal("Chennai West", after["C05"].Group?.Name);
    }

    [Fact]
    public async Task Excluded_rows_are_not_selected_for_assignment()
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);
        var batchId = await harness.UploadAsync(TenCustomers());
        var c01 = (await harness.RowsAsync(batchId)).Single(r => r.CustomerId == "C01");

        await using var db = test.CreateContext();
        var wf = harness.Workflow(db);
        await wf.SetExcludedAsync(batchId, [c01.Id], true);
        var group = await wf.CreateDraftGroupAsync(batchId, "Everyone", null);

        Assert.Equal(9, await wf.AssignAsync(batchId, new ImportRowSelection { Filter = new ImportRowFilter() }, $"d:{group.Id}"));
        Assert.Equal(9, (await harness.Queries(db).GetSummaryAsync(batchId)).ToImport);
    }
}
