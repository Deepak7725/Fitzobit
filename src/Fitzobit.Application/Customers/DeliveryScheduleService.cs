using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Common;
using Fitzobit.Application.Dictionaries;
using Fitzobit.Domain.Deliveries;
using Fitzobit.Domain.Dictionaries;

namespace Fitzobit.Application.Customers;

/// <param name="DeliveryDays">Delivery days in the package (the frequency's value).</param>
/// <param name="CalendarDays">Calendar days from start to end, inclusive (Sundays included).</param>
public sealed record SchedulePreviewDto(DateOnly StartDate, DateOnly EndDate, int DeliveryDays, int CalendarDays, string Frequency);

/// <summary>
/// Package dates for customers: validates start dates and calculates end dates from a frequency's delivery days, using
/// <see cref="IDeliveryCalendarService"/> (Monday–Saturday; Sundays are not delivery days).
/// </summary>
public sealed class DeliveryScheduleService(IDeliveryCalendarService calendar, DictionaryService dictionaries, IClock clock)
{
    /// <summary>How far before/after today a start date may be.</summary>
    public const int MaxStartDateOffsetDays = 366;

    /// <summary>Fitzobit operates in India; "today" is the date in India Standard Time (UTC+5:30, no daylight saving).</summary>
    public static DateOnly Today(IClock clock) => DateOnly.FromDateTime(clock.UtcNow.AddMinutes(330));

    public IDeliveryCalendarService Calendar => calendar;

    public void ValidateStartDate(DateOnly startDate)
    {
        if (!calendar.IsDeliveryDay(startDate))
            throw new BusinessRuleException($"{startDate:dd MMM yyyy} is a Sunday. The start date must be a delivery day (Monday to Saturday).");
        var today = Today(clock);
        if (Math.Abs(startDate.DayNumber - today.DayNumber) > MaxStartDateOffsetDays)
            throw new BusinessRuleException($"The start date must be within {MaxStartDateOffsetDays} days of today.");
    }

    /// <summary>The end date for a new package: <paramref name="startDate"/> plus the frequency's delivery days.</summary>
    public async Task<SchedulePreviewDto> PreviewAsync(int frequencyId, DateOnly startDate, CancellationToken ct = default)
    {
        ValidateStartDate(startDate);
        var frequency = await dictionaries.ResolveSelectionAsync(DictionaryType.Frequency, frequencyId, null, ct);
        var days = DeliveryDays(frequency);
        var end = calendar.CalculateEndDate(startDate, days);
        return new SchedulePreviewDto(startDate, end, days, end.DayNumber - startDate.DayNumber + 1, frequency.DisplayName);
    }

    /// <summary>The end date of a package, with paused days (inside the package) moved to the end.</summary>
    public DateOnly CalculateEndDate(DateOnly startDate, DictionaryEntry frequency, IReadOnlyCollection<DateOnly> pausedDates) =>
        calendar.CalculateEndDate(startDate, DeliveryDays(frequency), pausedDates.Where(d => d >= startDate).ToList());

    public static int DeliveryDays(DictionaryEntry frequency) =>
        frequency.NumericValue is { } days && days is >= 1 and <= DeliveryCalendarService.MaxDeliveryDays
            ? days
            : throw new BusinessRuleException($"The frequency \"{frequency.DisplayName}\" has no delivery-day count. Set it in Settings → Dropdown values.");
}
