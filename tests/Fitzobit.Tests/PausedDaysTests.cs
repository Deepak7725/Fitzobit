using ClosedXML.Excel;
using Fitzobit.Application.Customers;
using Fitzobit.Application.DailyOrders;
using Fitzobit.Domain.Deliveries;
using Fitzobit.Infrastructure.Exports;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;

namespace Fitzobit.Tests;

/// <summary>
/// No paused days limit (owner, 2026-11-05): every paused day moves the end date, an end date moved onto an upcoming day paused for
/// everyone pauses that day too, the paused count ("4 (2 own + 2 for everyone)") is shown in the list, exports and Daily Orders, and the
/// UnlimitedPauses migration recalculates current packages whose later paused days did not move the end date.
/// </summary>
public class PausedDaysTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static FestivePauseService Festive(TestDatabase test, FitzobitDbContext db) =>
        new(db, test.Schedule(db), test.AuditFor(db), test.User, test.Clock, test.Automation(db));

    /// <summary>A monthly package Mon 12 Oct – Tue 10 Nov 2026 (today is Wednesday 7 October), Wed 14 Oct and Fri 13 Nov paused for
    /// everyone (only 14 Oct falls inside: end → Wed 11 Nov), then own pauses on Thu 15 and Fri 16 Oct.</summary>
    private static async Task<(int Id, PausedPackageDto Paused)> SeedAsync(TestDatabase test)
    {
        await using var db = test.CreateContext();
        var id = (await test.Commands(db).CreateAsync(test.NewCustomer(frequency: "MONTHLY"))).Id;
        await Festive(test, db).CreateAsync(new FestivePauseRequest { Dates = [D(10, 14), D(11, 13)], Reason = "Diwali" });
        var result = await test.Pauses(db).PauseAsync(id, new PauseCustomerRequest { Dates = [D(10, 15), D(10, 16)] });
        return (id, Assert.Single(result.Packages));
    }

    [Fact]
    public async Task An_end_date_moved_onto_a_day_paused_for_everyone_pauses_that_day_too()
    {
        using var test = new TestDatabase();
        var (id, paused) = await SeedAsync(test);

        // Own pauses: Wed 11 → Thu 12 → Fri 13 Nov, which is paused for everyone → Sat 14 Nov.
        Assert.Equal((D(11, 11), D(11, 14), 2), (paused.PreviousEndDate, paused.NewEndDate, paused.PausedCount));
        Assert.Equal([D(11, 13)], paused.FestiveDays);

        await using var db = test.CreateContext();
        var package = await db.CustomerPackages.AsNoTracking().Include(p => p.Pauses).SingleAsync(p => p.CustomerId == id);
        Assert.Equal((D(11, 14), 2), (package.EndDate, package.PausedCount));
        Assert.Equal([(D(10, 14), true), (D(10, 15), false), (D(10, 16), false), (D(11, 13), true)],
            package.Pauses.OrderBy(p => p.PausedDate).Select(p => (p.PausedDate, p.FestivePauseId != null)));
        Assert.Equal(1, (await Festive(test, db).ListAsync()).Single(f => f.Date == D(11, 13)).Packages);

        // The preview shows the same without saving.
        var preview = await test.Pauses(db).PreviewAsync(id, new PauseCustomerRequest { Dates = [D(10, 17)] });
        Assert.Equal((D(11, 14), D(11, 16)), (preview.Packages.Single().PreviousEndDate, preview.Packages.Single().NewEndDate));
    }

    [Fact]
    public async Task The_paused_count_shows_own_and_for_everyone_days_in_the_list_exports_and_daily_orders()
    {
        using var test = new TestDatabase();
        var (id, _) = await SeedAsync(test);
        await using var db = test.CreateContext();
        var other = (await test.Commands(db).CreateAsync(test.NewCustomer("Bala", "9000000002"))).Id; // weekly from Mon 12 Oct: paused 14 Oct for everyone

        var list = await test.Queries(db).ListAsync(new CustomerFilter { Sort = "paused", Desc = true }, 1, 25);
        Assert.Equal([id, other], list.Items.Select(i => i.Id));
        var item = list.Items[0];
        Assert.Equal((4, 2, 2, "4 (2 own + 2 for everyone)"), (item.PausedDays, item.OwnPausedDays, item.FestivePausedDays, item.PausedText));
        Assert.Equal("1 (for everyone)", list.Items[1].PausedText);
        Assert.Equal("2 (own)", CustomerPauseService.PausedDaysText(2, 0));
        Assert.Equal("0", CustomerPauseService.PausedDaysText(0, 0));

        var export = new CustomerExportService(db, test.Queries(db), [new CustomerExcelWriter()], test.AuditFor(db), test.User, test.Clock,
            Options.Create(new ExportOptions()));
        var file = await export.ExportAsync(new CustomerExportRequest { Scope = ExportScope.All, Columns = ["customerId", "paused"], Filter = new() { Sort = "paused" } });
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var sheet = workbook.Worksheet("Customers");
        Assert.Equal(["Customer ID", "Paused Days"], sheet.Row(1).Cells(1, 2).Select(c => c.GetString()));
        Assert.Equal(["1 (for everyone)", "4 (2 own + 2 for everyone)"], new[] { 2, 3 }.Select(r => sheet.Cell(r, 2).GetString()));

        var orders = new DailyOrdersService(db, new DeliveryCalendarService(), [new DailyOrdersExcelWriter()], test.AuditFor(db), test.User, test.Clock,
            Options.Create(new ExportOptions()));
        var day = await orders.GetAsync(D(10, 17));
        var customers = day.Groups.SelectMany(g => g.Customers).ToDictionary(c => c.Id);
        Assert.Equal("4 (2 own + 2 for everyone)", customers[id].PausedText);
        Assert.Equal("1 (for everyone)", customers[other].PausedText);
        var excel = await orders.ExportAsync(D(10, 17), ExportFormat.Excel);
        using var ordersBook = new XLWorkbook(new MemoryStream(excel.Content));
        var groupSheet = ordersBook.Worksheets.Skip(1).First();
        Assert.Equal("Paused Days", groupSheet.Cell(5, 12).GetString());
        Assert.Contains("4 (2 own + 2 for everyone)", groupSheet.Column(12).CellsUsed().Select(c => c.GetString()));
    }

    [Fact]
    public async Task The_migration_lets_earlier_paused_days_beyond_three_move_the_end_date_of_current_packages()
    {
        using var test = new TestDatabase(migrate: false);
        int current, past;
        await using (var db = test.CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261104164420_FestivePauses");
            current = (await test.Commands(db).CreateAsync(test.NewCustomer(frequency: "MONTHLY"))).Id;     // Mon 12 Oct – Tue 10 Nov
            past = (await test.Commands(db).CreateAsync(test.NewCustomer("Bala", "9000000002"))).Id;         // weekly Mon 12 – Sat 17 Oct
            // As the old rule stored them: 13–15 Oct moved the end to Fri 13 Nov, 16 and 17 Oct did not.
            await db.Database.ExecuteSqlAsync($"""
                UPDATE "CustomerPackages" SET "EndDate" = '2026-11-13', "PausedCount" = 3 WHERE "CustomerId" = {current};
                INSERT INTO "PackagePauses" ("PackageId", "PausedDate", "PreviousEndDate", "NewEndDate", "CreatedAt", "CreatedBy")
                SELECT p."Id", v.d::date, v.prev::date, v.next::date, now(), 'old'
                FROM "CustomerPackages" p,
                     (VALUES ('2026-10-13', '2026-11-10', '2026-11-11'), ('2026-10-14', '2026-11-11', '2026-11-12'),
                             ('2026-10-15', '2026-11-12', '2026-11-13'), ('2026-10-16', '2026-11-13', '2026-11-13'),
                             ('2026-10-17', '2026-11-13', '2026-11-13')) AS v(d, prev, next)
                WHERE p."CustomerId" = {current};
                UPDATE "CustomerPackages" SET "ArchivedAt" = now() WHERE "CustomerId" = {past};
                INSERT INTO "PackagePauses" ("PackageId", "PausedDate", "PreviousEndDate", "NewEndDate", "CreatedAt", "CreatedBy")
                SELECT "Id", '2026-10-14', '2026-10-17', '2026-10-17', now(), 'old' FROM "CustomerPackages" WHERE "CustomerId" = {past};
                """);
        }

        await using (var db = test.CreateContext())
            await db.Database.MigrateAsync();

        await using var check = test.CreateContext();
        var package = await check.CustomerPackages.AsNoTracking().Include(p => p.Pauses).SingleAsync(p => p.CustomerId == current);
        // Fri 13 Nov + 2 delivery days: Sat 14, (Sunday skipped) Mon 16 Nov.
        Assert.Equal((D(11, 16), 5), (package.EndDate, package.PausedCount));
        Assert.Equal([(D(11, 13), D(11, 14)), (D(11, 14), D(11, 16))],
            package.Pauses.Where(p => p.PausedDate >= D(10, 16)).OrderBy(p => p.PausedDate).Select(p => (p.PreviousEndDate, p.NewEndDate)));
        Assert.All(package.Pauses, p => Assert.True(p.ExtendedEndDate));

        var archived = await check.CustomerPackages.AsNoTracking().Include(p => p.Pauses).SingleAsync(p => p.CustomerId == past);
        Assert.Equal((D(10, 17), false), (archived.EndDate, archived.Pauses.Single().ExtendedEndDate)); // past packages stay as they were

        var customerCode = (await check.Customers.SingleAsync(c => c.Id == current)).CustomerId;
        var audit = await check.AuditLogs.SingleAsync(a => a.Summary.StartsWith("Package end date recalculated"));
        Assert.Equal(("Customer", customerCode, "system"), (audit.EntityType, audit.EntityKey, audit.PerformedBy));
        Assert.Contains("13 Nov 2026 → 16 Nov 2026", audit.Summary);

        // A paused count above 3 is allowed now.
        await using var more = test.CreateContext();
        await test.Pauses(more).PauseAsync(current, new PauseCustomerRequest { Dates = [D(10, 19)] });
        var after = await more.CustomerPackages.AsNoTracking().SingleAsync(p => p.CustomerId == current);
        Assert.Equal((D(11, 17), 6), (after.EndDate, after.PausedCount));
    }
}
