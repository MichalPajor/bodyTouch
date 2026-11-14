using System.Numerics;

namespace BodyTouchGame.Geometry;

public static class TriangleMath
{
    /// <summary>Closest point to <paramref name="p"/> on triangle ABC (Ericson, Real-Time Collision Detection 5.1.5).</summary>
    public static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;

        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + d1 / (d1 - d3) * ab;

        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + d2 / (d2 - d6) * ac;

        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (d4 - d3) / (d4 - d3 + (d5 - d6)) * (c - b);

        var denominator = 1f / (va + vb + vc);
        return a + ab * (vb * denominator) + ac * (vc * denominator);
    }

    /// <summary>Barycentric (u, v) of point <paramref name="p"/> in triangle ABC: p = (1-u-v)A + uB + vC.</summary>
    public static (float U, float V) Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var v0 = b - a;
        var v1 = c - a;
        var v2 = p - a;
        var d00 = Vector3.Dot(v0, v0);
        var d01 = Vector3.Dot(v0, v1);
        var d11 = Vector3.Dot(v1, v1);
        var d20 = Vector3.Dot(v2, v0);
        var d21 = Vector3.Dot(v2, v1);
        var denominator = d00 * d11 - d01 * d01;
        if (denominator == 0) return (0, 0);
        var u = (d11 * d20 - d01 * d21) / denominator;
        var v = (d00 * d21 - d01 * d20) / denominator;
        return (u, v);
    }
}
