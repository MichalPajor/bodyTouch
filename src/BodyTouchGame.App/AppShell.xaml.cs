using BodyTouchGame.App.Services;
using BodyTouchGame.App.Views;

namespace BodyTouchGame.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(Routes.Settings, typeof(SettingsPage));
        Routing.RegisterRoute(Routes.Exclusions, typeof(ExclusionsPage));
        Routing.RegisterRoute(Routes.PlayersSelect, typeof(PlayersSelectPage));
        Routing.RegisterRoute(Routes.GameConfig, typeof(GameConfigPage));
        Routing.RegisterRoute(Routes.ZoneSet, typeof(ZoneSetPage));
        Routing.RegisterRoute(Routes.Game, typeof(GamePage));
    }
}
