using Microsoft.Maui.Controls.Shapes;

namespace BodyTouchGame.App.Controls;

public enum PartyDialogKind
{
    Info,
    Question,
    Input,
}

/// <summary>
/// The app's own dialog ("Impreza – konfetti" style, spec 13) instead of the system alert: a dimmed screen with a
/// card – accent icon square with the pink hard shadow, Unbounded title, message, optional text field, secondary
/// button and the primary Party button. Shown as a transparent modal page; the result comes from <see cref="Result"/>.
/// </summary>
public sealed class PartyDialog : ContentPage
{
    private readonly TaskCompletionSource<(bool Accepted, string? Text)> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Entry? _entry;
    private bool _closing;

    public PartyDialog(PartyDialogKind kind, string title, string message, string accept, string? cancel,
        int maxLength = 0, string initialValue = "")
    {
        BackgroundColor = Color.FromArgb("#D90B0A14");
        Shell.SetPresentationMode(this, PresentationMode.ModalNotAnimated);

        var accent = kind == PartyDialogKind.Question ? Res.Color("PartyPink") : kind == PartyDialogKind.Input ? Res.Color("PartyCyan") : Res.Color("PartyViolet");

        // Icon: orange square with the pink hard shadow (as on the curtain screen).
        var icon = new Grid
        {
            WidthRequest = 64,
            HeightRequest = 64,
            HorizontalOptions = LayoutOptions.Start,
            Children =
            {
                new BoxView { Color = Res.Color("PartyPink"), Margin = new Thickness(5, 5, 0, 0) },
                new Grid
                {
                    BackgroundColor = Res.Color("Primary"),
                    Margin = new Thickness(0, 0, 5, 5),
                    Children =
                    {
                        new LineIcon
                        {
                            Data = kind switch
                            {
                                PartyDialogKind.Question => "M9 9 A3 3 0 1 1 13.2 11.8 C12.5 12.2 12 12.8 12 13.6 V14.5 M12 18 V18.6",
                                PartyDialogKind.Input => "M4 20 L8.5 19 L19 8.5 L15.5 5 L5 15.5 Z M13.5 7 L17 10.5",
                                _ => "M12 6.5 V13.5 M12 17.5 V18.2",
                            },
                            StrokeColor = Res.Color("OnPrimary"),
                            StrokeWidth = 2.6,
                            IconSize = 30,
                            HorizontalOptions = LayoutOptions.Center,
                            VerticalOptions = LayoutOptions.Center,
                        },
                    },
                },
            },
        };

        var content = new VerticalStackLayout
        {
            Spacing = 14,
            Padding = new Thickness(22, 26, 22, 22),
            Children =
            {
                icon,
                new Label { Text = title, FontFamily = Res.Font("FontTitle"), FontSize = 22, LineHeight = 0.95, Margin = new Thickness(0, 4, 0, 0) },
            },
        };
        if (!string.IsNullOrEmpty(message))
            content.Children.Add(new Label { Text = message, FontSize = 15, TextColor = Res.Color("Muted") });

        if (kind == PartyDialogKind.Input)
        {
            _entry = new Entry
            {
                Text = initialValue,
                MaxLength = maxLength > 0 ? maxLength : int.MaxValue,
                FontSize = 17,
                ReturnType = ReturnType.Done,
                Keyboard = Keyboard.Text,
            };
            _entry.Completed += (_, _) => Close(true);
            content.Children.Add(new Border
            {
                Padding = new Thickness(12, 2),
                BackgroundColor = Res.Color("Background"),
                Stroke = Res.Color("PartyCyan"),
                Content = _entry,
            });
        }

        var acceptButton = new PartyButton { Text = accept, ButtonHeight = 50, FontSize = 16 };
        acceptButton.Clicked += (_, _) => Close(true);
        var buttons = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        if (cancel is not null)
        {
            var cancelButton = new Button { Text = cancel, HeightRequest = 50, Padding = new Thickness(18, 0), VerticalOptions = LayoutOptions.Start };
            cancelButton.Clicked += (_, _) => Close(false);
            buttons.ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)];
            buttons.Add(cancelButton, 0);
            buttons.Add(acceptButton, 1);
        }
        else
        {
            buttons.Add(acceptButton);
        }

        acceptButton.Margin = new Thickness(0, 0, -5, -5);
        content.Children.Add(buttons);

        var card = new Border
        {
            Padding = 0,
            // An input dialog sits near the top so the on-screen keyboard does not cover its buttons.
            Margin = kind == PartyDialogKind.Input ? new Thickness(20, 64, 20, 0) : new Thickness(20, 0),
            VerticalOptions = kind == PartyDialogKind.Input ? LayoutOptions.Start : LayoutOptions.Center,
            BackgroundColor = Res.Color("SurfaceSolid"),
            Content = new Grid
            {
                Children =
                {
                    content,
                    new BoxView { Color = accent, HeightRequest = 4, VerticalOptions = LayoutOptions.Start },
                },
            },
        };

        // A tap outside the card cancels (like the system dialog); the card itself swallows taps.
        var root = new Grid { Children = { card } };
        var outside = new TapGestureRecognizer();
        outside.Tapped += (_, _) => Close(false);
        root.GestureRecognizers.Add(outside);
        card.GestureRecognizers.Add(new TapGestureRecognizer());
        Content = root;
    }

    /// <summary>Accepted (or false on cancel / back / tap outside) and the entered text for input dialogs.</summary>
    public Task<(bool Accepted, string? Text)> Result => _result.Task;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_entry is null) return;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
        {
            _entry.Focus();
            _entry.CursorPosition = _entry.Text?.Length ?? 0;
        });
    }

    protected override bool OnBackButtonPressed()
    {
        Close(false);
        return true;
    }

    private async void Close(bool accepted)
    {
        if (_closing) return;
        _closing = true;
        _entry?.Unfocus();
        await Navigation.PopModalAsync(false);
        _result.TrySetResult((accepted, accepted ? _entry?.Text : null));
    }
}
