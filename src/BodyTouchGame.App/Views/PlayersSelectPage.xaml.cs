using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class PlayersSelectPage : ContentPage
{
    private readonly PlayersSelectViewModel _viewModel;

    public PlayersSelectPage(PlayersSelectViewModel viewModel)
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
