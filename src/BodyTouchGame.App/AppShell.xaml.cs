using BodyTouchGame.App.Services;
using BodyTouchGame.App.Views;

namespace BodyTouchGame.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(Routes.Settings, typeof(SettingsPage));
        Routing.RegisterRoute(Routes.Prototype, typeof(PrototypePage));
    }
}
