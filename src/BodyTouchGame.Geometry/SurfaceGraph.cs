using System.Numerics;

namespace BodyTouchGame.Geometry;

/// <summary>
/// Undirected weighted graph over surface vertices in CSR form.
/// Neighbours of vertex <c>v</c> are <c>Neighbors[RowOffsets[v] .. RowOffsets[v + 1]]</c>,
/// with edge lengths in centimetres at the same positions in <c>EdgeLengths</c>.
/// Each undirected edge is stored twice (once per direction).
/// </summary>
public sealed class SurfaceGraph
{
    internal readonly Vector3[] PositionsArray;
    internal readonly int[] RowOffsetsArray;
    internal readonly int[] NeighborsArray;
    internal readonly float[] EdgeLengthsArray;

    public SurfaceGraph(Vector3[] positions, int[] rowOffsets, int[] neighbors, float[] edgeLengths)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(rowOffsets);
        ArgumentNullException.ThrowIfNull(neighbors);
        ArgumentNullException.ThrowIfNull(edgeLengths);

        if (rowOffsets.Length != positions.Length + 1)
            throw new ArgumentException("rowOffsets must have VertexCount + 1 entries.", nameof(rowOffsets));
        if (neighbors.Length != edgeLengths.Length)
            throw new ArgumentException("neighbors and edgeLengths must have the same length.", nameof(edgeLengths));
        if (rowOffsets[0] != 0 || rowOffsets[^1] != neighbors.Length)
            throw new ArgumentException("rowOffsets must start at 0 and end at the edge count.", nameof(rowOffsets));

        for (var v = 0; v < positions.Length; v++)
        {
            if (rowOffsets[v + 1] < rowOffsets[v])
                throw new ArgumentException($"rowOffsets must be non-decreasing (vertex {v}).", nameof(rowOffsets));
            var p = positions[v];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                throw new ArgumentException($"Vertex {v} has a non-finite position.", nameof(positions));
        }

        for (var e = 0; e < neighbors.Length; e++)
        {
            if ((uint)neighbors[e] >= (uint)positions.Length)
                throw new ArgumentException($"Edge {e} points to a vertex out of range.", nameof(neighbors));
            if (!float.IsFinite(edgeLengths[e]) || edgeLengths[e] < 0)
                throw new ArgumentException($"Edge {e} has an invalid length.", nameof(edgeLengths));
        }

        PositionsArray = positions;
        RowOffsetsArray = rowOffsets;
        NeighborsArray = neighbors;
        EdgeLengthsArray = edgeLengths;
    }

    public int VertexCount => PositionsArray.Length;

    /// <summary>Number of directed edge entries (twice the number of undirected edges).</summary>
    public int EdgeCount => NeighborsArray.Length;

    public ReadOnlySpan<Vector3> Positions => PositionsArray;
    public ReadOnlySpan<int> RowOffsets => RowOffsetsArray;
    public ReadOnlySpan<int> Neighbors => NeighborsArray;
    public ReadOnlySpan<float> EdgeLengths => EdgeLengthsArray;

    public ReadOnlySpan<int> GetNeighbors(int vertex) =>
        NeighborsArray.AsSpan(RowOffsetsArray[vertex], RowOffsetsArray[vertex + 1] - RowOffsetsArray[vertex]);
}
