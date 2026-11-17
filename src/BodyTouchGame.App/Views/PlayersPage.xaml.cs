using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class PlayersPage : ContentPage
{
    private readonly PlayersViewModel _viewModel;

    public PlayersPage(PlayersViewModel viewModel)
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
