namespace Fitzobit.Domain.Geography;

public readonly record struct GeoPoint(double Latitude, double Longitude);

/// <summary>A point to cluster, identified by <see cref="Key"/> (e.g. a customer's database id).</summary>
public readonly record struct ClusterInput(int Key, GeoPoint Point);

/// <param name="ClusterOf">Cluster index (0-based) for every input key.</param>
/// <param name="Centers">Cluster centres, ordered north-west first (latitude descending, then longitude).</param>
/// <param name="InertiaKm2">Sum of squared distances to the centres, in km² (lower = tighter groups).</param>
public sealed record ClusteringResult(IReadOnlyDictionary<int, int> ClusterOf, IReadOnlyList<GeoPoint> Centers, double InertiaKm2)
{
    public int ClusterCount => Centers.Count;
    public static readonly ClusteringResult Empty = new(new Dictionary<int, int>(), [], 0);
}

/// <summary>
/// Deterministic geographic K-Means for delivery-group suggestions. Coordinates are projected to kilometres around the
/// data's mean latitude (accurate at city scale). k-means++ starts with fixed seeds and a fixed number of restarts; the
/// tightest result wins, so the same customers always give the same groups. This is a practical classification aid,
/// not a route optimiser.
/// </summary>
public static class GeoClustering
{
    public const int Restarts = 8;
    public const int MaxIterations = 100;

    /// <param name="groupCount">Requested number of clusters; reduced to the number of distinct locations when there are fewer.</param>
    public static ClusteringResult KMeans(IReadOnlyCollection<ClusterInput> inputs, int groupCount)
    {
        if (groupCount < 1) throw new ArgumentOutOfRangeException(nameof(groupCount), groupCount, "At least one group is needed.");
        if (inputs.Count == 0) return ClusteringResult.Empty;

        var ordered = inputs.OrderBy(i => i.Key).ToArray();
        var projection = Projection.For(ordered.Select(i => i.Point));
        var xy = ordered.Select(i => projection.ToKm(i.Point)).ToArray();
        var k = Math.Min(groupCount, xy.Distinct().Count());

        (int[] Labels, (double X, double Y)[] Centers, double Inertia)? best = null;
        for (var seed = 1; seed <= Restarts; seed++)
        {
            var run = Lloyd(xy, InitialCenters(xy, k, new Random(seed)));
            if (best is null || run.Inertia < best.Value.Inertia - 1e-9) best = run;
        }
        return Result(ordered, best!.Value.Labels, best.Value.Centers, projection);
    }

    /// <summary>Silhouette scores this close to the best count as equally good; the smallest such group count wins.</summary>
    public const double SilhouetteTolerance = 0.05;

    /// <summary>
    /// Finds the natural number of location clusters (2…<paramref name="maxGroups"/>) from the K-Means results' average
    /// silhouette (how much closer places are to their own group than to the next one). Separation is judged on distinct
    /// locations, and the fewest groups scoring within <see cref="SilhouetteTolerance"/> of the best win: customers located
    /// only to their locality share one point, which would otherwise make "one group per locality" look perfect instead of
    /// grouping neighbouring localities into one delivery area. Deterministic; large data sets are evaluated on an evenly
    /// spread sample of at most <paramref name="sampleSize"/> customers.
    /// </summary>
    /// <returns>The chosen count and its silhouette (−1…1; above about 0.5 means clearly separated groups).</returns>
    public static (int GroupCount, double Silhouette) ChooseGroupCount(IReadOnlyCollection<ClusterInput> inputs, int maxGroups = 15, int sampleSize = 1500)
    {
        var ordered = inputs.OrderBy(i => i.Key).ToArray();
        var distinct = ordered.Select(i => i.Point).Distinct().Count();
        if (distinct < 3) return (Math.Max(1, distinct), 0);
        var step = Math.Max(1, ordered.Length / sampleSize);
        var sample = ordered.Where((_, i) => i % step == 0).ToArray();
        var projection = Projection.For(sample.Select(i => i.Point));
        var places = sample.GroupBy(i => i.Point).Select(g => g.First()).ToArray(); // one per distinct location
        var xy = places.Select(i => projection.ToKm(i.Point)).ToArray();

        var scores = new List<(int GroupCount, double Silhouette)>();
        for (var k = 2; k <= Math.Min(maxGroups, places.Length - 1); k++)
        {
            var labels = KMeans(sample, k);
            scores.Add((k, Silhouette(xy, places.Select(i => labels.ClusterOf[i.Key]).ToArray(), labels.ClusterCount)));
        }
        if (scores.Count == 0) return (1, 0);
        var best = scores.Max(s => s.Silhouette);
        return scores.First(s => s.Silhouette >= best - SilhouetteTolerance);
    }

