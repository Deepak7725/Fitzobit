namespace Fitzobit.Domain.Geography;

public enum BoundaryKind
{
    /// <summary>No located customers.</summary>
    None,
    /// <summary>One location: drawn as the point only, no area.</summary>
    Point,
    /// <summary>Two locations (or all on one line): a line, not an area.</summary>
    Line,
    /// <summary>Three or more locations: the convex hull.</summary>
    Polygon
}

/// <summary>The visual outline of a group's customers. Points are in drawing order (counter-clockwise for polygons).</summary>
public sealed record GroupBoundary(BoundaryKind Kind, IReadOnlyList<GeoPoint> Points);

public static class GroupBoundaries
{
    /// <summary>Turns smaller than this (in degrees²) are rounding noise: such points count as lying on one line.</summary>
    private const double CollinearTolerance = 1e-12;

    /// <summary>
    /// Convex hull (Andrew's monotone chain, O(n log n)) of the distinct locations. Groups with fewer than three distinct
    /// locations never get an area, so a boundary never suggests more than the data shows.
    /// </summary>
    public static GroupBoundary For(IEnumerable<GeoPoint> points)
    {
        var distinct = points.Distinct().OrderBy(p => p.Longitude).ThenBy(p => p.Latitude).ToList();
        switch (distinct.Count)
        {
            case 0: return new GroupBoundary(BoundaryKind.None, []);
            case 1: return new GroupBoundary(BoundaryKind.Point, distinct);
            case 2: return new GroupBoundary(BoundaryKind.Line, distinct);
        }

        var hull = new List<GeoPoint>(distinct.Count * 2);
        foreach (var pass in new[] { distinct, Enumerable.Reverse(distinct).ToList() })
        {
            var start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= CollinearTolerance) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1); // the last point of each pass starts the other one
        }

        return hull.Count < 3
            ? new GroupBoundary(BoundaryKind.Line, [distinct[0], distinct[^1]]) // all points on one line
            : new GroupBoundary(BoundaryKind.Polygon, hull);
    }

    /// <summary>Positive when o → a → b turns counter-clockwise (x = longitude, y = latitude).</summary>
    private static double Cross(GeoPoint o, GeoPoint a, GeoPoint b) =>
        (a.Longitude - o.Longitude) * (b.Latitude - o.Latitude) - (a.Latitude - o.Latitude) * (b.Longitude - o.Longitude);
}
