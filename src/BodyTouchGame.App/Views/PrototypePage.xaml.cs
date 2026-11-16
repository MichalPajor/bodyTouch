using System.Globalization;
using BodyTouchGame.App.Bridge;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.App.Services;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Zones;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.App.Views;

/// <summary>
/// Stage 3 test screen: tap the model -> C# measures the surface distance to the selected zone ->
/// the path is drawn on the model. Shows load and compute times; the page shows the FPS counter.
/// </summary>
public partial class PrototypePage : ContentPage
{
    private const string DefaultZone = "leg-l.knee";
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pl-PL");

    private readonly BodyDataService _bodyData;
    private readonly BodyViewBridge _bridge;
    private List<ZoneDefinition> _zones = [];
    private PinPlacedMessage? _lastPin;
    private static readonly double[] PixelRatios = [2.0, 1.5, 1.0];
    private int _pixelRatioIndex;

    public PrototypePage(BodyDataService bodyData)
    {
        InitializeComponent();
        _bodyData = bodyData;
        _bridge = Body.Bridge;
        _bridge.PinPlaced += OnPinPlaced;
        _bridge.Error += (_, e) => StatusLabel.Text = string.Format(Culture, AppResources.PrototypeError, e.Message);
        _bridge.StatsReceived += OnStats;
        ResolutionButton.Text = string.Format(Culture, AppResources.PrototypeResolution, PixelRatios[0]);
        _ = InitialiseAsync();
    }

    private async Task InitialiseAsync()
    {
        try
        {
            var dataTask = _bodyData.LoadAsync();
            var model = await Body.WhenModelLoaded;
            await _bridge.SetStatsVisibleAsync(true);
            await dataTask;

            _zones = _bodyData.Zones!.Zones.Where(z => z.Level == ZoneLevel.Medium && z.Drawable).ToList();
            ZonePicker.ItemsSource = _zones.Select(z => z.NamePl).ToList();
            ZonePicker.SelectedIndex = Math.Max(0, _zones.FindIndex(z => z.Id == DefaultZone));
            StatusLabel.Text = string.Format(Culture, AppResources.PrototypeLoaded, model?.LoadMs ?? 0, _bodyData.GraphLoadMs);
            Console.WriteLine($"[BTG] model loaded: {model}, graph {_bodyData.GraphLoadMs:F1} ms");
        }
        catch (Exception e)
        {
            StatusLabel.Text = string.Format(Culture, AppResources.PrototypeError, e.Message);
            Console.WriteLine($"[BTG] init failed: {e}");
        }
    }

    private ZoneDefinition? SelectedZone => ZonePicker.SelectedIndex >= 0 && ZonePicker.SelectedIndex < _zones.Count ? _zones[ZonePicker.SelectedIndex] : null;

    private async void OnZoneChanged(object? sender, EventArgs e)
    {
        if (SelectedZone is not { } zone) return;
        await _bridge.SetZoneColorsAsync(new Dictionary<string, string> { [zone.Id] = "#FF9A3C" });
        if (_lastPin is not null) await MeasureAsync(_lastPin);
    }

    private async void OnPinPlaced(object? sender, PinPlacedMessage pin)
    {
        _lastPin = pin;
        await MeasureAsync(pin);
    }

    private async Task MeasureAsync(PinPlacedMessage pin)
    {
        if (SelectedZone is not { } zone) return;
        try
        {
            var measurement = await _bodyData.MeasureAsync(new SurfacePoint(pin.TriangleIndex, pin.BaryU, pin.BaryV), zone.Id);
            ResultLabel.Text = measurement.IsInside
                ? string.Format(Culture, AppResources.PrototypeInside, zone.NamePl, measurement.ComputeMs)
                : string.Format(Culture, AppResources.PrototypeResult, zone.NamePl, measurement.DistanceCm, measurement.ComputeMs);
            Console.WriteLine($"[BTG] pin {pin.TriangleIndex} ({string.Join(",", pin.ZoneIds.Values)}) -> {zone.Id}: {measurement.DistanceCm:F2} cm, {measurement.ComputeMs:F2} ms, {measurement.Path.Length} path points");

            var flat = new float[measurement.Path.Length * 3];
            for (var i = 0; i < measurement.Path.Length; i++)
            {
                flat[i * 3] = measurement.Path[i].X;
                flat[i * 3 + 1] = measurement.Path[i].Y;
                flat[i * 3 + 2] = measurement.Path[i].Z;
            }

            await _bridge.ShowRevealAsync(new RevealPayload(new SurfacePointDto(pin.TriangleIndex, pin.BaryU, pin.BaryV), zone.Id, flat, measurement.DistanceCm));
        }
        catch (Exception e)
        {
            StatusLabel.Text = string.Format(Culture, AppResources.PrototypeError, e.Message);
        }
    }

    private void OnStats(object? sender, StatsMessage stats)
    {
        BenchmarkButton.IsEnabled = true;
        BenchmarkLabel.Text = string.Format(Culture, AppResources.PrototypeBenchmarkResult, stats.FpsAvg, stats.FpsMin, stats.FrameMsP95, stats.Width, stats.Height);
        Console.WriteLine($"[BTG] benchmark: {stats}");
    }

    private async void OnBenchmark(object? sender, EventArgs e)
    {
        BenchmarkButton.IsEnabled = false;
        BenchmarkLabel.Text = AppResources.PrototypeBenchmarkRunning;
        await _bridge.StartBenchmarkAsync();
    }

    private async void OnResolution(object? sender, EventArgs e)
    {
        _pixelRatioIndex = (_pixelRatioIndex + 1) % PixelRatios.Length;
        var applied = await _bridge.SetPixelRatioAsync(PixelRatios[_pixelRatioIndex]);
        ResolutionButton.Text = string.Format(Culture, AppResources.PrototypeResolution, applied);
    }

}
