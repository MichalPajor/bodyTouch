using System.Numerics;

namespace BodyTouchGame.Geometry;

/// <summary>
/// Builds <see cref="SurfaceGraph"/> and <see cref="BodyGraph"/> from triangle meshes.
/// Used by data-preparation tools and tests; the app only loads ready-made files.
/// </summary>
public static class BodyGraphBuilder
{
    /// <summary>Default neighbourhood depth for graph edges, see <see cref="BuildGraph"/>.</summary>
    public const int DefaultRingDepth = 2;

    /// <summary>
    /// Builds the surface graph of a triangle mesh. Every vertex is connected to all vertices within
    /// <paramref name="ringDepth"/> mesh edges, with the straight-line length as weight.
    /// Depth 1 uses mesh edges only: shortest paths then zig-zag and overestimate surface distance
    /// (up to ~40 % in unlucky directions on a regular grid). Depth 2 adds many more directions and keeps
    /// the error at a few percent, while still connecting only vertices that are neighbours on the surface
    /// (no shortcuts through gaps between, e.g., the hand and the thigh).
    /// </summary>
    public static SurfaceGraph BuildGraph(Vector3[] positions, int[] indices, int ringDepth = DefaultRingDepth)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);
        if (indices.Length % 3 != 0)
            throw new ArgumentException("indices must contain a multiple of 3 entries.", nameof(indices));
        ArgumentOutOfRangeException.ThrowIfLessThan(ringDepth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ringDepth, 3);

        var vertexCount = positions.Length;
        var adjacency = new HashSet<int>[vertexCount];
        for (var v = 0; v < vertexCount; v++) adjacency[v] = [];

        for (var i = 0; i < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            if ((uint)a >= (uint)vertexCount || (uint)b >= (uint)vertexCount || (uint)c >= (uint)vertexCount)
                throw new ArgumentException($"Triangle {i / 3} references a vertex out of range.", nameof(indices));
            Link(adjacency, a, b);
            Link(adjacency, b, c);
            Link(adjacency, c, a);
        }

        var rowOffsets = new int[vertexCount + 1];
        var rings = new List<int>[vertexCount];
        var stamp = new int[vertexCount];
        var frontier = new List<int>();
        var next = new List<int>();

        for (var v = 0; v < vertexCount; v++)
        {
            var ring = new List<int>();
            stamp[v] = v + 1;
            frontier.Clear();
            frontier.Add(v);
            for (var depth = 0; depth < ringDepth && frontier.Count > 0; depth++)
            {
                next.Clear();
                foreach (var u in frontier)
                foreach (var w in adjacency[u])
                {
                    if (stamp[w] == v + 1) continue;
                    stamp[w] = v + 1;
                    next.Add(w);
                    ring.Add(w);
                }

                (frontier, next) = (next, frontier);
            }

            ring.Sort();
            rings[v] = ring;
            rowOffsets[v + 1] = rowOffsets[v] + ring.Count;
        }

        var neighbors = new int[rowOffsets[^1]];
        var lengths = new float[rowOffsets[^1]];
        for (var v = 0; v < vertexCount; v++)
        {
            var offset = rowOffsets[v];
            foreach (var w in rings[v])
            {
                neighbors[offset] = w;
                lengths[offset] = Vector3.Distance(positions[v], positions[w]);
                offset++;
            }
        }

        return new SurfaceGraph(positions, rowOffsets, neighbors, lengths);
    }

    /// <summary>Builds a body graph whose helper graph is the render mesh itself.</summary>
    public static BodyGraph Build(Vector3[] meshPositions, int[] meshIndices, int ringDepth = DefaultRingDepth)
    {
        var graph = BuildGraph(meshPositions, meshIndices, ringDepth);
        var triangleCount = meshIndices.Length / 3;

        var anchors = (int[])meshIndices.Clone();
        var vertexTriangles = new int[meshPositions.Length];
        Array.Fill(vertexTriangles, -1);
        for (var t = 0; t < triangleCount; t++)
        for (var k = 0; k < 3; k++)
        {
            var v = meshIndices[t * 3 + k];
            if (vertexTriangles[v] < 0) vertexTriangles[v] = t;
        }

        if (Array.IndexOf(vertexTriangles, -1) >= 0)
        {
            var index = new TriangleSpatialIndex(meshPositions, meshIndices);
            for (var v = 0; v < vertexTriangles.Length; v++)
                if (vertexTriangles[v] < 0) vertexTriangles[v] = index.FindNearest(meshPositions[v], out _);
        }

        return new BodyGraph(graph, meshPositions, meshIndices, anchors, vertexTriangles);
    }

    /// <summary>
    /// Builds a body graph from a detailed render mesh and a simplified helper mesh covering the same surface.
    /// Each render triangle is anchored to the corners of the helper triangle nearest to its centroid;
    /// each helper vertex is mapped to the nearest render triangle.
    /// </summary>
    public static BodyGraph Build(
        Vector3[] meshPositions, int[] meshIndices,
        Vector3[] graphPositions, int[] graphIndices,
        int ringDepth = DefaultRingDepth)
    {
        ArgumentNullException.ThrowIfNull(meshPositions);
        ArgumentNullException.ThrowIfNull(meshIndices);
        ArgumentNullException.ThrowIfNull(graphPositions);
        ArgumentNullException.ThrowIfNull(graphIndices);

        var graph = BuildGraph(graphPositions, graphIndices, ringDepth);
        var triangleCount = meshIndices.Length / 3;

        var graphIndex = new TriangleSpatialIndex(graphPositions, graphIndices);
        var anchors = new int[triangleCount * BodyGraph.AnchorsPerTriangle];
        for (var t = 0; t < triangleCount; t++)
        {
            var centroid = (meshPositions[meshIndices[t * 3]] + meshPositions[meshIndices[t * 3 + 1]] +
                            meshPositions[meshIndices[t * 3 + 2]]) / 3f;
            var g = graphIndex.FindNearest(centroid, out _);
            anchors[t * 3] = graphIndices[g * 3];
            anchors[t * 3 + 1] = graphIndices[g * 3 + 1];
            anchors[t * 3 + 2] = graphIndices[g * 3 + 2];
        }

        var meshIndex = new TriangleSpatialIndex(meshPositions, meshIndices);
        var vertexTriangles = new int[graphPositions.Length];
        for (var v = 0; v < graphPositions.Length; v++)
            vertexTriangles[v] = meshIndex.FindNearest(graphPositions[v], out _);

        return new BodyGraph(graph, meshPositions, meshIndices, anchors, vertexTriangles);
    }

    private static void Link(HashSet<int>[] adjacency, int a, int b)
    {
        if (a == b) return;
        adjacency[a].Add(b);
        adjacency[b].Add(a);
    }
}
