namespace BodyTouchGame.App.Services;

/// <summary>User options from the Settings screen, stored in <see cref="IPreferences"/>.</summary>
public interface ISettingsService
{
    bool SoundEnabled { get; set; }
    bool VibrationEnabled { get; set; }
    bool ConfettiEnabled { get; set; }
}

public sealed class SettingsService(IPreferences preferences) : ISettingsService
{
    private const string SoundKey = "settings.sound";
    private const string VibrationKey = "settings.vibration";
    private const string ConfettiKey = "settings.confetti";

    public bool SoundEnabled
    {
        get => preferences.Get(SoundKey, true);
        set => preferences.Set(SoundKey, value);
    }

    public bool VibrationEnabled
    {
        get => preferences.Get(VibrationKey, true);
        set => preferences.Set(VibrationKey, value);
    }

    public bool ConfettiEnabled
    {
        get => preferences.Get(ConfettiKey, true);
        set => preferences.Set(ConfettiKey, value);
    }
}

/// <summary>Shell routes of the app.</summary>
public static class Routes
{
    public const string Start = "start";
    public const string Settings = "settings";

    /// <summary>Stage 3 technical prototype (developer screen: tap on the version label in Settings).</summary>
    public const string Prototype = "prototype";
}

public interface INavigationService
{
    Task GoToAsync(string route);
    Task GoBackAsync();
}

public sealed class ShellNavigationService : INavigationService
{
    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
}
