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

	/// <summary>The "remove_ads" purchase state is refreshed from Google Play in the background on start.
	/// (Ads consent starts on the Start screen, after the first-start tutorial.)</summary>
	protected override void OnStart()
	{
		base.OnStart();
		var services = IPlatformApplication.Current!.Services;
		Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1.5), async () =>
			await services.GetRequiredService<Services.PurchaseService>().RefreshAsync());
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