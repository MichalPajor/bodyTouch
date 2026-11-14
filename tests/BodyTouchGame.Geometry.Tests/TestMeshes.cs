using System.Numerics;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Geometry.Tests;

internal sealed record TestMesh(Vector3[] Positions, int[] Indices)
{
    public int TriangleCount => Indices.Length / 3;

    public Vector3 Centroid(int triangle) =>
        (Positions[Indices[triangle * 3]] + Positions[Indices[triangle * 3 + 1]] + Positions[Indices[triangle * 3 + 2]]) / 3f;

    /// <summary>Surface point on the triangle closest to <paramref name="position"/> (brute force).</summary>
    public SurfacePoint Locate(Vector3 position)
    {
        var bestTriangle = -1;
        var bestDistance = float.MaxValue;
        var bestPoint = Vector3.Zero;
        for (var t = 0; t < TriangleCount; t++)
        {
            var (a, b, c) = Corners(t);
            var closest = TriangleMath.ClosestPoint(position, a, b, c);
            var distance = Vector3.DistanceSquared(position, closest);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestTriangle = t;
                bestPoint = closest;
            }
        }

        var (pa, pb, pc) = Corners(bestTriangle);
        var (u, v) = TriangleMath.Barycentric(bestPoint, pa, pb, pc);
        return new SurfacePoint(bestTriangle, u, v);
    }

    public (Vector3 A, Vector3 B, Vector3 C) Corners(int t) =>
        (Positions[Indices[t * 3]], Positions[Indices[t * 3 + 1]], Positions[Indices[t * 3 + 2]]);

    public static TestMesh Combine(TestMesh first, TestMesh second)
    {
        var positions = first.Positions.Concat(second.Positions).ToArray();
        var offset = first.Positions.Length;
        var indices = first.Indices.Concat(second.Indices.Select(i => i + offset)).ToArray();
        return new TestMesh(positions, indices);
    }
}

/// <summary>Synthetic meshes with analytically known surface distances. Units: centimetres.</summary>
internal static class TestMeshes
{
    /// <summary>Flat rectangle in the XY plane (z = 0) from (0,0) to (width,height). Cells are split
    /// with alternating diagonals; <paramref name="jitter"/> moves interior vertices randomly by up to
    /// jitter * step to imitate an irregular, artist-made mesh.</summary>
    public static TestMesh Plane(float width, float height, float step, float jitter = 0, int seed = 1)
    {
        var nx = (int)MathF.Round(width / step);
        var ny = (int)MathF.Round(height / step);
        var random = new Random(seed);
        var positions = new Vector3[(nx + 1) * (ny + 1)];
        for (var j = 0; j <= ny; j++)
        for (var i = 0; i <= nx; i++)
        {
            var p = new Vector3(i * width / nx, j * height / ny, 0);
            if (jitter > 0 && i > 0 && j > 0 && i < nx && j < ny)
                p += new Vector3((float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1), 0) * (jitter * step);
            positions[j * (nx + 1) + i] = p;
        }

        return new TestMesh(positions, GridIndices(nx, ny, wrapX: false));
    }