    private static double Silhouette((double X, double Y)[] xy, int[] labels, int k)
    {
        var total = 0.0;
        var sums = new double[k];
        var counts = new int[k];
        foreach (var l in labels) counts[l]++;
        for (var i = 0; i < xy.Length; i++)
        {
            Array.Clear(sums);
            for (var j = 0; j < xy.Length; j++)
                if (i != j) sums[labels[j]] += Math.Sqrt(Distance2(xy[i], xy[j]));
            var own = labels[i];
            if (counts[own] <= 1) continue; // a single-member group scores 0
            var a = sums[own] / (counts[own] - 1);
            var b = double.MaxValue;
            for (var c = 0; c < k; c++)
                if (c != own && counts[c] > 0) b = Math.Min(b, sums[c] / counts[c]);
            total += b == double.MaxValue || Math.Max(a, b) == 0 ? 0 : (b - a) / Math.Max(a, b);
        }
        return total / xy.Length;
    }

    /// <summary>
    /// Evens out group sizes: no group keeps more than <c>ceil(n / k × (1 + tolerance))</c> customers. Customers are moved
    /// out of oversized groups to the nearest group with room, cheapest extra distance first.
    /// </summary>
    public static ClusteringResult Balance(IReadOnlyCollection<ClusterInput> inputs, ClusteringResult clustering, double tolerance = 0.15)
    {
        var k = clustering.ClusterCount;
        if (k < 2 || inputs.Count == 0) return clustering;

        var ordered = inputs.OrderBy(i => i.Key).ToArray();
        var projection = Projection.For(ordered.Select(i => i.Point));
        var xy = ordered.Select(i => projection.ToKm(i.Point)).ToArray();
        var centers = clustering.Centers.Select(projection.ToKm).ToArray();
        var labels = ordered.Select(i => clustering.ClusterOf[i.Key]).ToArray();
        var capacity = (int)Math.Ceiling(ordered.Length / (double)k * (1 + tolerance));
        var sizes = new int[k];
        foreach (var l in labels) sizes[l]++;

        foreach (var cluster in Enumerable.Range(0, k).OrderByDescending(c => sizes[c]).ThenBy(c => c).ToList())
        {
            if (sizes[cluster] <= capacity) continue;
            var moves = Enumerable.Range(0, xy.Length).Where(p => labels[p] == cluster)
                .Select(p => (Point: p, Cost: Enumerable.Range(0, k).Where(c => c != cluster)
                    .Select(c => (Target: c, Extra: Distance2(xy[p], centers[c]) - Distance2(xy[p], centers[cluster])))
                    .OrderBy(t => t.Extra).ThenBy(t => t.Target).ToArray()))
                .OrderBy(m => m.Cost[0].Extra).ThenBy(m => m.Point).ToList();
            foreach (var move in moves)
            {
                if (sizes[cluster] <= capacity) break;
                foreach (var (target, _) in move.Cost)
                {
                    if (sizes[target] >= capacity) continue;
                    labels[move.Point] = target;
                    sizes[cluster]--;
                    sizes[target]++;
                    break;
                }
            }
        }
        return Result(ordered, labels, Means(xy, labels, centers), projection);
    }

