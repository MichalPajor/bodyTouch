using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace BodyTouchGame.App.Controls;

internal static class Res
{
    public static Color Color(string key) => (Color)Application.Current!.Resources[key];
    public static string Font(string key) => (string)Application.Current!.Resources[key];
}

/// <summary>Square avatar with the player's initial (mockups 2 and 9).</summary>
public sealed class Avatar : ContentView
{
    public static readonly BindableProperty NameProperty =
        BindableProperty.Create(nameof(Name), typeof(string), typeof(Avatar), string.Empty, propertyChanged: (b, _, _) => ((Avatar)b).Update());

    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(string), typeof(Avatar), "#FF3D9A", propertyChanged: (b, _, _) => ((Avatar)b).Update());

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(Avatar), 42.0, propertyChanged: (b, _, _) => ((Avatar)b).Update());

    private readonly Label _label;
    private readonly Grid _box;

    public Avatar()
    {
        _label = new Label { FontFamily = Res.Font("FontTitle"), TextColor = Res.Color("OnPrimary"), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _box = new Grid { Children = { _label } };
        Content = _box;
        InputTransparent = true;
        Update();
    }

    public string Name { get => (string)GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    private void Update()
    {
        _box.WidthRequest = _box.HeightRequest = Size;
        _box.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb(string.IsNullOrEmpty(Color) ? "#FF3D9A" : Color);
        _label.FontSize = Size * 0.43;
        _label.Text = string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();
    }
}

/// <summary>
/// Selectable chip / option button. Style "Chip": selected = Primary fill (presets, levels; mockup 3).
/// Style "Option": selected = cyan border and tint (touch method; mockup 3).
/// </summary>
public sealed class ChoiceButton : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(ChoiceButton), string.Empty, propertyChanged: (b, _, n) => ((ChoiceButton)b)._label.Text = (string)n);

    public static readonly BindableProperty IsSelectedProperty =
        BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(ChoiceButton), false, propertyChanged: (b, _, _) => ((ChoiceButton)b).Update());

    public static readonly BindableProperty IsOptionProperty =
        BindableProperty.Create(nameof(IsOption), typeof(bool), typeof(ChoiceButton), false, propertyChanged: (b, _, _) => ((ChoiceButton)b).Update());

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(ChoiceButton));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(ChoiceButton));

    private readonly Label _label;
    private readonly Border _border;

    public ChoiceButton()
    {
        _label = new Label { FontFamily = Res.Font("FontBold"), FontSize = 14, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.NoWrap };
        _border = new Border { Padding = new Thickness(12, 0), HeightRequest = 40, Content = _label };
        Content = _border;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (IsEnabled && Command?.CanExecute(CommandParameter) == true) Command.Execute(CommandParameter);
        };
        GestureRecognizers.Add(tap);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsEnabled)) Update();
        };
        Update();
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public bool IsOption { get => (bool)GetValue(IsOptionProperty); set => SetValue(IsOptionProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    /// <summary>Height of the button (40 for chips, 48 for options).</summary>
    public double ButtonHeight { get => _border.HeightRequest; set => _border.HeightRequest = value; }

    private void Update()
    {
        Opacity = IsEnabled ? 1 : 0.45;
        if (IsOption)
        {
            _border.Stroke = IsSelected ? Res.Color("PartyCyan") : Res.Color("Border");
            _border.BackgroundColor = IsSelected ? Microsoft.Maui.Graphics.Color.FromArgb("#1F22D3EE") : Colors.Transparent;
            _label.TextColor = IsSelected ? Res.Color("PartyCyan") : Res.Color("Text");
        }
        else
        {
            _border.Stroke = IsSelected ? Res.Color("Primary") : Res.Color("Border");
            _border.BackgroundColor = IsSelected ? Res.Color("Primary") : Res.Color("Surface");
            _label.TextColor = IsSelected ? Res.Color("OnPrimary") : Res.Color("Text");
        }
    }
}

/// <summary>Square slider (mockup 3): 6 px track, Primary fill, white 20 × 20 thumb. Integer values.</summary>
public sealed class SquareSlider : ContentView
{
    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(int), typeof(SquareSlider), 0, BindingMode.TwoWay, propertyChanged: (b, _, _) => ((SquareSlider)b).Reposition());

    public static readonly BindableProperty MinimumProperty =
        BindableProperty.Create(nameof(Minimum), typeof(int), typeof(SquareSlider), 0, propertyChanged: (b, _, _) => ((SquareSlider)b).Reposition());

    public static readonly BindableProperty MaximumProperty =
        BindableProperty.Create(nameof(Maximum), typeof(int), typeof(SquareSlider), 10, propertyChanged: (b, _, _) => ((SquareSlider)b).Reposition());

    private readonly BoxView _fill;
    private readonly BoxView _thumb;
    private readonly Grid _root;
    private double _panStartX;

    public SquareSlider()
    {
        var track = new BoxView { HeightRequest = 6, Color = Res.Color("Border"), VerticalOptions = LayoutOptions.Center };
        _fill = new BoxView { HeightRequest = 6, Color = Res.Color("Primary"), VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Start };
        _thumb = new BoxView { WidthRequest = 20, HeightRequest = 20, Color = Res.Color("Text"), VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Start };
        _root = new Grid { HeightRequest = 32, BackgroundColor = Colors.Transparent, Children = { track, _fill, _thumb } };
        Content = _root;
        _root.SizeChanged += (_, _) => Reposition();

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) =>
        {
            if (e.GetPosition(_root) is { } p) SetFromX(p.X);
        };
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            if (e.StatusType == GestureStatus.Started) _panStartX = _thumb.TranslationX + 10;
            if (e.StatusType == GestureStatus.Running) SetFromX(_panStartX + e.TotalX);
        };
        _root.GestureRecognizers.Add(tap);
        _root.GestureRecognizers.Add(pan);
    }

    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    private void SetFromX(double x)
    {
        var width = Math.Max(1, _root.Width - 20);
        var fraction = Math.Clamp((x - 10) / width, 0, 1);
        Value = (int)Math.Round(Minimum + fraction * (Maximum - Minimum));
    }

    private void Reposition()
    {
        if (_root.Width <= 0 || Maximum <= Minimum) return;
        var fraction = Math.Clamp((double)(Value - Minimum) / (Maximum - Minimum), 0, 1);
        var x = fraction * (_root.Width - 20);
        _thumb.TranslationX = x;
        _fill.WidthRequest = x + 10;
    }
}

