using System.Diagnostics;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Zones;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.App.Services;

/// <summary>
/// Loads body_graph.bin and body_zones.json from the app package once: the zone catalog and the surface distance
/// calculator used by the game.
/// </summary>
public sealed class BodyDataService
{
    private const string GraphAsset = "body_graph.bin";
    private const string ZonesAsset = "wwwroot/models/body_zones.json";

    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public BodyGraph? Body { get; private set; }
    public ZoneCatalog? Zones { get; private set; }
    public GeodesicDistanceCalculator? Distances { get; private set; }

    private readonly Dictionary<Core.Config.ZoneLevel, IReadOnlyDictionary<string, int>> _colorSlots = [];

    /// <summary>Colour slot of a zone: neighbouring zones of one level always differ (zone editor, zone set).</summary>
    public int ColorSlot(string zoneId)
    {
        var zone = Zones!.Get(zoneId);
        lock (_colorSlots)
        {
            if (!_colorSlots.TryGetValue(zone.Level, out var slots))
                _colorSlots[zone.Level] = slots = ZoneColoring.Slots(Zones, Body!, zone.Level);
            return slots.GetValueOrDefault(zoneId);
        }
    }

    /// <summary>Loads the data once. Runs entirely off the UI thread.</summary>
    public async Task LoadAsync()
    {
        await _loadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Body is not null) return;
            var stopwatch = Stopwatch.StartNew();
            var (body, zones) = await Task.Run(async () =>
            {
                byte[] graphBytes;
                await using (var graphStream = await FileSystem.OpenAppPackageFileAsync(GraphAsset).ConfigureAwait(false))
                {
                    using var buffer = new MemoryStream();
                    await graphStream.CopyToAsync(buffer).ConfigureAwait(false);
                    graphBytes = buffer.ToArray();
                }

                var graph = BodyGraphSerializer.Read(new MemoryStream(graphBytes, writable: false));
                await using var zonesStream = await FileSystem.OpenAppPackageFileAsync(ZonesAsset).ConfigureAwait(false);
                return (graph, await ZoneCatalog.LoadAsync(zonesStream).ConfigureAwait(false));
            }).ConfigureAwait(false);

            Distances = new GeodesicDistanceCalculator(body, zones);   // checks that both describe the same mesh
            Body = body;
            Zones = zones;
            Console.WriteLine($"[BTG] body data loaded in {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
        }
        finally
        {
            _loadLock.Release();
        }
    }
}
