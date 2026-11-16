using BodyTouchGame.Core.Config;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Core.Zones;

/// <summary>
/// A user-defined target point (spec 7): a named point on the body with a radius measured along the surface,
/// belonging to chosen levels. <see cref="VertexIds"/> = graph vertices within the radius, computed once on save.
/// </summary>
public sealed record CustomPoint(
    long Id,
    string Name,
    SurfacePoint Point,
    float RadiusCm,
    IReadOnlyList<ZoneLevel> Levels,
    IReadOnlyList<int> VertexIds)
{
    public const float MinRadiusCm = 1;
    public const float MaxRadiusCm = 10;
    public const float DefaultRadiusCm = 2;

    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) problems.Add("Name is required.");
        if (!float.IsFinite(RadiusCm) || RadiusCm is < MinRadiusCm or > MaxRadiusCm)
            problems.Add($"RadiusCm must be {MinRadiusCm}..{MaxRadiusCm}.");
        if (Point.TriangleIndex < 0) problems.Add("Point triangle index is invalid.");
        return problems;
    }
}

/// <summary>
/// User change of level membership of a built-in zone (spec 7, item 3): the zone is added to a level
/// (<see cref="IsIncluded"/> = true) or removed from it (false). Built-in data stays untouched.
/// </summary>
public sealed record LevelOverride(string ZoneId, ZoneLevel Level, bool IsIncluded);