/// <summary>Info tile with a 3 px accent bar on top: caption + value (Draw screen: Dotyk / Ruch / Czas; mockup 4).</summary>
public sealed class InfoTile : ContentView
{
    public static readonly BindableProperty CaptionProperty =
        BindableProperty.Create(nameof(Caption), typeof(string), typeof(InfoTile), string.Empty, propertyChanged: (b, _, n) => ((InfoTile)b)._caption.Text = (string)n);

    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(string), typeof(InfoTile), string.Empty, propertyChanged: (b, _, n) => ((InfoTile)b)._value.Text = (string)n);

    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(InfoTile), Colors.Transparent, propertyChanged: (b, _, n) => ((InfoTile)b)._accent.Color = (Color)n);

    private readonly Label _caption;
    private readonly Label _value;
    private readonly BoxView _accent;

    public InfoTile()
    {
        _caption = new Label { FontSize = 12, TextColor = Res.Color("Muted") };
        _value = new Label { FontFamily = Res.Font("FontBold"), FontSize = 15, LineBreakMode = LineBreakMode.TailTruncation };
        _accent = new BoxView { HeightRequest = 3, VerticalOptions = LayoutOptions.Start };
        Content = new Border
        {
            Padding = 0,
            Content = new Grid
            {
                Children =
                {
                    new VerticalStackLayout { Padding = new Thickness(12, 13, 12, 12), Spacing = 2, Children = { _caption, _value } },
                    _accent,
                },
            },
        };
    }

    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
}

