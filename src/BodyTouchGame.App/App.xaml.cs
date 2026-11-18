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

	/// <summary>
	/// Rebuilds the screens after a language change (texts are read when a page is created), returning to Settings.
	/// </summary>
	public static async Task ReloadShellAsync(string route)
	{
		var window = Current?.Windows.FirstOrDefault();
		if (window is null) return;
		var shell = new AppShell();
		window.Page = shell;
		await shell.GoToAsync(route, false);
	}
}