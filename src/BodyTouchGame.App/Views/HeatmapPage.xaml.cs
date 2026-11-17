using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class HeatmapPage : ContentPage
{
    private readonly HeatmapViewModel _viewModel;

    public HeatmapPage(HeatmapViewModel viewModel)
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
