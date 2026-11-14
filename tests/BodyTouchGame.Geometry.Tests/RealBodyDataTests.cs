using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using BodyDataTool;
using BodyTouchGame.Geometry;
using Xunit.Abstractions;

namespace BodyTouchGame.Geometry.Tests;

/// <summary>
/// Tests on the real model shipped with the app (body.glb, body_zones.json, body_graph.bin).
/// Coordinates are in the glTF frame: Y up, front +Z, centimetres.
/// </summary>
public class RealBodyDataTests(ITestOutputHelper output)
{
    private static readonly Lazy<(BodyZonesFile Zones, BodyGraph Body, TriangleMesh Glb)> Data = new(() =>
    {
        var models = Path.Combine(RepositoryRoot(), "src", "BodyTouchGame.App", "Resources", "Raw");
        return (BodyZonesFile.Read(Path.Combine(models, "wwwroot", "models", "body_zones.json")),
            BodyGraphSerializer.ReadFile(Path.Combine(models, "body_graph.bin")),
            GlbReader.ReadMesh(Path.Combine(models, "wwwroot", "models", "body.glb"), "Body"));
    });

    private static BodyZonesFile Zones => Data.Value.Zones;
    private static BodyGraph Body => Data.Value.Body;

    [Fact]
    public void DataFilesAreValid()
    {
        var result = BodyDataValidator.Validate(Zones, Body);
        foreach (var (key, value) in result.Statistics) output.WriteLine($"{key}: {value}");
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void GraphDescribesExactlyTheMeshInTheGlb()
    {
        var glb = Data.Value.Glb;
        Assert.Equal(glb.Indices, Body.MeshIndices.ToArray());
        Assert.Equal(glb.Positions, Body.MeshPositions.ToArray());
        Assert.Equal(glb.TriangleCount, Zones.TriangleCount);
    }

    [Theory]
    [InlineData("l", "hand")]
    [InlineData("r", "hand")]
    [InlineData("l", "forearm")]
    [InlineData("r", "forearm")]
    public void ArmToTrunkAndThighCrossesNoGap(string side, string armPart)
    {
        // The closest pair of points between the arm part and the trunk/thigh, i.e. straight across the gap.
        // Along the surface the path has to go up the arm to the armpit and down the body, so it must rise
        // above the elbow and be clearly longer than the gap (a shortcut would give a ratio of about 1).
        const float minRatio = 1.5f;
        var mesh = Data.Value.Glb;
        var arm = TrianglesOf($"arm-{side}.{armPart}");
        var body = TrianglesOf($"leg-{side}.thigh", $"leg-{side}.hip", $"belly.flank-{side}", "belly.center");
        var (a, b) = arm.SelectMany(x => body.Select(y => (x, y)))
            .MinBy(p => Vector3.DistanceSquared(mesh.Centroid(p.x), mesh.Centroid(p.y)));
        var from = new SurfacePoint(a, 1 / 3f, 1 / 3f);
        var to = new SurfacePoint(b, 1 / 3f, 1 / 3f);
        var solver = new GeodesicSolver(Body);

        var straight = Vector3.Distance(Body.GetPosition(from), Body.GetPosition(to));
        var surface = solver.DistanceToPoint(from, to).DistanceCm;
        var highest = solver.GetLastPath().Max(p => p.Y);
        var elbowHeight = Body.GetPosition(Landmark($"arm-{side}.elbow")).Y;

        output.WriteLine($"{side} {armPart}: gap (straight) {straight:F1} cm, surface {surface:F1} cm, path reaches y = {highest:F1} cm (elbow {elbowHeight:F1} cm)");
        Assert.True(surface > minRatio * straight, "The surface path is too short: it must not jump across the gap.");
        Assert.True(highest > elbowHeight, "The path must go up the arm to the armpit.");
    }

    private static int[] TrianglesOf(params string[] zoneIds)
    {
        var ids = zoneIds.Select(id => Zones.Zones.FindIndex(z => z.Id == id)).ToHashSet();
        var labels = Zones.TriangleZones["Medium"];
        return Enumerable.Range(0, labels.Length).Where(t => ids.Contains(labels[t])).ToArray();
    }

    public static TheoryData<string, string, float, float> Pairs() => new()
    {
        // from, to, expected surface distance range [cm] for a 170 cm adult
        { "arm-l.hand", "arm-l.shoulder", 40, 75 },
        { "arm-l.elbow", "arm-l.hand", 18, 40 },
        { "arm-l.hand", "arm-r.hand", 110, 190 },
        { "leg-l.foot", "leg-r.foot", 130, 220 },
        { "leg-l.knee", "leg-l.calf", 8, 30 },
        { "head.ear-l", "head.ear-r", 20, 45 },
        { "head.face", "neck.back", 15, 40 },
        { "chest.l", "upper-back.l", 15, 50 },
        { "belly.center", "lower-back.l", 20, 50 },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void SurfaceDistancesHaveTheRightOrderOfMagnitude(string from, string to, float min, float max)
    {
        var solver = new GeodesicSolver(Body);
        var a = Landmark(from);
        var b = Landmark(to);

        var surface = solver.DistanceToPoint(a, b).DistanceCm;
        var straight = Vector3.Distance(Body.GetPosition(a), Body.GetPosition(b));

        output.WriteLine($"{from} → {to}: straight {straight:F1} cm, surface {surface:F1} cm");
        Assert.InRange(surface, min, max);
        Assert.True(surface >= straight * 0.98f, "Surface distance cannot be shorter than the straight line.");
    }

    [Fact]
    public void PinInsideEveryZoneGivesZero()
    {
        var solver = new GeodesicSolver(Body);
        foreach (var level in new[] { "Easy", "Medium" })
        {
            var labels = Zones.TriangleZones[level];
            for (var z = 0; z < Zones.Zones.Count; z++)
            {
                if (Zones.Zones[z].Level != level) continue;
                var region = ZoneRegion.FromTriangleLabels(Body, labels, z);
                var triangle = Array.IndexOf(labels, z);
                Assert.Equal(0, solver.DistanceToZone(new SurfacePoint(triangle, 0.3f, 0.3f), region).DistanceCm);
            }
        }
    }

    [Fact]
    public void QueriesOnTheRealGraphAreFast()
    {
        var solver = new GeodesicSolver(Body);
        var labels = Zones.TriangleZones["Medium"];
        var regions = Enumerable.Range(0, Zones.Zones.Count).Where(z => Zones.Zones[z].Level == "Medium")
            .Select(z => ZoneRegion.FromTriangleLabels(Body, labels, z)).ToArray();
        var random = new Random(1);
        for (var i = 0; i < 20; i++) solver.DistanceToZone(new SurfacePoint(random.Next(Body.TriangleCount), 0.3f, 0.3f), regions[i % regions.Length]);

        var times = new List<double>();
        for (var i = 0; i < 300; i++)
        {
            var pin = new SurfacePoint(random.Next(Body.TriangleCount), 0.3f, 0.3f);
            var start = Stopwatch.GetTimestamp();
            solver.DistanceToZone(pin, regions[random.Next(regions.Length)]);
            solver.GetLastPath();
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        times.Sort();
        output.WriteLine($"Real graph ({Body.Graph.VertexCount} vertices): avg {times.Average():F3} ms, median {times[times.Count / 2]:F3} ms, max {times[^1]:F3} ms");
        Assert.True(times[^1] < 50);
    }

    /// <summary>The zone triangle closest to the zone's centre of mass.</summary>
    private static SurfacePoint Landmark(string zoneId)
    {
        var index = Zones.Zones.FindIndex(z => z.Id == zoneId);
        var labels = Zones.TriangleZones[Zones.Zones[index].Level];
        var mesh = Data.Value.Glb;
        var triangles = Enumerable.Range(0, labels.Length).Where(t => labels[t] == index).ToArray();
        var centre = triangles.Aggregate(Vector3.Zero, (sum, t) => sum + mesh.Centroid(t)) / triangles.Length;
        var best = triangles.MinBy(t => Vector3.DistanceSquared(mesh.Centroid(t), centre));
        return new SurfacePoint(best, 1 / 3f, 1 / 3f);
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
