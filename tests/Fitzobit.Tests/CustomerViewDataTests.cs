using System.Text.Json;
using ClosedXML.Excel;
using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Application.Imports.Locations;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Infrastructure.Exports;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitzobit.Tests;

/// <summary>View Data: server-side filters, search, sort and paging; customer edits; group removal; Excel export.</summary>
public class CustomerViewDataTests
{
    private const string MediumMonthly = "MEDIUM-FIT-BOX-SUBSCRIPTION-5-ITEMS-MONTHLY";
    private const string RegularMonthly = "REGULAR-FIT-BOX-SUBSCRIPTION-3-ITEMS-MONTHLY";
    private const string LargeMonthly = "LARGE-FIT-BOX-SUBSCRIPTION-8-ITEMS-MONTHLY";

    private sealed record Data(int NorthId, int SouthId, int MediumPlanId, Dictionary<string, int> Ids);

    /// <summary>
    /// 40 customers CB001–CB040: even numbers in 641002, odd in 641018; plan by i % 3 (0 Medium, 1 Regular, 2 Large);
    /// 1–20 in Coimbatore North, 21–35 in Coimbatore South, 36–40 without a group; CB040 inactive.
    /// </summary>
    private static async Task<Data> SeedAsync(TestDatabase test, ImportHarness harness)
    {
        await using var db = test.CreateContext();
        var plans = await db.Plans.ToDictionaryAsync(p => p.Code, p => p.Id);
        var customers = Enumerable.Range(1, 40).Select(i =>
        {
            var pincode = i % 2 == 0 ? "641002" : "641018";
            var c = new Customer
            {
                CustomerId = $"CB{i:000}",
                CustomerName = i == 7 ? "Anitha Raman" : $"Customer {i:00}",
                Pincode = pincode,
                PlanId = plans[(i % 3) switch { 0 => MediumMonthly, 1 => RegularMonthly, _ => LargeMonthly }],
                Status = i == 40 ? CustomerStatus.Inactive : CustomerStatus.Active
            };
            c.SetPhoneNumber($"98000{i:00000}");
            c.SetLocation($"{i} Main Road, RS Puram, Coimbatore {pincode}");
            return c;
        }).ToList();
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();

        var groups = harness.Groups(db);
        var north = await groups.CreateAsync("Coimbatore North", null);
        var south = await groups.CreateAsync("Coimbatore South", null);
        await groups.MoveAsync(customers.Take(20).Select(c => c.Id).ToList(), north.Id, "Initial");
        await groups.MoveAsync(customers.Skip(20).Take(15).Select(c => c.Id).ToList(), south.Id, "Initial");
        return new Data(north.Id, south.Id, plans[MediumMonthly], customers.ToDictionary(c => c.CustomerId, c => c.Id));
    }

    private static async Task<List<string>> IdsAsync(TestDatabase test, CustomerFilter filter, int pageSize = 100)
    {
        await using var db = test.CreateContext();
        return (await new CustomerQueryService(db).ListAsync(filter, 1, pageSize)).Items.Select(i => i.CustomerId).ToList();
    }

    private static CustomerCommandService Commands(TestDatabase test, FitzobitDbContext db) =>
        new(db, new LocationClassificationService(ImportHarness.LocationOptions), test.AuditFor(db));

    private static CustomerExportService Export(TestDatabase test, FitzobitDbContext db) =>
        new(db, new CustomerQueryService(db), new CustomerExcelWriter(), test.AuditFor(db), test.User, test.Clock, Options.Create(new ExportOptions()));

    [Fact]
    public async Task Filters_combine_group_plan_and_pincode()
    {
        using var test = new TestDatabase();
        var data = await SeedAsync(test, await ImportHarness.CreateAsync(test));

        var ids = await IdsAsync(test, new CustomerFilter { Group = data.NorthId.ToString(), PlanId = data.MediumPlanId, Pincode = "641002" });

        Assert.Equal(["CB006", "CB012", "CB018"], ids);
    }

