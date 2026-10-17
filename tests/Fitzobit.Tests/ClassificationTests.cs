using Fitzobit.Application.Classification;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Domain.Geography;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>Geographic group suggestions, manager edits, saving with history, and group boundaries.</summary>
public class ClassificationTests
{
    /// <summary>Three well-separated areas of five customers each (north: N…, west: W…, south-east: S…).</summary>
    private static readonly (string Prefix, double Lat, double Lng)[] Areas = [("N", 11.05, 77.00), ("W", 11.00, 76.90), ("S", 10.95, 77.05)];

    private static async Task<Dictionary<string, int>> SeedAsync(TestDatabase test, int perArea = 5, string status = "ACTIVE")
    {
        var statusId = test.Entry(DictionaryType.CustomerStatus, status);
        await using var db = test.CreateContext();
        var customers = new List<Customer>();
        foreach (var (prefix, lat, lng) in Areas)
            for (var i = 0; i < perArea; i++)
            {
                var c = new Customer
                {
                    CustomerId = $"{prefix}{i + 1:00}", CustomerName = $"Customer {prefix}{i + 1}", Pincode = "64100" + i, StatusId = statusId,
                    Latitude = (decimal)(lat + i * 0.002), Longitude = (decimal)(lng + (i % 2) * 0.002),
                    GeocodeStatus = GeocodeStatus.Located, GeocodeMatchLevel = GeoMatchLevel.Address
                };
                c.SetPhoneNumber($"98{customers.Count:00000000}");
                c.SetAddress($"{i + 1} Street, Area {prefix}");
                customers.Add(c);
            }
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();
        return customers.ToDictionary(c => c.CustomerId, c => c.Id);
    }

