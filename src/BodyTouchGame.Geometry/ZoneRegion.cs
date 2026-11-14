namespace BodyTouchGame.Geometry;

/// <summary>
/// A zone on the body as used by the distance search: the render triangles it covers
/// (to tell whether a pin is inside) and the graph vertices it contains (search sources).
/// Build once per zone and reuse.
/// </summary>
public sealed class ZoneRegion
{
    private readonly ulong[] _triangleMask;
    private readonly int[] _vertices;

    private ZoneRegion(BodyGraph body, ulong[] triangleMask, int triangleCount, int[] vertices)
    {
        Body = body;
        _triangleMask = triangleMask;
        TriangleCount = triangleCount;
        _vertices = vertices;
    }

    public BodyGraph Body { get; }

    /// <summary>Number of render triangles in the zone.</summary>
    public int TriangleCount { get; }

    /// <summary>Graph vertices of the zone, sorted ascending.</summary>
    public ReadOnlySpan<int> Vertices => _vertices;

    public bool ContainsTriangle(int triangle) =>
        (uint)triangle < (uint)Body.TriangleCount && (_triangleMask[triangle >> 6] & (1UL << (triangle & 63))) != 0;

    /// <summary>Zone made of the given render triangles.</summary>
    public static ZoneRegion FromTriangles(BodyGraph body, IEnumerable<int> triangles)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(triangles);

        var mask = new ulong[(body.TriangleCount + 63) / 64];
        var count = 0;
        foreach (var t in triangles)
        {
            if ((uint)t >= (uint)body.TriangleCount)
                throw new ArgumentOutOfRangeException(nameof(triangles), t, "Triangle index out of range.");
            ref var word = ref mask[t >> 6];
            var bit = 1UL << (t & 63);
            if ((word & bit) != 0) continue;
            word |= bit;
            count++;
        }

        if (count == 0) throw new ArgumentException("A zone must contain at least one triangle.", nameof(triangles));
        return Create(body, mask, count);
    }

    /// <summary>Zone made of all render triangles whose label equals <paramref name="label"/>
    /// (e.g. one level of <c>triangleZones</c> from body_zones.json).</summary>
    public static ZoneRegion FromTriangleLabels(BodyGraph body, ReadOnlySpan<int> labels, int label)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (labels.Length != body.TriangleCount)
            throw new ArgumentException("There must be one label per render triangle.", nameof(labels));

        var mask = new ulong[(body.TriangleCount + 63) / 64];
        var count = 0;
        for (var t = 0; t < labels.Length; t++)
        {
            if (labels[t] != label) continue;
            mask[t >> 6] |= 1UL << (t & 63);
            count++;
        }

        if (count == 0) throw new ArgumentException($"No triangle has label {label}.", nameof(label));
        return Create(body, mask, count);
    }

    private static ZoneRegion Create(BodyGraph body, ulong[] mask, int triangleCount)
    {
        bool Contains(int t) => (mask[t >> 6] & (1UL << (t & 63))) != 0;

        // A graph vertex belongs to the zone when its nearest render triangle does.
        var vertices = new List<int>();
        var vertexTriangles = body.VertexTriangles;
        for (var v = 0; v < vertexTriangles.Length; v++)
            if (Contains(vertexTriangles[v])) vertices.Add(v);

        // Very small zones (e.g. a phalanx) may contain no graph vertex: fall back to their anchors.
        if (vertices.Count == 0)
        {
            var set = new SortedSet<int>();
            for (var t = 0; t < body.TriangleCount; t++)
            {
                if (!Contains(t)) continue;
                foreach (var anchor in body.GetAnchors(t))
                    if (anchor >= 0) set.Add(anchor);
            }

            vertices.AddRange(set);
        }

        return new ZoneRegion(body, mask, triangleCount, vertices.ToArray());
    }
}
