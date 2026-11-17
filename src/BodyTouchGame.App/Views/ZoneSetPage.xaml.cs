using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class ZoneSetPage : ContentPage
{
    private readonly ZoneSetViewModel _viewModel;

    public ZoneSetPage(ZoneSetViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync(Body.Bridge, Body.WhenModelLoaded);
    }
}
