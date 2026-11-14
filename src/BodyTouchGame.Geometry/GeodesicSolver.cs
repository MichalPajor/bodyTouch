using System.Numerics;

namespace BodyTouchGame.Geometry;

/// <summary>Result of a surface distance query.</summary>
/// <param name="DistanceCm">Distance along the body surface in centimetres;
/// <see cref="float.PositiveInfinity"/> when the pin cannot be reached (disconnected surface).</param>
/// <param name="IsInsideTarget">True when the pin lies inside the target zone / point radius (distance 0).</param>
public readonly record struct GeodesicResult(float DistanceCm, bool IsInsideTarget)
{
    public bool IsReachable => float.IsFinite(DistanceCm);
}

/// <summary>
/// Surface distance between a pin and a target (zone or point) using multi-source Dijkstra
/// over the <see cref="BodyGraph"/> helper graph.
/// All working memory is allocated once in the constructor; distance queries do not allocate.
/// Not thread-safe: use one instance per thread.
/// </summary>
public sealed class GeodesicSolver
{
    private const int NoVertex = -1;

    private readonly BodyGraph _body;
    private readonly Vector3[] _positions;
    private readonly int[] _rowOffsets;
    private readonly int[] _neighbors;
    private readonly float[] _lengths;

    private readonly float[] _distance;
    private readonly int[] _previous;
    private readonly int[] _stamp;
    private readonly int[] _heap;
    private readonly int[] _heapPosition;
    private int _heapCount;
    private int _generation;

    // State of the most recent distance query, used by GetLastPath.
    private int _pathGeneration = -1;
    private bool _lastInside;
    private bool _lastDirect;
    private bool _lastIsPointTarget;
    private int _lastAnchor = NoVertex;
    private Vector3 _lastPin;
    private Vector3 _lastTarget;
    private float _lastRadius;

    public GeodesicSolver(BodyGraph body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _body = body;
        var graph = body.Graph;
        _positions = graph.PositionsArray;
        _rowOffsets = graph.RowOffsetsArray;
        _neighbors = graph.NeighborsArray;
        _lengths = graph.EdgeLengthsArray;

        var n = graph.VertexCount;
        _distance = new float[n];
        _previous = new int[n];
        _stamp = new int[n];
        _heap = new int[n];
        _heapPosition = new int[n];
    }

    public BodyGraph Body => _body;

    /// <summary>Distance from the pin to the nearest point of the zone (0 when the pin lies in the zone).</summary>
    public GeodesicResult DistanceToZone(SurfacePoint pin, ZoneRegion zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (!ReferenceEquals(zone.Body, _body))
            throw new ArgumentException("The zone was built for a different body graph.", nameof(zone));

        var pinPosition = _body.GetPosition(pin);
        BeginQuery();
        RememberQuery(pinPosition, isPointTarget: false, target: default, radius: 0);

        if (zone.ContainsTriangle(pin.TriangleIndex))
        {
            _lastInside = true;
            return new GeodesicResult(0, true);
        }

        foreach (var v in zone.Vertices) Seed(v, 0);

        var best = SearchToward(pin, pinPosition, out _lastAnchor);
        return new GeodesicResult(best, false);
    }

    /// <summary>
    /// Distance from the pin to a target point with a radius (custom points, manually chosen targets):
    /// max(0, surface distance between the points - radius).
    /// </summary>
    public GeodesicResult DistanceToPoint(SurfacePoint pin, SurfacePoint target, float radiusCm = 0)
    {
        if (!float.IsFinite(radiusCm) || radiusCm < 0)
            throw new ArgumentOutOfRangeException(nameof(radiusCm), radiusCm, "Radius must be a finite, non-negative number.");

        var pinPosition = _body.GetPosition(pin);
        var targetPosition = _body.GetPosition(target);
        BeginQuery();
        RememberQuery(pinPosition, isPointTarget: true, targetPosition, radiusCm);

        SeedFromPoint(target, targetPosition);
        var centreDistance = SearchToward(pin, pinPosition, out _lastAnchor);

        // Both points on the same triangle: the straight segment lies on the surface.
        if (pin.TriangleIndex == target.TriangleIndex)
        {
            var direct = Vector3.Distance(pinPosition, targetPosition);
            if (direct <= centreDistance)
            {
                centreDistance = direct;
                _lastDirect = true;
            }
        }

        if (centreDistance <= radiusCm)
        {
            _lastInside = true;
            return new GeodesicResult(0, true);
        }

        return new GeodesicResult(centreDistance - radiusCm, false);
    }

