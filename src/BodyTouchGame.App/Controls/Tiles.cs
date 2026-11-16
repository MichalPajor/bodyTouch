using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace BodyTouchGame.App.Controls;

/// <summary>Tile button with a 3 px accent bar on top (Start screen: Gracze, Rankingi, Ustawienia; spec 13).</summary>
public sealed class AccentTile : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(AccentTile), string.Empty, propertyChanged: (b, _, n) => ((AccentTile)b)._label.Text = (string)n);

    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(AccentTile), Colors.Transparent, propertyChanged: (b, _, n) => ((AccentTile)b)._accent.Color = (Color)n);

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(AccentTile), propertyChanged: (b, o, n) => ((AccentTile)b).OnCommandChanged((ICommand?)o, (ICommand?)n));

    private readonly Label _label;
    private readonly BoxView _accent;

    public AccentTile()
    {
        _accent = new BoxView { HeightRequest = 3, VerticalOptions = LayoutOptions.Start };
        _label = new Label
        {
            FontFamily = (string)Application.Current!.Resources["FontBold"],
            FontSize = 15,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        Content = new Border
        {
            HeightRequest = 72,
            Padding = 0,
            Content = new Grid { Children = { _label, _accent } },
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (IsEnabled && Command?.CanExecute(null) == true) Command.Execute(null);
        };
        GestureRecognizers.Add(tap);
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    private void OnCommandChanged(ICommand? old, ICommand? command)
    {
        if (old is not null) old.CanExecuteChanged -= OnCanExecuteChanged;
        if (command is not null) command.CanExecuteChanged += OnCanExecuteChanged;
        OnCanExecuteChanged(this, EventArgs.Empty);
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e)
    {
        var enabled = Command?.CanExecute(null) ?? false;
        IsEnabled = enabled;
        Opacity = enabled ? 1 : 0.45;
    }
}

/// <summary>Square switch, 48 × 28 (spec 13): on = Primary track with a dark thumb on the right.</summary>
public sealed class SquareSwitch : ContentView
{
    public static readonly BindableProperty IsToggledProperty =
        BindableProperty.Create(nameof(IsToggled), typeof(bool), typeof(SquareSwitch), false, BindingMode.TwoWay,
            propertyChanged: (b, _, _) => ((SquareSwitch)b).UpdateVisual());

    private readonly Border _track;
    private readonly BoxView _thumb;

    public SquareSwitch()
    {
        _thumb = new BoxView { WidthRequest = 22, HeightRequest = 22, VerticalOptions = LayoutOptions.Center };
        _track = new Border
        {
            WidthRequest = 48,
            HeightRequest = 28,
            Padding = 3,
            StrokeThickness = 0,
            Content = _thumb,
        };
        Content = _track;
        WidthRequest = 48;
        HeightRequest = 28;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (IsEnabled) IsToggled = !IsToggled;
        };
        GestureRecognizers.Add(tap);
        UpdateVisual();
    }

    public bool IsToggled { get => (bool)GetValue(IsToggledProperty); set => SetValue(IsToggledProperty, value); }

    private void UpdateVisual()
    {
        var resources = Application.Current!.Resources;
        _track.BackgroundColor = IsToggled ? (Color)resources["Primary"] : (Color)resources["Border"];
        _thumb.Color = IsToggled ? (Color)resources["OnPrimary"] : (Color)resources["Muted"];
        _thumb.HorizontalOptions = IsToggled ? LayoutOptions.End : LayoutOptions.Start;
    }
}

