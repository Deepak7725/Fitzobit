using System.Globalization;
using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Deliveries;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Customers;

/// <summary>Settings → General → Renewal reminders.</summary>
/// <param name="Message">Template with {name}, {package}, {endDate} and {daysLeft}.</param>
public sealed record RenewalSettingsDto(int DaysBefore, string Message, string DefaultMessage, DateTime? UpdatedAt, string? UpdatedBy);

public sealed class SaveRenewalSettingsRequest
{
    public int DaysBefore { get; set; }
    public string? Message { get; set; }
}

/// <summary>A package whose renewal is due, with the ready-to-send WhatsApp link (null when the phone number is not a valid mobile).</summary>
public sealed record RenewalDueDto(
    int PackageId,
    int CustomerId,
    string CustomerCode,
    string CustomerName,
    string PhoneNumber,
    string Status,
    string Package,
    DateOnly StartDate,
    DateOnly EndDate,
    int DaysLeft,
    DateTime? RemindedAt,
    string? RemindedBy,
    string Message,
    string? WhatsAppUrl);

public sealed record RenewalListDto(int DaysBefore, IReadOnlyList<RenewalDueDto> Items);

public sealed record ReminderRecordedDto(DateTime RemindedAt, string RemindedBy);

/// <summary>
/// Renewal reminders (owner's decisions, 2026-10-26). A package is <b>due</b> when it has started, its delivery days left (today
/// included, paused days not counted) are at most the configured number (default 3), its customer is Active or Paused, and the customer
/// has no other current package starting on or after its end date (already renewed). The reminder itself is a free WhatsApp
/// click-to-chat link to the customer's main phone with the message filled in; the user presses Send in WhatsApp. Clicking it records
/// who sent a reminder and when (the app cannot see whether the message was actually sent).
/// </summary>
public sealed class RenewalReminderService(IFitzobitDbContext db, IDeliveryCalendarService calendar, IAuditLogger audit, ICurrentUser currentUser,
    IClock clock)
{
    public const string DaysKey = "Renewal.DaysBefore";
    public const string MessageKey = "Renewal.Message";
    public const int DefaultDays = 3;
    public const int MaxDays = 30;
    public const int MaxMessageLength = 1000;

    public const string DefaultMessage =
        "Hi {name}, your Fitzobit {package} package ends on {endDate}. Would you like to renew? Just reply to this message. Thank you!";

    public async Task<RenewalSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var values = await db.AppSettings.AsNoTracking().Where(s => s.Key == DaysKey || s.Key == MessageKey).ToListAsync(ct);
        var days = values.FirstOrDefault(s => s.Key == DaysKey);
        var message = values.FirstOrDefault(s => s.Key == MessageKey);
        var latest = values.OrderByDescending(s => s.UpdatedAt).FirstOrDefault();
        return new RenewalSettingsDto(int.TryParse(days?.Value, out var d) ? d : DefaultDays, string.IsNullOrWhiteSpace(message?.Value) ? DefaultMessage : message.Value,
            DefaultMessage, latest?.UpdatedAt, latest?.UpdatedBy);
    }

    public async Task<RenewalSettingsDto> SaveSettingsAsync(SaveRenewalSettingsRequest request, CancellationToken ct = default)
    {
        if (request.DaysBefore is < 1 or > MaxDays) throw new BusinessRuleException($"Days before the end must be between 1 and {MaxDays}.");
        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message)) throw new BusinessRuleException("Enter the reminder message.");
        if (message.Length > MaxMessageLength) throw new BusinessRuleException($"The message must be at most {MaxMessageLength} characters.");

        var before = await GetSettingsAsync(ct);
        await SetAsync(DaysKey, request.DaysBefore.ToString(CultureInfo.InvariantCulture), ct);
        await SetAsync(MessageKey, message, ct);
        audit.RecordChange(AuditActions.SettingsUpdated, "Settings", "RenewalReminders", "Renewal reminder settings changed",
            new Dictionary<string, object?> { ["DaysBefore"] = before.DaysBefore, ["Message"] = before.Message },
            new Dictionary<string, object?> { ["DaysBefore"] = request.DaysBefore, ["Message"] = message });
        await db.SaveChangesAsync(ct);
        return await GetSettingsAsync(ct);
    }

    /// <summary>Every package whose renewal is due (optionally of one customer), soonest end first.</summary>
    public async Task<RenewalListDto> ListDueAsync(int? customerId = null, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        var today = DeliveryScheduleService.Today(clock);
        var packages = await db.CustomerPackages.AsNoTracking()
            .Include(p => p.Customer!).ThenInclude(c => c.Status)
            .Include(p => p.Preference).Include(p => p.MealType).Include(p => p.Frequency).Include(p => p.Pauses)
            .Where(p => p.ArchivedAt == null && p.EndDate >= today && (customerId == null || p.CustomerId == customerId)
                        && (p.Customer!.Status!.Code == CustomerStatusCodes.Active || p.Customer.Status.Code == CustomerStatusCodes.Paused))
            .ToListAsync(ct);

        var items = new List<RenewalDueDto>();
        foreach (var p in packages)
        {
            if (p.StartDate > today) continue; // not started yet
            var daysLeft = DaysLeft(p, today);
            if (daysLeft > settings.DaysBefore) continue;
            if (packages.Any(o => o.Id != p.Id && o.CustomerId == p.CustomerId && o.StartDate >= p.EndDate)) continue; // already renewed

            var name = PackageName(p);
            var message = Fill(settings.Message, p.Customer!.CustomerName, name, p.EndDate, daysLeft);
            items.Add(new RenewalDueDto(p.Id, p.CustomerId, p.Customer.CustomerId, p.Customer.CustomerName, p.Customer.PhoneNumber, p.Customer.Status!.DisplayName,
                name, p.StartDate, p.EndDate, daysLeft, p.RenewalReminderAt, p.RenewalReminderBy, message,
                WhatsAppUrl(p.Customer.NormalizedPhoneNumber, message)));
        }
        return new RenewalListDto(settings.DaysBefore, items.OrderBy(i => i.EndDate).ThenBy(i => i.CustomerCode).ToList());
    }

    /// <summary>Records that a reminder was sent for the package (pressing "Send reminder").</summary>
    public async Task<ReminderRecordedDto> RecordReminderAsync(int packageId, CancellationToken ct = default)
    {
        var package = await db.CustomerPackages.Include(p => p.Customer).Include(p => p.Preference).Include(p => p.MealType).Include(p => p.Frequency)
                          .FirstOrDefaultAsync(p => p.Id == packageId, ct)
                      ?? throw new NotFoundException($"Package {packageId} was not found.");
        if (package.IsPast) throw new BusinessRuleException(CustomerPackageService.PastPackageMessage);

        var previous = package.RenewalReminderAt;
        package.RenewalReminderAt = clock.UtcNow;
        package.RenewalReminderBy = currentUser.DisplayName;
        audit.RecordChange(AuditActions.RenewalReminderSent, nameof(Customer), package.Customer!.CustomerId,
            $"Renewal reminder sent: {package.Customer.CustomerId} {PackageName(package)} (ends {package.EndDate:dd MMM yyyy})",
            new Dictionary<string, object?> { ["RenewalReminderAt"] = previous }, new Dictionary<string, object?> { ["RenewalReminderAt"] = package.RenewalReminderAt });
        await db.SaveChangesAsync(ct);
        return new ReminderRecordedDto(package.RenewalReminderAt.Value, package.RenewalReminderBy!);
    }

    /// <summary>Delivery days from today (or the start) to the end date, both included, without paused days.</summary>
    private int DaysLeft(CustomerPackage p, DateOnly today)
    {
        var from = p.StartDate > today ? p.StartDate : today;
        return calendar.CountDeliveryDays(from, p.EndDate) - p.Pauses.Count(x => x.PausedDate >= from && x.PausedDate <= p.EndDate);
    }

    private static string PackageName(CustomerPackage p) =>
        $"{p.Preference!.DisplayName} {p.MealType!.DisplayName} ×{p.Quantity} · {p.Frequency!.DisplayName}";

    /// <summary>Fills {name}, {package}, {endDate} ("Tue, 03 Nov 2026") and {daysLeft} (case-insensitive).</summary>
    public static string Fill(string template, string name, string package, DateOnly endDate, int daysLeft) =>
        template.Replace("{name}", name, StringComparison.OrdinalIgnoreCase)
            .Replace("{package}", package, StringComparison.OrdinalIgnoreCase)
            .Replace("{endDate}", endDate.ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{daysLeft}", daysLeft.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

    /// <summary>https://wa.me/91XXXXXXXXXX?text=… for a 10-digit Indian mobile number; otherwise null.</summary>
    public static string? WhatsAppUrl(string? normalizedPhone, string message) =>
        normalizedPhone is { Length: 10 } phone && phone[0] is >= '6' and <= '9' && phone.All(char.IsDigit)
            ? $"https://wa.me/91{phone}?text={Uri.EscapeDataString(message)}"
            : null;

    private async Task SetAsync(string key, string value, CancellationToken ct)
    {
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null) db.AppSettings.Add(setting = new AppSetting { Key = key });
        setting.Value = value;
        setting.UpdatedBy = currentUser.UserName;
    }
}
