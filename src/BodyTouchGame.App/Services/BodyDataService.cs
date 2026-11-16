using System.Diagnostics;
using System.Numerics;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.App.Services;

/// <summary>Result of a surface distance measurement with timing (prototype diagnostics).</summary>
public sealed record DistanceMeasurement(float DistanceCm, bool IsInside, Vector3[] Path, double ComputeMs);

/// <summary>
/// Loads body_graph.bin and body_zones.json from the app package once (zone catalog and distances from Core).
/// </summary>
public sealed class BodyDataService
{
    public const string ModelUrl = "models/body.glb";
    private const string GraphAsset = "body_graph.bin";
    private const string ZonesAsset = "wwwroot/models/body_zones.json";

    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public BodyGraph? Body { get; private set; }
    public ZoneCatalog? Zones { get; private set; }
    public GeodesicDistanceCalculator? Distances { get; private set; }
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
                ZoneCatalog data;
                await using (var zonesStream = await FileSystem.OpenAppPackageFileAsync(ZonesAsset).ConfigureAwait(false))
                {
                    data = await ZoneCatalog.LoadAsync(zonesStream).ConfigureAwait(false);
                }

                return (graph, data, (readMs, parseMs, zonesMs: step.Elapsed.TotalMilliseconds));
            }).ConfigureAwait(false);

            Distances = new GeodesicDistanceCalculator(body, zones);   // checks that both describe the same mesh
            Body = body;
            Zones = zones;
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
        var distances = Distances ?? throw new InvalidOperationException("Body data not loaded.");
        distances.Prepare(zoneId);
        var stopwatch = Stopwatch.StartNew();
        var result = distances.Measure(pin, new ZoneTarget(zoneId));
        stopwatch.Stop();
        return new DistanceMeasurement(result.DistanceCm, result.IsInside, result.Path.ToArray(), stopwatch.Elapsed.TotalMilliseconds);
    });
}
