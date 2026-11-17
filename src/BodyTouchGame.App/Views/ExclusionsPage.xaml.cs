using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class ExclusionsPage : ContentPage
{
    private readonly ExclusionsViewModel _viewModel;

    public ExclusionsPage(ExclusionsViewModel viewModel)
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
