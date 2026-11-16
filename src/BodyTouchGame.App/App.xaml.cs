using Microsoft.Extensions.DependencyInjection;

namespace BodyTouchGame.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Dark;   // the app has a dark theme only (spec 13)
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}