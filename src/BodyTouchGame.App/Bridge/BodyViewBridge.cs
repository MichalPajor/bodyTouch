using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Targets;

namespace BodyTouchGame.App.Bridge;

/// <summary>
/// C# side of the 3D view bridge (spec section 12).
/// C# -> JS: <see cref="HybridWebView.InvokeJavaScriptAsync{TReturnType}"/> on functions of <c>window.bodyView</c>.
/// JS -> C#: body-view.js calls <c>window.HybridWebView.InvokeDotNet("Receive", [eventName, json])</c>;
/// payloads are deserialized with <see cref="BridgeJsonContext"/>. Events are raised on the UI thread.
/// </summary>
public sealed class BodyViewBridge
{
    private readonly HybridWebView _webView;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BodyViewBridge(HybridWebView webView)
    {
        _webView = webView;
        _webView.SetInvokeJavaScriptTarget(new Receiver(this));
    }

    public event EventHandler? Ready;
    public event EventHandler<PinPlacedMessage>? PinPlaced;
    public event EventHandler<ZoneTappedMessage>? ZoneTapped;
    public event EventHandler<ErrorMessage>? Error;
    public event EventHandler<StatsMessage>? StatsReceived;

    /// <summary>Completes when the page has initialised (onReady).</summary>
    public Task WhenReady => _ready.Task;

    public Task<ModelLoadedResult?> LoadModelAsync(string url) =>
        _webView.InvokeJavaScriptAsync("bodyView.loadModel", BridgeJsonContext.Default.ModelLoadedResult, [url], [BridgeJsonContext.Default.String]);

    /// <summary>Mode: View, Draw, Guess, Reveal, Editor.</summary>
    public Task SetModeAsync(string mode, ViewModeOptions? options = null) =>
        Invoke("bodyView.setMode", [mode, options ?? new ViewModeOptions()], [BridgeJsonContext.Default.String, BridgeJsonContext.Default.ViewModeOptions]);

    /// <summary>View = Front, Back, Left or Right.</summary>
    public Task ResetCameraAsync(string view) => Invoke("bodyView.resetCamera", [view], [BridgeJsonContext.Default.String]);

    public Task SetZoneColorsAsync(Dictionary<string, string> colors) =>
        Invoke("bodyView.setZoneColors", [colors], [BridgeJsonContext.Default.DictionaryStringString]);

    /// <summary>Highlights the target; <paramref name="turnCamera"/> = false keeps the camera (editor preview).</summary>
    public Task FocusTargetAsync(TargetDto target, bool animate = true, bool turnCamera = true) =>
        Invoke("bodyView.focusTarget", [target, animate, turnCamera],
            [BridgeJsonContext.Default.TargetDto, BridgeJsonContext.Default.Boolean, BridgeJsonContext.Default.Boolean]);

    /// <summary>Custom point markers (empty list = none).</summary>
    public Task SetCustomPointsAsync(List<CustomPointDto> points) =>
        Invoke("bodyView.setCustomPoints", [points], [BridgeJsonContext.Default.ListCustomPointDto]);

    public Task ClearTargetAsync() => Invoke("bodyView.clearTarget", null, null);

    public Task ClearPinAsync() => Invoke("bodyView.clearPin", null, null);

    public Task ShowRevealAsync(RevealPayload payload) =>
        Invoke("bodyView.showReveal", [payload], [BridgeJsonContext.Default.RevealPayload]);

    /// <summary>Heat map: arrows target zone -> most often guessed zone (spec 10).</summary>
    public Task SetArrowsAsync(List<ArrowDto> arrows) =>
        Invoke("bodyView.setArrows", [arrows], [BridgeJsonContext.Default.ListArrowDto]);

    public Task ClearArrowsAsync() => Invoke("bodyView.clearArrows", null, null);

    public Task<ViewDebugState?> GetDebugStateAsync() =>
        _webView.InvokeJavaScriptAsync("bodyView.getDebugState", BridgeJsonContext.Default.ViewDebugState);

    public Task ZoomByAsync(double factor) => Invoke("bodyView.zoomBy", [factor], [BridgeJsonContext.Default.Double]);

