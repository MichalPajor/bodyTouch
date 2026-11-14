using System.Numerics;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Geometry.Tests;

public class PathTests
{
    private static readonly TestMesh PlaneMesh = TestMeshes.Plane(100, 100, 2, jitter: 0.3f, seed: 5);
    private static readonly BodyGraph Plane = BodyGraphBuilder.Build(PlaneMesh.Positions, PlaneMesh.Indices);

    [Fact]
    public void PathRunsFromPinToTargetAndMatchesDistance()
    {
        var solver = new GeodesicSolver(Plane);
        var pin = PlaneMesh.Locate(new Vector3(15, 20, 0));
        var target = PlaneMesh.Locate(new Vector3(85, 70, 0));

        var distance = solver.DistanceToPoint(pin, target).DistanceCm;
        var path = solver.GetLastPath();

        Assert.Equal(Plane.GetPosition(pin), path[0]);
        Assert.Equal(Plane.GetPosition(target), path[^1]);
        // Chaikin cuts corners, so the smoothed path is a little shorter than the graph distance.
        Assert.InRange(Polyline.Length(path), distance * 0.9f, distance * 1.001f);
    }

    [Fact]
    public void PathToZoneEndsOnZoneVertex()
    {
        var solver = new GeodesicSolver(Plane);
        var zone = ZoneRegion.FromTriangles(Plane, Enumerable.Range(0, PlaneMesh.TriangleCount).Where(t => PlaneMesh.Centroid(t).X >= 70));

        solver.DistanceToZone(PlaneMesh.Locate(new Vector3(20, 50, 0)), zone);
        var path = solver.GetLastPath();

        var end = path[^1];
        Assert.Contains(zone.Vertices.ToArray(), v => Plane.Graph.Positions[v] == end);
    }

    [Fact]
    public void PathToPointWithRadiusEndsOnRadiusBoundary()
    {
        var solver = new GeodesicSolver(Plane);
        var targetPosition = new Vector3(50, 50, 0);

        solver.DistanceToPoint(PlaneMesh.Locate(new Vector3(90, 55, 0)), PlaneMesh.Locate(targetPosition), radiusCm: 10);
        var path = solver.GetLastPath();

        Assert.InRange(Vector3.Distance(path[^1], targetPosition), 8.5f, 10.5f);
    }

    [Fact]
    public void SmoothingAddsPointsAndKeepsEndpoints()
    {
        var solver = new GeodesicSolver(Plane);
        solver.DistanceToPoint(PlaneMesh.Locate(new Vector3(10, 10, 0)), PlaneMesh.Locate(new Vector3(90, 40, 0)));

        var raw = solver.GetLastPath(smoothingIterations: 0);
        var smooth = solver.GetLastPath(smoothingIterations: 2);

        Assert.True(smooth.Length > raw.Length);
        Assert.Equal(raw[0], smooth[0]);
        Assert.Equal(raw[^1], smooth[^1]);
    }

    [Fact]
    public void PathOnCylinderStaysCloseToSurface()
    {
        var mesh = TestMeshes.Cylinder(10, 60, 64, 30);
        var body = BodyGraphBuilder.Build(mesh.Positions, mesh.Indices);
        var solver = new GeodesicSolver(body);

        solver.DistanceToPoint(mesh.Locate(new Vector3(10, 0, 10)), mesh.Locate(new Vector3(-10, 0, 40)));

        foreach (var p in solver.GetLastPath())
            Assert.InRange(MathF.Sqrt(p.X * p.X + p.Y * p.Y), 9.0f, 10.01f);
    }

    [Fact]
    public void PathIsUnavailableBeforeAnyQueryAndAfterAnotherQuery()
    {
        var solver = new GeodesicSolver(Plane);
        Assert.Throws<InvalidOperationException>(() => solver.GetLastPath());

        var p = PlaneMesh.Locate(new Vector3(30, 30, 0));
        solver.DistanceToPoint(p, PlaneMesh.Locate(new Vector3(60, 60, 0)));
        solver.VerticesWithin(p, 5);
        Assert.Throws<InvalidOperationException>(() => solver.GetLastPath());
    }
}

public class PolylineTests
{
    [Fact]
    public void ChaikinKeepsEndpointsAndDoublesSegments()
    {
        Vector3[] points = [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0)];

        var once = Polyline.Chaikin(points, 1);
        var twice = Polyline.Chaikin(points, 2);

        Assert.Equal(6, once.Length); // 2 per segment + 2 endpoints: 2 * 2 + 2
        Assert.Equal(12, twice.Length); // 5 segments: 2 * 5 + 2
        Assert.Equal(points[0], twice[0]);
        Assert.Equal(points[^1], twice[^1]);
        Assert.True(Polyline.Length(twice) < Polyline.Length(points));
    }

    [Fact]
    public void ChaikinRemovesDuplicatesAndLeavesShortLinesUntouched()
    {
        Vector3[] points = [new(0, 0, 0), new(0, 0, 0), new(5, 0, 0)];

        Assert.Equal([new Vector3(0, 0, 0), new Vector3(5, 0, 0)], Polyline.Chaikin(points, 2));
    }

    [Fact]
    public void TrimEndRemovesArcLengthFromEnd()
    {
        Vector3[] points = [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0)];

        var trimmed = Polyline.TrimEnd(points, 15);

        Assert.Equal(2, trimmed.Length);
        Assert.Equal(new Vector3(5, 0, 0), trimmed[^1]);
        Assert.Single(Polyline.TrimEnd(points, 100));
    }
}
