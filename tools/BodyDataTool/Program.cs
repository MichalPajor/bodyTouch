using System.Numerics;
using System.Text.Json;
using BodyDataTool;
using BodyTouchGame.Geometry;

// Body Touch Game data tool.
//   build     body.glb + Blender intermediates -> body_zones.json + body_graph.bin, then validate
//   validate  check the files shipped with the app
// Paths default to the repository layout; run from anywhere inside the repository.
//
//   dotnet run --project tools/BodyDataTool -- build
//   dotnet run --project tools/BodyDataTool -- validate

var command = args.Length > 0 ? args[0] : "build";
var repo = FindRepositoryRoot();
var paths = new
{
    Glb = Path.Combine(repo, "src/BodyTouchGame.App/Resources/Raw/wwwroot/models/body.glb"),
    Zones = Path.Combine(repo, "src/BodyTouchGame.App/Resources/Raw/wwwroot/models/body_zones.json"),
    Graph = Path.Combine(repo, "src/BodyTouchGame.App/Resources/Raw/body_graph.bin"),
    Faces = Path.Combine(repo, "tools/blender/build/body_faces.json"),
    Helper = Path.Combine(repo, "tools/blender/build/body_helper.obj"),
};

try
{
    switch (command)
    {
        case "build":
            Build();
            return Validate();
        case "validate":
            return Validate();
        default:
            Console.Error.WriteLine($"Unknown command '{command}'. Use 'build' or 'validate'.");
            return 2;
    }
}
catch (Exception e) when (e is InvalidDataException or IOException)
{
    Console.Error.WriteLine($"ERROR: {e.Message}");
    return 1;
}

void Build()
{
    var render = GlbReader.ReadMesh(paths.Glb, "Body");
    var helper = ObjReader.Read(paths.Helper);
    var faces = BlenderFacesFile.Read(paths.Faces);
    Console.WriteLine($"Render mesh: {render.Positions.Length} vertices, {render.TriangleCount} triangles");
    Console.WriteLine($"Helper mesh: {helper.Positions.Length} vertices, {helper.TriangleCount} triangles");

    var map = TriangleMatcher.Match(render, faces.Centroids, faces.TriangleCount);
    var triangleZones = faces.Labels.ToDictionary(
        level => level.Key,
        level => map.Select(blenderTriangle => level.Value[blenderTriangle]).ToArray());

    var zones = new BodyZonesFile { TriangleCount = render.TriangleCount, Zones = faces.Zones, TriangleZones = triangleZones };
    zones.Write(paths.Zones);

    var body = BodyGraphBuilder.Build(render.Positions, render.Indices, helper.Positions, helper.Indices);
    BodyGraphSerializer.WriteFile(body, paths.Graph);
    Console.WriteLine($"Written {Relative(paths.Zones)} ({new FileInfo(paths.Zones).Length / 1024} KiB) and {Relative(paths.Graph)} ({new FileInfo(paths.Graph).Length / 1024} KiB)");
}

int Validate()
{
    var zones = BodyZonesFile.Read(paths.Zones);
    var body = BodyGraphSerializer.ReadFile(paths.Graph);
    var glb = GlbReader.ReadMesh(paths.Glb, "Body");
    var result = BodyDataValidator.Validate(zones, body);

    // The graph must describe exactly the mesh shipped in body.glb.
    if (glb.TriangleCount != body.TriangleCount || !glb.Indices.AsSpan().SequenceEqual(body.MeshIndices) ||
        !glb.Positions.AsSpan().SequenceEqual(body.MeshPositions))
        result.Problems.Add("body_graph.bin does not match the 'Body' mesh in body.glb; run 'build' again.");

    Console.WriteLine(JsonSerializer.Serialize(result.Statistics, new JsonSerializerOptions { WriteIndented = true }));
    foreach (var problem in result.Problems) Console.Error.WriteLine($"PROBLEM: {problem}");
    Console.WriteLine(result.IsValid ? "VALID" : $"INVALID ({result.Problems.Count} problems)");
    return result.IsValid ? 0 : 1;
}

string Relative(string path) => Path.GetRelativePath(repo, path);

static string FindRepositoryRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "BodyTouchGame.sln"))) return dir.FullName;
    throw new IOException("Run the tool from inside the repository (BodyTouchGame.sln not found).");
}

/// <summary>Maps every .glb triangle to the Blender triangle with the same centroid.</summary>
internal static class TriangleMatcher
{
    private const float Tolerance = 0.01f; // cm; centroids are written with 4 decimals

    public static int[] Match(TriangleMesh render, float[] centroids, int blenderCount)
    {
        if (render.TriangleCount != blenderCount)
            throw new InvalidDataException($"glb has {render.TriangleCount} triangles, Blender export has {blenderCount}.");

        var grid = new Dictionary<(int, int, int), List<int>>();
        for (var t = 0; t < blenderCount; t++)
        {
            var key = Key(new Vector3(centroids[t * 3], centroids[t * 3 + 1], centroids[t * 3 + 2]));
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = [];
            list.Add(t);
        }

        var map = new int[render.TriangleCount];
        var used = new bool[blenderCount];
        for (var t = 0; t < render.TriangleCount; t++)
        {
            var c = render.Centroid(t);
            var (kx, ky, kz) = Key(c);
            var best = -1;
            var bestDistance = Tolerance;
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    for (var dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue((kx + dx, ky + dy, kz + dz), out var list)) continue;
                        foreach (var b in list)
                        {
                            var d = Vector3.Distance(c, new Vector3(centroids[b * 3], centroids[b * 3 + 1], centroids[b * 3 + 2]));
                            if (d <= bestDistance && !used[b]) { bestDistance = d; best = b; }
                        }
                    }

            if (best < 0) throw new InvalidDataException($"glb triangle {t} has no matching Blender triangle (centroid {c}).");
            used[best] = true;
            map[t] = best;
        }

        return map;
    }

    private static (int, int, int) Key(Vector3 p) =>
        ((int)MathF.Floor(p.X / Tolerance), (int)MathF.Floor(p.Y / Tolerance), (int)MathF.Floor(p.Z / Tolerance));
}
