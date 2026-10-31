using ClosedXML.Excel;
using Fitzobit.Application.Customers;
using Fitzobit.Application.DailyOrders;
using Fitzobit.Domain.Deliveries;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Infrastructure.Exports;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Fitzobit.Tests;

/// <summary>
/// Daily Orders: the boxes delivered on one day (started, not finished, not paused, Active customers), in total per meal type ×
/// preference and per delivery group, with each customer's packages on one line in delivery-time order; Excel and PDF exports.
/// </summary>
public class DailyOrdersTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static DailyOrdersService Service(TestDatabase test, FitzobitDbContext db) =>
        new(db, new DeliveryCalendarService(), [new DailyOrdersExcelWriter(), new DailyOrdersPdfWriter()], test.AuditFor(db), test.User, test.Clock,
            Options.Create(new ExportOptions()));

    private sealed record Seed(int Anitha, int Bala, int Chitra, int Devi, int Esther);

    /// <summary>Today is Wednesday 7 October 2026.</summary>
    private static async Task<Seed> SeedAsync(TestDatabase test)
    {
        await using var db = test.CreateContext();
        async Task<int> Create(string name, string phone, string frequency, DateOnly start, int hour, string meal = "MEDIUM", string preference = "VEG")
        {
            var request = test.NewCustomer(name, phone, frequency: frequency, start: start);
            request.Packages[0] = test.NewPackage(frequency, start, meal, preference);
            request.PreferredDeliveryTime = new TimeOnly(hour, 0);
            return (await test.Commands(db).CreateAsync(request)).Id;
        }

        var anitha = await Create("Anitha", "9000000001", "WEEKLY", D(10, 5), 8);                      // Veg Medium, Mon 5 – Sat 10
        await test.Packages(db).AddAsync(anitha, test.NewPackage("MONTHLY", D(10, 5), "OATS", "WITH-EGG", quantity: 2));
        var bala = await Create("Bala", "9000000002", "WEEKLY", D(10, 5), 6);                          // earlier delivery time
        var chitra = await Create("Chitra", "9000000003", "WEEKLY", D(10, 5), 7);                      // will be Inactive
        var devi = await Create("Devi", "9000000004", "WEEKLY", D(10, 12), 7);                         // starts next week
        var esther = await Create("Esther", "9000000005", "WEEKLY", D(10, 5), 9, "LARGE", "NONE");     // no group

        var groups = await test.Groups(db).CreateGroupsAsync(2);
        await test.Assignments(db).AssignManyAsync([anitha, bala, chitra, devi], groups[0].Id);
        var detail = await test.Queries(db).GetDetailAsync(chitra);
        await test.Commands(db).UpdateAsync(chitra, CustomerTests.Update(detail, r => r.StatusId = test.Entry(DictionaryType.CustomerStatus, "INACTIVE")));
        await test.Pauses(db).PauseAsync(anitha, new PauseCustomerRequest { Dates = [D(10, 8)] }); // only the monthly package pauses
        return new Seed(anitha, bala, chitra, devi, esther);
    }

    [Fact]
    public async Task Totals_and_groups_count_boxes_delivered_that_day()
    {
        using var test = new TestDatabase();
        var seed = await SeedAsync(test);
        await using var db = test.CreateContext();
        var service = Service(test, db);

        Assert.Equal(D(10, 8), service.DefaultDate()); // the next delivery day
        var thursday = await service.GetAsync(D(10, 8));
        Assert.Equal((true, 3, 3), (thursday.IsDeliveryDay, thursday.TotalBoxes, thursday.TotalCustomers)); // Anitha's oats paused, Chitra inactive, Devi not started
        Assert.Equal([("Medium", "Veg", 2), ("Large", "None", 1)], thursday.Totals.Select(t => (t.MealType, t.Preference, t.Boxes)));

        Assert.Equal(["Group 1", "No group"], thursday.Groups.Select(g => g.Label));
        var group1 = thursday.Groups[0];
        Assert.Equal((2, 2), (group1.Customers.Count, group1.Boxes));
        Assert.Equal([seed.Bala, seed.Anitha], group1.Customers.Select(c => c.Id)); // 6:00 before 8:00
        Assert.Equal("Veg Medium ×1", group1.Customers[1].ItemsText);

        var friday = await service.GetAsync(D(10, 9));
        var anitha = friday.Groups[0].Customers.Single(c => c.Id == seed.Anitha);
        Assert.Equal(("Veg Medium ×1 + With Egg Oats ×2", 3), (anitha.ItemsText, anitha.Boxes)); // both packages in one bag
        Assert.Equal([("Medium", "Veg", 2), ("Large", "None", 1), ("Oats", "With Egg", 2)], friday.Totals.Select(t => (t.MealType, t.Preference, t.Boxes)));
        Assert.Equal(5, friday.TotalBoxes);
    }

    [Fact]
    public async Task Sundays_have_no_deliveries()
    {
        using var test = new TestDatabase();
        await SeedAsync(test);
        await using var db = test.CreateContext();
        var sunday = await Service(test, db).GetAsync(D(10, 11));
        Assert.Equal((false, 0, 0), (sunday.IsDeliveryDay, sunday.TotalBoxes, sunday.Groups.Count));
    }

    [Fact]
    public async Task Excel_has_a_totals_sheet_and_one_sheet_per_group()
    {
        using var test = new TestDatabase();
        await SeedAsync(test);
        await using var db = test.CreateContext();

        var file = await Service(test, db).ExportAsync(D(10, 9), ExportFormat.Excel);
        Assert.Equal("Fitzobit_Daily_Orders_2026-10-09.xlsx", file.FileName);
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        Assert.Equal(["Kitchen totals", "Group 1", "No group"], workbook.Worksheets.Select(s => s.Name));

        var group = workbook.Worksheet("Group 1");
        Assert.Equal("Customer ID", group.Cell(5, 2).GetString());
        Assert.Equal(("Bala", "Veg Medium ×1"), (group.Cell(6, 3).GetString(), group.Cell(6, 4).GetString()));
        Assert.Equal(("Anitha", "Veg Medium ×1 + With Egg Oats ×2", 3), (group.Cell(7, 3).GetString(), group.Cell(7, 4).GetString(), group.Cell(7, 5).GetValue<int>()));
        Assert.Equal("9000000002", group.Cell(6, 6).GetString()); // phones stay text

        var totals = workbook.Worksheet("Kitchen totals");
        Assert.Contains(totals.CellsUsed(), c => c.GetString() == "By delivery group");
    }

    [Fact]
    public async Task Pdf_is_written_and_the_export_is_audited()
    {
        using var test = new TestDatabase();
        await SeedAsync(test);
        await using var db = test.CreateContext();

        var file = await Service(test, db).ExportAsync(D(10, 9), ExportFormat.Pdf);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(file.Content, 0, 4));
        Assert.Contains(db.AuditLogs, a => a.EntityType == "DailyOrders" && a.EntityKey == "2026-10-09");
    }
}
