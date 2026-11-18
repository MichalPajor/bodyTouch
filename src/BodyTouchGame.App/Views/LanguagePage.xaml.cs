using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class LanguagePage : ContentPage
{
    private readonly LanguageViewModel _viewModel;

    public LanguagePage(LanguageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Load();
    }
}
