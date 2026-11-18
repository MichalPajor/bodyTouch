using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class TutorialPage : ContentPage
{
    private const double SceneAspect = 300.0 / 360.0;   // height / width of tutorial.html's scene
    private readonly TutorialViewModel _viewModel;
    private bool _loaded;

    public TutorialPage(TutorialViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        Scene.Source = new UrlWebViewSource { Url = "tutorial/tutorial.html" };
        Scene.Navigated += async (_, _) =>
        {
            _loaded = true;
            await ShowSceneAsync(_viewModel.Index);
        };
        viewModel.SceneChanged += async (_, index) => await ShowSceneAsync(index);
        SceneFrame.SizeChanged += (_, _) => Scene.HeightRequest = SceneFrame.Width * SceneAspect;

        // Swipe left / right between the scenes.
        var left = new SwipeGestureRecognizer { Direction = SwipeDirection.Left };
        left.Swiped += (_, _) => viewModel.NextCommand.Execute(null);
        var right = new SwipeGestureRecognizer { Direction = SwipeDirection.Right };
        right.Swiped += (_, _) => viewModel.PreviousCommand.Execute(null);
        Content.GestureRecognizers.Add(left);
        Content.GestureRecognizers.Add(right);
    }

    private async Task ShowSceneAsync(int index)
    {
        if (!_loaded) return;
        try
        {
            await Scene.EvaluateJavaScriptAsync($"showScene({index})");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[BTG] tutorial scene: {e.Message}");
        }
    }

    /// <summary>Android back = skip (the tutorial is never a dead end).</summary>
    protected override bool OnBackButtonPressed()
    {
        _viewModel.FinishCommand.Execute(null);
        return true;
    }
}
