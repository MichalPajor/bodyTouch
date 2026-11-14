using System.Numerics;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Geometry.Tests;

public class BodyGraphBuilderTests
{
    [Fact]
    public void GraphEdgesAreSymmetricWithEuclideanLengths()
    {
        var mesh = TestMeshes.Sphere(20, 24, 12);
        var graph = BodyGraphBuilder.BuildGraph(mesh.Positions, mesh.Indices);

        for (var v = 0; v < graph.VertexCount; v++)
        {
            var neighbors = graph.GetNeighbors(v);
            var lengths = graph.EdgeLengths.Slice(graph.RowOffsets[v], neighbors.Length);
            for (var i = 0; i < neighbors.Length; i++)
            {
                var w = neighbors[i];
                Assert.NotEqual(v, w);
                Assert.Equal(Vector3.Distance(graph.Positions[v], graph.Positions[w]), lengths[i], 4);
                Assert.True(graph.GetNeighbors(w).Contains(v), $"Edge {v}->{w} has no reverse edge.");
            }
        }
    }

    [Fact]
    public void RingDepthControlsNeighbourhood()
    {
        var mesh = TestMeshes.Plane(20, 20, 2);
        var ring1 = BodyGraphBuilder.BuildGraph(mesh.Positions, mesh.Indices, 1);
        var ring2 = BodyGraphBuilder.BuildGraph(mesh.Positions, mesh.Indices, 2);

        // Interior vertex of a grid with alternating diagonals: 4 or 8 mesh neighbours.
        var interior = 5 * 11 + 5;
        Assert.InRange(ring1.GetNeighbors(interior).Length, 4, 8);
        Assert.True(ring2.GetNeighbors(interior).Length > 2 * ring1.GetNeighbors(interior).Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => BodyGraphBuilder.BuildGraph(mesh.Positions, mesh.Indices, 0));
    }

    [Fact]
    public void SeparateHelperMeshGivesAccurateDistances()
    {
        var render = TestMeshes.Plane(100, 100, 1, jitter: 0.3f, seed: 11);
        var helper = TestMeshes.Plane(100, 100, 2.5f, jitter: 0.3f, seed: 12);
        var body = BodyGraphBuilder.Build(render.Positions, render.Indices, helper.Positions, helper.Indices);
        var solver = new GeodesicSolver(body);

        foreach (var (a, b) in new (Vector3, Vector3)[] { (new(10, 20, 0), new(90, 75, 0)), (new(50, 5, 0), new(52, 95, 0)), (new(30, 30, 0), new(36, 38, 0)) })
        {
            var result = solver.DistanceToPoint(render.Locate(a), render.Locate(b));
            var expected = Vector3.Distance(a, b);
            Assert.InRange(result.DistanceCm, expected * 0.9f, expected * 1.1f);
        }
    }

    [Fact]
    public void AnchorsAndVertexTrianglesPointToNearbyGeometry()
    {
        var render = TestMeshes.Plane(40, 40, 1);
        var helper = TestMeshes.Plane(40, 40, 4);
        var body = BodyGraphBuilder.Build(render.Positions, render.Indices, helper.Positions, helper.Indices);

        for (var t = 0; t < body.TriangleCount; t++)
        {
            var centroid = render.Centroid(t);
            foreach (var anchor in body.GetAnchors(t))
                Assert.True(Vector3.Distance(centroid, body.Graph.Positions[anchor]) <= 4 * 1.5f);
        }

        for (var v = 0; v < body.Graph.VertexCount; v++)
            Assert.True(Vector3.Distance(body.Graph.Positions[v], render.Centroid(body.VertexTriangles[v])) <= 1.0f);
    }

    [Fact]
    public void InvalidGraphDataIsRejected()
    {
        Vector3[] positions = [Vector3.Zero, Vector3.UnitX];

        Assert.Throws<ArgumentException>(() => new SurfaceGraph(positions, [0, 1], [1], [1f]));
        Assert.Throws<ArgumentException>(() => new SurfaceGraph(positions, [0, 1, 1], [5], [1f]));
        Assert.Throws<ArgumentException>(() => new SurfaceGraph(positions, [0, 1, 1], [1], [-1f]));
        Assert.Throws<ArgumentException>(() => BodyGraphBuilder.BuildGraph(positions, [0, 1, 9]));
    }
}

public class BodyGraphSerializerTests
{
    private static BodyGraph CreateBody()
    {
        var render = TestMeshes.Plane(30, 30, 1);
        var helper = TestMeshes.Plane(30, 30, 3);
        return BodyGraphBuilder.Build(render.Positions, render.Indices, helper.Positions, helper.Indices);
    }

    [Fact]
    public void RoundTripPreservesAllData()
    {
        var body = CreateBody();
        using var stream = new MemoryStream();

        BodyGraphSerializer.Write(body, stream);
        stream.Position = 0;
        var copy = BodyGraphSerializer.Read(stream);

        Assert.Equal(body.Graph.Positions.ToArray(), copy.Graph.Positions.ToArray());
        Assert.Equal(body.Graph.RowOffsets.ToArray(), copy.Graph.RowOffsets.ToArray());
        Assert.Equal(body.Graph.Neighbors.ToArray(), copy.Graph.Neighbors.ToArray());
        Assert.Equal(body.Graph.EdgeLengths.ToArray(), copy.Graph.EdgeLengths.ToArray());
        Assert.Equal(body.MeshPositions.ToArray(), copy.MeshPositions.ToArray());
        Assert.Equal(body.MeshIndices.ToArray(), copy.MeshIndices.ToArray());
        Assert.Equal(body.TriangleAnchors.ToArray(), copy.TriangleAnchors.ToArray());
        Assert.Equal(body.VertexTriangles.ToArray(), copy.VertexTriangles.ToArray());
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void ReadsFromNonSeekableStream()
    {
        using var buffer = new MemoryStream();
        BodyGraphSerializer.Write(CreateBody(), buffer);

        using var stream = new NonSeekableStream(buffer.ToArray());
        var copy = BodyGraphSerializer.Read(stream);

        Assert.True(copy.TriangleCount > 0);
    }

    [Fact]
    public void BadMagicNumberIsRejected()
    {
        using var stream = new MemoryStream(new byte[64]);
        Assert.Throws<InvalidDataException>(() => BodyGraphSerializer.Read(stream));
    }

    [Fact]
    public void TruncatedFileIsRejected()
    {
        using var buffer = new MemoryStream();
        BodyGraphSerializer.Write(CreateBody(), buffer);
        var bytes = buffer.ToArray()[..^10];

        Assert.Throws<InvalidDataException>(() => BodyGraphSerializer.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void CorruptedContentIsRejected()
    {
        using var buffer = new MemoryStream();
        BodyGraphSerializer.Write(CreateBody(), buffer);
        var bytes = buffer.ToArray();
        // First neighbour index (after the 24-byte header, N positions and N+1 row offsets) set out of range.
        var n = BitConverter.ToInt32(bytes, 8);
        var neighborOffset = 24 + n * 12 + (n + 1) * 4;
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, neighborOffset);

        Assert.Throws<InvalidDataException>(() => BodyGraphSerializer.Read(new MemoryStream(bytes)));
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
        public override long Position { get => base.Position; set => throw new NotSupportedException(); }
    }
}
