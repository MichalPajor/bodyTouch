using System.Numerics;

namespace BodyTouchGame.Geometry;

public static class Polyline
{
    public static float Length(ReadOnlySpan<Vector3> points)
    {
        var length = 0f;
        for (var i = 1; i < points.Length; i++) length += Vector3.Distance(points[i - 1], points[i]);
        return length;
    }

    /// <summary>
    /// Chaikin corner cutting for an open polyline. Endpoints are kept; every segment is replaced by
    /// points at 1/4 and 3/4 of its length. Consecutive duplicate points are removed first.
    /// </summary>
    public static Vector3[] Chaikin(ReadOnlySpan<Vector3> points, int iterations)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(iterations);
        var current = RemoveDuplicates(points);
        for (var iteration = 0; iteration < iterations && current.Count >= 3; iteration++)
        {
            var next = new List<Vector3>(current.Count * 2) { current[0] };
            for (var i = 0; i < current.Count - 1; i++)
            {
                var p = current[i];
                var q = current[i + 1];
                next.Add(Vector3.Lerp(p, q, 0.25f));
                next.Add(Vector3.Lerp(p, q, 0.75f));
            }

            next.Add(current[^1]);
            current = next;
        }

        return current.ToArray();
    }

    /// <summary>Removes <paramref name="length"/> centimetres of arc length from the end of the polyline.
    /// The first point is always kept.</summary>
    public static Vector3[] TrimEnd(ReadOnlySpan<Vector3> points, float length)
    {
        if (points.Length == 0) return [];
        if (length <= 0) return points.ToArray();

        var remaining = length;
        for (var i = points.Length - 1; i > 0; i--)
        {
            var segment = Vector3.Distance(points[i - 1], points[i]);
            if (segment > remaining)
            {
                var result = new Vector3[i + 1];
                points[..i].CopyTo(result);
                result[i] = Vector3.Lerp(points[i], points[i - 1], remaining / segment);
                return result;
            }

            remaining -= segment;
        }

        return [points[0]];
    }

    private static List<Vector3> RemoveDuplicates(ReadOnlySpan<Vector3> points)
    {
        var result = new List<Vector3>(points.Length);
        foreach (var p in points)
            if (result.Count == 0 || Vector3.DistanceSquared(result[^1], p) > 1e-12f) result.Add(p);
        return result;
    }
}
