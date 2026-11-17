using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class ZoneEditorPage : ContentPage
{
    private readonly ZoneEditorViewModel _viewModel;

    public ZoneEditorPage(ZoneEditorViewModel viewModel)
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
