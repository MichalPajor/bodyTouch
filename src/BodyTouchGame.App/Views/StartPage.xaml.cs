using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class StartPage : ContentPage
{
    private static bool _tutorialOffered;
    private readonly StartViewModel _viewModel;
    private readonly IPreferences _preferences;

    private readonly Services.AdService _ads;

    public StartPage(StartViewModel viewModel, IPreferences preferences, Services.AdService ads)
    {
        _ads = ads;
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _preferences = preferences;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // First start: the animated tutorial (once; "?" shows it again any time).
        if (!_tutorialOffered && !TutorialViewModel.WasSeen(_preferences))
        {
            _tutorialOffered = true;
            await Shell.Current.GoToAsync(Services.Routes.Tutorial);
            return;
        }

        // Ads consent (UMP form, EEA) and the ads SDK – after the tutorial, never over it.
        if (Platform.CurrentActivity is { } activity) _ads.Start(activity);
        await _viewModel.LoadAsync();
    }
}
