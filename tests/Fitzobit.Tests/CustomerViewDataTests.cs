using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>View Data: server-side filters, search, sort and paging; filter options from the dictionaries; group removal.</summary>
public class CustomerViewDataTests
{
    internal sealed record Data(int NorthId, int SouthId, int MediumId, int InactiveId, Dictionary<string, int> Ids);

    /// <summary>
    /// 40 customers CB001–CB040: even numbers in 641002, odd in 641018; meal type by i % 3 (0 Medium, 1 Regular, 2 Large); weekly, starting
    /// Mon 12 Oct 2026, except multiples of 4 (monthly); 1–20 in Group 1, 21–35 in Group 2, 36–40 without a group; CB040 inactive.
    /// </summary>
    internal static async Task<Data> SeedAsync(TestDatabase test)
    {
        var codes = new Dictionary<string, int>
        {
            ["MEDIUM"] = test.Entry(DictionaryType.MealType, "MEDIUM"), ["REGULAR"] = test.Entry(DictionaryType.MealType, "REGULAR"),
            ["LARGE"] = test.Entry(DictionaryType.MealType, "LARGE"), ["WEEKLY"] = test.Entry(DictionaryType.Frequency, "WEEKLY"),
            ["MONTHLY"] = test.Entry(DictionaryType.Frequency, "MONTHLY"), ["VEG"] = test.Entry(DictionaryType.Preference, "VEG"),
            ["ACTIVE"] = test.Entry(DictionaryType.CustomerStatus, "ACTIVE"), ["INACTIVE"] = test.Entry(DictionaryType.CustomerStatus, "INACTIVE")
        };
        await using var db = test.CreateContext();
        var calendar = new Domain.Deliveries.DeliveryCalendarService();
        var start = new DateOnly(2026, 10, 12);
        var customers = Enumerable.Range(1, 40).Select(i =>
        {
            var pincode = i % 2 == 0 ? "641002" : "641018";
            var monthly = i % 4 == 0;
            var c = new Customer
            {
                CustomerId = $"CB{i:000}",
                CustomerName = i == 7 ? "Anitha Raman" : $"Customer {i:00}",
                Pincode = pincode,
                PreferredDeliveryTime = new TimeOnly(6 + i % 12, 0),
                StatusId = codes[i == 40 ? "INACTIVE" : "ACTIVE"]
            };
            c.Packages.Add(new CustomerPackage
            {
                PreferenceId = codes["VEG"],
                MealTypeId = codes[(i % 3) switch { 0 => "MEDIUM", 1 => "REGULAR", _ => "LARGE" }],
                Quantity = 1,
                FrequencyId = codes[monthly ? "MONTHLY" : "WEEKLY"],
                StartDate = start,
                EndDate = calendar.CalculateEndDate(start, monthly ? 26 : 6)
            });
            c.SetPhoneNumber($"98000{i:00000}");
            c.SetAddress($"{i} Main Road, RS Puram, Coimbatore {pincode}");
            return c;
        }).ToList();
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();

        var groups = test.Groups(db);
        var created = await groups.CreateGroupsAsync(2);
        var (north, south) = (created[0], created[1]);
        await groups.MoveAsync(customers.Take(20).Select(c => c.Id).ToList(), north.Id, "Initial");
        await groups.MoveAsync(customers.Skip(20).Take(15).Select(c => c.Id).ToList(), south.Id, "Initial");
        return new Data(north.Id, south.Id, codes["MEDIUM"], codes["INACTIVE"], customers.ToDictionary(c => c.CustomerId, c => c.Id));
    }

    private static async Task<List<string>> IdsAsync(TestDatabase test, CustomerFilter filter, int pageSize = 100)
    {
        await using var db = test.CreateContext();
        return (await test.Queries(db).ListAsync(filter, 1, pageSize)).Items.Select(i => i.CustomerId).ToList();
    }

    [Fact]
    public async Task Filters_combine_group_meal_type_and_pincode()
    {
        using var test = new TestDatabase();
        var data = await SeedAsync(test);

        var ids = await IdsAsync(test, new CustomerFilter { Group = data.NorthId.ToString(), MealTypeId = data.MediumId, Pincode = "641002" });

        Assert.Equal(["CB006", "CB012", "CB018"], ids);
    }

