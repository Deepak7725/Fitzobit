using System.Text.Json;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>Numbered delivery groups: creation, numbering under concurrency, notes, (de)activation, deletion, moves and history.</summary>
public class DeliveryGroupServiceTests
{
    internal static async Task<int[]> AddCustomersAsync(TestDatabase test, int count, string prefix = "G")
    {
        var active = test.Entry(DictionaryType.CustomerStatus, "ACTIVE");
        await using var db = test.CreateContext();
        var customers = Enumerable.Range(1, count).Select(i =>
        {
            var c = new Customer { CustomerId = $"{prefix}{i:000}", CustomerName = $"Group Person {i}", Pincode = "641002", StatusId = active };
            c.SetPhoneNumber($"98000{i:00000}");
            return c;
        }).ToList();
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();
        return customers.Select(c => c.Id).ToArray();
    }

    [Fact]
    public async Task Groups_are_created_with_consecutive_numbers()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var groups = test.Groups(db);

        var first = await groups.CreateGroupsAsync(3, "Morning route");
        var more = await groups.CreateGroupsAsync(2);

        Assert.Equal([1, 2, 3], first.Select(g => g.GroupNumber));
        Assert.Equal([4, 5], more.Select(g => g.GroupNumber));
        Assert.Equal("Morning route", (await db.DeliveryGroups.SingleAsync(g => g.GroupNumber == 2)).Description);
        await Assert.ThrowsAsync<BusinessRuleException>(() => groups.CreateGroupsAsync(0));
        await Assert.ThrowsAsync<BusinessRuleException>(() => groups.CreateGroupsAsync(51));
    }

    [Fact]
    public async Task Concurrent_creation_never_duplicates_a_group_number()
    {
        using var test = new TestDatabase();
        var created = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = test.CreateContext();
            return await test.Groups(db).CreateGroupsAsync(2);
        }));

        var numbers = created.SelectMany(c => c.Select(g => g.GroupNumber)).Order().ToList();
        Assert.Equal(Enumerable.Range(1, 12), numbers);
    }

    [Fact]
    public async Task The_database_rejects_duplicate_or_zero_group_numbers()
    {
        using var test = new TestDatabase();
        await using (var db = test.CreateContext())
            await test.Groups(db).CreateGroupsAsync(1);

        await using (var db = test.CreateContext())
        {
            db.DeliveryGroups.Add(new Domain.DeliveryGroups.DeliveryGroup { GroupNumber = 1 });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = test.CreateContext())
        {
            db.DeliveryGroups.Add(new Domain.DeliveryGroups.DeliveryGroup { GroupNumber = 0 });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Notes_can_be_edited_and_groups_deactivated_and_reactivated()
    {
        using var test = new TestDatabase();
        await using var db = test.CreateContext();
        var groups = test.Groups(db);
        var group = (await groups.CreateGroupsAsync(1))[0];

        await groups.UpdateNotesAsync(group.Id, "  RS Puram, 7–9 am ");
        await groups.SetActiveAsync(group.Id, false);
        Assert.Equal(RecordStatus.Inactive, (await db.DeliveryGroups.SingleAsync()).Status);
        await groups.SetActiveAsync(group.Id, true);

        var saved = await db.DeliveryGroups.SingleAsync();
        Assert.Equal((1, "RS Puram, 7–9 am", RecordStatus.Active), (saved.GroupNumber, saved.Description, saved.Status));
        Assert.Contains(await db.AuditLogs.Select(a => a.Summary).ToListAsync(), s => s == "Group 1 notes updated");
    }

    [Fact]
    public async Task Bulk_move_previews_then_moves_with_history_and_blocks_deactivating_a_used_group()
    {
        using var test = new TestDatabase();
        var ids = await AddCustomersAsync(test, 27);

        await using var db = test.CreateContext();
        var groups = test.Groups(db);
        var created = await groups.CreateGroupsAsync(2);
        var (one, two) = (created[0], created[1]);
        Assert.Equal(27, (await groups.MoveAsync(ids, one.Id, "Initial")).Changed);

        var preview = await groups.PreviewMoveAsync(ids, two.Id);
        Assert.Equal("27 customers will be moved from Group 1 to Group 2.", Assert.Single(preview.Messages));

        await Assert.ThrowsAsync<BusinessRuleException>(() => groups.SetActiveAsync(one.Id, false));
        Assert.Equal(27, (await groups.MoveAsync(ids, two.Id, "Route split")).Changed);
        await groups.SetActiveAsync(one.Id, false);

        Assert.Equal(27, await db.CustomerDeliveryGroupAssignments.CountAsync(a => a.IsActive && a.DeliveryGroupId == two.Id));
        Assert.Equal(27, await db.CustomerDeliveryGroupAssignmentHistory.CountAsync(h => h.PreviousGroupId == one.Id && h.NewGroupId == two.Id
                                                                                          && h.Reason == "Route split" && h.ChangedBy == "test-user"));
        Assert.Equal(27, (await groups.GetCustomersAsync(two.Id.ToString(), null, 1, 50)).Total);
        Assert.Equal(0, (await groups.GetCustomersAsync("unassigned", null, 1, 50)).Total);
        await Assert.ThrowsAsync<BusinessRuleException>(() => groups.MoveAsync(ids, one.Id, null)); // inactive group
    }

    [Fact]
    public async Task One_save_can_assign_move_and_remove_with_history_for_each_change()
    {
        using var test = new TestDatabase();
        var ids = await AddCustomersAsync(test, 4);
        await using var db = test.CreateContext();
        var created = await test.Groups(db).CreateGroupsAsync(2);
        await test.Assignments(db).AssignManyAsync([ids[0], ids[1]], created[0].Id, "Initial");

        var result = await test.Assignments(db).ApplyAsync(new Dictionary<int, int?>
        {
            [ids[0]] = created[1].Id, // move 1 → 2
            [ids[1]] = null,          // remove from 1
            [ids[2]] = created[0].Id, // assign to 1
            [ids[3]] = null           // already without a group
        }, "Map reclassification");

        Assert.Equal((1, 1, 1, 1), (result.Assigned, result.Moved, result.Removed, result.Unchanged));
        var active = await db.CustomerDeliveryGroupAssignments.Where(a => a.IsActive).ToDictionaryAsync(a => a.CustomerId, a => a.DeliveryGroupId);
        Assert.Equal(created[1].Id, active[ids[0]]);
        Assert.False(active.ContainsKey(ids[1]));
        Assert.Equal(created[0].Id, active[ids[2]]);

        var history = await db.CustomerDeliveryGroupAssignmentHistory.Where(h => h.Reason == "Map reclassification").ToListAsync();
        Assert.Equal(3, history.Count);
        Assert.Contains(history, h => h.CustomerId == ids[0] && h.PreviousGroupId == created[0].Id && h.NewGroupId == created[1].Id);
        Assert.Contains(history, h => h.CustomerId == ids[1] && h.PreviousGroupId == created[0].Id && h.NewGroupId == null);
        var audit = await db.AuditLogs.SingleAsync(a => a.Action == AuditActions.CustomerReassigned);
        Assert.Equal("G001 moved from Group 1 to Group 2", audit.Summary);
        Assert.Equal(2, JsonDocument.Parse(audit.Details!).RootElement.GetProperty("NewGroupNumber").GetInt32());
    }

    [Fact]
    public async Task Only_never_used_groups_can_be_deleted()
    {
        using var test = new TestDatabase();
        var ids = await AddCustomersAsync(test, 1);
        await using var db = test.CreateContext();
        var groups = test.Groups(db);
        var created = await groups.CreateGroupsAsync(2);

        await groups.MoveAsync(ids, created[1].Id, null);
        await groups.DeleteAsync(created[0].Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() => groups.DeleteAsync(created[1].Id));
        Assert.Equal([2], await db.DeliveryGroups.Select(g => g.GroupNumber).ToListAsync());
    }

    [Fact]
    public async Task Overview_shows_counts_pincodes_meal_types_frequencies_and_statuses_per_group()
    {
        using var test = new TestDatabase();
        await using (var db = test.CreateContext())
        {
            var paused = test.NewCustomer("Paused Person", "9811111112", "641018");
            paused.StatusId = test.Entry(DictionaryType.CustomerStatus, "PAUSED");
            var a = await test.Commands(db).CreateAsync(test.NewCustomer("Weekly Person", "9811111111"));
            var b = await test.Commands(db).CreateAsync(paused);
            var group = (await test.Groups(db).CreateGroupsAsync(2))[0];
            await test.Assignments(db).AssignManyAsync([a.Id, b.Id], group.Id);
        }

        await using var check = test.CreateContext();
        var overview = await new Application.DeliveryGroups.DeliveryGroupQueryService(check).GetOverviewAsync();
        var g1 = overview.Groups[0];
        Assert.Equal((1, "Group 1", 2), (g1.GroupNumber, g1.Name, g1.ActiveCustomerCount));
        Assert.Equal(["641002", "641018"], g1.Pincodes);
        Assert.Equal([("Medium", 2)], g1.MealTypes.Select(m => (m.Label, m.Count)));
        Assert.Equal([("Weekly", 2)], g1.Frequencies.Select(m => (m.Label, m.Count)));
        Assert.Equal([("Active", 1), ("Paused", 1)], g1.StatusCounts.Select(m => (m.Label, m.Count)));
        Assert.Equal(0, g1.LocatedCount);
        Assert.Equal(0, overview.Groups[1].ActiveCustomerCount);
    }
}
