using System.Text.Json;
using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>Add/Edit customer: generated ids, validation, delivery time, packages on create, first note, audit and concurrency.</summary>
public class CustomerTests
{
    private static async Task<(int Id, string CustomerId)> CreateAsync(TestDatabase test, CreateCustomerRequest request)
    {
        await using var db = test.CreateContext();
        var result = await test.Commands(db).CreateAsync(request);
        return (result.Id, result.CustomerId);
    }

    [Fact]
    public async Task New_customers_get_generated_ids_a_delivery_time_and_their_packages()
    {
        using var test = new TestDatabase();

        var first = await CreateAsync(test, test.NewCustomer(note: "  Gate code 1234. Call before delivery.  "));
        var request = test.NewCustomer("Karthik N", "9123456780", frequency: "MONTHLY", start: new DateOnly(2026, 10, 5));
        request.PreferredDeliveryTime = new TimeOnly(13, 15, 42);
        request.Packages.Add(test.NewPackage("WEEKLY", new DateOnly(2026, 10, 10), "OATS", "WITH-EGG", quantity: 2));
        var second = await CreateAsync(test, request);

        Assert.Equal(("FITZ-000001", "FITZ-000002"), (first.CustomerId, second.CustomerId));

        await using var db = test.CreateContext();
        var one = await db.Customers.Include(c => c.Status).Include(c => c.Packages).Include(c => c.Notes).SingleAsync(c => c.Id == first.Id);
        Assert.Equal(new TimeOnly(7, 30), one.PreferredDeliveryTime);
        Assert.Equal(CustomerStatusCodes.Active, one.Status!.Code);
        Assert.Equal(("Anitha Raman", "9876543210", "641002", "Coimbatore"), (one.CustomerName, one.NormalizedPhoneNumber, one.Pincode, one.City));
        var weekly = Assert.Single(one.Packages);
        Assert.Equal((new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 17), 1, 0), (weekly.StartDate, weekly.EndDate, weekly.Quantity, weekly.PausedCount));
        var note = Assert.Single(one.Notes);
        Assert.Equal(("Gate code 1234. Call before delivery.", "test-user"), (note.Text, note.CreatedBy));