    /// <summary>
    /// Graph vertices within <paramref name="radiusCm"/> of the point along the surface, sorted ascending
    /// (zone of a custom point, spec section 7). Never empty: when no vertex is that close, the nearest
    /// anchor vertex is returned. Invalidates the path of the previous distance query.
    /// </summary>
    public int[] VerticesWithin(SurfacePoint centre, float radiusCm)
    {
        if (!float.IsFinite(radiusCm) || radiusCm < 0)
            throw new ArgumentOutOfRangeException(nameof(radiusCm), radiusCm, "Radius must be a finite, non-negative number.");

        var centrePosition = _body.GetPosition(centre);
        BeginQuery();
        _pathGeneration = -1;
        SeedFromPoint(centre, centrePosition);

        var nearest = _heapCount > 0 ? _heap[0] : NoVertex;
        var result = new List<int>();
        while (_heapCount > 0)
        {
            var u = PopMin();
            var du = _distance[u];
            if (du > radiusCm) break;
            result.Add(u);
            Relax(u, du);
        }

        if (result.Count == 0 && nearest != NoVertex) result.Add(nearest);
        result.Sort();
        return result.ToArray();
    }

    /// <summary>
    /// Path of the most recent distance query, from the pin towards the target, as 3D points
    /// smoothed with Chaikin's algorithm. For a point target with a radius, the path ends on the
    /// radius boundary. Returns a single point when the pin was inside the target and an empty
    /// array when the target was unreachable. Must be called before the next query.
    /// </summary>
    public Vector3[] GetLastPath(int smoothingIterations = 2)
    {
        if (_pathGeneration != _generation)
            throw new InvalidOperationException("No distance query to build a path for, or another query has run since.");

        if (_lastInside) return [_lastPin];
        if (_lastDirect) return Finish([_lastPin, _lastTarget], smoothingIterations);
        if (_lastAnchor == NoVertex) return [];

        var raw = new List<Vector3> { _lastPin };
        for (var v = _lastAnchor; v != NoVertex; v = _previous[v]) raw.Add(_positions[v]);
        if (_lastIsPointTarget) raw.Add(_lastTarget);
        return Finish(raw.ToArray(), smoothingIterations);
    }

    private Vector3[] Finish(Vector3[] raw, int smoothingIterations)
    {
        var smooth = Polyline.Chaikin(raw, smoothingIterations);
        return _lastIsPointTarget && _lastRadius > 0 ? Polyline.TrimEnd(smooth, _lastRadius) : smooth;
    }

    /// <summary>Runs Dijkstra from the seeded sources until the pin's anchors are settled.</summary>
    private float SearchToward(SurfacePoint pin, Vector3 pinPosition, out int bestAnchor)
    {
        Span<int> anchors = stackalloc int[BodyGraph.AnchorsPerTriangle];
        Span<float> offsets = stackalloc float[BodyGraph.AnchorsPerTriangle];
        var anchorCount = CollectAnchors(pin.TriangleIndex, pinPosition, anchors, offsets);

        var best = float.PositiveInfinity;
        bestAnchor = NoVertex;
        while (_heapCount > 0)
        {
            var u = PopMin();
            var du = _distance[u];
            if (du >= best) break;

            for (var i = 0; i < anchorCount; i++)
            {
                if (anchors[i] != u) continue;
                var candidate = du + offsets[i];
                if (candidate < best)
                {
                    best = candidate;
                    bestAnchor = u;
                }
            }

            Relax(u, du);
        }

        return best;
    }

