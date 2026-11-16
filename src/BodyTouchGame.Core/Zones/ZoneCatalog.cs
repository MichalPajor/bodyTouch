using System.Text.Json;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Serialization;

namespace BodyTouchGame.Core.Zones;

public enum ZoneSide
{
    Left,
    Right,
    Center,
}

/// <summary>A built-in zone (body_zones.json, spec 4.3).</summary>
public sealed record ZoneDefinition(string Id, ZoneLevel Level, string? ParentId, string NamePl, ZoneSide Side, bool Drawable = true);

/// <summary>
/// Built-in zones and the zone of every render triangle per level, loaded from body_zones.json
/// (format: docs/formaty/body_zones.md).
/// </summary>
public sealed class ZoneCatalog
{
    private readonly Dictionary<string, ZoneDefinition> _byId;
    private readonly Dictionary<string, int> _indexById;
    private readonly Dictionary<ZoneLevel, int[]> _triangleZones;
    private readonly Dictionary<string, List<ZoneDefinition>> _children;

    public ZoneCatalog(IReadOnlyList<ZoneDefinition> zones, IReadOnlyDictionary<ZoneLevel, int[]> triangleZones, int triangleCount)
    {
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(triangleZones);

        _byId = new Dictionary<string, ZoneDefinition>(StringComparer.Ordinal);
        _indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < zones.Count; i++)
        {
            if (!_byId.TryAdd(zones[i].Id, zones[i])) throw new ArgumentException($"Duplicate zone id '{zones[i].Id}'.", nameof(zones));
            _indexById[zones[i].Id] = i;
        }

        _children = new Dictionary<string, List<ZoneDefinition>>(StringComparer.Ordinal);
        foreach (var zone in zones)
        {
            if (zone.ParentId is null) continue;
            if (!_byId.ContainsKey(zone.ParentId)) throw new ArgumentException($"Zone '{zone.Id}' has unknown parent '{zone.ParentId}'.", nameof(zones));
            if (!_children.TryGetValue(zone.ParentId, out var list)) _children[zone.ParentId] = list = [];
            list.Add(zone);
        }

        _triangleZones = new Dictionary<ZoneLevel, int[]>();
        foreach (var (level, labels) in triangleZones)
        {
            if (labels.Length != triangleCount)
                throw new ArgumentException($"triangleZones[{level}] has {labels.Length} entries, expected {triangleCount}.", nameof(triangleZones));
            foreach (var label in labels)
            {
                if ((uint)label >= (uint)zones.Count || zones[label].Level != level)
                    throw new ArgumentException($"triangleZones[{level}] contains an invalid zone index {label}.", nameof(triangleZones));
            }

            _triangleZones[level] = labels;
        }

        Zones = zones;
        TriangleCount = triangleCount;
        Levels = _triangleZones.Keys.Order().ToArray();
    }

    public IReadOnlyList<ZoneDefinition> Zones { get; }
    public int TriangleCount { get; }

    /// <summary>Levels that have triangle data (Easy and Medium until stage 8).</summary>
    public IReadOnlyList<ZoneLevel> Levels { get; }

    public bool HasLevel(ZoneLevel level) => _triangleZones.ContainsKey(level);

    public ZoneDefinition Get(string id) =>
        _byId.TryGetValue(id, out var zone) ? zone : throw new KeyNotFoundException($"Unknown zone '{id}'.");

    public bool TryGet(string id, out ZoneDefinition zone) => _byId.TryGetValue(id, out zone!);

    public int IndexOf(string id) => _indexById.TryGetValue(id, out var index) ? index : -1;

    /// <summary>Zone of a render triangle on a level, or null when the level has no data.</summary>
    public ZoneDefinition? ZoneAt(int triangle, ZoneLevel level)
    {
        if (!_triangleZones.TryGetValue(level, out var labels)) return null;
        if ((uint)triangle >= (uint)labels.Length) throw new ArgumentOutOfRangeException(nameof(triangle));
        return Zones[labels[triangle]];
    }

    /// <summary>Zones of a triangle on every level with data, coarsest first.</summary>
    public IEnumerable<ZoneDefinition> ZonesAt(int triangle)
    {
        foreach (var level in Levels) yield return ZoneAt(triangle, level)!;
    }

    public ReadOnlySpan<int> TriangleLabels(ZoneLevel level) =>
        _triangleZones.TryGetValue(level, out var labels) ? labels : throw new KeyNotFoundException($"No triangle data for level {level}.");

    public IEnumerable<ZoneDefinition> Ancestors(string id)
    {
        for (var zone = Get(id); zone.ParentId is not null;)
        {
            zone = Get(zone.ParentId);
            yield return zone;
        }
    }

    public IEnumerable<ZoneDefinition> Descendants(string id)
    {
        if (!_children.TryGetValue(id, out var children)) yield break;
        foreach (var child in children)
        {
            yield return child;
            foreach (var grandchild in Descendants(child.Id)) yield return grandchild;
        }
    }

    public static ZoneCatalog Load(Stream json)
    {
        var dto = JsonSerializer.Deserialize(json, CoreJsonContext.Default.BodyZonesFileDto)
                  ?? throw new InvalidDataException("body_zones.json is empty.");
        return FromDto(dto);
    }

    public static async Task<ZoneCatalog> LoadAsync(Stream json, CancellationToken cancellationToken = default)
    {
        var dto = await JsonSerializer.DeserializeAsync(json, CoreJsonContext.Default.BodyZonesFileDto, cancellationToken).ConfigureAwait(false)
                  ?? throw new InvalidDataException("body_zones.json is empty.");
        return FromDto(dto);
    }

    private static ZoneCatalog FromDto(BodyZonesFileDto dto)
    {
        try
        {
            var zones = dto.Zones.Select(z => new ZoneDefinition(z.Id, z.Level, z.ParentId, z.NamePl, z.Side, z.Drawable)).ToList();
            var triangles = dto.TriangleZones.ToDictionary(kv => Enum.Parse<ZoneLevel>(kv.Key), kv => kv.Value);
            return new ZoneCatalog(zones, triangles, dto.TriangleCount);
        }
        catch (Exception e) when (e is ArgumentException or KeyNotFoundException)
        {
            throw new InvalidDataException($"body_zones.json is invalid: {e.Message}", e);
        }
    }
}
