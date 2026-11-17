using BodyTouchGame.App.Services;
using BodyTouchGame.App.ViewModels;
using BodyTouchGame.App.Views;
using BodyTouchGame.Core.Persistence;
using BodyTouchGame.Data;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;

namespace BodyTouchGame.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // The interface is Polish only: Polish number formats (4,8 cm) and sorting regardless of the phone language.
        var polish = new System.Globalization.CultureInfo("pl-PL");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = polish;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = polish;
        System.Globalization.CultureInfo.CurrentCulture = polish;
        System.Globalization.CultureInfo.CurrentUICulture = polish;

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

#if ANDROID
        // Square style (spec 13): no Material underline under text fields; the frame comes from the surrounding Border.
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (handler, _) =>
            handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
#endif

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
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<GameSetupService>();
        services.AddSingleton(AudioManager.Current);
        services.AddSingleton<FeedbackService>();

        // Screens
        services.AddTransient<StartPage>();
        services.AddTransient<StartViewModel>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ExclusionsPage>();
        services.AddTransient<ExclusionsViewModel>();
        services.AddTransient<PlayersSelectPage>();
        services.AddTransient<PlayersSelectViewModel>();
        services.AddTransient<GameConfigPage>();
        services.AddTransient<GameConfigViewModel>();
        services.AddTransient<ZoneSetPage>();
        services.AddTransient<ZoneSetViewModel>();
        services.AddTransient<GamePage>();
        services.AddTransient<GameViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