    private static (double X, double Y)[] InitialCenters((double X, double Y)[] xy, int k, Random rng)
    {
        var centers = new List<(double X, double Y)> { xy[rng.Next(xy.Length)] };
        var nearest = xy.Select(p => Distance2(p, centers[0])).ToArray();
        while (centers.Count < k)
        {
            var total = nearest.Sum();
            var target = rng.NextDouble() * total;
            var chosen = Array.FindIndex(nearest, d => d > 0);
            for (var i = 0; i < xy.Length; i++)
            {
                if (nearest[i] <= 0) continue;
                target -= nearest[i];
                chosen = i;
                if (target <= 0) break;
            }
            centers.Add(xy[chosen]);
            for (var i = 0; i < xy.Length; i++) nearest[i] = Math.Min(nearest[i], Distance2(xy[i], xy[chosen]));
        }
        return centers.ToArray();
    }

    private static (int[] Labels, (double X, double Y)[] Centers, double Inertia) Lloyd((double X, double Y)[] xy, (double X, double Y)[] centers)
    {
        var labels = new int[xy.Length];
        Array.Fill(labels, -1);
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var changed = false;
            for (var i = 0; i < xy.Length; i++)
            {
                var nearest = Nearest(xy[i], centers);
                if (nearest == labels[i]) continue;
                labels[i] = nearest;
                changed = true;
            }
            if (!changed) break;
            centers = Means(xy, labels, centers);
        }
        var inertia = xy.Select((p, i) => Distance2(p, centers[labels[i]])).Sum();
        return (labels, centers, inertia);
    }

    private static int Nearest((double X, double Y) p, (double X, double Y)[] centers)
    {
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var c = 0; c < centers.Length; c++)
        {
            var d = Distance2(p, centers[c]);
            if (d < bestDistance - 1e-12) { best = c; bestDistance = d; }
        }
        return best;
    }

    /// <summary>Mean of each cluster; an empty cluster keeps its previous centre.</summary>
    private static (double X, double Y)[] Means((double X, double Y)[] xy, int[] labels, (double X, double Y)[] previous)
    {
        var sums = new (double X, double Y, int N)[previous.Length];
        for (var i = 0; i < xy.Length; i++)
        {
            var s = sums[labels[i]];
            sums[labels[i]] = (s.X + xy[i].X, s.Y + xy[i].Y, s.N + 1);
        }
        return sums.Select((s, c) => s.N == 0 ? previous[c] : (s.X / s.N, s.Y / s.N)).ToArray();
    }

    private static double Distance2((double X, double Y) a, (double X, double Y) b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    /// <summary>Renumbers clusters north-west first so the result does not depend on internal cluster order.</summary>
    private static ClusteringResult Result(ClusterInput[] ordered, int[] labels, (double X, double Y)[] centers, Projection projection)
    {
        var used = labels.Distinct().ToList();
        var geo = used.ToDictionary(c => c, c => projection.ToGeo(centers[c]));
        var order = used.OrderByDescending(c => Math.Round(geo[c].Latitude, 6)).ThenBy(c => geo[c].Longitude).ToList();
        var index = order.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        var clusterOf = ordered.Select((input, i) => (input.Key, Cluster: index[labels[i]])).ToDictionary(x => x.Key, x => x.Cluster);
        var inertia = ordered.Select((_, i) => Distance2(projection.ToKm(ordered[i].Point), centers[labels[i]])).Sum();
        return new ClusteringResult(clusterOf, order.Select(c => geo[c]).ToList(), inertia);
    }

    /// <summary>Equirectangular projection to kilometres around a reference point (accurate within a city).</summary>
    private sealed record Projection(double Lat0, double Lng0, double KmPerDegreeLng)
    {
        private const double KmPerDegreeLat = 110.574;

        public static Projection For(IEnumerable<GeoPoint> points)
        {
            var list = points.ToList();
            var lat0 = list.Average(p => p.Latitude);
            var lng0 = list.Average(p => p.Longitude);
            return new Projection(lat0, lng0, 111.320 * Math.Cos(lat0 * Math.PI / 180));
        }

        public (double X, double Y) ToKm(GeoPoint p) => ((p.Longitude - Lng0) * KmPerDegreeLng, (p.Latitude - Lat0) * KmPerDegreeLat);

        public GeoPoint ToGeo((double X, double Y) km) => new(Lat0 + km.Y / KmPerDegreeLat, Lng0 + km.X / KmPerDegreeLng);
    }
}