    private static async Task<int> AddUnlocatedAsync(TestDatabase test, string customerId, string pincode)
    {
        await using var db = test.CreateContext();
        var c = new Customer { CustomerId = customerId, CustomerName = "No Location", Pincode = pincode, StatusId = test.Entry(DictionaryType.CustomerStatus, "ACTIVE"),
            GeocodeStatus = GeocodeStatus.NotFound };
        c.SetPhoneNumber("9700000001");
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    private static async Task<SuggestionDto> SuggestAsync(TestDatabase test, int groups, bool balance = false, bool pincode = false)
    {
        await using var db = test.CreateContext();
        return await test.Classification(db).SuggestAsync(new SuggestRequest { GroupCount = groups, Balance = balance, UsePincodeForUnlocated = pincode });
    }

    private static Dictionary<string, int?> ByCustomer(SuggestionDto s, Dictionary<string, int> ids) =>
        ids.ToDictionary(x => x.Key, x => s.Assignments.Single(a => a.Id == x.Value).GroupNumber);

    [Fact]
    public async Task Customers_are_grouped_by_location_into_the_requested_number_of_groups()
    {
        using var test = new TestDatabase();
        var ids = await SeedAsync(test);

        var suggestion = await SuggestAsync(test, 3);

        Assert.Equal((3, 3), (suggestion.RequestedGroups, suggestion.GroupsUsed));
        var groupOf = ByCustomer(suggestion, ids);
        foreach (var (prefix, _, _) in Areas)
            Assert.Single(groupOf.Where(x => x.Key.StartsWith(prefix)).Select(x => x.Value).Distinct());
        // No existing groups: numbered north-west first (north = 1, west = 2, south-east = 3), all new.
        Assert.Equal((1, 2, 3), (groupOf["N01"], groupOf["W01"], groupOf["S01"]));
        Assert.Equal([1, 2, 3], suggestion.Changes.NewGroupNumbers);
        Assert.Equal(15, suggestion.Changes.Assigned);
        Assert.All(suggestion.Groups, g => Assert.Equal(("polygon", 5), (g.Boundary.Kind, g.LocatedCount)));
    }

    [Fact]
    public async Task Suggestions_are_deterministic_and_honour_the_group_count()
    {
        using var test = new TestDatabase();
        var ids = await SeedAsync(test);

        var first = await SuggestAsync(test, 5);
        var second = await SuggestAsync(test, 5);
        Assert.Equal(first.Assignments, second.Assignments);
        Assert.Equal(5, first.GroupsUsed);
        Assert.Equal(5, first.Assignments.Select(a => a.GroupNumber).Distinct().Count());

        var two = await SuggestAsync(test, 2);
        Assert.Equal(2, two.Assignments.Select(a => a.GroupNumber).Distinct().Count());
        await Assert.ThrowsAsync<BusinessRuleException>(() => SuggestAsync(test, 0));
    }

    [Fact]
    public async Task Fewer_distinct_locations_than_groups_gives_fewer_groups_with_an_explanation()
    {
        using var test = new TestDatabase();
        await SeedAsync(test, perArea: 1);

        var suggestion = await SuggestAsync(test, 6);

        Assert.Equal(3, suggestion.GroupsUsed);
        Assert.Contains(suggestion.Notes, n => n.Contains("Only 3 distinct location(s)"));
    }

    [Fact]
    public async Task Without_coordinates_nothing_is_grouped_and_customers_stay_unassigned()
    {
        using var test = new TestDatabase();
        var id = await AddUnlocatedAsync(test, "X01", "641002");

        var suggestion = await SuggestAsync(test, 3);

        Assert.Equal(0, suggestion.GroupsUsed);
        Assert.Contains(suggestion.Notes, n => n.Contains("No customer in scope has a location"));
        var a = Assert.Single(suggestion.Assignments);
        Assert.Equal((id, (int?)null, "unchanged"), (a.Id, a.GroupNumber, a.Basis));
        Assert.Equal(0, suggestion.Changes.Assigned + suggestion.Changes.Moved + suggestion.Changes.Removed);
    }

    [Fact]
    public async Task Customers_without_a_location_keep_their_group_or_follow_their_pincode_when_asked()
    {
        using var test = new TestDatabase();
        var ids = await SeedAsync(test);
        var unlocated = await AddUnlocatedAsync(test, "X01", "641000"); // same pincode as N01, W01, S01 …

        var keep = await SuggestAsync(test, 3);
        var kept = keep.Assignments.Single(a => a.Id == unlocated);
        Assert.Equal(((int?)null, "unchanged"), (kept.GroupNumber, kept.Basis));
        Assert.Contains(keep.Notes, n => n.Contains("1 customer(s) have no location"));

        var byPincode = await SuggestAsync(test, 3, pincode: true);
        var p = byPincode.Assignments.Single(a => a.Id == unlocated);
        Assert.Equal("pincode", p.Basis);
        Assert.Equal(ByCustomer(byPincode, ids)["N01"], p.GroupNumber); // tie between the three areas → lowest number
    }

    [Fact]
    public async Task Suggested_groups_reuse_existing_numbers_to_minimise_changes()
    {
        using var test = new TestDatabase();
        var ids = await SeedAsync(test);
        await using (var db = test.CreateContext())
        {
            var created = await test.Groups(db).CreateGroupsAsync(7);
            await test.Assignments(db).AssignManyAsync(ids.Where(x => x.Key.StartsWith("S")).Select(x => x.Value).ToList(), created[6].Id); // south → Group 7
            await test.Assignments(db).AssignManyAsync(ids.Where(x => x.Key.StartsWith("N")).Select(x => x.Value).ToList(), created[4].Id); // north → Group 5
        }

        var suggestion = await SuggestAsync(test, 3);

        var groupOf = ByCustomer(suggestion, ids);
        Assert.Equal((5, 7), (groupOf["N01"], groupOf["S01"]));
        Assert.Equal(1, groupOf["W01"]); // the lowest unused existing group
        Assert.Equal((5, 0, 0), (suggestion.Changes.Assigned, suggestion.Changes.Moved, suggestion.Changes.Removed));
        Assert.Empty(suggestion.Changes.NewGroupNumbers);
    }

    [Fact]
    public async Task Balancing_limits_group_sizes()
    {
        using var test = new TestDatabase();
        var statusId = test.Entry(DictionaryType.CustomerStatus, "ACTIVE");
        await using (var db = test.CreateContext())
        {
            // 10 customers close together in the north, 2 in the south.
            for (var i = 0; i < 12; i++)
            {
                var north = i < 10;
                var c = new Customer
                {
                    CustomerId = $"B{i:00}", CustomerName = "Balance", StatusId = statusId, GeocodeStatus = GeocodeStatus.Located,
                    Latitude = (decimal)(north ? 11.05 + i * 0.001 : 10.90 + i * 0.001), Longitude = (decimal)(north ? 77.0 + i * 0.001 : 77.0)
                };
                c.SetPhoneNumber($"97{i:00000000}");
                db.Customers.Add(c);
            }
            await db.SaveChangesAsync();
        }

        var plain = await SuggestAsync(test, 2);
        Assert.Equal([10, 2], plain.Groups.Select(g => g.CustomerCount).OrderDescending());

        var balanced = await SuggestAsync(test, 2, balance: true);
        Assert.True(balanced.Groups.Max(g => g.CustomerCount) <= 7, string.Join(",", balanced.Groups.Select(g => g.CustomerCount)));
        Assert.Contains(balanced.Notes, n => n.Contains("balanced"));
    }

    [Fact]
    public async Task Only_active_and_paused_customers_are_classified_by_default()
    {
        using var test = new TestDatabase();
        await SeedAsync(test, perArea: 2, status: "INACTIVE");

        await using var db = test.CreateContext();
        var map = await test.Classification(db).GetMapAsync(null);
        Assert.Equal((0, 0), (map.InScope, map.Points.Count));
        var all = await test.Classification(db).GetMapAsync(["ACTIVE", "INACTIVE", "PAUSED"]);
        Assert.Equal((6, 6), (all.InScope, all.Points.Count));
    }

    [Fact]
    public async Task Map_data_lists_points_unlocated_customers_and_group_boundaries()
    {
        using var test = new TestDatabase();
        var ids = await SeedAsync(test);
        await AddUnlocatedAsync(test, "X01", "641002");
        await using var db = test.CreateContext();
        var group = (await test.Groups(db).CreateGroupsAsync(1))[0];
        await test.Assignments(db).AssignManyAsync([ids["N01"], ids["N02"]], group.Id);

        var map = await test.Classification(db).GetMapAsync(null);

        Assert.Equal((16, 15, 1, 14), (map.InScope, map.Located, map.UnlocatedTotal, map.Unassigned));
        Assert.Equal("X01", Assert.Single(map.Unlocated).CustomerId);
        var n01 = map.Points.Single(p => p.CustomerId == "N01");
        Assert.Equal((11.05, 77.0, (int?)1), (n01.Lat, n01.Lng, n01.GroupNumber));
        var g = Assert.Single(map.Groups);
        Assert.Equal((1, 2, "line"), (g.Number, g.CustomerCount, g.Boundary.Kind)); // two points: a line, not an area
    }

    [Fact]
    public async Task A_manager_edited_draft_is_evaluated_then_saved_in_one_transaction_with_history()
    {
        using var test = new TestDatabase();
        var ids = await SeedAsync(test);
        var suggestion = await SuggestAsync(test, 3);

        // The manager moves N01 into the west group and leaves S05 without a group.
        var draft = suggestion.Assignments.Select(a => new DraftAssignment { CustomerId = a.Id, GroupNumber = a.GroupNumber }).ToList();
        draft.Single(d => d.CustomerId == ids["N01"]).GroupNumber = 2;
        draft.Single(d => d.CustomerId == ids["S05"]).GroupNumber = null;

        await using (var db = test.CreateContext())
        {
            var evaluation = await test.Classification(db).EvaluateAsync(new DraftRequest { Assignments = draft });
            Assert.Equal((14, 0, 0), (evaluation.Changes.Assigned, evaluation.Changes.Moved, evaluation.Changes.Removed));
            Assert.Equal([4, 6, 4], evaluation.Groups.Select(g => g.CustomerCount));
            Assert.Contains("6 customers assigned to Group 2 (new)", evaluation.Changes.Messages);

            var saved = await test.Classification(db).ApplyAsync(new DraftRequest { Assignments = draft, Reason = "First grouping" });
            Assert.Equal((14, 0, 0), (saved.Assigned, saved.Moved, saved.Removed));
            Assert.Equal([1, 2, 3], saved.CreatedGroupNumbers);
        }

        await using (var check = test.CreateContext())
        {
            Assert.Equal([1, 2, 3], await check.DeliveryGroups.OrderBy(g => g.GroupNumber).Select(g => g.GroupNumber).ToListAsync());
            Assert.Equal(14, await check.CustomerDeliveryGroupAssignments.CountAsync(a => a.IsActive));
            Assert.Equal(14, await check.CustomerDeliveryGroupAssignmentHistory.CountAsync(h => h.Reason == "First grouping"));
            Assert.False(await check.CustomerDeliveryGroupAssignments.AnyAsync(a => a.CustomerId == ids["S05"]));

            // Moving N01 back is an ordinary reassignment with history; saving the same draft again changes nothing.
            var back = draft.Select(d => new DraftAssignment { CustomerId = d.CustomerId, GroupNumber = d.CustomerId == ids["N01"] ? 1 : d.GroupNumber }).ToList();
            var again = await test.Classification(check).ApplyAsync(new DraftRequest { Assignments = back, Reason = "Fix" });
            Assert.Equal((0, 1, 0, 14), (again.Assigned, again.Moved, again.Removed, again.Unchanged));
            Assert.Empty(again.CreatedGroupNumbers);
            var history = await check.CustomerDeliveryGroupAssignmentHistory.Where(h => h.CustomerId == ids["N01"]).OrderBy(h => h.Id).ToListAsync();
            Assert.Equal(2, history.Count);
            Assert.Equal(history[0].NewGroupId, history[1].PreviousGroupId);
        }
    }

    [Fact]
    public async Task Saving_into_an_inactive_group_is_refused_and_nothing_changes()
    {
        using var test = new TestDatabase();
        var ids = await SeedAsync(test, perArea: 1);
        await using var db = test.CreateContext();
        var groups = await test.Groups(db).CreateGroupsAsync(2);
        await test.Groups(db).SetActiveAsync(groups[1].Id, false);

        var draft = new DraftRequest
        {
            Assignments = [new() { CustomerId = ids["N01"], GroupNumber = 1 }, new() { CustomerId = ids["W01"], GroupNumber = 2 }]
        };
        await Assert.ThrowsAsync<BusinessRuleException>(() => test.Classification(db).ApplyAsync(draft));

        await using var check = test.CreateContext();
        Assert.Equal(0, await check.CustomerDeliveryGroupAssignments.CountAsync());
        await Assert.ThrowsAsync<BusinessRuleException>(() => test.Classification(check).ApplyAsync(
            new DraftRequest { Assignments = [new() { CustomerId = ids["N01"], GroupNumber = 1 }, new() { CustomerId = ids["N01"], GroupNumber = 2 }] }));
    }

    [Fact]
    public async Task Auto_finds_the_natural_number_of_location_clusters()
    {
        using var test = new TestDatabase();
        var ids = await SeedAsync(test);

        await using var db = test.CreateContext();
        var suggestion = await test.Classification(db).SuggestAsync(new SuggestRequest { Auto = true });

        Assert.Equal(3, suggestion.GroupsUsed); // three separate areas → three groups
        Assert.Contains(suggestion.Notes, n => n.StartsWith("Auto: the customer locations form 3 natural group(s)"));
        var groupOf = ByCustomer(suggestion, ids);
        foreach (var (prefix, _, _) in Areas)
            Assert.Single(groupOf.Where(x => x.Key.StartsWith(prefix)).Select(x => x.Value).Distinct());
    }

    [Fact]
    public void Natural_group_count_follows_the_data_not_a_fixed_number()
    {
        static List<ClusterInput> Blobs(int count)
        {
            var random = new Random(7);
            var list = new List<ClusterInput>();
            for (var b = 0; b < count; b++)
                for (var i = 0; i < 40; i++)
                    list.Add(new ClusterInput(list.Count, new GeoPoint(11 + (b % 3) * 0.06 + random.NextDouble() * 0.01, 76.9 + (b / 3) * 0.08 + random.NextDouble() * 0.01)));
            return list;
        }

        Assert.Equal(2, GeoClustering.ChooseGroupCount(Blobs(2)).GroupCount);
        Assert.Equal(5, GeoClustering.ChooseGroupCount(Blobs(5)).GroupCount);
        Assert.Equal(6, GeoClustering.ChooseGroupCount(Blobs(6)).GroupCount);
    }

    [Fact]
    public void Customers_sharing_a_locality_point_do_not_split_an_area_into_one_group_per_locality()
    {
        // Located only to their locality, many customers share one point. Central Coimbatore (RS Puram, Gandhipuram, Saibaba
        // Colony) and the south (Sundarapuram, Kurichi, Eachanari) are two delivery areas, not six groups.
        (double Lat, double Lng, int Customers)[] localities =
        [
            (11.0080, 76.9502, 6), (11.0183, 76.9725, 5), (11.0247, 76.9440, 4),
            (10.9560, 76.9700, 6), (10.9620, 76.9640, 4), (10.9300, 76.9800, 3)
        ];
        var inputs = localities.SelectMany(l => Enumerable.Repeat(new GeoPoint(l.Lat, l.Lng), l.Customers)).Select((p, i) => new ClusterInput(i, p)).ToList();

        Assert.Equal(2, GeoClustering.ChooseGroupCount(inputs).GroupCount);
    }

    // ---------- Group boundaries ----------

    [Fact]
    public void Boundaries_never_draw_an_area_for_fewer_than_three_locations()
    {
        Assert.Equal(BoundaryKind.None, GroupBoundaries.For([]).Kind);
        Assert.Equal(BoundaryKind.Point, GroupBoundaries.For([new(11, 77), new(11, 77)]).Kind);
        var line = GroupBoundaries.For([new(11, 77), new(11.1, 77.1)]);
        Assert.Equal((BoundaryKind.Line, 2), (line.Kind, line.Points.Count));
        var collinear = GroupBoundaries.For([new(11, 77), new(11.05, 77.05), new(11.1, 77.1)]);
        Assert.Equal(BoundaryKind.Line, collinear.Kind);
        Assert.Equal([new GeoPoint(11, 77), new GeoPoint(11.1, 77.1)], collinear.Points);
    }

    [Fact]
    public void Three_or_more_locations_give_their_convex_hull()
    {
        GeoPoint[] square = [new(11.0, 77.0), new(11.0, 77.1), new(11.1, 77.1), new(11.1, 77.0)];
        var hull = GroupBoundaries.For([.. square, new(11.05, 77.05), new(11.02, 77.03), square[0]]);

        Assert.Equal(BoundaryKind.Polygon, hull.Kind);
        Assert.Equal(4, hull.Points.Count);
        Assert.Equal(square.ToHashSet(), hull.Points.ToHashSet()); // interior points are not part of the outline

        var triangle = GroupBoundaries.For([new(11, 77), new(11, 77.1), new(11.1, 77.05)]);
        Assert.Equal((BoundaryKind.Polygon, 3), (triangle.Kind, triangle.Points.Count));
    }

    [Fact]
    public void Hulls_of_thousands_of_points_are_fast()
    {
        var random = new Random(4);
        var points = Enumerable.Range(0, 20_000).Select(_ => new GeoPoint(11 + random.NextDouble() * 0.2, 76.9 + random.NextDouble() * 0.2)).ToList();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var hull = GroupBoundaries.For(points);
        Assert.Equal(BoundaryKind.Polygon, hull.Kind);
        Assert.True(watch.ElapsedMilliseconds < 1000, $"{watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Each_group_lists_the_areas_its_customers_live_in()
    {
        using var test = new TestDatabase();
        var statusId = test.Entry(DictionaryType.CustomerStatus, "ACTIVE");
        // Central Coimbatore (RS Puram, Gandhipuram, Avinashi Road) and the south (Sundarapuram, Kurichi, Eachanari).
        (string Area, double Lat, double Lng)[] places =
        [
            ("RS Puram", 11.0089, 76.9500), ("R.S. Puram", 11.0095, 76.9510), ("RS Puram", 11.0080, 76.9490), ("Gandhipuram", 11.0183, 76.9725),
            ("Gandhipuram", 11.0175, 76.9710), ("Avinashi Road", 11.0250, 76.9900),
            ("Sundarapuram", 10.9560, 76.9700), ("Sundarapuram", 10.9550, 76.9690), ("Kurichi", 10.9620, 76.9640), ("Eachanari", 10.9300, 76.9800)
        ];
        await using (var db = test.CreateContext())
        {
            for (var i = 0; i < places.Length; i++)
            {
                var c = new Customer
                {
                    CustomerId = $"CBE{i + 1:00}", CustomerName = $"Customer {i + 1}", Pincode = "641002", Locality = places[i].Area, StatusId = statusId,
                    Latitude = (decimal)places[i].Lat, Longitude = (decimal)places[i].Lng, GeocodeStatus = GeocodeStatus.Located, GeocodeMatchLevel = GeoMatchLevel.Locality
                };
                c.SetPhoneNumber($"97{i:00000000}");
                c.SetAddress($"{i + 1} Street, {places[i].Area}, Coimbatore");
                db.Customers.Add(c);
            }
            await db.SaveChangesAsync();
        }

        var suggestion = await SuggestAsync(test, 2);

        var areas = suggestion.Groups.Select(g => g.Areas.Select(a => (a.Name, a.Count)).ToList()).ToList();
        Assert.Contains([("RS Puram", 3), ("Gandhipuram", 2), ("Avinashi Road", 1)], areas);
        Assert.Contains([("Sundarapuram", 2), ("Eachanari", 1), ("Kurichi", 1)], areas);

        await using var context = test.CreateContext();
        var map = await test.Classification(context).GetMapAsync(null);
        Assert.Equal("R.S. Puram", map.Points.Single(p => p.CustomerId == "CBE02").Area);
    }
}