    /// <summary>Closed cube surface [0,side]^3 with each face split into divisions x divisions cells.</summary>
    public static TestMesh Cube(float side, int divisions)
    {
        var faces = new (Vector3 Origin, Vector3 U, Vector3 V)[]
        {
            (new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)), // bottom z=0
            (new(0, 0, 1), new(1, 0, 0), new(0, 1, 0)), // top z=side
            (new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)), // front y=0
            (new(0, 1, 0), new(1, 0, 0), new(0, 0, 1)), // back y=side
            (new(0, 0, 0), new(0, 1, 0), new(0, 0, 1)), // left x=0
            (new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)), // right x=side
        };

        var positions = new List<Vector3>();
        var lookup = new Dictionary<(int, int, int), int>();
        var indices = new List<int>();
        int Vertex(Vector3 unit)
        {
            var key = ((int)MathF.Round(unit.X * divisions), (int)MathF.Round(unit.Y * divisions), (int)MathF.Round(unit.Z * divisions));
            if (lookup.TryGetValue(key, out var index)) return index;
            lookup[key] = positions.Count;
            positions.Add(new Vector3(key.Item1, key.Item2, key.Item3) * (side / divisions));
            return positions.Count - 1;
        }

        foreach (var (origin, u, v) in faces)
        {
            var grid = new int[divisions + 1, divisions + 1];
            for (var i = 0; i <= divisions; i++)
            for (var j = 0; j <= divisions; j++)
                grid[i, j] = Vertex(origin + u * ((float)i / divisions) + v * ((float)j / divisions));

            for (var i = 0; i < divisions; i++)
            for (var j = 0; j < divisions; j++)
                AddCell(indices, grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1], (i + j) % 2 == 0);
        }

        return new TestMesh(positions.ToArray(), indices.ToArray());
    }

    /// <summary>Open cylinder (side surface only) around the Z axis, z from 0 to height.</summary>
    public static TestMesh Cylinder(float radius, float height, int segments, int rings)
    {
        var positions = new Vector3[segments * (rings + 1)];
        for (var j = 0; j <= rings; j++)
        for (var i = 0; i < segments; i++)
        {
            var angle = 2 * MathF.PI * i / segments;
            positions[j * segments + i] = new Vector3(radius * MathF.Cos(angle), radius * MathF.Sin(angle), height * j / rings);
        }

        return new TestMesh(positions, GridIndices(segments, rings, wrapX: true));
    }

    /// <summary>UV sphere centred at the origin.</summary>
    public static TestMesh Sphere(float radius, int segments, int rings)
    {
        var positions = new List<Vector3> { new(0, 0, -radius) };
        for (var j = 1; j < rings; j++)
        {
            var polar = MathF.PI * j / rings;
            for (var i = 0; i < segments; i++)
            {
                var azimuth = 2 * MathF.PI * i / segments;
                positions.Add(radius * new Vector3(MathF.Sin(polar) * MathF.Cos(azimuth), MathF.Sin(polar) * MathF.Sin(azimuth), -MathF.Cos(polar)));
            }
        }

        positions.Add(new Vector3(0, 0, radius));
        var top = positions.Count - 1;
        int Ring(int j, int i) => 1 + (j - 1) * segments + (i % segments);

        var indices = new List<int>();
        for (var i = 0; i < segments; i++)
        {
            indices.AddRange([0, Ring(1, i + 1), Ring(1, i)]);
            indices.AddRange([top, Ring(rings - 1, i), Ring(rings - 1, i + 1)]);
        }

        for (var j = 1; j < rings - 1; j++)
        for (var i = 0; i < segments; i++)
            AddCell(indices, Ring(j, i), Ring(j, i + 1), Ring(j + 1, i + 1), Ring(j + 1, i), (i + j) % 2 == 0);

        return new TestMesh(positions.ToArray(), indices.ToArray());
    }

    /// <summary>Torus around the Z axis: a closed surface without poles, used for performance tests.</summary>
    public static TestMesh Torus(float majorRadius, float minorRadius, int majorSegments, int minorSegments)
    {
        var positions = new Vector3[majorSegments * minorSegments];
        for (var j = 0; j < minorSegments; j++)
        for (var i = 0; i < majorSegments; i++)
        {
            var u = 2 * MathF.PI * i / majorSegments;
            var v = 2 * MathF.PI * j / minorSegments;
            var r = majorRadius + minorRadius * MathF.Cos(v);
            positions[j * majorSegments + i] = new Vector3(r * MathF.Cos(u), r * MathF.Sin(u), minorRadius * MathF.Sin(v));
        }

        var indices = new List<int>();
        for (var j = 0; j < minorSegments; j++)
        for (var i = 0; i < majorSegments; i++)
        {
            int a = j * majorSegments + i;
            int b = j * majorSegments + (i + 1) % majorSegments;
            int c = (j + 1) % minorSegments * majorSegments + (i + 1) % majorSegments;
            int d = (j + 1) % minorSegments * majorSegments + i;
            AddCell(indices, a, b, c, d, (i + j) % 2 == 0);
        }

        return new TestMesh(positions, indices.ToArray());
    }

    private static int[] GridIndices(int nx, int ny, bool wrapX)
    {
        var columns = wrapX ? nx : nx + 1;
        var indices = new List<int>(nx * ny * 6);
        for (var j = 0; j < ny; j++)
        for (var i = 0; i < nx; i++)
        {
            var i1 = wrapX ? (i + 1) % nx : i + 1;
            AddCell(indices, j * columns + i, j * columns + i1, (j + 1) * columns + i1, (j + 1) * columns + i, (i + j) % 2 == 0);
        }

        return indices.ToArray();
    }

    /// <summary>Quad a-b-c-d (counter-clockwise) split along a-c or b-d.</summary>
    private static void AddCell(List<int> indices, int a, int b, int c, int d, bool splitAc)
    {
        if (splitAc) indices.AddRange([a, b, c, a, c, d]);
        else indices.AddRange([a, b, d, b, c, d]);
    }
}