    private void SeedFromPoint(SurfacePoint point, Vector3 position)
    {
        Span<int> anchors = stackalloc int[BodyGraph.AnchorsPerTriangle];
        Span<float> offsets = stackalloc float[BodyGraph.AnchorsPerTriangle];
        var count = CollectAnchors(point.TriangleIndex, position, anchors, offsets);
        for (var i = 0; i < count; i++) Seed(anchors[i], offsets[i]);
    }

    private int CollectAnchors(int triangle, Vector3 position, Span<int> anchors, Span<float> offsets)
    {
        var count = 0;
        foreach (var anchor in _body.GetAnchors(triangle))
        {
            if (anchor < 0) continue;
            anchors[count] = anchor;
            offsets[count] = Vector3.Distance(position, _positions[anchor]);
            count++;
        }

        return count;
    }

    private void RememberQuery(Vector3 pin, bool isPointTarget, Vector3 target, float radius)
    {
        _pathGeneration = _generation;
        _lastPin = pin;
        _lastTarget = target;
        _lastRadius = radius;
        _lastIsPointTarget = isPointTarget;
        _lastInside = false;
        _lastDirect = false;
        _lastAnchor = NoVertex;
    }

    private void BeginQuery()
    {
        if (++_generation == int.MaxValue)
        {
            Array.Clear(_stamp);
            _generation = 1;
        }

        _heapCount = 0;
    }

    private void Seed(int vertex, float distance)
    {
        if (_stamp[vertex] != _generation)
        {
            _stamp[vertex] = _generation;
            _distance[vertex] = distance;
            _previous[vertex] = NoVertex;
            Push(vertex);
        }
        else if (_heapPosition[vertex] >= 0 && distance < _distance[vertex])
        {
            _distance[vertex] = distance;
            _previous[vertex] = NoVertex;
            SiftUp(_heapPosition[vertex]);
        }
    }

    private void Relax(int u, float du)
    {
        for (var e = _rowOffsets[u]; e < _rowOffsets[u + 1]; e++)
        {
            var v = _neighbors[e];
            var candidate = du + _lengths[e];
            if (_stamp[v] != _generation)
            {
                _stamp[v] = _generation;
                _distance[v] = candidate;
                _previous[v] = u;
                Push(v);
            }
            else if (_heapPosition[v] >= 0 && candidate < _distance[v])
            {
                _distance[v] = candidate;
                _previous[v] = u;
                SiftUp(_heapPosition[v]);
            }
        }
    }

    // Indexed binary min-heap keyed by _distance. _heapPosition[v] is the slot of v, or -1 once settled.

    private void Push(int vertex)
    {
        var slot = _heapCount++;
        _heap[slot] = vertex;
        _heapPosition[vertex] = slot;
        SiftUp(slot);
    }

    private int PopMin()
    {
        var top = _heap[0];
        _heapPosition[top] = -1;
        var last = _heap[--_heapCount];
        if (_heapCount > 0)
        {
            _heap[0] = last;
            _heapPosition[last] = 0;
            SiftDown(0);
        }

        return top;
    }

    private void SiftUp(int slot)
    {
        var vertex = _heap[slot];
        var key = _distance[vertex];
        while (slot > 0)
        {
            var parentSlot = (slot - 1) >> 1;
            var parent = _heap[parentSlot];
            if (_distance[parent] <= key) break;
            _heap[slot] = parent;
            _heapPosition[parent] = slot;
            slot = parentSlot;
        }

        _heap[slot] = vertex;
        _heapPosition[vertex] = slot;
    }

    private void SiftDown(int slot)
    {
        var vertex = _heap[slot];
        var key = _distance[vertex];
        while (true)
        {
            var child = 2 * slot + 1;
            if (child >= _heapCount) break;
            if (child + 1 < _heapCount && _distance[_heap[child + 1]] < _distance[_heap[child]]) child++;
            var childVertex = _heap[child];
            if (_distance[childVertex] >= key) break;
            _heap[slot] = childVertex;
            _heapPosition[childVertex] = slot;
            slot = child;
        }

        _heap[slot] = vertex;
        _heapPosition[vertex] = slot;
    }
}
