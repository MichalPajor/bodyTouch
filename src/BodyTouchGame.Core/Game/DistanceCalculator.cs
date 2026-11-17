using System.Numerics;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Core.Game;

/// <summary>Surface distance from the pin to the target and the path for the reveal animation.</summary>
/// <param name="DistanceCm">Centimetres; <see cref="float.PositiveInfinity"/> when unreachable.</param>
public sealed record DistanceResult(float DistanceCm, bool IsInside, IReadOnlyList<Vector3> Path);

public interface IDistanceCalculator
{
    DistanceResult Measure(SurfacePoint pin, Target target);
}

/// <summary>
/// <see cref="IDistanceCalculator"/> on the body graph (spec 8): zone targets measure to the zone, custom points to
/// the point minus its radius, manual points to the point. Zone regions are cached. Thread-safe (serialised).
/// </summary>
public sealed class GeodesicDistanceCalculator : IDistanceCalculator
{
    private readonly BodyGraph _body;
    private readonly ZoneCatalog _catalog;
    private readonly GeodesicSolver _solver;
    private readonly Dictionary<string, ZoneRegion> _regions = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public GeodesicDistanceCalculator(BodyGraph body, ZoneCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.TriangleCount != body.TriangleCount)
            throw new ArgumentException("The zone catalog and the body graph describe different meshes.", nameof(catalog));
        _body = body;
        _catalog = catalog;
        _solver = new GeodesicSolver(body);
    }

    public DistanceResult Measure(SurfacePoint pin, Target target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_lock)
        {
            var result = target switch
            {
                ZoneTarget zone => _solver.DistanceToZone(pin, Region(zone.ZoneId)),
                CustomPointTarget point => _solver.DistanceToPoint(pin, point.Point, point.RadiusCm),
                ManualPointTarget manual => _solver.DistanceToPoint(pin, manual.Point),
                _ => throw new ArgumentException($"Unsupported target {target.GetType().Name}.", nameof(target)),
            };
            return new DistanceResult(result.DistanceCm, result.IsInsideTarget, _solver.GetLastPath());
        }
    }

    /// <summary>Graph vertices within <paramref name="radiusCm"/> of the point along the surface (custom point zone,
    /// spec 7: computed once when the point is saved).</summary>
    public IReadOnlyList<int> VerticesWithin(SurfacePoint point, float radiusCm)
    {
        lock (_lock) return _solver.VerticesWithin(point, radiusCm);
    }

    /// <summary>Builds (and caches) the region of a zone ahead of time, e.g. when the target is drawn.</summary>
    public void Prepare(string zoneId)
    {
        lock (_lock) Region(zoneId);
    }

    private ZoneRegion Region(string zoneId)
    {
        if (_regions.TryGetValue(zoneId, out var region)) return region;
        var zone = _catalog.Get(zoneId);
        region = ZoneRegion.FromTriangleLabels(_body, _catalog.TriangleLabels(zone.Level), _catalog.IndexOf(zoneId));
        _regions[zoneId] = region;
        return region;
    }
}

/// <summary>Current time (injectable for tests).</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}
