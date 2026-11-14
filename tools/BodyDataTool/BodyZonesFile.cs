using System.Text.Json;
using System.Text.Json.Serialization;

namespace BodyDataTool;

public sealed record ZoneDefinition(
    string Id,
    string Level,
    string? ParentId,
    string NamePl,
    string Side,
    bool Drawable = true);

/// <summary>body_zones.json (spec 4.3): zone list and the zone of every render triangle per level.</summary>
public sealed class BodyZonesFile
{
    public static readonly string[] LevelOrder = ["Easy", "Medium", "Hard", "Hardcore"];

    public int Version { get; init; } = 1;

    /// <summary>Number of triangles of the 'Body' mesh in body.glb (consistency check for the app).</summary>
    public int TriangleCount { get; init; }

    public required List<ZoneDefinition> Zones { get; init; }

    /// <summary>Level -> zone index (into <see cref="Zones"/>) for every triangle, in .glb index order.</summary>
    public required Dictionary<string, int[]> TriangleZones { get; init; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static BodyZonesFile Read(string path) =>
        JsonSerializer.Deserialize<BodyZonesFile>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"{path} is empty.");

    /// <summary>Writes one zone per line and each triangle array on a single line (readable diffs, small file).</summary>
    public void Write(string path)
    {
        using var stream = File.Create(path);
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        writer.WriteLine("{");
        writer.WriteLine($"  \"version\": {Version},");
        writer.WriteLine($"  \"triangleCount\": {TriangleCount},");
        writer.WriteLine("  \"zones\": [");
        for (var i = 0; i < Zones.Count; i++)
            writer.WriteLine($"    {JsonSerializer.Serialize(Zones[i], JsonOptions)}{(i < Zones.Count - 1 ? "," : "")}");
        writer.WriteLine("  ],");
        writer.WriteLine("  \"triangleZones\": {");
        var levels = LevelOrder.Where(TriangleZones.ContainsKey).ToList();
        for (var i = 0; i < levels.Count; i++)
            writer.WriteLine($"    \"{levels[i]}\": [{string.Join(",", TriangleZones[levels[i]])}]{(i < levels.Count - 1 ? "," : "")}");
        writer.WriteLine("  }");
        writer.WriteLine("}");
    }
}

/// <summary>Intermediate file written by tools/blender/export_assets.py.</summary>
public sealed class BlenderFacesFile
{
    public int Version { get; init; }
    public required List<ZoneDefinition> Zones { get; init; }
    public int TriangleCount { get; init; }
    public required float[] Centroids { get; init; }
    public required Dictionary<string, int[]> Labels { get; init; }

    public static BlenderFacesFile Read(string path) =>
        JsonSerializer.Deserialize<BlenderFacesFile>(File.ReadAllText(path), BodyZonesFile.JsonOptions)
        ?? throw new InvalidDataException($"{path} is empty.");
}