    [Fact]
    public async Task Status_frequency_address_incomplete_and_no_group_filters()
    {
        using var test = new TestDatabase();
        var data = await SeedAsync(test);

        Assert.Equal(["CB036", "CB037", "CB038", "CB039", "CB040"], await IdsAsync(test, new CustomerFilter { Group = "none" }));
        Assert.Equal(["CB040"], await IdsAsync(test, new CustomerFilter { StatusId = data.InactiveId }));
        Assert.Equal(10, (await IdsAsync(test, new CustomerFilter { FrequencyId = test.Entry(DictionaryType.Frequency, "MONTHLY") })).Count);
        Assert.Equal(40, (await IdsAsync(test, new CustomerFilter { Address = "  rs PURAM " })).Count);
        Assert.Equal(["CB016"], await IdsAsync(test, new CustomerFilter { Address = "16 Main" }));
        Assert.Empty(await IdsAsync(test, new CustomerFilter { Pincode = "64100" }));
        Assert.Empty(await IdsAsync(test, new CustomerFilter { Incomplete = true }));
        Assert.Equal(15, (await IdsAsync(test, new CustomerFilter { Group = data.SouthId.ToString() })).Count);
    }

    [Fact]
    public async Task Search_covers_id_name_phone_pincode_meal_type_and_group()
    {
        using var test = new TestDatabase();
        await SeedAsync(test);

        Assert.Equal(["CB007"], await IdsAsync(test, new CustomerFilter { Search = "anitha" }));
        Assert.Equal(["CB007"], await IdsAsync(test, new CustomerFilter { Search = "9800000007" }));
        Assert.Equal(["CB012"], await IdsAsync(test, new CustomerFilter { Search = "cb012" }));
        Assert.Equal(20, (await IdsAsync(test, new CustomerFilter { Search = "641018" })).Count);
        Assert.Equal(15, (await IdsAsync(test, new CustomerFilter { Search = "Group 2" })).Count);
        Assert.Equal(15, (await IdsAsync(test, new CustomerFilter { Search = "g2" })).Count);
        Assert.Equal(13, (await IdsAsync(test, new CustomerFilter { Search = "large" })).Count);
        Assert.Empty(await IdsAsync(test, new CustomerFilter { Search = "zzz-no-match" }));
    }

    [Fact]
    public async Task Sorting_and_paging_are_server_side()
    {
        using var test = new TestDatabase();
        await SeedAsync(test);
        await using var db = test.CreateContext();
        var queries = test.Queries(db);

        var page2 = await queries.ListAsync(new CustomerFilter(), 2, 25);
        Assert.Equal((40, 2, 25, 15), (page2.Total, page2.Page, page2.PageSize, page2.Items.Count));
        Assert.Equal("CB026", page2.Items[0].CustomerId);
        Assert.Equal(25, (await queries.ListAsync(new CustomerFilter(), 1, 7)).PageSize);
        Assert.Equal("CB040", (await queries.ListAsync(new CustomerFilter { Sort = "customerId", Desc = true }, 1, 10)).Items[0].CustomerId);
        Assert.Equal("Anitha Raman", (await queries.ListAsync(new CustomerFilter { Sort = "customerName" }, 1, 10)).Items[0].CustomerName);

        // Customers without a group come last in both directions.
        var byGroupDesc = await queries.ListAsync(new CustomerFilter { Sort = "group", Desc = true }, 1, 100);
        Assert.Equal(2, byGroupDesc.Items[0].GroupNumber);
        Assert.Null(byGroupDesc.Items[^1].GroupNumber);

        var byEnd = await queries.ListAsync(new CustomerFilter { Sort = "endDate", Desc = true }, 1, 100);
        Assert.Equal(new DateOnly(2026, 11, 10), byEnd.Items[0].EndDate);
        Assert.Equal(new DateOnly(2026, 10, 17), byEnd.Items[^1].EndDate);

        var byTime = await queries.ListAsync(new CustomerFilter { Sort = "deliveryTime" }, 1, 100);
        Assert.Equal(byTime.Items.Select(i => i.PreferredDeliveryTime).Order(), byTime.Items.Select(i => i.PreferredDeliveryTime));
    }

