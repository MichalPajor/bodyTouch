using System.Numerics;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Geometry.Tests;

public class ZoneAndPointTargetTests
{
    private static readonly TestMesh PlaneMesh = TestMeshes.Plane(100, 100, 2, jitter: 0.3f, seed: 3);
    private static readonly BodyGraph Plane = BodyGraphBuilder.Build(PlaneMesh.Positions, PlaneMesh.Indices);

    private static ZoneRegion RightStrip(float fromX) =>
        ZoneRegion.FromTriangles(Plane, Enumerable.Range(0, PlaneMesh.TriangleCount).Where(t => PlaneMesh.Centroid(t).X >= fromX));

    [Fact]
    public void DistanceToZoneIsDistanceToItsNearestEdge()
    {
        var zone = RightStrip(60);
        var solver = new GeodesicSolver(Plane);

        var result = solver.DistanceToZone(PlaneMesh.Locate(new Vector3(20, 50, 0)), zone);

        Assert.False(result.IsInsideTarget);
        // The zone boundary lies between x = 58 and x = 60 (triangle centroids >= 60).
        Assert.InRange(result.DistanceCm, 38 * 0.9f, 40 * 1.1f);
    }

    [Fact]
    public void PinInsideZoneGivesZeroDistanceAndSinglePointPath()
    {
        var zone = RightStrip(60);
        var solver = new GeodesicSolver(Plane);

        var result = solver.DistanceToZone(PlaneMesh.Locate(new Vector3(80, 50, 0)), zone);

        Assert.True(result.IsInsideTarget);
        Assert.Equal(0, result.DistanceCm);
        Assert.Single(solver.GetLastPath());
    }

    [Fact]
    public void DistanceToZoneOnCylinderBand()
    {
        var mesh = TestMeshes.Cylinder(10, 60, 64, 30);
        var body = BodyGraphBuilder.Build(mesh.Positions, mesh.Indices);
        var band = ZoneRegion.FromTriangles(body, Enumerable.Range(0, mesh.TriangleCount).Where(t => mesh.Centroid(t).Z >= 40));
        var solver = new GeodesicSolver(body);

        var result = solver.DistanceToZone(mesh.Locate(new Vector3(-10, 0, 10)), band);

        Assert.InRange(result.DistanceCm, 28 * 0.9f, 30 * 1.1f);
    }

    [Fact]
    public void ZoneFromTriangleLabelsMatchesZoneFromTriangles()
    {
        var labels = Enumerable.Range(0, PlaneMesh.TriangleCount).Select(t => PlaneMesh.Centroid(t).X >= 60 ? 7 : 1).ToArray();

        var fromLabels = ZoneRegion.FromTriangleLabels(Plane, labels, 7);
        var fromTriangles = RightStrip(60);

        Assert.Equal(fromTriangles.TriangleCount, fromLabels.TriangleCount);
        Assert.Equal(fromTriangles.Vertices.ToArray(), fromLabels.Vertices.ToArray());
    }

    [Fact]
    public void TinyZoneWithoutOwnGraphVertexFallsBackToAnchors()
    {
        // Render mesh much finer than the helper graph: a single render triangle owns no graph vertex.
        var render = TestMeshes.Plane(40, 40, 0.5f);
        var helper = TestMeshes.Plane(40, 40, 4);
        var body = BodyGraphBuilder.Build(render.Positions, render.Indices, helper.Positions, helper.Indices);
        var triangle = render.Locate(new Vector3(21.1f, 21.1f, 0)).TriangleIndex;

        var zone = ZoneRegion.FromTriangles(body, [triangle]);

        Assert.NotEmpty(zone.Vertices.ToArray());
        var solver = new GeodesicSolver(body);
        Assert.InRange(solver.DistanceToZone(render.Locate(new Vector3(5, 21, 0)), zone).DistanceCm, 16 * 0.9f, 16 * 1.15f);
    }

    [Fact]
    public void ZoneFromDifferentBodyIsRejected()
    {
        var other = BodyGraphBuilder.Build(PlaneMesh.Positions, PlaneMesh.Indices);
        var zone = ZoneRegion.FromTriangles(other, [0]);
        var solver = new GeodesicSolver(Plane);

        Assert.Throws<ArgumentException>(() => solver.DistanceToZone(new SurfacePoint(1, 0.3f, 0.3f), zone));
    }

    [Fact]
    public void EmptyZoneIsRejected() =>
        Assert.Throws<ArgumentException>(() => ZoneRegion.FromTriangles(Plane, []));

    [Fact]
    public void PointTargetWithRadiusSubtractsRadius()
    {
        var solver = new GeodesicSolver(Plane);

        var result = solver.DistanceToPoint(PlaneMesh.Locate(new Vector3(80, 50, 0)), PlaneMesh.Locate(new Vector3(50, 50, 0)), radiusCm: 10);

        Assert.False(result.IsInsideTarget);
        Assert.InRange(result.DistanceCm, 20 * 0.9f, 20 * 1.1f);
    }

    [Fact]
    public void PinWithinRadiusIsInside()
    {
        var solver = new GeodesicSolver(Plane);

        var result = solver.DistanceToPoint(PlaneMesh.Locate(new Vector3(53, 50, 0)), PlaneMesh.Locate(new Vector3(50, 50, 0)), radiusCm: 5);

        Assert.True(result.IsInsideTarget);
        Assert.Equal(0, result.DistanceCm);
    }

    [Fact]
    public void PointsOnSameTriangleUseStraightSegment()
    {
        var solver = new GeodesicSolver(Plane);
        var a = new SurfacePoint(100, 0.2f, 0.2f);
        var b = new SurfacePoint(100, 0.5f, 0.3f);

        var result = solver.DistanceToPoint(a, b);

        Assert.Equal(Vector3.Distance(Plane.GetPosition(a), Plane.GetPosition(b)), result.DistanceCm, 4);
        Assert.Equal(2, solver.GetLastPath().Length);
    }

    [Fact]
    public void SamePointHasZeroDistance()
    {
        var solver = new GeodesicSolver(Plane);
        var p = PlaneMesh.Locate(new Vector3(30, 30, 0));

        Assert.Equal(0, solver.DistanceToPoint(p, p).DistanceCm);
    }

    [Fact]
    public void DisconnectedSurfacesAreUnreachable()
    {
        var first = TestMeshes.Plane(20, 20, 2);
        var second = TestMeshes.Plane(20, 20, 2);
        var shifted = second with { Positions = second.Positions.Select(p => p + new Vector3(0, 0, 50)).ToArray() };
        var mesh = TestMesh.Combine(first, shifted);
        var body = BodyGraphBuilder.Build(mesh.Positions, mesh.Indices);
        var solver = new GeodesicSolver(body);

        var result = solver.DistanceToPoint(mesh.Locate(new Vector3(10, 10, 0)), mesh.Locate(new Vector3(10, 10, 50)));

        Assert.False(result.IsReachable);
        Assert.Empty(solver.GetLastPath());
    }

    [Fact]
    public void InvalidInputsAreRejected()
    {
        var solver = new GeodesicSolver(Plane);
        var p = new SurfacePoint(0, 0.3f, 0.3f);

        Assert.Throws<ArgumentOutOfRangeException>(() => solver.DistanceToPoint(new SurfacePoint(-1, 0, 0), p));
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.DistanceToPoint(new SurfacePoint(Plane.TriangleCount, 0, 0), p));
        Assert.Throws<ArgumentException>(() => solver.DistanceToPoint(new SurfacePoint(0, float.NaN, 0), p));
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.DistanceToPoint(p, p, -1));
    }
}
