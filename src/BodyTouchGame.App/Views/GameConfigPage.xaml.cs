using BodyTouchGame.App.Controls;
using BodyTouchGame.App.ViewModels;

namespace BodyTouchGame.App.Views;

public partial class GameConfigPage : ContentPage
{
    private readonly GameConfigViewModel _viewModel;

    public GameConfigPage(GameConfigViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        // Touch method: 2 × 2 option buttons (cyan when selected).
        for (var i = 0; i < viewModel.TouchMethods.Count; i++)
        {
            var item = viewModel.TouchMethods[i];
            var button = new ChoiceButton { Text = item.Name, IsOption = true, ButtonHeight = 48, Command = viewModel.SelectTouchMethodCommand, CommandParameter = item };
            Grid.SetColumn(button, i % 2);
            Grid.SetRow(button, i / 2);
            TouchMethodGrid.Children.Add(button);
        }

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameConfigViewModel.TouchMethod)) UpdateTouchMethods();
        };
        UpdateTouchMethods();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private void UpdateTouchMethods()
    {
        foreach (var button in TouchMethodGrid.Children.OfType<ChoiceButton>())
            button.IsSelected = ((TouchMethodItem)button.CommandParameter!).Method == _viewModel.TouchMethod;
    }
}
