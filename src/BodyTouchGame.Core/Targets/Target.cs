using BodyTouchGame.Geometry;

namespace BodyTouchGame.Core.Targets;

public enum TargetKind
{
    Zone,
    CustomPoint,
    ManualPoint,
}

/// <summary>What the guesser has to find in a round.</summary>
public abstract record Target(TargetKind Kind);

/// <summary>A built-in zone drawn by the app.</summary>
public sealed record ZoneTarget(string ZoneId) : Target(TargetKind.Zone);

/// <summary>A custom point drawn by the app; distance = surface distance to the point minus its radius.</summary>
public sealed record CustomPointTarget(long PointId, string Name, SurfacePoint Point, float RadiusCm) : Target(TargetKind.CustomPoint);

/// <summary>A point placed by the model (<c>TargetSelection.ChosenByModel</c>, decision D4); radius 0.</summary>
public sealed record ManualPointTarget(SurfacePoint Point) : Target(TargetKind.ManualPoint);