/// <summary>Navigation row in a settings card: label and a square-capped chevron, 60 px high.</summary>
public sealed class SettingsRow : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(SettingsRow), string.Empty, propertyChanged: (b, _, n) => ((SettingsRow)b)._label.Text = (string)n);

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(SettingsRow), propertyChanged: (b, o, n) => ((SettingsRow)b).OnCommandChanged((ICommand?)o, (ICommand?)n));

    public static readonly BindableProperty ShowDividerProperty =
        BindableProperty.Create(nameof(ShowDivider), typeof(bool), typeof(SettingsRow), true, propertyChanged: (b, _, n) => ((SettingsRow)b)._divider.IsVisible = (bool)n);

    private readonly Label _label;
    private readonly BoxView _divider;

    public SettingsRow()
    {
        _label = new Label { FontFamily = (string)Application.Current!.Resources["FontBold"], FontSize = 15, VerticalOptions = LayoutOptions.Center };
        var chevron = new Microsoft.Maui.Controls.Shapes.Path
        {
            Data = (Microsoft.Maui.Controls.Shapes.Geometry)new PathGeometryConverter().ConvertFromInvariantString("M9 6 L15 12 L9 18")!,
            Stroke = new SolidColorBrush((Color)Application.Current.Resources["Text"]),
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Square,
            WidthRequest = 18,
            HeightRequest = 18,
            Aspect = Stretch.None,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 14, 0),
        };
        _label.Margin = new Thickness(14, 0, 40, 0);
        _divider = new BoxView { HeightRequest = 1, Color = (Color)Application.Current.Resources["Border"], VerticalOptions = LayoutOptions.End };
        Content = new Grid { HeightRequest = 60, BackgroundColor = Colors.Transparent, Children = { _label, chevron, _divider } };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (IsEnabled && Command?.CanExecute(null) == true) Command.Execute(null);
        };
        GestureRecognizers.Add(tap);
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public bool ShowDivider { get => (bool)GetValue(ShowDividerProperty); set => SetValue(ShowDividerProperty, value); }

    private void OnCommandChanged(ICommand? old, ICommand? command)
    {
        if (old is not null) old.CanExecuteChanged -= OnCanExecuteChanged;
        if (command is not null) command.CanExecuteChanged += OnCanExecuteChanged;
        OnCanExecuteChanged(this, EventArgs.Empty);
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e)
    {
        var enabled = Command?.CanExecute(null) ?? false;
        IsEnabled = enabled;
        Opacity = enabled ? 1 : 0.45;
    }
}

/// <summary>Row with a label and a <see cref="SquareSwitch"/>, 60 px high.</summary>
public sealed class SettingsToggleRow : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(SettingsToggleRow), string.Empty, propertyChanged: (b, _, n) => ((SettingsToggleRow)b)._label.Text = (string)n);

    public static readonly BindableProperty IsToggledProperty =
        BindableProperty.Create(nameof(IsToggled), typeof(bool), typeof(SettingsToggleRow), false, BindingMode.TwoWay,
            propertyChanged: (b, _, n) => ((SettingsToggleRow)b)._switch.IsToggled = (bool)n);

    public static readonly BindableProperty ShowDividerProperty =
        BindableProperty.Create(nameof(ShowDivider), typeof(bool), typeof(SettingsToggleRow), true, propertyChanged: (b, _, n) => ((SettingsToggleRow)b)._divider.IsVisible = (bool)n);

    private readonly Label _label;
    private readonly SquareSwitch _switch;
    private readonly BoxView _divider;

    public SettingsToggleRow()
    {
        _label = new Label { FontFamily = (string)Application.Current!.Resources["FontBold"], FontSize = 15, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(14, 0) };
        // The whole row toggles; the switch itself does not handle taps (no double toggle).
        _switch = new SquareSwitch { HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 14, 0), InputTransparent = true };
        _switch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SquareSwitch.IsToggled)) IsToggled = _switch.IsToggled;
        };
        _divider = new BoxView { HeightRequest = 1, Color = (Color)Application.Current.Resources["Border"], VerticalOptions = LayoutOptions.End };
        Content = new Grid { HeightRequest = 60, BackgroundColor = Colors.Transparent, Children = { _label, _switch, _divider } };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => _switch.IsToggled = !_switch.IsToggled;
        ((Grid)Content).GestureRecognizers.Add(tap);
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsToggled { get => (bool)GetValue(IsToggledProperty); set => SetValue(IsToggledProperty, value); }
    public bool ShowDivider { get => (bool)GetValue(ShowDividerProperty); set => SetValue(ShowDividerProperty, value); }
}
