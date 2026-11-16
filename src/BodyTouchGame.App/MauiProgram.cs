using BodyTouchGame.App.Services;
using BodyTouchGame.App.ViewModels;
using BodyTouchGame.App.Views;
using BodyTouchGame.Core.Persistence;
using BodyTouchGame.Data;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

namespace BodyTouchGame.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Unbounded-Bold.ttf", "UnboundedBold");
                fonts.AddFont("Unbounded-ExtraBold.ttf", "UnboundedExtraBold");
                fonts.AddFont("PlusJakartaSans-Regular.ttf", "JakartaRegular");
                fonts.AddFont("PlusJakartaSans-SemiBold.ttf", "JakartaSemiBold");
                fonts.AddFont("PlusJakartaSans-Bold.ttf", "JakartaBold");
                fonts.AddFont("PlusJakartaSans-ExtraBold.ttf", "JakartaExtraBold");
            });

        var services = builder.Services;

        // Platform
        services.AddSingleton(Preferences.Default);
        services.AddSingleton(AppInfo.Current);

        // Data (SQLite file in the app data directory)
        services.AddSingleton(_ => new BodyTouchDatabase(Path.Combine(FileSystem.AppDataDirectory, "bodytouch.db3")));
        services.AddSingleton<IPlayerRepository, PlayerRepository>();
        services.AddSingleton<IExclusionRepository, ExclusionRepository>();
        services.AddSingleton<IPresetRepository, PresetRepository>();
        services.AddSingleton<ICustomPointRepository, CustomPointRepository>();
        services.AddSingleton<ILevelOverrideRepository, LevelOverrideRepository>();
        services.AddSingleton<ISessionRepository, SessionRepository>();

        // App services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<INavigationService, ShellNavigationService>();
        services.AddSingleton<BodyDataService>();

        // Screens
        services.AddTransient<StartPage>();
        services.AddTransient<StartViewModel>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<PrototypePage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
