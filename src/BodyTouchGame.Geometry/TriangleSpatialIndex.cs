using System.Numerics;

namespace BodyTouchGame.Geometry;

/// <summary>
/// Uniform grid over triangle bounding boxes for nearest-triangle queries.
/// Used when building data files, not during gameplay.
/// </summary>
public sealed class TriangleSpatialIndex
{
    private const int MaxCells = 2_000_000;

    private readonly Vector3[] _positions;
    private readonly int[] _indices;
    private readonly Vector3 _origin;
    private readonly float _cellSize;
    private readonly int _nx, _ny, _nz;
    private readonly int[] _cellStart;
    private readonly int[] _cellItems;
    private readonly int[] _visitStamp;
    private int _visitGeneration;

    public TriangleSpatialIndex(Vector3[] positions, int[] indices)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);
        if (indices.Length == 0 || indices.Length % 3 != 0)
            throw new ArgumentException("indices must contain a positive multiple of 3 entries.", nameof(indices));

        _positions = positions;
        _indices = indices;
        var triangleCount = indices.Length / 3;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        double edgeSum = 0;
        for (var t = 0; t < triangleCount; t++)
        {
            var (a, b, c) = Corners(t);
            min = Vector3.Min(min, Vector3.Min(a, Vector3.Min(b, c)));
            max = Vector3.Max(max, Vector3.Max(a, Vector3.Max(b, c)));
            edgeSum += Vector3.Distance(a, b) + Vector3.Distance(b, c) + Vector3.Distance(c, a);
        }

        var extent = Vector3.Max(max - min, new Vector3(1e-3f));
        var cellSize = MathF.Max((float)(edgeSum / (3.0 * triangleCount)) * 2f, 1e-3f);
        while (CellCount(extent, cellSize) > MaxCells) cellSize *= 1.5f;

        _origin = min;
        _cellSize = cellSize;
        _nx = Dimension(extent.X, cellSize);
        _ny = Dimension(extent.Y, cellSize);
        _nz = Dimension(extent.Z, cellSize);

        var counts = new int[_nx * _ny * _nz + 1];
        for (var t = 0; t < triangleCount; t++) ForEachCell(t, cell => counts[cell + 1]++);
        for (var i = 1; i < counts.Length; i++) counts[i] += counts[i - 1];
        _cellStart = counts;

        _cellItems = new int[counts[^1]];
        var fill = (int[])counts.Clone();
        for (var t = 0; t < triangleCount; t++)
        {
            var triangle = t;
            ForEachCell(t, cell => _cellItems[fill[cell]++] = triangle);
        }

        _visitStamp = new int[triangleCount];
    }

    /// <summary>Returns the triangle nearest to <paramref name="point"/> and the closest point on it.</summary>
    public int FindNearest(Vector3 point, out Vector3 closestPoint)
    {
        if (++_visitGeneration == int.MaxValue)
        {
            Array.Clear(_visitStamp);
            _visitGeneration = 1;
        }

        var cx = Math.Clamp((int)((point.X - _origin.X) / _cellSize), 0, _nx - 1);
        var cy = Math.Clamp((int)((point.Y - _origin.Y) / _cellSize), 0, _ny - 1);
        var cz = Math.Clamp((int)((point.Z - _origin.Z) / _cellSize), 0, _nz - 1);

        var best = -1;
        var bestDistanceSquared = float.MaxValue;
        closestPoint = default;
        var maxRing = Math.Max(_nx, Math.Max(_ny, _nz));

        for (var ring = 0; ring <= maxRing; ring++)
        {
            var ringMinDistanceSquared = float.MaxValue;
            for (var x = cx - ring; x <= cx + ring; x++)
            {
                if ((uint)x >= (uint)_nx) continue;
                for (var y = cy - ring; y <= cy + ring; y++)
                {
                    if ((uint)y >= (uint)_ny) continue;
                    for (var z = cz - ring; z <= cz + ring; z++)
                    {
                        if ((uint)z >= (uint)_nz) continue;
                        if (Math.Max(Math.Abs(x - cx), Math.Max(Math.Abs(y - cy), Math.Abs(z - cz))) != ring) continue;

                        var boxDistanceSquared = BoxDistanceSquared(point, x, y, z);
                        ringMinDistanceSquared = MathF.Min(ringMinDistanceSquared, boxDistanceSquared);
                        if (boxDistanceSquared >= bestDistanceSquared) continue;

                        var cell = (x * _ny + y) * _nz + z;
                        for (var i = _cellStart[cell]; i < _cellStart[cell + 1]; i++)
                        {
                            var t = _cellItems[i];
                            if (_visitStamp[t] == _visitGeneration) continue;
                            _visitStamp[t] = _visitGeneration;

                            var (a, b, c) = Corners(t);
                            var candidate = TriangleMath.ClosestPoint(point, a, b, c);
                            var distanceSquared = Vector3.DistanceSquared(point, candidate);
                            if (distanceSquared < bestDistanceSquared)
                            {
                                bestDistanceSquared = distanceSquared;
                                best = t;
                                closestPoint = candidate;
                            }
                        }
                    }
                }
            }

            if (best >= 0 && ringMinDistanceSquared >= bestDistanceSquared) break;
        }

        return best;
    }

    private (Vector3 A, Vector3 B, Vector3 C) Corners(int triangle)
    {
        var i = triangle * 3;
        return (_positions[_indices[i]], _positions[_indices[i + 1]], _positions[_indices[i + 2]]);
    }

    private void ForEachCell(int triangle, Action<int> action)
    {
        var (a, b, c) = Corners(triangle);
        var lo = Vector3.Min(a, Vector3.Min(b, c)) - _origin;
        var hi = Vector3.Max(a, Vector3.Max(b, c)) - _origin;
        int x0 = Cell(lo.X, _nx), x1 = Cell(hi.X, _nx);
        int y0 = Cell(lo.Y, _ny), y1 = Cell(hi.Y, _ny);
        int z0 = Cell(lo.Z, _nz), z1 = Cell(hi.Z, _nz);
        for (var x = x0; x <= x1; x++)
        for (var y = y0; y <= y1; y++)
        for (var z = z0; z <= z1; z++)
            action((x * _ny + y) * _nz + z);
    }

    private int Cell(float offset, int count) => Math.Clamp((int)(offset / _cellSize), 0, count - 1);

    private float BoxDistanceSquared(Vector3 p, int x, int y, int z)
    {
        var lo = _origin + new Vector3(x, y, z) * _cellSize;
        var hi = lo + new Vector3(_cellSize);
        var d = Vector3.Max(Vector3.Max(lo - p, p - hi), Vector3.Zero);
        return d.LengthSquared();
    }

    private static int Dimension(float extent, float cellSize) => Math.Max(1, (int)MathF.Ceiling(extent / cellSize));

    private static long CellCount(Vector3 extent, float cellSize) =>
        (long)Dimension(extent.X, cellSize) * Dimension(extent.Y, cellSize) * Dimension(extent.Z, cellSize);
}