    [Fact]
    public async Task Package_filters_match_customers_having_any_package_with_the_value()
    {
        using var test = new TestDatabase();
        var data = await SeedAsync(test);
        await using (var db = test.CreateContext())
            await test.Packages(db).AddAsync(data.Ids["CB001"], test.NewPackage("MONTHLY", new DateOnly(2026, 10, 13), "OATS", "WITH-EGG"));

        var oats = test.Entry(DictionaryType.MealType, "OATS");
        var withEgg = test.Entry(DictionaryType.Preference, "WITH-EGG");
        Assert.Equal(["CB001"], await IdsAsync(test, new CustomerFilter { MealTypeId = oats }));
        Assert.Equal(["CB001"], await IdsAsync(test, new CustomerFilter { PreferenceId = withEgg }));
        Assert.Contains("CB001", await IdsAsync(test, new CustomerFilter { MealTypeId = test.Entry(DictionaryType.MealType, "REGULAR") })); // its first package
        Assert.Equal(["CB001"], await IdsAsync(test, new CustomerFilter { Search = "oats" }));
        Assert.Equal(11, (await IdsAsync(test, new CustomerFilter { FrequencyId = test.Entry(DictionaryType.Frequency, "MONTHLY") })).Count);

        await using var check = test.CreateContext();
        var row = (await test.Queries(check).ListAsync(new CustomerFilter(), 1, 100)).Items.Single(i => i.CustomerId == "CB001");
        Assert.Equal(2, row.Packages.Count);
        Assert.Equal((new DateOnly(2026, 10, 12), new DateOnly(2026, 11, 11)), (row.StartDate, row.EndDate));
    }

    [Fact]
    public async Task Incomplete_lists_customers_without_a_package_or_delivery_time()
    {
        using var test = new TestDatabase();
        await SeedAsync(test);
        await using (var db = test.CreateContext())
        {
            var noTime = await db.Customers.SingleAsync(c => c.CustomerId == "CB005");
            noTime.PreferredDeliveryTime = null;
            db.CustomerPackages.RemoveRange(db.CustomerPackages.Where(p => p.Customer!.CustomerId == "CB009"));
            await db.SaveChangesAsync();
        }
        Assert.Equal(["CB005", "CB009"], await IdsAsync(test, new CustomerFilter { Incomplete = true }));
    }

    [Fact]
    public async Task Filter_options_come_from_groups_and_dictionaries()
    {
        using var test = new TestDatabase();
        await SeedAsync(test);
        await using var db = test.CreateContext();
        await test.Dictionaries(db).UpdateAsync(test.Entry(DictionaryType.MealType, "OATS"), new() { DisplayName = "Oats", IsActive = false });

        var options = await test.Queries(db).GetFilterOptionsAsync();

        Assert.Equal(["Group 1", "Group 2"], options.Groups.Select(g => g.Name));
        Assert.Equal(["Regular", "Medium", "Large", "Meal bowl", "Oats"], options.MealTypes.Select(m => m.Name));
        Assert.False(options.MealTypes.Single(m => m.Name == "Oats").IsActive); // inactive values stay filterable
        Assert.Equal(["Active", "Inactive", "Paused"], options.Statuses.Select(s => s.Name));
        Assert.Equal([10, 25, 50, 100], options.PageSizes);
    }

    [Fact]
    public async Task Removing_customers_from_their_group_records_history_and_audit()
    {
        using var test = new TestDatabase();
        var data = await SeedAsync(test);
        int[] ids = [data.Ids["CB001"], data.Ids["CB002"], data.Ids["CB021"], data.Ids["CB036"]];

        await using var db = test.CreateContext();
        var preview = await test.Groups(db).PreviewUnassignAsync(ids);
        Assert.Equal((4, 3, 1), (preview.Selected, preview.WillMove, preview.AlreadyInTarget));

        test.Clock.UtcNow = test.Clock.UtcNow.AddHours(1);
        var result = await new Application.DeliveryGroups.DeliveryGroupAssignmentService(db, test.User, test.Clock, test.AuditFor(db))
            .UnassignManyAsync(ids, "Moved out of area");
        Assert.Equal((4, 3, 1), (result.Requested, result.Changed, result.AlreadyInGroup));

        Assert.Equal(8, (await IdsAsync(test, new CustomerFilter { Group = "none" })).Count);
        var detail = await test.Queries(db).GetDetailAsync(data.Ids["CB001"]);
        Assert.Null(detail.GroupId);
        Assert.Equal(((int?)1, "Moved out of area"), (detail.History[0].PreviousGroupNumber, detail.History[0].Reason));
        Assert.Equal(3, await db.AuditLogs.CountAsync(a => a.Action == AuditActions.CustomerUnassigned));
    }
}
