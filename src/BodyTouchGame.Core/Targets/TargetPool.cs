using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Zones;

namespace BodyTouchGame.Core.Targets;

/// <summary>Source of randomness (injectable for deterministic tests).</summary>
public interface IRandomSource
{
    /// <summary>Uniform integer in [0, maxExclusive).</summary>
    int Next(int maxExclusive);
}

public sealed class SystemRandomSource(Random? random = null) : IRandomSource
{
    private readonly Random _random = random ?? Random.Shared;

    public int Next(int maxExclusive) => _random.Next(maxExclusive);
}

/// <summary>
/// Builds the list of targets that may be drawn for a model (spec 5, 7):
/// <list type="bullet">
/// <item>built-in zones whose effective level (built-in level with <see cref="LevelOverride"/>s applied) is selected,
/// plus individually selected zones; zones with <c>drawable = false</c> never;</item>
/// <item>custom points belonging to a selected level or selected individually (also inside non-drawable zones, D12);</item>
/// <item>minus everything excluded by the model's profile.</item>
/// </list>
/// An excluded zone blocks itself and every zone lying inside it (its descendants): excluding "head" blocks "face",
/// excluding "face" does not block "head" (owner's decision after stage 4). A custom point is excluded when one of
/// the zones containing its centre is excluded.
/// </summary>
public sealed class TargetPoolBuilder(ZoneCatalog catalog)
{
    public IReadOnlyList<Target> Build(
        ZoneSetSelection selection,
        IReadOnlyCollection<string> excludedZoneIds,
        IReadOnlyList<LevelOverride>? overrides = null,
        IReadOnlyList<CustomPoint>? customPoints = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(excludedZoneIds);

        var levels = selection.Levels.ToHashSet();
        var explicitZones = selection.ZoneIds.ToHashSet(StringComparer.Ordinal);
        var blocked = ExpandExclusions(excludedZoneIds);
        var overrideMap = (overrides ?? [])
            .Where(o => catalog.TryGet(o.ZoneId, out _))
            .GroupBy(o => o.ZoneId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var pool = new List<Target>();
        foreach (var zone in catalog.Zones)
        {
            if (!zone.Drawable || blocked.Contains(zone.Id)) continue;
            var effective = EffectiveLevels(zone, overrideMap);
            if (effective.Overlaps(levels) || explicitZones.Contains(zone.Id)) pool.Add(new ZoneTarget(zone.Id));
        }

        var excluded = excludedZoneIds.ToHashSet(StringComparer.Ordinal);
        var explicitPoints = selection.CustomPointIds.ToHashSet();
        foreach (var point in (customPoints ?? []).OrderBy(p => p.Id))
        {
            if (!point.Levels.Any(levels.Contains) && !explicitPoints.Contains(point.Id)) continue;
            if (catalog.ZonesAt(point.Point.TriangleIndex).Any(z => excluded.Contains(z.Id))) continue;
            pool.Add(new CustomPointTarget(point.Id, point.Name, point.Point, point.RadiusCm));
        }

        return pool;
    }

    /// <summary>Built-in level of the zone, removed or extended by user overrides.</summary>
    public static HashSet<ZoneLevel> EffectiveLevels(ZoneDefinition zone, IReadOnlyDictionary<string, List<LevelOverride>> overrides)
    {
        var levels = new HashSet<ZoneLevel> { zone.Level };
        if (overrides.TryGetValue(zone.Id, out var list))
        {
            foreach (var o in list)
            {
                if (o.IsIncluded) levels.Add(o.Level);
                else levels.Remove(o.Level);
            }
        }

        return levels;
    }

    private HashSet<string> ExpandExclusions(IEnumerable<string> excluded)
    {
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in excluded)
        {
            if (!catalog.TryGet(id, out _)) continue;           // unknown ids (e.g. from an older model) are ignored
            blocked.Add(id);
            foreach (var d in catalog.Descendants(id)) blocked.Add(d.Id);
        }

        return blocked;
    }
}

public static class TargetDrawer
{
    /// <summary>
    /// Draws a target uniformly from the pool, never the guesser's previous target (if anything else is available).
    /// Returns null for an empty pool.
    /// </summary>
    public static Target? Draw(IReadOnlyList<Target> pool, Target? previousForGuesser, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(random);
        if (pool.Count == 0) return null;
        var candidates = previousForGuesser is not null && pool.Count > 1
            ? pool.Where(t => !t.Equals(previousForGuesser)).ToList()
            : pool;
        if (candidates.Count == 0) candidates = pool;
        return candidates[random.Next(candidates.Count)];
    }
}
