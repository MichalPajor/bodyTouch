using System.Numerics;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Geometry.Tests;

internal sealed record AnalyticCase(string Name, string MeshKey, Vector3 From, Vector3 To, float ExpectedCm);

/// <summary>Point pairs on synthetic meshes with analytically known surface distances.</summary>
internal static class AnalyticCases
{
    public const float CubeSide = 40;
    public const float CylinderRadius = 10;
    public const float SphereRadius = 20;

    public static readonly IReadOnlyDictionary<string, Lazy<TestMesh>> Meshes = new Dictionary<string, Lazy<TestMesh>>
    {
        ["plane"] = new(() => TestMeshes.Plane(100, 100, 2)),
        ["plane-irregular"] = new(() => TestMeshes.Plane(100, 100, 2, jitter: 0.3f, seed: 7)),
        ["cube"] = new(() => TestMeshes.Cube(CubeSide, 20)),
        ["cylinder"] = new(() => TestMeshes.Cylinder(CylinderRadius, 60, 64, 30)),
        ["sphere"] = new(() => TestMeshes.Sphere(SphereRadius, 64, 32)),
    };

    public static readonly IReadOnlyList<AnalyticCase> All = Build();

    private static List<AnalyticCase> Build()
    {
        var cases = new List<AnalyticCase>();

        foreach (var plane in new[] { "plane", "plane-irregular" })
        {
            cases.Add(Flat(plane, "poziomo", new(10, 50, 0), new(90, 50, 0)));
            cases.Add(Flat(plane, "przekątna", new(10, 10, 0), new(90, 90, 0)));
            cases.Add(Flat(plane, "przekątna odwrotna", new(10, 90, 0), new(90, 10, 0)));
            cases.Add(Flat(plane, "skos 22,6°", new(20, 30, 0), new(80, 55, 0)));
            cases.Add(Flat(plane, "skos -23,2°", new(20, 70, 0), new(90, 40, 0)));
            cases.Add(Flat(plane, "prawie pionowo", new(50, 10, 0), new(60, 90, 0)));
            cases.Add(Flat(plane, "krótko 6 cm", new(47, 52, 0), new(53, 52, 0)));
        }

        const float s = CubeSide;
        cases.Add(new("ta sama ściana", "cube", new(5, 5, s), new(35, 30, s), MathF.Sqrt(30 * 30 + 25 * 25)));
        // Adjacent faces: unfold the front face (y = 0) onto the top face across the edge y = 0, z = side.
        cases.Add(new("sąsiednie ściany (góra–przód)", "cube", new(20, 10, s), new(25, 0, 15), MathF.Sqrt(5 * 5 + (10 + (s - 15)) * (10 + (s - 15)))));
        // Unfold the right face (x = side) onto the top face across the edge x = side, z = side.
        cases.Add(new("sąsiednie ściany (góra–prawa)", "cube", new(30, 30, s), new(s, 20, 10), MathF.Sqrt((s - 30 + (s - 10)) * (s - 30 + (s - 10)) + 10 * 10)));
        cases.Add(new("przeciwległe środki ścian", "cube", new(s / 2, s / 2, 0), new(s / 2, s / 2, s), 2 * s));

        cases.Add(Cyl("pół obwodu", 0, 30, MathF.PI, 30));
        cases.Add(Cyl("wzdłuż osi", 0, 5, 0, 55));
        cases.Add(Cyl("spirala 90°", 0, 10, MathF.PI / 2, 50));
        cases.Add(Cyl("spirala 135°", 0, 10, 3 * MathF.PI / 4, 30));

        cases.Add(Sph("równik 90°", 90, 0, 90, 90));
        cases.Add(Sph("ukośnie", 60, 0, 120, 135));
        cases.Add(Sph("krótko", 80, 10, 100, 30));
        cases.Add(Sph("prawie antypody", 70, 0, 110, 170));

        return cases;
    }

    private static AnalyticCase Flat(string mesh, string name, Vector3 a, Vector3 b) => new(name, mesh, a, b, Vector3.Distance(a, b));

    private static AnalyticCase Cyl(string name, float angleA, float zA, float angleB, float zB)
    {
        var r = CylinderRadius;
        var a = new Vector3(r * MathF.Cos(angleA), r * MathF.Sin(angleA), zA);
        var b = new Vector3(r * MathF.Cos(angleB), r * MathF.Sin(angleB), zB);
        var arc = r * MathF.Abs(angleB - angleA);
        return new(name, "cylinder", a, b, MathF.Sqrt(arc * arc + (zB - zA) * (zB - zA)));
    }

    private static AnalyticCase Sph(string name, float polarA, float azimuthA, float polarB, float azimuthB)
    {
        var a = SpherePoint(polarA, azimuthA);
        var b = SpherePoint(polarB, azimuthB);
        var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1, 1));
        return new(name, "sphere", a, b, SphereRadius * angle);
    }

    private static Vector3 SpherePoint(float polarDegrees, float azimuthDegrees)
    {
        var polar = polarDegrees * MathF.PI / 180;
        var azimuth = azimuthDegrees * MathF.PI / 180;
        return SphereRadius * new Vector3(MathF.Sin(polar) * MathF.Cos(azimuth), MathF.Sin(polar) * MathF.Sin(azimuth), -MathF.Cos(polar));
    }
}
