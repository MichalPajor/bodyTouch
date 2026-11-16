using System.Text.Json.Serialization;

namespace BodyTouchGame.App.Bridge;

// Payloads exchanged with body-view.js (spec section 12). JSON uses camelCase property names.

/// <summary>A point on the 'Body' mesh: triangle (three.js faceIndex) and barycentric coordinates.</summary>
public sealed record SurfacePointDto(int TriangleIndex, float BaryU, float BaryV);

/// <summary>JS -> C#: the user placed (or moved) the pin.</summary>
public sealed record PinPlacedMessage(int TriangleIndex, float BaryU, float BaryV, Dictionary<string, string> ZoneIds);

/// <summary>JS -> C#: an error in the view.</summary>
public sealed record ErrorMessage(string Message);

/// <summary>JS -> C#: diagnostics of the FPS benchmark (prototype screen only).</summary>
public sealed record StatsMessage(double FpsAvg, double FpsMin, double FrameMsP95, int Frames, double Seconds, int Width, int Height);

/// <summary>Result of loadModel.</summary>
public sealed record ModelLoadedResult(int Triangles, double LoadMs, double PageMs);

/// <summary>C# -> JS: result animation. Path = flat [x, y, z, ...] in model coordinates (cm).</summary>
public sealed record RevealPayload(SurfacePointDto Pin, string TargetZoneId, float[] Path, float DistanceCm);

/// <summary>Source-generated serialization for all bridge payloads (no reflection, trimming-safe).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PinPlacedMessage))]
[JsonSerializable(typeof(ErrorMessage))]
[JsonSerializable(typeof(StatsMessage))]
[JsonSerializable(typeof(ModelLoadedResult))]
[JsonSerializable(typeof(RevealPayload))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
public sealed partial class BridgeJsonContext : JsonSerializerContext;