/// <summary>Row with a 3 px accent bar on the left: caption on the left, value on the right (mockups 8, 9).</summary>
public sealed class AccentRow : ContentView
{
    public static readonly BindableProperty CaptionProperty =
        BindableProperty.Create(nameof(Caption), typeof(string), typeof(AccentRow), string.Empty, propertyChanged: (b, _, n) => ((AccentRow)b)._caption.Text = (string)n);

    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(string), typeof(AccentRow), string.Empty, propertyChanged: (b, _, n) => ((AccentRow)b)._value.Text = (string)n);

    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(AccentRow), Colors.Transparent, propertyChanged: (b, _, n) => ((AccentRow)b)._accent.Color = (Color)n);

    private readonly Label _caption;
    private readonly Label _value;
    private readonly BoxView _accent;

    public AccentRow()
    {
        _caption = new Label { FontSize = 14, TextColor = Res.Color("Muted"), VerticalOptions = LayoutOptions.Center };
        _value = new Label { FontFamily = Res.Font("FontBold"), FontSize = 15, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End, HorizontalTextAlignment = TextAlignment.End };
        _accent = new BoxView { WidthRequest = 3, HorizontalOptions = LayoutOptions.Start };
        Content = new Border
        {
            Padding = 0,
            Content = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(3), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
                ColumnSpacing = 0,
                Children = { _accent },
            },
        };
        var grid = (Grid)((Border)Content).Content!;
        _caption.Margin = new Thickness(14, 10, 8, 10);
        _value.Margin = new Thickness(0, 10, 14, 10);
        grid.Add(_caption, 1);
        grid.Add(_value, 2);
    }

    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
}

/// <summary>Line icon from an SVG path in a 24 × 24 box, square caps (spec 13).</summary>
public sealed class LineIcon : ContentView
{
    public static readonly BindableProperty DataProperty =
        BindableProperty.Create(nameof(Data), typeof(string), typeof(LineIcon), string.Empty, propertyChanged: (b, _, _) => ((LineIcon)b).Update());

    public static readonly BindableProperty StrokeColorProperty =
        BindableProperty.Create(nameof(StrokeColor), typeof(Color), typeof(LineIcon), Colors.White, propertyChanged: (b, _, _) => ((LineIcon)b).Update());

    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(nameof(IconSize), typeof(double), typeof(LineIcon), 18.0, propertyChanged: (b, _, _) => ((LineIcon)b).Update());

    public static readonly BindableProperty StrokeWidthProperty =
        BindableProperty.Create(nameof(StrokeWidth), typeof(double), typeof(LineIcon), 2.0, propertyChanged: (b, _, _) => ((LineIcon)b).Update());

    private readonly Microsoft.Maui.Controls.Shapes.Path _path = new() { StrokeLineCap = PenLineCap.Square, StrokeLineJoin = PenLineJoin.Miter, Aspect = Stretch.None };

    public LineIcon()
    {
        Content = _path;
        InputTransparent = true;
    }

    public string Data { get => (string)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public Color StrokeColor { get => (Color)GetValue(StrokeColorProperty); set => SetValue(StrokeColorProperty, value); }
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    private void Update()
    {
        if (string.IsNullOrEmpty(Data)) return;
        _path.Data = (Microsoft.Maui.Controls.Shapes.Geometry)new PathGeometryConverter().ConvertFromInvariantString(Data)!;
        _path.Stroke = new SolidColorBrush(StrokeColor);
        _path.StrokeThickness = StrokeWidth;
        var scale = IconSize / 24.0;
        _path.Scale = scale;
        _path.WidthRequest = _path.HeightRequest = 24;
        WidthRequest = HeightRequest = IconSize;
        _path.AnchorX = _path.AnchorY = 0;
    }
}
