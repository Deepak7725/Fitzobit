using Fitzobit.Application.Common;
using Fitzobit.Application.DeliveryGroups;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.DeliveryGroups;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

public class DeliveryGroupAssignmentTests
{
    private sealed record Seeded(int CustomerId, int NorthId, int SouthId, int InactiveId);

    private static async Task<Seeded> SeedAsync(TestDatabase test)
    {
        await using var db = test.CreateContext();
        var customer = new Customer { CustomerId = "CUST005", CustomerName = "Karthik N", Pincode = "641002", StatusId = test.Entry(Domain.Dictionaries.DictionaryType.CustomerStatus, "ACTIVE") };
        customer.SetPhoneNumber("9090909090");
        customer.SetAddress("RS Puram, Coimbatore");
        var north = new DeliveryGroup { GroupNumber = 1 };
        var south = new DeliveryGroup { GroupNumber = 2 };
        var inactive = new DeliveryGroup { GroupNumber = 3, Status = RecordStatus.Inactive };
        db.AddRange(customer, north, south, inactive);
        await db.SaveChangesAsync();
        return new Seeded(customer.Id, north.Id, south.Id, inactive.Id);
    }

    private static DeliveryGroupAssignmentService Service(TestDatabase test, Infrastructure.Persistence.FitzobitDbContext db) =>
        new(db, test.User, test.Clock, test.AuditFor(db));

    [Fact]
    public async Task Reassigning_keeps_exactly_one_active_group_and_records_history()
    {
        using var test = new TestDatabase();
        var s = await SeedAsync(test);

        await using (var db = test.CreateContext())
        {
            var first = await Service(test, db).AssignAsync(s.CustomerId, s.NorthId, "Initial grouping");
            Assert.True(first.Changed);
            Assert.Null(first.PreviousGroupId);
        }

        await using (var db = test.CreateContext())
        {
            var second = await Service(test, db).AssignAsync(s.CustomerId, s.SouthId, "Route change");
            Assert.Equal(s.NorthId, second.PreviousGroupId);
        }

        await using (var db = test.CreateContext())
        {
            var assignments = await db.CustomerDeliveryGroupAssignments.Where(a => a.CustomerId == s.CustomerId).ToListAsync();
            Assert.Equal(2, assignments.Count);
            var active = Assert.Single(assignments, a => a.IsActive);
            Assert.Equal(s.SouthId, active.DeliveryGroupId);

            var history = await db.CustomerDeliveryGroupAssignmentHistory.OrderBy(h => h.Id).ToListAsync();
            Assert.Equal(2, history.Count);
            Assert.Equal((null, s.NorthId), (history[0].PreviousGroupId, history[0].NewGroupId));
            Assert.Equal((s.NorthId, s.SouthId), (history[1].PreviousGroupId, history[1].NewGroupId));
            Assert.Equal("Route change", history[1].Reason);

            var actions = await db.AuditLogs.Select(a => a.Action).ToListAsync();
            Assert.Equal([AuditActions.CustomerAssigned, AuditActions.CustomerReassigned], actions);
        }
    }

    [Fact]
    public async Task Assigning_to_the_current_group_changes_nothing()
    {
        using var test = new TestDatabase();
        var s = await SeedAsync(test);
        await using (var db = test.CreateContext()) await Service(test, db).AssignAsync(s.CustomerId, s.NorthId);

        await using (var db = test.CreateContext())
        {
            var again = await Service(test, db).AssignAsync(s.CustomerId, s.NorthId);
            Assert.False(again.Changed);
            Assert.Equal(1, await db.CustomerDeliveryGroupAssignments.CountAsync());
            Assert.Equal(1, await db.CustomerDeliveryGroupAssignmentHistory.CountAsync());
        }
    }

    [Fact]
    public async Task Inactive_groups_cannot_receive_customers()
    {
        using var test = new TestDatabase();
        var s = await SeedAsync(test);
        await using var db = test.CreateContext();
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service(test, db).AssignAsync(s.CustomerId, s.InactiveId));
    }

    [Fact]
    public async Task Database_rejects_a_second_active_assignment_even_without_the_service()
    {
        using var test = new TestDatabase();
        var s = await SeedAsync(test);
        await using var db = test.CreateContext();
        db.CustomerDeliveryGroupAssignments.AddRange(
            new CustomerDeliveryGroupAssignment { CustomerId = s.CustomerId, DeliveryGroupId = s.NorthId, AssignedAt = test.Clock.UtcNow, IsActive = true },
            new CustomerDeliveryGroupAssignment { CustomerId = s.CustomerId, DeliveryGroupId = s.SouthId, AssignedAt = test.Clock.UtcNow, IsActive = true });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Inactive_history_rows_are_allowed_alongside_one_active_row()
    {
        using var test = new TestDatabase();
        var s = await SeedAsync(test);
        await using var db = test.CreateContext();
        db.CustomerDeliveryGroupAssignments.AddRange(
            new CustomerDeliveryGroupAssignment { CustomerId = s.CustomerId, DeliveryGroupId = s.NorthId, AssignedAt = test.Clock.UtcNow, IsActive = false },
            new CustomerDeliveryGroupAssignment { CustomerId = s.CustomerId, DeliveryGroupId = s.SouthId, AssignedAt = test.Clock.UtcNow, IsActive = false },
            new CustomerDeliveryGroupAssignment { CustomerId = s.CustomerId, DeliveryGroupId = s.SouthId, AssignedAt = test.Clock.UtcNow, IsActive = true });
        await db.SaveChangesAsync();
        Assert.Equal(3, await db.CustomerDeliveryGroupAssignments.CountAsync());
    }
}
