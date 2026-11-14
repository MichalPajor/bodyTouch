using System.Numerics;

namespace BodyTouchGame.Geometry;

/// <summary>
/// Everything needed to measure distances on the body: the helper surface graph,
/// the render mesh triangles (to turn a <see cref="SurfacePoint"/> into a 3D position)
/// and the mappings between render triangles and graph vertices.
/// </summary>
public sealed class BodyGraph
{
    /// <summary>Each render triangle has exactly this many anchor slots; unused slots hold -1.</summary>
    public const int AnchorsPerTriangle = 3;

    internal readonly Vector3[] MeshPositionsArray;
    internal readonly int[] MeshIndicesArray;
    internal readonly int[] TriangleAnchorsArray;
    internal readonly int[] VertexTrianglesArray;

    /// <param name="graph">Helper graph (about 5k vertices on the real model).</param>
    /// <param name="meshPositions">Render mesh vertex positions, in centimetres.</param>
    /// <param name="meshIndices">Render mesh triangles, 3 indices each, in the same order as in the .glb.</param>
    /// <param name="triangleAnchors">For each render triangle, <see cref="AnchorsPerTriangle"/> graph vertices
    /// used as entry points for a point lying on that triangle (-1 = unused slot, at least one must be set).</param>
    /// <param name="vertexTriangles">For each graph vertex, the nearest render triangle (used to assign graph
    /// vertices to zones).</param>
    public BodyGraph(SurfaceGraph graph, Vector3[] meshPositions, int[] meshIndices, int[] triangleAnchors, int[] vertexTriangles)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(meshPositions);
        ArgumentNullException.ThrowIfNull(meshIndices);
        ArgumentNullException.ThrowIfNull(triangleAnchors);
        ArgumentNullException.ThrowIfNull(vertexTriangles);

        if (meshIndices.Length == 0 || meshIndices.Length % 3 != 0)
            throw new ArgumentException("meshIndices must contain a positive multiple of 3 entries.", nameof(meshIndices));
        foreach (var index in meshIndices)
        {
            if ((uint)index >= (uint)meshPositions.Length)
                throw new ArgumentException("meshIndices contains an index out of range.", nameof(meshIndices));
        }

        var triangleCount = meshIndices.Length / 3;
        if (triangleAnchors.Length != triangleCount * AnchorsPerTriangle)
            throw new ArgumentException("triangleAnchors must have 3 entries per render triangle.", nameof(triangleAnchors));
        for (var t = 0; t < triangleCount; t++)
        {
            var hasAnchor = false;
            for (var k = 0; k < AnchorsPerTriangle; k++)
            {
                var anchor = triangleAnchors[t * AnchorsPerTriangle + k];
                if (anchor == -1) continue;
                if ((uint)anchor >= (uint)graph.VertexCount)
                    throw new ArgumentException($"Triangle {t} has an anchor out of range.", nameof(triangleAnchors));
                hasAnchor = true;
            }

            if (!hasAnchor)
                throw new ArgumentException($"Triangle {t} has no anchor vertex.", nameof(triangleAnchors));
        }

        if (vertexTriangles.Length != graph.VertexCount)
            throw new ArgumentException("vertexTriangles must have one entry per graph vertex.", nameof(vertexTriangles));
        foreach (var triangle in vertexTriangles)
        {
            if ((uint)triangle >= (uint)triangleCount)
                throw new ArgumentException("vertexTriangles contains a triangle out of range.", nameof(vertexTriangles));
        }

        Graph = graph;
        MeshPositionsArray = meshPositions;
        MeshIndicesArray = meshIndices;
        TriangleAnchorsArray = triangleAnchors;
        VertexTrianglesArray = vertexTriangles;
    }

    public SurfaceGraph Graph { get; }

    public int TriangleCount => MeshIndicesArray.Length / 3;
    public int MeshVertexCount => MeshPositionsArray.Length;

    public ReadOnlySpan<Vector3> MeshPositions => MeshPositionsArray;
    public ReadOnlySpan<int> MeshIndices => MeshIndicesArray;
    public ReadOnlySpan<int> TriangleAnchors => TriangleAnchorsArray;
    public ReadOnlySpan<int> VertexTriangles => VertexTrianglesArray;

    public ReadOnlySpan<int> GetAnchors(int triangle)
    {
        ValidateTriangle(triangle);
        return TriangleAnchorsArray.AsSpan(triangle * AnchorsPerTriangle, AnchorsPerTriangle);
    }

    public Vector3 GetPosition(SurfacePoint point)
    {
        ValidateTriangle(point.TriangleIndex);
        if (!float.IsFinite(point.BaryU) || !float.IsFinite(point.BaryV))
            throw new ArgumentException("Barycentric coordinates must be finite.", nameof(point));

        var i = point.TriangleIndex * 3;
        var a = MeshPositionsArray[MeshIndicesArray[i]];
        var b = MeshPositionsArray[MeshIndicesArray[i + 1]];
        var c = MeshPositionsArray[MeshIndicesArray[i + 2]];
        var w = 1f - point.BaryU - point.BaryV;
        return w * a + point.BaryU * b + point.BaryV * c;
    }

    private void ValidateTriangle(int triangle)
    {
        if ((uint)triangle >= (uint)TriangleCount)
            throw new ArgumentOutOfRangeException(nameof(triangle), triangle, "Triangle index out of range.");
    }
}
