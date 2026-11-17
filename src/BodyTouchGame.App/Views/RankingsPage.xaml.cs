using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class RankingsPage : ContentPage
{
    private readonly RankingsViewModel _viewModel;

    public RankingsPage(RankingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
