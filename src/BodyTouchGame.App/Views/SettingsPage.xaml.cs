using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        Scroller.SizeChanged += (_, _) => ContentGrid.MinimumHeightRequest = Scroller.Height;
    }
}
