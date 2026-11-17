using System.Text.Json.Serialization;

namespace BodyTouchGame.App.Bridge;

// Payloads exchanged with body-view.js (spec section 12). JSON uses camelCase property names.

/// <summary>A point on the 'Body' mesh: triangle (three.js faceIndex) and barycentric coordinates.</summary>
public sealed record SurfacePointDto(int TriangleIndex, float BaryU, float BaryV);

/// <summary>A target for the view: Kind = "Zone" (ZoneId) or "Point" (triangle, barycentric, radius).</summary>
public sealed record TargetDto(string Kind, string? ZoneId = null, int? TriangleIndex = null, float? BaryU = null, float? BaryV = null, float? RadiusCm = null);

/// <summary>Options of setMode: AllowTargetPin (Draw, ChosenByModel), Level (Editor).</summary>
public sealed record ViewModeOptions(bool AllowTargetPin = false, string? Level = null);

/// <summary>JS -> C#: the user placed (or moved) a pin (guess pin, or the model's target pin in Draw).</summary>
public sealed record PinPlacedMessage(int TriangleIndex, float BaryU, float BaryV, Dictionary<string, string> ZoneIds);

/// <summary>JS -> C#: a zone was tapped in Editor mode.</summary>
public sealed record ZoneTappedMessage(string ZoneId, string Level);

/// <summary>JS -> C#: an error in the view.</summary>
public sealed record ErrorMessage(string Message);

/// <summary>JS -> C#: diagnostics of the FPS benchmark.</summary>
public sealed record StatsMessage(double FpsAvg, double FpsMin, double FrameMsP95, int Frames, double Seconds, int Width, int Height);

/// <summary>Result of loadModel.</summary>
public sealed record ModelLoadedResult(int Triangles, double LoadMs, double PageMs);

/// <summary>Result of getDebugState: what the view currently holds (target-secrecy check, spec 5).</summary>
public sealed record ViewDebugState(string Mode, bool HasTarget, bool HasPin, bool HasReveal, int Arrows = 0);

/// <summary>C# -> JS: heat map arrow from a target zone to the zone most often guessed instead of it.</summary>
public sealed record ArrowDto(string FromZoneId, string ToZoneId, int Count);

/// <summary>C# -> JS: result. Path = flat [x, y, z, ...] in model coordinates (cm).</summary>
public sealed record RevealPayload(SurfacePointDto? Pin, TargetDto Target, float[] Path, float? DistanceCm);

/// <summary>Source-generated serialization for all bridge payloads (no reflection, trimming-safe).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PinPlacedMessage))]
[JsonSerializable(typeof(ZoneTappedMessage))]
[JsonSerializable(typeof(ErrorMessage))]
[JsonSerializable(typeof(StatsMessage))]
[JsonSerializable(typeof(ModelLoadedResult))]
[JsonSerializable(typeof(ViewDebugState))]
[JsonSerializable(typeof(RevealPayload))]
[JsonSerializable(typeof(TargetDto))]
[JsonSerializable(typeof(ViewModeOptions))]
[JsonSerializable(typeof(List<ArrowDto>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
public sealed partial class BridgeJsonContext : JsonSerializerContext;