    [Fact]
    public async Task Status_location_import_and_no_group_filters()
    {
        using var test = new TestDatabase();
        var data = await SeedAsync(test, await ImportHarness.CreateAsync(test));

        Assert.Equal(["CB036", "CB037", "CB038", "CB039", "CB040"], await IdsAsync(test, new CustomerFilter { Status = "Unassigned" }));
        Assert.Equal(5, (await IdsAsync(test, new CustomerFilter { Group = "none" })).Count);
        Assert.Equal(["CB040"], await IdsAsync(test, new CustomerFilter { Status = "Inactive" }));
        Assert.Equal(39, (await IdsAsync(test, new CustomerFilter { Status = "Active" })).Count);
        Assert.Equal(40, (await IdsAsync(test, new CustomerFilter { Location = "  rs PURAM " })).Count);
        Assert.Equal(["CB016"], await IdsAsync(test, new CustomerFilter { Location = "16 Main" }));
        Assert.Empty(await IdsAsync(test, new CustomerFilter { Pincode = "64100" }));
        Assert.Empty(await IdsAsync(test, new CustomerFilter { ImportBatchId = 999 }));
        Assert.Equal(15, (await IdsAsync(test, new CustomerFilter { Group = data.SouthId.ToString() })).Count);
    }

    [Fact]
    public async Task Search_covers_id_name_phone_pincode_plan_and_group()
    {
        using var test = new TestDatabase();
        await SeedAsync(test, await ImportHarness.CreateAsync(test));

        Assert.Equal(["CB007"], await IdsAsync(test, new CustomerFilter { Search = "anitha" }));
        Assert.Equal(["CB007"], await IdsAsync(test, new CustomerFilter { Search = "9800000007" }));
        Assert.Equal(["CB012"], await IdsAsync(test, new CustomerFilter { Search = "cb012" }));
        Assert.Equal(20, (await IdsAsync(test, new CustomerFilter { Search = "641018" })).Count);
        Assert.Equal(15, (await IdsAsync(test, new CustomerFilter { Search = "coimbatore south" })).Count);
        Assert.Equal(13, (await IdsAsync(test, new CustomerFilter { Search = "Large Fit Box" })).Count);
        Assert.Empty(await IdsAsync(test, new CustomerFilter { Search = "zzz-no-match" }));
    }

    [Fact]
    public async Task Sorting_and_paging_are_server_side()
    {
        using var test = new TestDatabase();
        await SeedAsync(test, await ImportHarness.CreateAsync(test));
        await using var db = test.CreateContext();
        var queries = new CustomerQueryService(db);

        var page2 = await queries.ListAsync(new CustomerFilter(), 2, 25);
        Assert.Equal((40, 2, 25, 15), (page2.Total, page2.Page, page2.PageSize, page2.Items.Count));
        Assert.Equal("CB026", page2.Items[0].CustomerId);

        var invalidSize = await queries.ListAsync(new CustomerFilter(), 1, 7);
        Assert.Equal(25, invalidSize.PageSize);

        var byIdDesc = await queries.ListAsync(new CustomerFilter { Sort = "customerId", Desc = true }, 1, 10);
        Assert.Equal("CB040", byIdDesc.Items[0].CustomerId);

        var byName = await queries.ListAsync(new CustomerFilter { Sort = "customerName" }, 1, 10);
        Assert.Equal("Anitha Raman", byName.Items[0].CustomerName);

        // Customers without a group come last in both directions, on SQLite and PostgreSQL alike.
        var byGroupDesc = await queries.ListAsync(new CustomerFilter { Sort = "group", Desc = true }, 1, 100);
        Assert.Equal("Coimbatore South", byGroupDesc.Items[0].GroupName);
        Assert.Null(byGroupDesc.Items[^1].GroupName);
        var byGroupAsc = await queries.ListAsync(new CustomerFilter { Sort = "group" }, 1, 100);
        Assert.Equal("Coimbatore North", byGroupAsc.Items[0].GroupName);
        Assert.Null(byGroupAsc.Items[^1].GroupName);

        var byPlan = await queries.ListAsync(new CustomerFilter { Sort = "plan" }, 1, 100);
        Assert.Equal(byPlan.Items.Select(i => i.PlanName).Order(StringComparer.Ordinal), byPlan.Items.Select(i => i.PlanName));
    }

    [Fact]
    public async Task Filter_options_list_groups_plans_and_completed_imports()
    {
        using var test = new TestDatabase();
        await SeedAsync(test, await ImportHarness.CreateAsync(test));
        await using var db = test.CreateContext();

        var options = await new CustomerQueryService(db).GetFilterOptionsAsync();

        Assert.Equal(["Coimbatore North", "Coimbatore South"], options.Groups.Select(g => g.Name));
        Assert.Equal(10, options.Plans.Count);
        Assert.Empty(options.Imports);
        Assert.Equal([10, 25, 50, 100], options.PageSizes);
    }

