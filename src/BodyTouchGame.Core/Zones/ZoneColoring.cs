using BodyTouchGame.Core.Config;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Core.Zones;

/// <summary>
/// Colour slots for zones so that neighbouring zones never share a colour (zone editor, zone set preview – the
/// borders must be visible at a glance). Zones are neighbours when they share a mesh vertex; slots come from a
/// greedy colouring of that graph, largest degree first.
/// </summary>
public static class ZoneColoring
{
    /// <summary>Colour slot (0, 1, 2, …) of every zone of <paramref name="level"/> that has triangles.</summary>
    public static IReadOnlyDictionary<string, int> Slots(ZoneCatalog catalog, BodyGraph body, ZoneLevel level)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(body);
        var neighbours = Neighbours(catalog, body, level);
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var zone in neighbours.Keys.OrderByDescending(z => neighbours[z].Count).ThenBy(z => z, StringComparer.Ordinal))
        {
            var used = neighbours[zone].Where(slots.ContainsKey).Select(n => slots[n]).ToHashSet();
            var slot = 0;
            while (used.Contains(slot)) slot++;
            slots[zone] = slot;
        }

        return slots;
    }

    /// <summary>Zones of the level that share at least one mesh vertex with each zone.</summary>
    public static IReadOnlyDictionary<string, HashSet<string>> Neighbours(ZoneCatalog catalog, BodyGraph body, ZoneLevel level)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(body);
        if (catalog.TriangleCount != body.TriangleCount)
            throw new ArgumentException("The zone catalog and the body graph describe different meshes.", nameof(catalog));
        var labels = catalog.TriangleLabels(level);
        var indices = body.MeshIndices;
        var zonesAtVertex = new Dictionary<int, HashSet<int>>();
        for (var t = 0; t < labels.Length; t++)
        {
            for (var k = 0; k < 3; k++)
            {
                var v = indices[t * 3 + k];
                if (!zonesAtVertex.TryGetValue(v, out var set)) zonesAtVertex[v] = set = [];
                set.Add(labels[t]);
            }
        }

        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var set in zonesAtVertex.Values)
        {
            foreach (var a in set)
            {
                var id = catalog.Zones[a].Id;
                if (!result.TryGetValue(id, out var n)) result[id] = n = new HashSet<string>(StringComparer.Ordinal);
                foreach (var b in set)
                {
                    if (a != b) n.Add(catalog.Zones[b].Id);
                }
            }
        }

        return result;
    }
}