        var two = await db.Customers.Include(c => c.Packages).SingleAsync(c => c.Id == second.Id);
        Assert.Equal(new TimeOnly(13, 15), two.PreferredDeliveryTime); // seconds dropped
        Assert.Equal([(new DateOnly(2026, 10, 5), new DateOnly(2026, 11, 3), 1), (new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 16), 2)],
            two.Packages.OrderBy(p => p.StartDate).Select(p => (p.StartDate, p.EndDate, p.Quantity)));

        var audit = await db.AuditLogs.Where(a => a.Action == AuditActions.CustomerCreated).OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(["FITZ-000001", "FITZ-000002"], audit.Select(a => a.EntityKey));
        var created = JsonDocument.Parse(audit[1].NewValues!).RootElement;
        Assert.Equal("13:15:00", created.GetProperty("PreferredDeliveryTime").GetString());
        Assert.Equal(2, created.GetProperty("Packages").GetArrayLength());
        Assert.Contains("With Egg Oats ×2 · Weekly", created.GetProperty("Packages")[1].GetString());
    }

    [Fact]
    public async Task The_alternative_phone_is_optional_validated_and_may_be_shared()
    {
        using var test = new TestDatabase();
        var request = test.NewCustomer();
        request.AlternatePhoneNumber = " +91 98765 00000 ";
        var first = await CreateAsync(test, request);

        foreach (var (alternate, expected) in new[] { ("09876543210", "must be different"), ("12345", "Invalid alternative phone number") })
        {
            var invalid = test.NewCustomer("Karthik N", "9876543210");
            invalid.AlternatePhoneNumber = alternate;
            await using var db = test.CreateContext();
            Assert.Contains(expected, (await Assert.ThrowsAsync<BusinessRuleException>(() => test.Commands(db).CreateAsync(invalid))).Message);
        }

        // Another customer may use the same number (e.g. family members), as main or alternative number.
        var family = test.NewCustomer("Karthik N", "9123456780");
        family.AlternatePhoneNumber = "9876500000";
        await using (var db = test.CreateContext()) Assert.Empty((await test.Commands(db).CreateAsync(family)).Warnings);

        await using (var db = test.CreateContext())
        {
            var detail = await test.Queries(db).GetDetailAsync(first.Id);
            Assert.Equal("+91 98765 00000", detail.AlternatePhoneNumber);
            var list = await test.Queries(db).ListAsync(new CustomerFilter { Sort = "customerId" }, 1, 25);
            Assert.Equal(["+91 98765 00000", "9876500000"], list.Items.Select(i => i.AlternatePhoneNumber));

            var result = await test.Commands(db).UpdateAsync(first.Id, Update(detail, r => r.AlternatePhoneNumber = "  "));
            Assert.Equal(["AlternatePhoneNumber"], result.ChangedFields);
        }
        await using var check = test.CreateContext();
        Assert.Null((await check.Customers.SingleAsync(c => c.Id == first.Id)).AlternatePhoneNumber);
    }

    [Theory]
    [InlineData("https://maps.app.goo.gl/AbCdEf123", true)]
    [InlineData("https://goo.gl/maps/AbCdEf123", true)]
    [InlineData("https://www.google.com/maps/place/Thanjavur/@10.78,79.13,15z", true)]
    [InlineData("https://www.google.co.in/maps?q=10.78,79.13", true)]
    [InlineData("http://maps.google.com/?q=Thanjavur", true)]
    [InlineData("https://www.google.com/search?q=thanjavur", false)] // Google, but not Maps
    [InlineData("https://goo.gl/AbCdEf", false)]
    [InlineData("https://example.com/maps/place", false)]
    [InlineData("maps.app.goo.gl/AbCdEf", false)]                     // not a full address
    [InlineData("javascript:alert(1)", false)]
    public async Task The_google_maps_link_is_optional_and_must_be_a_google_maps_address(string url, bool valid)
    {
        using var test = new TestDatabase();
        var request = test.NewCustomer();
        request.GoogleMapUrl = $"  {url}  ";
        await using var db = test.CreateContext();

        if (!valid)
        {
            Assert.Contains("Google Maps link", (await Assert.ThrowsAsync<BusinessRuleException>(() => test.Commands(db).CreateAsync(request))).Message);
            return;
        }
        var id = (await test.Commands(db).CreateAsync(request)).Id;
        var detail = await test.Queries(db).GetDetailAsync(id);
        Assert.Equal(url, detail.GoogleMapUrl);
        Assert.Equal(url, (await test.Queries(db).ListAsync(new CustomerFilter(), 1, 25)).Items.Single().GoogleMapUrl);

        var cleared = await test.Commands(db).UpdateAsync(id, Update(detail, r => r.GoogleMapUrl = ""));
        Assert.Equal(["GoogleMapUrl"], cleared.ChangedFields);
        Assert.Null((await test.Queries(db).GetDetailAsync(id)).GoogleMapUrl);
    }

    [Fact]
    public async Task Concurrent_creates_never_share_an_id()
    {
        using var test = new TestDatabase();

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
        {
            await using var db = test.CreateContext();
            return (await test.Commands(db).CreateAsync(test.NewCustomer($"Parallel Customer {i}", $"98000{i:00000}"))).CustomerId;
        }));

        Assert.Equal(12, results.Distinct().Count());
        Assert.All(results, id => Assert.Matches(@"^FITZ-\d{6}$", id));
    }

    [Fact]
    public async Task Existing_customer_ids_are_kept_and_never_reissued()
    {
        using var test = new TestDatabase();
        await using (var db = test.CreateContext())
        {
            // Customers carried over from the old model keep their ids, including one that already looks like a generated id.
            foreach (var id in new[] { "CUST001", "FITZ-000002" })
            {
                var legacy = new Customer { CustomerId = id, CustomerName = "Legacy Customer", StatusId = test.Entry(DictionaryType.CustomerStatus, "ACTIVE") };
                legacy.SetPhoneNumber("9000000001");
                legacy.SetAddress("1 Old Street, Coimbatore");
                db.Customers.Add(legacy);
            }
            await db.SaveChangesAsync();
        }

        var ids = new List<string>();
        for (var i = 0; i < 3; i++) ids.Add((await CreateAsync(test, test.NewCustomer(phone: $"981111111{i}"))).CustomerId);
        Assert.Equal(["FITZ-000001", "FITZ-000003", "FITZ-000004"], ids);

        await using var check = test.CreateContext();
        var legacyCustomer = await check.Customers.SingleAsync(c => c.CustomerId == "CUST001");
        var detail = await test.Queries(check).GetDetailAsync(legacyCustomer.Id);
        Assert.Equal(("CUST001", 0), (detail.CustomerId, detail.Packages.Count));

        // Editing a carried-over customer asks for the delivery time; a pincode it never had may stay empty.
        var missingTime = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            test.Commands(check).UpdateAsync(legacyCustomer.Id, Update(detail, r => r.CustomerName = "Legacy Customer Renamed")));
        Assert.Contains("Preferred delivery time is required", missingTime.Message);

        await test.Commands(check).UpdateAsync(legacyCustomer.Id,
            Update(detail, r => { r.CustomerName = "Legacy Customer Renamed"; r.PreferredDeliveryTime = new TimeOnly(18, 0); }));
        var saved = await check.Customers.AsNoTracking().SingleAsync(c => c.Id == legacyCustomer.Id);
        Assert.Equal(("CUST001", "Legacy Customer Renamed", null, new TimeOnly(18, 0)),
            (saved.CustomerId, saved.CustomerName, saved.Pincode, saved.PreferredDeliveryTime));
    }

    [Theory]
    [InlineData("name", "Customer name")]
    [InlineData("phone", "Invalid phone number")]
    [InlineData("address", "Address is required")]
    [InlineData("pincode", "Pincode must be 6 digits")]
    [InlineData("time", "Preferred delivery time is required")]
    [InlineData("noPackage", "Add at least one package")]
    [InlineData("frequency", "Frequency is required")]
    [InlineData("start", "Start date is required")]
    [InlineData("preference", "Preference is required")]
    [InlineData("mealType", "Meal type is required")]
    [InlineData("quantity", "Quantity must be between 1 and 20")]
    [InlineData("note", "at most 2,000 characters")]
    public async Task Create_validates_every_field(string field, string expected)
    {
        using var test = new TestDatabase();
        var r = test.NewCustomer();
        var p = r.Packages[0];
        switch (field)
        {
            case "name": r.CustomerName = " 1 "; break;
            case "phone": r.PhoneNumber = "12345"; break;
            case "address": r.Address = "  "; break;
            case "pincode": r.Pincode = "06410"; break;
            case "time": r.PreferredDeliveryTime = null; break;
            case "noPackage": r.Packages.Clear(); break;
            case "frequency": p.FrequencyId = null; break;
            case "start": p.StartDate = null; break;
            case "preference": p.PreferenceId = null; break;
            case "mealType": p.MealTypeId = null; break;
            case "quantity": p.Quantity = 21; break;
            case "note": r.Note = new string('x', 2001); break;
        }

        await using var db = test.CreateContext();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => test.Commands(db).CreateAsync(r));
        Assert.Contains(expected, error.Message);
        Assert.Equal(0, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task With_several_packages_the_error_names_the_package()
    {
        using var test = new TestDatabase();
        var r = test.NewCustomer();
        r.Packages.Add(test.NewPackage(start: new DateOnly(2026, 10, 11))); // a Sunday

        await using var db = test.CreateContext();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => test.Commands(db).CreateAsync(r));
        Assert.StartsWith("Package 2:", error.Message);
        Assert.Contains("Sunday", error.Message);
    }

    [Fact]
    public async Task Start_date_must_be_a_delivery_day_within_a_year()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();

        var sunday = await Assert.ThrowsAsync<BusinessRuleException>(() => test.Commands(db).CreateAsync(test.NewCustomer(start: new DateOnly(2026, 10, 11))));
        Assert.Contains("Sunday", sunday.Message);
        await Assert.ThrowsAsync<BusinessRuleException>(() => test.Commands(db).CreateAsync(test.NewCustomer(start: new DateOnly(2028, 1, 3))));
        await Assert.ThrowsAsync<BusinessRuleException>(() => test.Schedule(db).PreviewAsync(test.Entry(DictionaryType.Frequency, "WEEKLY"), new DateOnly(2026, 10, 11)));
    }

    [Fact]
    public async Task Dropdown_values_must_exist_match_their_type_and_be_active()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();

        var wrongType = test.NewCustomer();
        wrongType.Packages[0].MealTypeId = test.Entry(DictionaryType.Preference, "VEG");
        await Assert.ThrowsAsync<BusinessRuleException>(() => test.Commands(db).CreateAsync(wrongType));

        var missing = test.NewCustomer();
        missing.Packages[0].PreferenceId = 999_999;
        await Assert.ThrowsAsync<BusinessRuleException>(() => test.Commands(db).CreateAsync(missing));

        var oats = test.Entry(DictionaryType.MealType, "OATS");
        await test.Dictionaries(db).UpdateAsync(oats, new() { DisplayName = "Oats", IsActive = false });
        var inactive = test.NewCustomer();
        inactive.Packages[0].MealTypeId = oats;
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => test.Commands(db).CreateAsync(inactive));
        Assert.Contains("no longer available", error.Message);
    }

    [Fact]
    public async Task Schedule_preview_uses_the_frequency_delivery_days()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var schedule = test.Schedule(db);

        var weekly = await schedule.PreviewAsync(test.Entry(DictionaryType.Frequency, "WEEKLY"), new DateOnly(2026, 10, 10));
        Assert.Equal((new DateOnly(2026, 10, 16), 6, 7), (weekly.EndDate, weekly.DeliveryDays, weekly.CalendarDays)); // Sat → Fri, Sunday skipped

        var monthly = await schedule.PreviewAsync(test.Entry(DictionaryType.Frequency, "MONTHLY"), new DateOnly(2026, 10, 5));
        Assert.Equal((new DateOnly(2026, 11, 3), 26), (monthly.EndDate, monthly.DeliveryDays));
    }

    [Fact]
    public async Task Edit_records_old_and_new_values_and_detects_concurrent_changes()
    {
        using var test = new TestDatabase();
        var (id, customerId) = await CreateAsync(test, test.NewCustomer());

        await using var db = test.CreateContext();
        var detail = await test.Queries(db).GetDetailAsync(id);
        test.Clock.UtcNow = test.Clock.UtcNow.AddMinutes(5);

        var inactive = test.Entry(DictionaryType.CustomerStatus, "INACTIVE");
        var result = await test.Commands(db).UpdateAsync(id, Update(detail, r =>
        {
            r.CustomerName = "  Anitha   Raman K ";
            r.Address = "12 Gandhipuram, Coimbatore 641012";
            r.Pincode = "641012";
            r.PreferredDeliveryTime = new TimeOnly(19, 0);
            r.StatusId = inactive;
        }));
        Assert.Equal(["CustomerName", "Address", "Pincode", "PreferredDeliveryTime", "Status"], result.ChangedFields);

        await using (var check = test.CreateContext())
        {
            var saved = await check.Customers.SingleAsync(c => c.Id == id);
            Assert.Equal((customerId, "Anitha Raman K", "641012", inactive, new TimeOnly(19, 0)),
                (saved.CustomerId, saved.CustomerName, saved.Pincode, saved.StatusId, saved.PreferredDeliveryTime));
            var audit = await check.AuditLogs.SingleAsync(a => a.Action == AuditActions.CustomerUpdated);
            Assert.Equal("07:30:00", JsonDocument.Parse(audit.OldValues!).RootElement.GetProperty("PreferredDeliveryTime").GetString());
        }

        // A second editor still holding the original UpdatedAt must not silently overwrite the change.
        await using var other = test.CreateContext();
        var conflict = await Assert.ThrowsAsync<BusinessRuleException>(() => test.Commands(other).UpdateAsync(id, Update(detail, r => r.CustomerName = "Someone Else")));
        Assert.Contains("changed by test-user", conflict.Message);
    }

    [Fact]
    public async Task Concurrent_saves_of_the_same_customer_are_detected_by_the_row_version()
    {
        using var test = new TestDatabase();
        var (id, _) = await CreateAsync(test, test.NewCustomer());

        await using var first = test.CreateContext();
        await using var second = test.CreateContext();
        var a = await first.Customers.SingleAsync(c => c.Id == id);
        var b = await second.Customers.SingleAsync(c => c.Id == id);
        a.CustomerName = "First Editor";
        await first.SaveChangesAsync();
        b.CustomerName = "Second Editor";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Theory]
    [InlineData("\u202A+91 98765 43210\u202C", PhoneCheck.ValidMobile, "9876543210")] // copied from WhatsApp / Contacts
    [InlineData("\u200E9876543210\u200F", PhoneCheck.ValidMobile, "9876543210")]
    [InlineData("98765\u200B43210", PhoneCheck.ValidMobile, "9876543210")]
    [InlineData("\u202A\u202C", PhoneCheck.Missing, null)]
    public void Hidden_marks_in_a_pasted_phone_number_are_ignored(string input, PhoneCheck check, string? normalized) =>
        Assert.Equal(new PhoneValidationResult(check, normalized), PhoneNumberValidator.Validate(input));

    [Fact]
    public async Task A_pasted_phone_number_with_hidden_marks_is_saved_without_them()
    {
        using var test = new TestDatabase();
        var request = test.NewCustomer("Pasted Number", "\u202A+91 90000 12345\u202C");
        request.AlternatePhoneNumber = "\u200E9876500000";
        var created = await CreateAsync(test, request);

        await using var db = test.CreateContext();
        var saved = await db.Customers.SingleAsync(c => c.Id == created.Id);
        Assert.Equal(("+91 90000 12345", "9000012345", "9876500000"), (saved.PhoneNumber, saved.NormalizedPhoneNumber, saved.AlternatePhoneNumber));
    }

    [Theory]
    [InlineData("+91 98765 43210", "9876543210")]
    [InlineData("09876543210", "9876543210")]
    [InlineData("98765-43210", "9876543210")]
    [InlineData("", null)]
    public void Phone_numbers_are_normalised(string input, string? expected) =>
        Assert.Equal(expected, Normalization.NormalizePhone(input));

    internal static UpdateCustomerRequest Update(CustomerDetailDto d, Action<UpdateCustomerRequest>? change = null)
    {
        var r = new UpdateCustomerRequest
        {
            CustomerName = d.CustomerName,
            PhoneNumber = d.PhoneNumber,
            Address = d.Address,
            Pincode = d.Pincode,
            PreferredDeliveryTime = d.PreferredDeliveryTime,
            StatusId = d.StatusId,
            ExpectedUpdatedAt = d.UpdatedAt
        };
        change?.Invoke(r);
        return r;
    }
}
