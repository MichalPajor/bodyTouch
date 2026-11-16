using System.Text.Json;

namespace BodyTouchGame.App.Bridge;

/// <summary>
/// C# side of the 3D view bridge (spec section 12).
/// C# -> JS: <see cref="HybridWebView.InvokeJavaScriptAsync{TReturnType}"/> on functions of <c>window.bodyView</c>.
/// JS -> C#: body-view.js calls <c>window.HybridWebView.InvokeDotNet("Receive", [eventName, json])</c>;
/// payloads are deserialized with <see cref="BridgeJsonContext"/>.
/// Events are raised on the UI thread.
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
    public event EventHandler<ErrorMessage>? Error;
    public event EventHandler<StatsMessage>? StatsReceived;

    /// <summary>Completes when the page has initialised (onReady).</summary>
    public Task WhenReady => _ready.Task;

    public Task<ModelLoadedResult?> LoadModelAsync(string url) =>
        _webView.InvokeJavaScriptAsync("bodyView.loadModel", BridgeJsonContext.Default.ModelLoadedResult,
            [url], [BridgeJsonContext.Default.String]);

    public Task SetModeAsync(string mode) => InvokeAsync("bodyView.setMode", mode);

    /// <summary>View = Front, Back, Left or Right.</summary>
    public Task ResetCameraAsync(string view) => InvokeAsync("bodyView.resetCamera", view);

    public Task SetZoneColorsAsync(Dictionary<string, string> colors) =>
        _webView.InvokeJavaScriptAsync("bodyView.setZoneColors", BridgeJsonContext.Default.Boolean,
            [colors], [BridgeJsonContext.Default.DictionaryStringString]);

    public Task ShowRevealAsync(RevealPayload payload) =>
        _webView.InvokeJavaScriptAsync("bodyView.showReveal", BridgeJsonContext.Default.Boolean,
            [payload], [BridgeJsonContext.Default.RevealPayload]);

    public Task ClearRevealAsync() =>
        _webView.InvokeJavaScriptAsync("bodyView.clearReveal", BridgeJsonContext.Default.Boolean);

    public Task ZoomByAsync(double factor) =>
        _webView.InvokeJavaScriptAsync("bodyView.zoomBy", BridgeJsonContext.Default.Boolean,
            [factor], [BridgeJsonContext.Default.Double]);

    /// <summary>Render resolution relative to CSS pixels; returns the ratio actually applied.</summary>
    public Task<double> SetPixelRatioAsync(double ratio) =>
        _webView.InvokeJavaScriptAsync("bodyView.setPixelRatio", BridgeJsonContext.Default.Double,
            [ratio], [BridgeJsonContext.Default.Double]);

    public Task StartBenchmarkAsync() =>
        _webView.InvokeJavaScriptAsync("bodyView.startBenchmark", BridgeJsonContext.Default.Boolean);

    private Task InvokeAsync(string function, string argument) =>
        _webView.InvokeJavaScriptAsync(function, BridgeJsonContext.Default.Boolean, [argument], [BridgeJsonContext.Default.String]);

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

    private T? Deserialize<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
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