    [Fact]
    public async Task Edit_validates_records_old_and_new_values_and_detects_concurrent_changes()
    {
        using var test = new TestDatabase();
        var data = await SeedAsync(test, await ImportHarness.CreateAsync(test));
        var id = data.Ids["CB007"];

        await using var db = test.CreateContext();
        var detail = await new CustomerQueryService(db).GetDetailAsync(id);
        test.Clock.UtcNow = test.Clock.UtcNow.AddMinutes(5);

        var request = new UpdateCustomerRequest
        {
            CustomerName = "  Anitha   Raman K ",
            PhoneNumber = "9876543210",
            Location = "7 Main Road, Gandhipuram, Coimbatore 641012",
            PlanId = detail.PlanId,
            Status = "Active",
            ExpectedUpdatedAt = detail.UpdatedAt
        };
        await Assert.ThrowsAsync<BusinessRuleException>(() => Commands(test, db).UpdateAsync(id,
            new UpdateCustomerRequest { CustomerName = "Anitha", PhoneNumber = "12345", Location = "x", Status = "Active", ExpectedUpdatedAt = detail.UpdatedAt }));

        var result = await Commands(test, db).UpdateAsync(id, request);
        Assert.True(result.Changed);
        Assert.Equal(["CustomerName", "PhoneNumber", "Location", "Pincode"], result.ChangedFields);

        await using (var check = test.CreateContext())
        {
            var saved = await check.Customers.SingleAsync(c => c.Id == id);
            Assert.Equal(("CB007", "Anitha Raman K", "641012", "9876543210"), (saved.CustomerId, saved.CustomerName, saved.Pincode, saved.NormalizedPhoneNumber));

            var audit = await check.AuditLogs.SingleAsync(a => a.Action == AuditActions.CustomerUpdated);
            Assert.Equal(("CB007", "test-user"), (audit.EntityKey, audit.PerformedBy));
            var oldValues = JsonDocument.Parse(audit.OldValues!).RootElement;
            var newValues = JsonDocument.Parse(audit.NewValues!).RootElement;
            Assert.Equal("Anitha Raman", oldValues.GetProperty("CustomerName").GetString());
            Assert.Equal("Anitha Raman K", newValues.GetProperty("CustomerName").GetString());
            Assert.Equal("641018", oldValues.GetProperty("Pincode").GetString());
            Assert.Equal("641012", newValues.GetProperty("Pincode").GetString());
            Assert.False(newValues.TryGetProperty("PlanId", out _));
        }

        // A second editor still holding the original UpdatedAt must not silently overwrite the change.
        test.Clock.UtcNow = test.Clock.UtcNow.AddMinutes(5);
        await using var other = test.CreateContext();
        var conflict = await Assert.ThrowsAsync<BusinessRuleException>(() => Commands(test, other).UpdateAsync(id,
            new UpdateCustomerRequest { CustomerName = "Someone Else", PhoneNumber = "9800000007", Location = detail.Location, PlanId = detail.PlanId,
                Status = "Active", ExpectedUpdatedAt = detail.UpdatedAt }));
        Assert.Contains("changed by test-user", conflict.Message);
        Assert.Equal("Anitha Raman K", (await other.Customers.AsNoTracking().SingleAsync(c => c.Id == id)).CustomerName);
    }

