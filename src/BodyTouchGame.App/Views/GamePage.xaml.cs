using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class GamePage : ContentPage
{
    private readonly GameViewModel _viewModel;
    private Window? _window;

    public GamePage(GameViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        viewModel.ConfettiRequested += (_, _) => Confetti.Play();

        // Texts over the 3D view: the model is framed in the area they leave free (the view itself keeps its size).
        DrawInfo.SizeChanged += (_, _) => UpdateBodyInsets();
        RevealInfo.SizeChanged += (_, _) => UpdateBodyInsets();
        RevealLegend.SizeChanged += (_, _) => UpdateBodyInsets();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameViewModel.State)) UpdateBodyInsets();
        };
    }

    /// <summary>Bottom part of the text blocks' scrim that fades out; the model may reach under it.</summary>
    private const double ScrimFade = 10;

    private void UpdateBodyInsets()
    {
        var top = _viewModel.IsDraw ? DrawInfo.Height - ScrimFade : _viewModel.IsReveal ? RevealInfo.Height - ScrimFade : 0;
        var bottom = _viewModel.IsReveal ? RevealLegend.Height : 0;
        Body.SetContentInsets(Math.Max(0, top), Math.Max(0, bottom));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync(Body.Bridge, Body.WhenModelLoaded);
    }

    // The lifecycle subscription follows navigation, not Appearing/Disappearing: Shell raises Disappearing when the
    // app goes to the background, which would unsubscribe before Window.Stopped arrives.
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (_window is not null) return;
        _window = Window ?? Application.Current?.Windows.FirstOrDefault();
        if (_window is not null) _window.Stopped += OnWindowStopped;
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        if (_window is not null) _window.Stopped -= OnWindowStopped;
        _window = null;
        base.OnNavigatedFrom(args);
    }

    /// <summary>App in the background or screen off: the session goes to the curtain (spec 5).</summary>
    private void OnWindowStopped(object? sender, EventArgs e)
    {
        Console.WriteLine("[BTG] window stopped -> interrupt");
        _viewModel.OnAppStopped();
    }

    /// <summary>Android back: abort the round or end the game, always with a confirmation.</summary>
    protected override bool OnBackButtonPressed()
    {
        _ = _viewModel.HandleBackAsync();
        return true;
    }
}
