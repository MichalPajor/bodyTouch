using BodyTouchGame.App.Bridge;
using BodyTouchGame.App.Resources.Strings;
using Microsoft.Maui.Controls.Shapes;

namespace BodyTouchGame.App.Controls;

/// <summary>
/// The 3D body panel used on every screen with the model (spec 12, 13): HybridWebView with body-view.js on a
/// Surface panel, "Obróć palcem" hint, zoom buttons and Przód / Bok / Tył views. The model loads automatically;
/// use <see cref="Bridge"/> for everything else (modes, zone colours, reveal).
/// </summary>
public sealed class BodyView : ContentView
{
    public const string DefaultModelUrl = "models/body.glb";

    public static readonly BindableProperty ShowCameraControlsProperty =
        BindableProperty.Create(nameof(ShowCameraControls), typeof(bool), typeof(BodyView), true,
            propertyChanged: (b, _, n) => ((BodyView)b).SetControlsVisible((bool)n));

    public static readonly BindableProperty IsFramedProperty =
        BindableProperty.Create(nameof(IsFramed), typeof(bool), typeof(BodyView), true,
            propertyChanged: (b, _, n) => ((BodyView)b)._frame.StrokeThickness = (bool)n ? 1 : 0);

    private readonly Border _frame;
    private readonly HybridWebView _webView;
    private readonly View[] _overlays;
    private readonly Dictionary<string, Button> _viewButtons = [];
    private readonly TaskCompletionSource<ModelLoadedResult?> _modelLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _sideIsLeft = true;

    public BodyView()
    {
        _webView = new HybridWebView { HybridRoot = "wwwroot", DefaultFile = "index.html" };
        Bridge = new BodyViewBridge(_webView);
        Bridge.Ready += async (_, _) => await LoadModelAsync();

        var resources = Application.Current!.Resources;
        var hint = new HorizontalStackLayout
        {
            Spacing = 6,
            Margin = new Thickness(14, 18, 0, 0),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = true,
            Children =
            {
                new Microsoft.Maui.Controls.Shapes.Path
                {
                    Data = (Microsoft.Maui.Controls.Shapes.Geometry)new PathGeometryConverter().ConvertFromInvariantString("M3 12 A9 4 0 0 0 19 14.5 M16 11 L19 14.5 L15 15.5")!,
                    Stroke = new SolidColorBrush((Color)resources["Muted"]),
                    StrokeThickness = 2,
                    StrokeLineCap = PenLineCap.Square,
                    WidthRequest = 16,
                    HeightRequest = 16,
                    Aspect = Stretch.Uniform,
                    VerticalOptions = LayoutOptions.Center,
                },
                new Label { Text = AppResources.RotateHint, FontFamily = (string)resources["FontSemiBold"], FontSize = 12, TextColor = (Color)resources["Muted"], VerticalOptions = LayoutOptions.Center },
            },
        };

        var zoom = new VerticalStackLayout
        {
            Spacing = 6,
            Margin = new Thickness(0, 14, 12, 0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Children =
            {
                ToolButton(AppResources.ZoomIn, AppResources.ZoomInDescription, () => Bridge.ZoomByAsync(1.4)),
                ToolButton(AppResources.ZoomOut, AppResources.ZoomOutDescription, () => Bridge.ZoomByAsync(1 / 1.4)),
            },
        };

        var views = new HorizontalStackLayout
        {
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 14),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Children = { ViewButton("Front", AppResources.ViewFront), ViewButton("Side", AppResources.ViewSide), ViewButton("Back", AppResources.ViewBack) },
        };
        SelectView("Front");

        _overlays = [hint, zoom, views];
        _frame = new Border
        {
            Padding = 0,
            BackgroundColor = (Color)resources["SurfaceSolid"],
            Content = new Grid { Children = { _webView, hint, zoom, views } },
        };
        Content = _frame;
    }

    public BodyViewBridge Bridge { get; }

    /// <summary>Completes when the model is loaded (null result if loading failed; see <see cref="BodyViewBridge.Error"/>).</summary>
    public Task<ModelLoadedResult?> WhenModelLoaded => _modelLoaded.Task;

    public bool ShowCameraControls { get => (bool)GetValue(ShowCameraControlsProperty); set => SetValue(ShowCameraControlsProperty, value); }

    /// <summary>Draws the 1 px panel border (off when the view sits inside another panel).</summary>
    public bool IsFramed { get => (bool)GetValue(IsFramedProperty); set => SetValue(IsFramedProperty, value); }

    /// <summary>Height of the bottom overlay (view buttons + margins), kept free when framing the model.</summary>
    private const double BottomOverlayHeight = 44 + 14 + 8;

    private async Task LoadModelAsync()
    {
        try
        {
            await Bridge.SetViewInsetsAsync(0, ShowCameraControls ? BottomOverlayHeight : 0);
            _modelLoaded.TrySetResult(await Bridge.LoadModelAsync(DefaultModelUrl));
        }
        catch (Exception e)
        {
            Console.WriteLine($"[BTG] model load failed: {e}");
            _modelLoaded.TrySetResult(null);
        }
    }

    private void SetControlsVisible(bool visible)
    {
        foreach (var overlay in _overlays) overlay.IsVisible = visible;
    }

    private static Button ToolButton(string text, string description, Func<Task> action)
    {
        var button = new Button { Text = text, Style = (Style)Application.Current!.Resources["ToolButton"] };
        SemanticProperties.SetDescription(button, description);
        button.Clicked += async (_, _) => await action();
        return button;
    }

    private Button ViewButton(string key, string text)
    {
        var resources = Application.Current!.Resources;
        var button = new Button
        {
            Text = text,
            HeightRequest = 44,
            Padding = new Thickness(16, 0),
            BackgroundColor = Colors.Transparent,
            FontSize = 14,
        };
        button.Clicked += async (_, _) =>
        {
            string view;
            if (key == "Side")
            {
                // Repeated taps on "Bok" alternate between the left and the right side.
                view = _viewButtons["Side"].BorderColor == (Color)resources["Primary"] && _sideIsLeft ? "Right" : "Left";
                _sideIsLeft = view == "Left";
            }
            else view = key;

            SelectView(key);
            await Bridge.ResetCameraAsync(view);
        };
        _viewButtons[key] = button;
        return button;
    }

    private void SelectView(string key)
    {
        var resources = Application.Current!.Resources;
        foreach (var (k, button) in _viewButtons)
        {
            var selected = k == key;
            button.BorderColor = selected ? (Color)resources["Primary"] : (Color)resources["Border"];
            button.TextColor = selected ? (Color)resources["Primary"] : (Color)resources["Muted"];
            button.FontFamily = selected ? (string)resources["FontBold"] : (string)resources["FontSemiBold"];
        }
    }
}
