using Fitzobit.Application.Dashboard;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>Dashboard figures are live PostgreSQL counts by status code, group, location and frequency.</summary>
public class DashboardTests
{
    [Fact]
    public async Task Summary_counts_statuses_groups_coordinates_and_recent_changes()
    {
        using var test = new TestDatabase();
        var data = await CustomerViewDataTests.SeedAsync(test);
        await using (var db = test.CreateContext())
        {
            // Two located customers, one of them only approximately (pincode area); one paused customer.
            var located = await db.Customers.Where(c => c.CustomerId == "CB001" || c.CustomerId == "CB002").ToListAsync();
            foreach (var c in located) (c.Latitude, c.Longitude, c.GeocodeStatus) = (11.0m, 76.9m, Domain.Customers.GeocodeStatus.Located);
            located[1].GeocodeMatchLevel = Domain.Customers.GeoMatchLevel.Pincode;
            (await db.Customers.SingleAsync(c => c.CustomerId == "CB003")).StatusId = test.Entry(DictionaryType.CustomerStatus, "PAUSED");
            await db.SaveChangesAsync();
        }

        await using var context = test.CreateContext();
        var summary = await new DashboardService(context, test.Clock).GetSummaryAsync();

        Assert.Equal((40, 38, 1, 1), (summary.TotalCustomers, summary.ActiveCustomers, summary.PausedCustomers, summary.InactiveCustomers));
        Assert.Equal((2, 5, 4), (summary.TotalDeliveryGroups, summary.UnassignedCustomers, summary.UnassignedActiveCustomers));
        Assert.Equal((2, 38, 1), (summary.WithCoordinates, summary.WithoutCoordinates, summary.ApproximateCoordinates));
        Assert.Equal(["ACTIVE", "INACTIVE", "PAUSED"], summary.CustomersByStatus.Select(s => s.Code).Order());
        Assert.Equal([("Weekly", 30), ("Monthly", 10)], summary.CustomersByFrequency.Select(f => (f.Label, f.Count)));
        Assert.Equal([("Group 1", 20), ("Group 2", 15), ("No group", 5)], summary.CustomersByGroup.Select(g => (g.Label, g.Count)));

        Assert.Equal(DashboardService.RecentChangeCount, summary.RecentChanges.Count);
        Assert.All(summary.RecentChanges, r => Assert.Equal(AuditActions.CustomerAssigned, r.Action));
        Assert.Equal(data.Ids[summary.RecentChanges[0].CustomerId!], summary.RecentChanges[0].CustomerKey);
    }
}
