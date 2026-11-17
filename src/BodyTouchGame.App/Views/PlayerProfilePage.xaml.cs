using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class PlayerProfilePage : ContentPage
{
    private readonly PlayerProfileViewModel _viewModel;

    public PlayerProfilePage(PlayerProfileViewModel viewModel)
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