    [Fact]
    public async Task Removing_customers_from_their_group_records_history_and_audit()
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);
        var data = await SeedAsync(test, harness);
        int[] ids = [data.Ids["CB001"], data.Ids["CB002"], data.Ids["CB021"], data.Ids["CB036"]];

        await using var db = test.CreateContext();
        var groups = harness.Groups(db);
        var preview = await groups.PreviewUnassignAsync(ids);
        Assert.Equal((4, 3, 1), (preview.Selected, preview.WillMove, preview.AlreadyInTarget));
        Assert.Equal(["2 customers will be removed from Coimbatore North.", "1 customer will be removed from Coimbatore South.",
            "1 customer already without a group (no change)."], preview.Messages);

        test.Clock.UtcNow = test.Clock.UtcNow.AddHours(1);
        var result = await new Application.DeliveryGroups.DeliveryGroupAssignmentService(db, test.User, test.Clock, test.AuditFor(db))
            .UnassignManyAsync(ids, "Moved out of area");
        Assert.Equal((4, 3, 1), (result.Requested, result.Changed, result.AlreadyInGroup));

        Assert.Equal(8, (await IdsAsync(test, new CustomerFilter { Status = "Unassigned" })).Count);
        var detail = await new CustomerQueryService(db).GetDetailAsync(data.Ids["CB001"]);
        Assert.Null(detail.GroupId);
        Assert.Equal(2, detail.History.Count);
        var latest = detail.History[0];
        Assert.Equal(("Coimbatore North", (int?)null, "Moved out of area", "test-user"), (latest.PreviousGroupName, latest.NewGroupId, latest.Reason, latest.ChangedBy));
        Assert.Contains(detail.Audit, a => a.Action == AuditActions.CustomerUnassigned);
        Assert.Equal(3, await db.AuditLogs.CountAsync(a => a.Action == AuditActions.CustomerUnassigned));
    }

    [Fact]
    public async Task Filtered_export_contains_only_matching_customers_with_text_and_date_cells()
    {
        using var test = new TestDatabase();
        var data = await SeedAsync(test, await ImportHarness.CreateAsync(test));
        await using var db = test.CreateContext();

        var filter = new CustomerFilter { Group = data.NorthId.ToString(), PlanId = data.MediumPlanId, Pincode = "641002", Sort = "customerId", Desc = true };
        var file = await Export(test, db).ExportAsync(ExportScope.Filtered, filter, null);

        Assert.Equal(3, file.RowCount);
        Assert.StartsWith("Fitzobit_Customers_Filtered_", file.FileName);
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var sheet = workbook.Worksheet("Customers");
        Assert.Equal(["CustomerID", "CustomerName", "PhoneNumber", "Location", "Pincode", "Plan", "DeliveryGroup", "Status", "CreatedAt", "UpdatedAt"],
            sheet.Row(1).Cells(1, 10).Select(c => c.GetString()));
        Assert.Equal(4, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal(["CB018", "CB012", "CB006"], Enumerable.Range(2, 3).Select(r => sheet.Cell(r, 1).GetString()));
        Assert.All(Enumerable.Range(2, 3), r =>
        {
            Assert.Equal("Coimbatore North", sheet.Cell(r, 7).GetString());
            Assert.Equal("641002", sheet.Cell(r, 5).GetString());
        });

        Assert.Equal(XLDataType.Text, sheet.Cell(2, 3).DataType);
        Assert.Equal("9800000018", sheet.Cell(2, 3).GetString());
        Assert.Equal(XLDataType.Text, sheet.Cell(2, 5).DataType);
        Assert.Equal(XLDataType.DateTime, sheet.Cell(2, 9).DataType);
        var created = (await db.Customers.SingleAsync(c => c.CustomerId == "CB018")).CreatedAt;
        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(created, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")), sheet.Cell(2, 9).GetDateTime());
        Assert.True(sheet.AutoFilter.IsEnabled);
        Assert.Equal(1, sheet.SheetView.SplitRow);

        var info = workbook.Worksheet("Export info");
        Assert.Contains(info.CellsUsed(), c => c.GetString() == "Coimbatore North");

        var audit = await db.AuditLogs.SingleAsync(a => a.Action == AuditActions.ExportPerformed);
        Assert.Contains("\"Pincode\":\"641002\"", audit.Details);
    }

    [Fact]
    public async Task All_and_selected_exports()
    {
        using var test = new TestDatabase();
        var data = await SeedAsync(test, await ImportHarness.CreateAsync(test));
        await using var db = test.CreateContext();

        // "All" ignores filters that happen to be set on the screen.
        var all = await Export(test, db).ExportAsync(ExportScope.All, new CustomerFilter { Pincode = "641002" }, null);
        Assert.Equal(40, all.RowCount);

        var selected = await Export(test, db).ExportAsync(ExportScope.Selected, new CustomerFilter(), [data.Ids["CB003"], data.Ids["CB039"], data.Ids["CB003"]]);
        Assert.Equal(2, selected.RowCount);
        using var workbook = new XLWorkbook(new MemoryStream(selected.Content));
        Assert.Equal(["CB003", "CB039"], new[] { 2, 3 }.Select(r => workbook.Worksheet("Customers").Cell(r, 1).GetString()));

        await Assert.ThrowsAsync<BusinessRuleException>(() => Export(test, db).ExportAsync(ExportScope.Selected, new CustomerFilter(), []));
    }
}
