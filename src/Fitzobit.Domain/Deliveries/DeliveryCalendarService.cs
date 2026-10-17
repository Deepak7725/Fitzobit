namespace Fitzobit.Domain.Deliveries;

/// <summary>
/// Delivery-day arithmetic. Fitzobit delivers Monday to Saturday; Sundays are never delivery days, so package lengths
/// and pauses are counted in delivery days, not calendar days. All date rules live here.
/// </summary>
public interface IDeliveryCalendarService
{
    bool IsDeliveryDay(DateOnly date);

    /// <summary>The first delivery day strictly after <paramref name="date"/>.</summary>
    DateOnly NextDeliveryDay(DateOnly date);

    /// <summary>Delivery days from <paramref name="start"/> to <paramref name="end"/>, both inclusive (0 when end is before start).</summary>
    int CountDeliveryDays(DateOnly start, DateOnly end);

    /// <summary>
    /// The date of the <paramref name="requiredDeliveryDays"/>-th delivery day counting <paramref name="startDate"/> as the first.
    /// Dates in <paramref name="skippedDates"/> (paused days) are not delivered, so they push the end date forward.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The start date is not a delivery day, or the day count is not positive.</exception>
    DateOnly CalculateEndDate(DateOnly startDate, int requiredDeliveryDays, IReadOnlyCollection<DateOnly>? skippedDates = null);

    /// <summary>The end date after pausing <paramref name="pausedDays"/> delivery day(s): each one moves to the next delivery day.</summary>
    DateOnly ExtendEndDateForPause(DateOnly currentEndDate, int pausedDays = 1);
}

public sealed class DeliveryCalendarService : IDeliveryCalendarService
{
    /// <summary>Upper bound for a package, so bad input can never loop for long.</summary>
    public const int MaxDeliveryDays = 1000;

    public bool IsDeliveryDay(DateOnly date) => date.DayOfWeek != DayOfWeek.Sunday;

    public DateOnly NextDeliveryDay(DateOnly date)
    {
        var next = date.AddDays(1);
        while (!IsDeliveryDay(next)) next = next.AddDays(1);
        return next;
    }

    public int CountDeliveryDays(DateOnly start, DateOnly end)
    {
        if (end < start) return 0;
        var days = end.DayNumber - start.DayNumber + 1;
        var fullWeeks = days / 7;
        var count = fullWeeks * 6; // every full week has exactly one Sunday
        for (var d = start.AddDays(fullWeeks * 7); d <= end; d = d.AddDays(1))
            if (IsDeliveryDay(d)) count++;
        return count;
    }

    public DateOnly CalculateEndDate(DateOnly startDate, int requiredDeliveryDays, IReadOnlyCollection<DateOnly>? skippedDates = null)
    {
        if (requiredDeliveryDays is < 1 or > MaxDeliveryDays)
            throw new ArgumentOutOfRangeException(nameof(requiredDeliveryDays), requiredDeliveryDays, $"A package has 1–{MaxDeliveryDays} delivery days.");
        if (!IsDeliveryDay(startDate))
            throw new ArgumentOutOfRangeException(nameof(startDate), startDate, "The start date must be a delivery day (Monday to Saturday).");

        var skipped = skippedDates is { Count: > 0 } ? new HashSet<DateOnly>(skippedDates) : null;
        var delivered = 0;
        for (var date = startDate; ; date = date.AddDays(1))
        {
            if (!IsDeliveryDay(date) || skipped?.Contains(date) == true) continue;
            if (++delivered == requiredDeliveryDays) return date;
        }
    }

    public DateOnly ExtendEndDateForPause(DateOnly currentEndDate, int pausedDays = 1)
    {
        if (pausedDays < 0) throw new ArgumentOutOfRangeException(nameof(pausedDays), pausedDays, "Paused days cannot be negative.");
        var end = currentEndDate;
        for (var i = 0; i < pausedDays; i++) end = NextDeliveryDay(end);
        return end;
    }
}
