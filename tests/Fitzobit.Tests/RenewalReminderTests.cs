using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Application.Dictionaries;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>
/// Renewal reminders: a started package of an Active or Paused customer is due when its delivery days left (paused days not counted)
/// are at most the configured number, unless the customer already has a following package; the reminder is a WhatsApp link.
/// </summary>
public class RenewalReminderTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    /// <summary>Sets the clock to 10 am India time on the given day.</summary>
    private static void On(TestDatabase test, int month, int day) =>
        test.Clock.UtcNow = new DateTime(2026, month, day, 10, 0, 0, DateTimeKind.Utc).AddMinutes(-330);

    /// <summary>A customer with one weekly package Mon 5 – Sat 10 Oct 2026 (today is Wednesday 7 October).</summary>
    private static async Task<int> CustomerAsync(TestDatabase test, string phone = "9876543210", string name = "Anitha Raman")
    {
        await using var db = test.CreateContext();
        return (await test.Commands(db).CreateAsync(test.NewCustomer(name, phone, start: D(10, 5)))).Id;
    }

    private static async Task<RenewalListDto> DueAsync(TestDatabase test, int? customerId = null)
    {
        await using var db = test.CreateContext();
        return await new RenewalReminderService(db, new Domain.Deliveries.DeliveryCalendarService(), test.AuditFor(db), test.User, test.Clock)
            .ListDueAsync(customerId);
    }

    private static RenewalReminderService Service(TestDatabase test, Infrastructure.Persistence.FitzobitDbContext db) =>
        new(db, new Domain.Deliveries.DeliveryCalendarService(), test.AuditFor(db), test.User, test.Clock);

    [Fact]
    public async Task A_started_package_is_due_three_delivery_days_before_its_end_with_a_whatsapp_link()
    {
        using var test = new TestDatabase();
        var id = await CustomerAsync(test);
        await using (var db = test.CreateContext())
            await test.Commands(db).CreateAsync(test.NewCustomer("Karthik N", "9123456780", start: D(10, 12))); // not started yet

        On(test, 10, 7);
        Assert.Empty((await DueAsync(test)).Items); // Wed–Sat: 4 delivery days left

        On(test, 10, 8);
        var due = await DueAsync(test);
        Assert.Equal(3, due.DaysBefore);
        var item = Assert.Single(due.Items);
        Assert.Equal((id, "Veg Medium ×1 · Weekly", D(10, 10), 3, "Active"), (item.CustomerId, item.Package, item.EndDate, item.DaysLeft, item.Status));
        Assert.Equal("Hi Anitha Raman, your Fitzobit Veg Medium ×1 · Weekly package ends on Sat, 10 Oct 2026. Would you like to renew? Just reply to this message. Thank you!",
            item.Message);
        Assert.Equal($"https://wa.me/919876543210?text={Uri.EscapeDataString(item.Message)}", item.WhatsAppUrl);
        Assert.Null(item.RemindedAt);
    }

    [Fact]
    public async Task Paused_days_are_not_counted_and_the_number_of_days_comes_from_settings()
    {
        using var test = new TestDatabase();
        var id = await CustomerAsync(test);
        await using (var db = test.CreateContext())
        {
            var weekly = await db.DictionaryEntries.SingleAsync(e => e.DictionaryType == DictionaryType.Frequency && e.Code == "WEEKLY");
            await test.Dictionaries(db).UpdateAsync(weekly.Id,
                new SaveDictionaryEntryRequest { DisplayName = weekly.DisplayName, NumericValue = weekly.NumericValue, CanPause = true });
            await test.Pauses(db).PauseAsync(id, new PauseCustomerRequest { Dates = [D(10, 8)] }); // end Sat 10 → Mon 12
        }

        On(test, 10, 7);
        Assert.Empty((await DueAsync(test)).Items); // Wed 7, Thu 8 (paused), Fri 9, Sat 10, Mon 12: 4 meals left

        await using (var db = test.CreateContext())
            await Service(test, db).SaveSettingsAsync(new SaveRenewalSettingsRequest { DaysBefore = 4, Message = "Renew {name}? {daysLeft} left, ends {endDate}." });
        var item = Assert.Single((await DueAsync(test)).Items);
        Assert.Equal((D(10, 12), 4), (item.EndDate, item.DaysLeft));
        Assert.Equal("Renew Anitha Raman? 4 left, ends Mon, 12 Oct 2026.", item.Message);
    }

    [Fact]
    public async Task A_customer_with_a_following_package_has_already_renewed()
    {
        using var test = new TestDatabase();
        var id = await CustomerAsync(test);
        On(test, 10, 8);
        Assert.Single((await DueAsync(test)).Items);

        await using (var db = test.CreateContext()) await test.Packages(db).AddAsync(id, test.NewPackage("MONTHLY", D(10, 12), "OATS"));
        Assert.Empty((await DueAsync(test)).Items);
    }

    [Theory]
    [InlineData("INACTIVE", false)]
    [InlineData("PAUSED", true)]
    public async Task Only_active_and_paused_customers_are_reminded(string statusCode, bool due)
    {
        using var test = new TestDatabase();
        var id = await CustomerAsync(test);
        await using (var db = test.CreateContext())
        {
            var detail = await test.Queries(db).GetDetailAsync(id);
            await test.Commands(db).UpdateAsync(id, CustomerTests.Update(detail, r => r.StatusId = test.Entry(DictionaryType.CustomerStatus, statusCode)));
        }

        On(test, 10, 8);
        Assert.Equal(due ? 1 : 0, (await DueAsync(test)).Items.Count);
    }

    [Fact]
    public async Task Sending_a_reminder_is_recorded_and_audited()
    {
        using var test = new TestDatabase();
        var id = await CustomerAsync(test);
        On(test, 10, 8);
        var packageId = Assert.Single((await DueAsync(test, id)).Items).PackageId;

        await using (var db = test.CreateContext())
        {
            var recorded = await Service(test, db).RecordReminderAsync(packageId);
            Assert.Equal((test.Clock.UtcNow, "Test User"), (recorded.RemindedAt, recorded.RemindedBy));
        }

        var item = Assert.Single((await DueAsync(test)).Items);
        Assert.Equal((test.Clock.UtcNow, "Test User"), (item.RemindedAt, item.RemindedBy));
        await using var check = test.CreateContext();
        Assert.Contains("Renewal reminder sent: FITZ-000001", (await check.AuditLogs.SingleAsync(a => a.Action == AuditActions.RenewalReminderSent)).Summary);
        await Assert.ThrowsAsync<NotFoundException>(() => Service(test, check).RecordReminderAsync(999_999));
    }

    [Fact]
    public async Task A_landline_gets_no_whatsapp_link()
    {
        using var test = new TestDatabase();
        await CustomerAsync(test, "0422 2345678");
        On(test, 10, 8);
        Assert.Null(Assert.Single((await DueAsync(test)).Items).WhatsAppUrl);
    }

    [Fact]
    public async Task Settings_have_defaults_and_are_validated()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var service = Service(test, db);

        var defaults = await service.GetSettingsAsync();
        Assert.Equal((3, RenewalReminderService.DefaultMessage, (DateTime?)null), (defaults.DaysBefore, defaults.Message, defaults.UpdatedAt));

        foreach (var (days, message, expected) in new[] { (0, "Hi", "between 1 and 30"), (31, "Hi", "between 1 and 30"), (3, "  ", "Enter the reminder message") })
            Assert.Contains(expected, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.SaveSettingsAsync(new SaveRenewalSettingsRequest { DaysBefore = days, Message = message }))).Message);

        var saved = await service.SaveSettingsAsync(new SaveRenewalSettingsRequest { DaysBefore = 5, Message = "  Hello {name}  " });
        Assert.Equal((5, "Hello {name}", "test-user"), (saved.DaysBefore, saved.Message, saved.UpdatedBy));
    }
}
