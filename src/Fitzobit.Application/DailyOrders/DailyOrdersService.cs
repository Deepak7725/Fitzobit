using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Deliveries;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitzobit.Application.DailyOrders;

/// <summary>Boxes of one meal type and preference, e.g. Medium · Veg · 12.</summary>
public sealed record MealTotalDto(string MealType, string Preference, int Boxes);

/// <summary>One package delivered that day.</summary>
public sealed record OrderItemDto(string Preference, string MealType, int Quantity, string Frequency)
{
    /// <summary>"Veg Medium ×2"</summary>
    public string Text => $"{Preference} {MealType} ×{Quantity}";
}

/// <summary>One customer's delivery that day: all their packages together (packed in one bag).</summary>
/// <param name="PausedDays">Paused days of the packages delivered that day (each day once); <see cref="OwnPausedDays"/> of them paused by
/// the customer, the rest for everyone.</param>
public sealed record CustomerOrdersDto(
    int Id,
    string CustomerId,
    string CustomerName,
    string PhoneNumber,
    string? AlternatePhoneNumber,
    string? Address,
    string? Pincode,
    string? GoogleMapUrl,
    TimeOnly? DeliveryTime,
    int Boxes,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<string> Notes,
    int PausedDays = 0,
    int OwnPausedDays = 0)
{
    /// <summary>"Veg Medium ×1 + With Egg Oats ×2"</summary>
    public string ItemsText => string.Join(" + ", Items.Select(i => i.Text));

    /// <summary>"5 (2 own + 3 for everyone)"; "0" when nothing was paused.</summary>
    public string PausedText => CustomerPauseService.PausedDaysText(OwnPausedDays, PausedDays - OwnPausedDays);
}

/// <summary>A delivery group's orders (GroupId null = customers without a group).</summary>
public sealed record GroupOrdersDto(int? GroupId, int? GroupNumber, string Label, int Boxes, IReadOnlyList<MealTotalDto> Totals,
    IReadOnlyList<CustomerOrdersDto> Customers);

/// <param name="IsDeliveryDay">False on Sundays (nothing is delivered).</param>
public sealed record DailyOrdersDto(DateOnly Date, bool IsDeliveryDay, int TotalBoxes, int TotalCustomers, IReadOnlyList<MealTotalDto> Totals,
    IReadOnlyList<GroupOrdersDto> Groups);

/// <summary>Writes the daily orders as Excel or PDF (Infrastructure: ClosedXML, MigraDoc).</summary>
public interface IDailyOrdersExportWriter
{
    ExportFormat Format { get; }
    string ContentType { get; }
    string FileExtension { get; }
    void Write(Stream output, DailyOrdersDto orders, string exportedBy, DateTime exportedAtUtc, TimeZoneInfo timeZone);
}

