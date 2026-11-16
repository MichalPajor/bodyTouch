using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.App.Services;

public sealed record ZoneInfo(string Id, string Level, string? ParentId, string NamePl, string Side, bool Drawable = true);

public sealed record BodyZonesData(int Version, int TriangleCount, List<ZoneInfo> Zones, Dictionary<string, int[]> TriangleZones);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BodyZonesData))]
internal sealed partial class BodyDataJsonContext : JsonSerializerContext;

/// <summary>Result of a surface distance measurement with timing (prototype diagnostics).</summary>
public sealed record DistanceMeasurement(float DistanceCm, bool IsInside, Vector3[] Path, double ComputeMs);

/// <summary>
/// Loads body_graph.bin and body_zones.json from the app package once and measures surface distances.
/// (Stage 3 prototype; zone handling moves to Core in stage 4.)
/// </summary>
public sealed class BodyDataService
{
    public const string ModelUrl = "models/body.glb";
    private const string GraphAsset = "body_graph.bin";
    private const string ZonesAsset = "wwwroot/models/body_zones.json";

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly Dictionary<string, ZoneRegion> _regions = [];
    private GeodesicSolver? _solver;

    public BodyGraph? Body { get; private set; }
    public BodyZonesData? Zones { get; private set; }
    public double GraphLoadMs { get; private set; }

    /// <summary>Loads the data once. Runs entirely off the UI thread (no UI access here).</summary>
    public async Task LoadAsync()
    {
        await _loadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Body is not null) return;
            var total = Stopwatch.StartNew();
            var (body, zones, timings) = await Task.Run(async () =>
            {
                var step = Stopwatch.StartNew();
                byte[] graphBytes;
                await using (var graphStream = await FileSystem.OpenAppPackageFileAsync(GraphAsset).ConfigureAwait(false))
                {
                    using var buffer = new MemoryStream();
                    await graphStream.CopyToAsync(buffer).ConfigureAwait(false);
                    graphBytes = buffer.ToArray();
                }

                var readMs = step.Elapsed.TotalMilliseconds;
                step.Restart();
                var graph = BodyGraphSerializer.Read(new MemoryStream(graphBytes, writable: false));
                var parseMs = step.Elapsed.TotalMilliseconds;
                step.Restart();
                BodyZonesData data;
                await using (var zonesStream = await FileSystem.OpenAppPackageFileAsync(ZonesAsset).ConfigureAwait(false))
                {
                    data = await JsonSerializer.DeserializeAsync(zonesStream, BodyDataJsonContext.Default.BodyZonesData).ConfigureAwait(false)
                           ?? throw new InvalidDataException("body_zones.json is empty.");
                }

                return (graph, data, (readMs, parseMs, zonesMs: step.Elapsed.TotalMilliseconds));
            }).ConfigureAwait(false);

            if (zones.TriangleCount != body.TriangleCount)
                throw new InvalidDataException($"body_zones.json ({zones.TriangleCount}) and body_graph.bin ({body.TriangleCount}) describe different meshes.");
            Body = body;
            Zones = zones;
            _solver = new GeodesicSolver(body);
            GraphLoadMs = total.Elapsed.TotalMilliseconds;
            LoadTimings = timings;
            Console.WriteLine($"[BTG] data: asset read {timings.readMs:F1} ms, graph parse+validate {timings.parseMs:F1} ms, zones json {timings.zonesMs:F1} ms, total {GraphLoadMs:F1} ms");
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>Breakdown of the last load (diagnostics).</summary>
    public (double readMs, double parseMs, double zonesMs) LoadTimings { get; private set; }

    /// <summary>Distance from the pin to a zone and the smoothed path; runs off the UI thread.</summary>
    public Task<DistanceMeasurement> MeasureAsync(SurfacePoint pin, string zoneId) => Task.Run(() =>
    {
        if (Body is null || Zones is null || _solver is null) throw new InvalidOperationException("Body data not loaded.");
        var region = GetRegion(zoneId);
        var stopwatch = Stopwatch.StartNew();
        var result = _solver.DistanceToZone(pin, region);
        var path = _solver.GetLastPath();
        stopwatch.Stop();
        return new DistanceMeasurement(result.DistanceCm, result.IsInsideTarget, path, stopwatch.Elapsed.TotalMilliseconds);
    });

    private ZoneRegion GetRegion(string zoneId)
    {
        lock (_regions)
        {
            if (_regions.TryGetValue(zoneId, out var cached)) return cached;
            var index = Zones!.Zones.FindIndex(z => z.Id == zoneId);
            if (index < 0) throw new ArgumentException($"Unknown zone '{zoneId}'.", nameof(zoneId));
            var region = ZoneRegion.FromTriangleLabels(Body!, Zones.TriangleZones[Zones.Zones[index].Level], index);
            _regions[zoneId] = region;
            return region;
        }
    }
}
