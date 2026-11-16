using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace BodyTouchGame.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (Window is not { } window) return;

        // Dark system bars with light icons (the app has a dark theme only). From Android 15 the bars are
        // transparent (edge-to-edge) and show the window background.
        var background = Android.Graphics.Color.ParseColor("#0D0A1A");
        window.DecorView.SetBackgroundColor(background);
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
#pragma warning disable CA1422 // deprecated from API 35, used only below it
            window.SetStatusBarColor(background);
            window.SetNavigationBarColor(background);
#pragma warning restore CA1422
        }

        if (WindowCompat.GetInsetsController(window, window.DecorView) is { } insets)
        {
            insets.AppearanceLightStatusBars = false;
            insets.AppearanceLightNavigationBars = false;
        }
    }
}