/// <summary>
/// Daily Orders (owner's design, 2026-10-31): for one day, every package that delivers then — started, not finished, not paused that
/// day — of customers whose status is Active, counted in boxes (quantity). Kitchen totals per meal type × preference, and per delivery
/// group the totals and the customers in delivery-time order with all their packages on one line (one bag). Sundays have no deliveries.
/// </summary>
public sealed class DailyOrdersService(IFitzobitDbContext db, IDeliveryCalendarService calendar, IEnumerable<IDailyOrdersExportWriter> writers,
    IAuditLogger audit, ICurrentUser currentUser, IClock clock, IOptions<ExportOptions> exportOptions)
{
    /// <summary>The day the page opens on: the next delivery day after today (Saturday → Monday).</summary>
    public DateOnly DefaultDate() => calendar.NextDeliveryDay(DeliveryScheduleService.Today(clock));

    public async Task<DailyOrdersDto> GetAsync(DateOnly date, CancellationToken ct = default)
    {
        if (!calendar.IsDeliveryDay(date)) return new DailyOrdersDto(date, false, 0, 0, [], []);

        // Past packages count too when looking back at a day they delivered; their date range decides.
        var packages = await db.CustomerPackages.AsNoTracking()
            .Where(p => p.StartDate <= date && p.EndDate >= date && !p.Pauses.Any(x => x.PausedDate == date)
                        && p.Customer!.Status!.Code == CustomerStatusCodes.Active)
            .Select(p => new
            {
                p.Id,
                p.CustomerId,
                p.Quantity,
                Preference = p.Preference!.DisplayName,
                PreferenceOrder = p.Preference.SortOrder,
                MealType = p.MealType!.DisplayName,
                MealOrder = p.MealType.SortOrder,
                Frequency = p.Frequency!.DisplayName,
                p.StartDate
            })
            .ToListAsync(ct);

        var customerIds = packages.Select(p => p.CustomerId).Distinct().ToList();
        var packageIds = packages.Select(p => p.Id).ToList();
        var pauses = (await db.PackagePauses.AsNoTracking().Where(x => packageIds.Contains(x.PackageId))
                .Select(x => new { x.Package!.CustomerId, x.PausedDate, Own = x.FestivePauseId == null }).ToListAsync(ct))
            .ToLookup(x => x.CustomerId);
        var customers = await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
            .Select(c => new
            {
                c.Id, c.CustomerId, c.CustomerName, c.PhoneNumber, c.AlternatePhoneNumber, c.Address, c.Pincode, c.GoogleMapUrl, c.PreferredDeliveryTime,
                GroupId = c.GroupAssignments.Where(a => a.IsActive).Select(a => (int?)a.DeliveryGroupId).FirstOrDefault(),
                GroupNumber = c.GroupAssignments.Where(a => a.IsActive).Select(a => (int?)a.DeliveryGroup!.GroupNumber).FirstOrDefault()
            })
            .ToListAsync(ct);
        var notes = (await db.CustomerNotes.AsNoTracking().Where(n => customerIds.Contains(n.CustomerId))
                .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Select(n => new { n.CustomerId, n.Text }).ToListAsync(ct))
            .ToLookup(n => n.CustomerId, n => string.Join(' ', n.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)));

        // Meal order for the totals: meal type sort order, then preference sort order.
        var order = packages.Select(p => (p.MealType, p.Preference, p.MealOrder, p.PreferenceOrder)).Distinct()
            .ToDictionary(x => (x.MealType, x.Preference), x => (x.MealOrder, x.PreferenceOrder));
        List<MealTotalDto> Totals(IEnumerable<(string MealType, string Preference, int Quantity)> items) =>
            items.GroupBy(i => (i.MealType, i.Preference))
                .Select(g => new MealTotalDto(g.Key.MealType, g.Key.Preference, g.Sum(i => i.Quantity)))
                .OrderBy(t => order[(t.MealType, t.Preference)].MealOrder).ThenBy(t => t.MealType)
                .ThenBy(t => order[(t.MealType, t.Preference)].PreferenceOrder).ThenBy(t => t.Preference)
                .ToList();

        var byCustomer = packages.ToLookup(p => p.CustomerId);
        var rows = customers.Select(c =>
        {
            var items = byCustomer[c.Id].OrderBy(p => p.MealOrder).ThenBy(p => p.PreferenceOrder).ThenBy(p => p.StartDate).ThenBy(p => p.Id)
                .Select(p => new OrderItemDto(p.Preference, p.MealType, p.Quantity, p.Frequency)).ToList();
            var paused = pauses[c.Id].ToList();
            return new
            {
                c.GroupId,
                c.GroupNumber,
                Order = new CustomerOrdersDto(c.Id, c.CustomerId, c.CustomerName, c.PhoneNumber, c.AlternatePhoneNumber, c.Address, c.Pincode, c.GoogleMapUrl,
                    c.PreferredDeliveryTime, items.Sum(i => i.Quantity), items, notes[c.Id].ToList(),
                    paused.Select(x => x.PausedDate).Distinct().Count(), paused.Where(x => x.Own).Select(x => x.PausedDate).Distinct().Count())
            };
        }).ToList();

        var groups = rows.GroupBy(r => (r.GroupId, r.GroupNumber))
            .OrderBy(g => g.Key.GroupNumber is null).ThenBy(g => g.Key.GroupNumber)
            .Select(g =>
            {
                var list = g.Select(r => r.Order).OrderBy(o => o.DeliveryTime is null).ThenBy(o => o.DeliveryTime).ThenBy(o => o.CustomerId).ToList();
                return new GroupOrdersDto(g.Key.GroupId, g.Key.GroupNumber, g.Key.GroupNumber is { } n ? DeliveryGroup.Label(n) : "No group",
                    list.Sum(o => o.Boxes), Totals(list.SelectMany(o => o.Items).Select(i => (i.MealType, i.Preference, i.Quantity))), list);
            })
            .ToList();

        return new DailyOrdersDto(date, true, packages.Sum(p => p.Quantity), customers.Count,
            Totals(packages.Select(p => (p.MealType, p.Preference, p.Quantity))), groups);
    }

    public async Task<ExportFile> ExportAsync(DateOnly date, ExportFormat format, CancellationToken ct = default)
    {
        var writer = writers.FirstOrDefault(w => w.Format == format) ?? throw new BusinessRuleException($"{format} export is not available.");
        var orders = await GetAsync(date, ct);
        var timeZone = ResolveTimeZone();
        var now = clock.UtcNow;
        using var stream = new MemoryStream();
        writer.Write(stream, orders, currentUser.DisplayName, now, timeZone);

        var fileName = $"Fitzobit_Daily_Orders_{date:yyyy-MM-dd}.{writer.FileExtension}";
        audit.Record(AuditActions.ExportPerformed, "DailyOrders", date.ToString("yyyy-MM-dd"),
            $"Daily orders for {date:dd MMM yyyy} exported to {format} ({orders.TotalCustomers} customer(s), {orders.TotalBoxes} box(es))",
            new { Date = date, Format = format.ToString(), orders.TotalCustomers, orders.TotalBoxes, FileName = fileName });
        await db.SaveChangesAsync(ct);
        return new ExportFile(stream.ToArray(), fileName, writer.ContentType, orders.TotalCustomers);
    }

    private TimeZoneInfo ResolveTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(exportOptions.Value.TimeZone); }
        catch (Exception) { return TimeZoneInfo.Utc; }
    }
}
