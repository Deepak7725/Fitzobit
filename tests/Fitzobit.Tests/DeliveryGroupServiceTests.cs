using Fitzobit.Application.Common;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

public class DeliveryGroupServiceTests
{
    private static async Task<int[]> AddCustomersAsync(TestDatabase test, int count)
    {
        await using var db = test.CreateContext();
        var customers = Enumerable.Range(1, count).Select(i =>
        {
            var c = new Customer { CustomerId = $"G{i:000}", CustomerName = $"Group Person {i}", Pincode = "641002" };
            c.SetPhoneNumber($"98000{i:00000}");
            return c;
        }).ToList();
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();
        return customers.Select(c => c.Id).ToArray();
    }

    [Fact]
    public async Task Groups_can_be_created_renamed_deactivated_and_reactivated()
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);
        await using var db = test.CreateContext();
        var groups = harness.Groups(db);

        var north = await groups.CreateAsync("Coimbatore North", null);
        await Assert.ThrowsAsync<BusinessRuleException>(() => groups.CreateAsync("COIMBATORE north", null));
        await groups.UpdateAsync(north.Id, "Coimbatore North-West", "RS Puram");
        await groups.SetActiveAsync(north.Id, false);
        Assert.Equal(RecordStatus.Inactive, (await db.DeliveryGroups.SingleAsync()).Status);
        await groups.SetActiveAsync(north.Id, true);

        var group = await db.DeliveryGroups.SingleAsync();
        Assert.Equal(("Coimbatore North-West", "RS Puram", RecordStatus.Active), (group.Name, group.Description, group.Status));
    }

    [Fact]
    public async Task Bulk_move_previews_then_moves_with_history_and_blocks_deactivating_a_used_group()
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);
        var ids = await AddCustomersAsync(test, 27);

        await using var db = test.CreateContext();
        var groups = harness.Groups(db);
        var north = await groups.CreateAsync("Coimbatore North", null);
        var south = await groups.CreateAsync("Coimbatore South", null);
        Assert.Equal(27, (await groups.MoveAsync(ids, north.Id, "Initial")).Changed);

        var preview = await groups.PreviewMoveAsync(ids, south.Id);
        Assert.Equal("27 customers will be moved from Coimbatore North to Coimbatore South.", Assert.Single(preview.Messages));

        await Assert.ThrowsAsync<BusinessRuleException>(() => groups.SetActiveAsync(north.Id, false));
        Assert.Equal(27, (await groups.MoveAsync(ids, south.Id, "Route split")).Changed);
        await groups.SetActiveAsync(north.Id, false);

        Assert.Equal(27, await db.CustomerDeliveryGroupAssignments.CountAsync(a => a.IsActive && a.DeliveryGroupId == south.Id));
        Assert.Equal(27, await db.CustomerDeliveryGroupAssignmentHistory.CountAsync(h => h.PreviousGroupId == north.Id && h.Reason == "Route split"));
        Assert.Equal(27, (await groups.GetCustomersAsync(south.Id.ToString(), null, 1, 50)).Total);
        Assert.Equal(0, (await groups.GetCustomersAsync("unassigned", null, 1, 50)).Total);
    }

    [Fact]
    public async Task Only_never_used_groups_can_be_deleted()
    {
        using var test = new TestDatabase();
        var harness = await ImportHarness.CreateAsync(test);
        var ids = await AddCustomersAsync(test, 1);
        await using var db = test.CreateContext();
        var groups = harness.Groups(db);

        var unused = await groups.CreateAsync("Spare", null);
        var used = await groups.CreateAsync("Busy", null);
        await groups.MoveAsync(ids, used.Id, null);

        await groups.DeleteAsync(unused.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() => groups.DeleteAsync(used.Id));
        Assert.Equal(["Busy"], await db.DeliveryGroups.Select(g => g.Name).ToListAsync());
    }
}