    /// <summary>CSS pixels covered by native overlays at the top / bottom; the model is framed between them.</summary>
    public Task SetViewInsetsAsync(double top, double bottom) =>
        Invoke("bodyView.setViewInsets", [top, bottom], [BridgeJsonContext.Default.Double, BridgeJsonContext.Default.Double]);

    /// <summary>Diagnostic FPS counter in the page (hidden by default).</summary>
    public Task SetStatsVisibleAsync(bool visible) => Invoke("bodyView.setStatsVisible", [visible], [BridgeJsonContext.Default.Boolean]);

    /// <summary>Executes the view commands of <see cref="ViewDirector"/> in order.</summary>
    public async Task ExecuteAsync(IEnumerable<ViewCommand> commands)
    {
        foreach (var command in commands)
        {
            switch (command)
            {
                case ClearTarget: await ClearTargetAsync(); break;
                case ClearPin: await ClearPinAsync(); break;
                case ResetCamera reset: await ResetCameraAsync(reset.View); break;
                case SetViewMode mode: await SetModeAsync(mode.Mode.ToString(), new ViewModeOptions(mode.AllowTargetPin)); break;
                case FocusTarget focus: await FocusTargetAsync(ToDto(focus.Target)); break;
                case ShowReveal reveal:
                    var path = new float[reveal.Path.Count * 3];
                    for (var i = 0; i < reveal.Path.Count; i++)
                    {
                        path[i * 3] = reveal.Path[i].X;
                        path[i * 3 + 1] = reveal.Path[i].Y;
                        path[i * 3 + 2] = reveal.Path[i].Z;
                    }

                    var pin = reveal.Pin is { } p ? new SurfacePointDto(p.TriangleIndex, p.BaryU, p.BaryV) : null;
                    await ShowRevealAsync(new RevealPayload(pin, ToDto(reveal.Target), path, reveal.DistanceCm));
                    break;
            }
        }
    }

    public static TargetDto ToDto(Target target) => target switch
    {
        ZoneTarget zone => new TargetDto("Zone", ZoneId: zone.ZoneId),
        CustomPointTarget point => new TargetDto("Point", null, point.Point.TriangleIndex, point.Point.BaryU, point.Point.BaryV, point.RadiusCm),
        ManualPointTarget manual => new TargetDto("Point", null, manual.Point.TriangleIndex, manual.Point.BaryU, manual.Point.BaryV, 0),
        _ => throw new ArgumentException($"Unsupported target {target.GetType().Name}.", nameof(target)),
    };

    private Task Invoke(string function, object?[]? args, JsonTypeInfo?[]? types) =>
        _webView.InvokeJavaScriptAsync(function, BridgeJsonContext.Default.Boolean, args, types);

    private void Dispatch(string eventName, string json)
    {
        switch (eventName)
        {
            case "onReady":
                _ready.TrySetResult();
                Raise(() => Ready?.Invoke(this, EventArgs.Empty));
                break;
            case "onPinPlaced" when Deserialize(json, BridgeJsonContext.Default.PinPlacedMessage) is { } pin:
                Raise(() => PinPlaced?.Invoke(this, pin));
                break;
            case "onZoneTapped" when Deserialize(json, BridgeJsonContext.Default.ZoneTappedMessage) is { } zone:
                Raise(() => ZoneTapped?.Invoke(this, zone));
                break;
            case "onStats" when Deserialize(json, BridgeJsonContext.Default.StatsMessage) is { } stats:
                Raise(() => StatsReceived?.Invoke(this, stats));
                break;
            case "onError" when Deserialize(json, BridgeJsonContext.Default.ErrorMessage) is { } error:
                Raise(() => Error?.Invoke(this, error));
                break;
            default:
                Raise(() => Error?.Invoke(this, new ErrorMessage($"Unknown bridge event '{eventName}'.")));
                break;
        }
    }

    private T? Deserialize<T>(string json, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException e)
        {
            Raise(() => Error?.Invoke(this, new ErrorMessage($"Invalid bridge payload: {e.Message}")));
            return default;
        }
    }

    private static void Raise(Action action) => MainThread.BeginInvokeOnMainThread(action);

    /// <summary>Object exposed to JavaScript. Only <see cref="Receive"/> is callable.</summary>
    public sealed class Receiver(BodyViewBridge owner)
    {
        public void Receive(string eventName, string json) => owner.Dispatch(eventName, json);
    }
}
