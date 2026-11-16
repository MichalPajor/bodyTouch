using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace BodyTouchGame.App.Controls;

/// <summary>
/// Primary button (spec 13): Primary background, dark Unbounded ExtraBold text, hard PartyPink shadow without blur.
/// The shadow is a solid rectangle offset by <see cref="ShadowOffset"/>; the control reserves that space on the
/// right and bottom, so give it a negative right/bottom margin to align the button with other content.
/// </summary>
public sealed class PartyButton : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(PartyButton), string.Empty, propertyChanged: (b, _, n) => ((PartyButton)b)._button.Text = (string)n);

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(PartyButton), propertyChanged: (b, _, n) => ((PartyButton)b)._button.Command = (ICommand?)n);

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(PartyButton), propertyChanged: (b, _, n) => ((PartyButton)b)._button.CommandParameter = n);

    public static readonly BindableProperty ButtonHeightProperty =
        BindableProperty.Create(nameof(ButtonHeight), typeof(double), typeof(PartyButton), 58.0, propertyChanged: (b, _, _) => ((PartyButton)b).UpdateLayout());

    public static readonly BindableProperty FontSizeProperty =
        BindableProperty.Create(nameof(FontSize), typeof(double), typeof(PartyButton), 18.0, propertyChanged: (b, _, n) => ((PartyButton)b)._button.FontSize = (double)n);

    public static readonly BindableProperty ShadowOffsetProperty =
        BindableProperty.Create(nameof(ShadowOffset), typeof(double), typeof(PartyButton), 5.0, propertyChanged: (b, _, _) => ((PartyButton)b).UpdateLayout());

    private readonly Button _button;
    private readonly Rectangle _shadow;

    public PartyButton()
    {
        _shadow = new Rectangle { Fill = (Brush)Application.Current!.Resources["PartyPinkBrush"], StrokeThickness = 0 };
        _button = new Button
        {
            BackgroundColor = (Color)Application.Current.Resources["Primary"],
            TextColor = (Color)Application.Current.Resources["OnPrimary"],
            FontFamily = (string)Application.Current.Resources["FontTitle"],
            FontSize = 18,
            BorderWidth = 0,
            CornerRadius = 0,
            Padding = new Thickness(12, 0),
        };
        _button.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsEnabled)) Opacity = _button.IsEnabled ? 1 : 0.45;
        };
        Content = new Grid { Children = { _shadow, _button } };
        UpdateLayout();
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public double ButtonHeight { get => (double)GetValue(ButtonHeightProperty); set => SetValue(ButtonHeightProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public double ShadowOffset { get => (double)GetValue(ShadowOffsetProperty); set => SetValue(ShadowOffsetProperty, value); }

    public event EventHandler? Clicked
    {
        add => _button.Clicked += value;
        remove => _button.Clicked -= value;
    }

    private void UpdateLayout()
    {
        var offset = ShadowOffset;
        _button.HeightRequest = ButtonHeight;
        _button.Margin = new Thickness(0, 0, offset, offset);
        _shadow.HeightRequest = ButtonHeight;
        _shadow.Margin = new Thickness(offset, offset, 0, 0);
    }
}
