using System.Diagnostics;
using System.Numerics;
using BodyTouchGame.Geometry;
using Xunit.Abstractions;

namespace BodyTouchGame.Geometry.Tests;

/// <summary>
/// Reference timings on a synthetic graph of the size planned for the body (~5k vertices).
/// The spec limit is 50 ms on a mid-range Android phone; this measures the development machine.
/// </summary>
public class PerformanceTests(ITestOutputHelper output)
{
    private const int Queries = 500;

    // Torus R = 40 cm, r = 15 cm: 100 x 50 = 5000 vertices, surface ~2.4 m^2 (adult body ~1.8 m^2).
    private static readonly TestMesh Mesh = TestMeshes.Torus(40, 15, 100, 50);
    private static readonly BodyGraph Body = BodyGraphBuilder.Build(Mesh.Positions, Mesh.Indices);

    [Fact]
    public void DistanceQueriesOnFiveThousandVertexGraphAreFast()
    {
        var solver = new GeodesicSolver(Body);
        var random = new Random(42);
        var points = Enumerable.Range(0, Queries * 2)
            .Select(_ => new SurfacePoint(random.Next(Body.TriangleCount), 0.25f, 0.25f))
            .ToArray();
        var zone = ZoneRegion.FromTriangles(Body, Enumerable.Range(0, Mesh.TriangleCount).Where(t => Mesh.Centroid(t).X > 45));

        for (var i = 0; i < 20; i++) solver.DistanceToPoint(points[i], points[i + 1]); // JIT warm-up

        var pointTimes = Time(i => solver.DistanceToPoint(points[2 * i], points[2 * i + 1]));
        var zoneTimes = Time(i => solver.DistanceToZone(points[2 * i], zone));
        var pathTimes = Time(i =>
        {
            solver.DistanceToPoint(points[2 * i], points[2 * i + 1]);
            solver.GetLastPath();
        });

        // Worst case for the search: the farthest point on the torus explores the whole graph.
        var far = Mesh.Locate(new Vector3(-55, 0, 0));
        var near = Mesh.Locate(new Vector3(55, 0, 0));
        var worst = Stopwatch.StartNew();
        var farResult = solver.DistanceToPoint(near, far);
        worst.Stop();

        output.WriteLine($"Graph: {Body.Graph.VertexCount} vertices, {Body.Graph.EdgeCount} directed edges, {Body.TriangleCount} triangles");
        output.WriteLine($"Point -> point : {Describe(pointTimes)}");
        output.WriteLine($"Point -> zone  : {Describe(zoneTimes)}");
        output.WriteLine($"With path      : {Describe(pathTimes)}");
        output.WriteLine($"Worst case     : {worst.Elapsed.TotalMilliseconds:F3} ms ({farResult.DistanceCm:F1} cm)");

        Assert.True(pointTimes.Max() < 50, "A single query exceeded 50 ms on the development machine.");
        Assert.True(pathTimes.Max() < 50);
    }

    [Fact]
    public void DistanceQueriesDoNotAllocate()
    {
        var solver = new GeodesicSolver(Body);
        var a = new SurfacePoint(10, 0.3f, 0.3f);
        var b = new SurfacePoint(Body.TriangleCount / 2, 0.3f, 0.3f);
        var zone = ZoneRegion.FromTriangles(Body, Enumerable.Range(2000, 50));
        for (var i = 0; i < 50; i++)
        {
            solver.DistanceToPoint(a, b);
            solver.DistanceToZone(b, zone);
        }

        // The counter also includes one-off allocations of the runtime itself (e.g. while methods move between JIT
        // tiers – seen on GitHub runners: 6336 B once, with a newer .NET than the development machine). The claim is
        // "queries do not allocate", so a round of queries must allocate nothing: an allocation in the queries would
        // show up in every round, a one-off runtime allocation only in one.
        var rounds = new List<long>();
        for (var round = 0; round < 5 && (rounds.Count == 0 || rounds[^1] != 0); round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 20; i++)
            {
                solver.DistanceToPoint(a, b);
                solver.DistanceToZone(b, zone);
            }

            rounds.Add(GC.GetAllocatedBytesForCurrentThread() - before);
        }

        output.WriteLine($"Allocated per round of 40 queries: {string.Join(", ", rounds)} B");
        Assert.Equal(0, rounds.Min());
    }

    [Fact]
    public void LoadingFromFileIsFast()
    {
        using var buffer = new MemoryStream();
        BodyGraphSerializer.Write(Body, buffer);
        var bytes = buffer.ToArray();

        var stopwatch = Stopwatch.StartNew();
        var loaded = BodyGraphSerializer.Read(new MemoryStream(bytes));
        stopwatch.Stop();

        output.WriteLine($"File size: {bytes.Length / 1024.0:F0} KiB, load + validation: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(Body.Graph.VertexCount, loaded.Graph.VertexCount);
    }

    private static double[] Time(Action<int> query)
    {
        var times = new double[Queries];
        for (var i = 0; i < Queries; i++)
        {
            var start = Stopwatch.GetTimestamp();
            query(i);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        return times;
    }

    private static string Describe(double[] times)
    {
        var sorted = times.Order().ToArray();
        return $"avg {times.Average():F3} ms, median {sorted[sorted.Length / 2]:F3} ms, p95 {sorted[(int)(sorted.Length * 0.95)]:F3} ms, max {sorted[^1]:F3} ms";
    }
}
