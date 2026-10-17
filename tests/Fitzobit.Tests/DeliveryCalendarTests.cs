using Fitzobit.Domain.Deliveries;

namespace Fitzobit.Tests;

/// <summary>Delivery-day rules: Monday–Saturday only; weekly = 6 and monthly = 26 delivery days; pauses move to the next delivery day.</summary>
public class DeliveryCalendarTests
{
    private static readonly DeliveryCalendarService Calendar = new();
    private static DateOnly D(int month, int day) => new(2026, month, day);

    [Fact]
    public void Sundays_are_not_delivery_days()
    {
        Assert.Equal(DayOfWeek.Monday, D(10, 5).DayOfWeek);
        Assert.True(Enumerable.Range(5, 6).All(d => Calendar.IsDeliveryDay(D(10, d)))); // Mon 5 – Sat 10
        Assert.False(Calendar.IsDeliveryDay(D(10, 11)));                               // Sun 11
        Assert.Equal(D(10, 12), Calendar.NextDeliveryDay(D(10, 10)));                  // Sat → Mon
        Assert.Equal(D(10, 12), Calendar.NextDeliveryDay(D(10, 11)));                  // Sun → Mon
        Assert.Equal(D(10, 7), Calendar.NextDeliveryDay(D(10, 6)));
    }

    [Fact]
    public void Weekly_package_is_six_delivery_days_from_monday_to_saturday()
    {
        var end = Calendar.CalculateEndDate(D(10, 5), 6);
        Assert.Equal(D(10, 10), end);
        Assert.Equal(DayOfWeek.Saturday, end.DayOfWeek);
        Assert.Equal(6, Calendar.CountDeliveryDays(D(10, 5), end));
    }

    [Fact]
    public void Weekly_package_starting_midweek_skips_sunday()
    {
        // Wed 7, Thu 8, Fri 9, Sat 10, (Sun 11 skipped), Mon 12, Tue 13.
        Assert.Equal(D(10, 13), Calendar.CalculateEndDate(D(10, 7), 6));
    }

    [Fact]
    public void Monthly_package_is_twenty_six_delivery_days()
    {
        // Four full weeks (24 days, Mon 5 – Sat 31 Oct), Sunday 1 Nov skipped, then Mon 2 and Tue 3 Nov.
        var end = Calendar.CalculateEndDate(D(10, 5), 26);
        Assert.Equal(new DateOnly(2026, 11, 3), end);
        Assert.Equal(26, Calendar.CountDeliveryDays(D(10, 5), end));
        Assert.NotEqual(D(10, 5).AddDays(30), end);
    }

    [Theory]
    [InlineData(2026, 10, 5, 2026, 10, 5, 1)]
    [InlineData(2026, 10, 5, 2026, 10, 11, 6)]
    [InlineData(2026, 10, 5, 2026, 10, 12, 7)]
    [InlineData(2026, 10, 11, 2026, 10, 11, 0)]
    [InlineData(2026, 10, 12, 2026, 10, 5, 0)]
    [InlineData(2026, 1, 1, 2026, 12, 31, 313)] // 365 days, 52 Sundays
    public void Counting_delivery_days_excludes_sundays(int y1, int m1, int d1, int y2, int m2, int d2, int expected) =>
        Assert.Equal(expected, Calendar.CountDeliveryDays(new DateOnly(y1, m1, d1), new DateOnly(y2, m2, d2)));

    [Fact]
    public void Counting_matches_day_by_day_counting_for_every_range_in_a_year()
    {
        var start = new DateOnly(2026, 1, 1);
        for (var length = 0; length < 60; length++)
            for (var offset = 0; offset < 7; offset++)
            {
                var from = start.AddDays(offset);
                var to = from.AddDays(length);
                var slow = Enumerable.Range(0, length + 1).Count(i => from.AddDays(i).DayOfWeek != DayOfWeek.Sunday);
                Assert.Equal(slow, Calendar.CountDeliveryDays(from, to));
            }
    }

    [Fact]
    public void End_date_is_always_a_delivery_day_with_exactly_the_required_days()
    {
        for (var startOffset = 0; startOffset < 14; startOffset++)
        {
            var start = D(10, 5).AddDays(startOffset);
            if (!Calendar.IsDeliveryDay(start)) continue;
            foreach (var days in new[] { 1, 6, 12, 26 })
            {
                var end = Calendar.CalculateEndDate(start, days);
                Assert.True(Calendar.IsDeliveryDay(end));
                Assert.Equal(days, Calendar.CountDeliveryDays(start, end));
            }
        }
    }

    [Fact]
    public void A_sunday_start_or_a_non_positive_package_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.CalculateEndDate(D(10, 11), 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.CalculateEndDate(D(10, 5), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.CalculateEndDate(D(10, 5), -3));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.CalculateEndDate(D(10, 5), DeliveryCalendarService.MaxDeliveryDays + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.ExtendEndDateForPause(D(10, 10), -1));
    }

    [Fact]
    public void Pausing_saturday_moves_the_meal_to_monday_not_sunday()
    {
        // Weekly customer, Mon 12 – Sat 17 Oct; skips Saturday: the meal moves to Monday 19.
        var end = Calendar.CalculateEndDate(D(10, 12), 6);
        Assert.Equal(D(10, 17), end);
        Assert.Equal(D(10, 19), Calendar.ExtendEndDateForPause(end));
        Assert.Equal(D(10, 19), Calendar.CalculateEndDate(D(10, 12), 6, [D(10, 17)]));
    }

    [Fact]
    public void Pauses_inside_the_package_extend_it_by_delivery_days()
    {
        var end = Calendar.CalculateEndDate(D(10, 5), 6); // Sat 10
        Assert.Equal(D(10, 14), Calendar.ExtendEndDateForPause(end, 3)); // Mon 12, Tue 13, Wed 14
        Assert.Equal(D(10, 10), Calendar.ExtendEndDateForPause(end, 0));
        Assert.Equal(D(10, 14), Calendar.CalculateEndDate(D(10, 5), 6, [D(10, 6), D(10, 8), D(10, 10)]));

        // Paused days outside the package (or Sundays) change nothing.
        Assert.Equal(D(10, 10), Calendar.CalculateEndDate(D(10, 5), 6, [D(10, 3), D(10, 11), D(10, 20)]));
    }
}
