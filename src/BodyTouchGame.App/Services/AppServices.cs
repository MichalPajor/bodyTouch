using System.Globalization;
using BodyTouchGame.App.Controls;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.Core.Config;
using Plugin.Maui.Audio;

namespace BodyTouchGame.App.Services;

/// <summary>User options from the Settings screen, stored in <see cref="IPreferences"/>.</summary>
public interface ISettingsService
{
    bool SoundEnabled { get; set; }
    bool ConfettiEnabled { get; set; }
}

public sealed class SettingsService(IPreferences preferences) : ISettingsService
{
    private const string SoundKey = "settings.sound";
    private const string ConfettiKey = "settings.confetti";

    public bool SoundEnabled
    {
        get => preferences.Get(SoundKey, true);
        set => preferences.Set(SoundKey, value);
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
    public const string Exclusions = "exclusions";
    public const string PlayersSelect = "players-select";
    public const string GameConfig = "game-config";
    public const string ZoneSet = "zone-set";
    public const string Game = "game";
    public const string Players = "players";
    public const string Player = "player";
    public const string Rankings = "rankings";
    public const string Heatmap = "heatmap";
    public const string ZoneEditor = "zone-editor";
}

public interface INavigationService
{
    Task GoToAsync(string route);
    Task GoBackAsync();

    /// <summary>Back to the Start screen (clears the navigation stack).</summary>
    Task GoHomeAsync();
}

public sealed class ShellNavigationService : INavigationService
{
    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");

    public Task GoHomeAsync() => Shell.Current.GoToAsync("//" + Routes.Start);
}

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);
    Task AlertAsync(string title, string message);

    /// <summary>Returns the entered text or null when cancelled.</summary>
    Task<string?> PromptAsync(string title, string message, string accept, int maxLength, string initialValue = "");
}

/// <summary>Dialogs in the app's own style (<see cref="PartyDialog"/>) instead of the system alerts.</summary>
public sealed class DialogService : IDialogService
{
    public async Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        (await ShowAsync(new PartyDialog(PartyDialogKind.Question, title, message, accept, cancel))).Accepted;

    public Task AlertAsync(string title, string message) =>
        ShowAsync(new PartyDialog(PartyDialogKind.Info, title, message, AppResources.Ok, null));

    public async Task<string?> PromptAsync(string title, string message, string accept, int maxLength, string initialValue = "")
    {
        var (accepted, text) = await ShowAsync(new PartyDialog(PartyDialogKind.Input, title, message, accept, AppResources.Cancel, maxLength, initialValue));
        return accepted ? text ?? string.Empty : null;
    }

    private static async Task<(bool Accepted, string? Text)> ShowAsync(PartyDialog dialog)
    {
        await Shell.Current.Navigation.PushModalAsync(dialog, false);
        return await dialog.Result;
    }
}

public enum SoundEffect
{
    TouchStart,
    TouchEnd,
    Tick,
    RevealPerfect,
    RevealPoints,
    RevealMiss,
    GameOver,
}

/// <summary>Game sounds (spec 13, Efekty), respecting the "Dźwięki" setting. No vibration (owner's decision after stage 6).</summary>
public sealed class FeedbackService(IAudioManager audio, ISettingsService settings)
{
    private readonly Dictionary<SoundEffect, IAudioPlayer?> _players = [];

    public Task TouchStartAsync() => PlayAsync(SoundEffect.TouchStart);

    public Task TouchEndAsync() => PlayAsync(SoundEffect.TouchEnd);

    public async Task PlayAsync(SoundEffect effect)
    {
        if (!settings.SoundEnabled) return;
        if (!_players.TryGetValue(effect, out var player))
            _players[effect] = player = await LoadAsync(effect);
        if (player is null) return;
        player.Stop();
        player.Play();
    }

    private async Task<IAudioPlayer?> LoadAsync(SoundEffect effect)
    {
        var file = effect switch
        {
            SoundEffect.TouchStart => "touch_start",
            SoundEffect.TouchEnd => "touch_end",
            SoundEffect.Tick => "tick",
            SoundEffect.RevealPerfect => "reveal_perfect",
            SoundEffect.RevealPoints => "reveal_points",
            SoundEffect.RevealMiss => "reveal_miss",
            _ => "game_over",
        };
        try
        {
            return audio.CreatePlayer(await FileSystem.OpenAppPackageFileAsync($"sounds/{file}.wav"));
        }
        catch (Exception e)
        {
            Console.WriteLine($"[BTG] sound {file}: {e.Message}");
            return null;
        }
    }
}

/// <summary>Polish plural forms: 1 runda, 2–4 rundy, 5+ rund (12–14 rund).</summary>
public static class PolishPlural
{
    public static string Format(int n, string one, string few, string many)
    {
        var form = n == 1 ? one
            : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few
            : many;
        return string.Format(CultureInfo.CurrentCulture, form, n);
    }

    public static string Rounds(int n) => Format(n, AppResources.RoundsOne, AppResources.RoundsFew, AppResources.RoundsMany);

    public static string Zones(int n) => Format(n, AppResources.ZonesOne, AppResources.ZonesFew, AppResources.ZonesMany);

    public static string Excluded(int n) => Format(n, AppResources.ExcludedOne, AppResources.ExcludedFew, AppResources.ExcludedMany);

    public static string Points(int n) => Format(n, AppResources.PointsOne, AppResources.PointsFew, AppResources.PointsMany);
}

/// <summary>Display texts for configuration values (from resources).</summary>
public static class ConfigTexts
{
    public static string TouchMethod(TouchMethod method) => method switch
    {
        Core.Config.TouchMethod.WholeHand => AppResources.TouchWholeHand,
        Core.Config.TouchMethod.TwoFingers => AppResources.TouchTwoFingers,
        Core.Config.TouchMethod.OneFinger => AppResources.TouchOneFinger,
        _ => AppResources.TouchTap,
    };

    public static string Level(ZoneLevel level) => level switch
    {
        ZoneLevel.Easy => AppResources.PresetEasy,
        ZoneLevel.Medium => AppResources.PresetMedium,
        ZoneLevel.Hard => AppResources.PresetHard,
        _ => AppResources.PresetHardcore,
    };

    /// <summary>Name of a preset: built-in keys are translated, custom names shown as entered.</summary>
    public static string PresetName(GamePreset preset) => preset.IsBuiltIn
        ? preset.Name switch
        {
            BuiltInPresets.EasyKey => AppResources.PresetEasy,
            BuiltInPresets.MediumKey => AppResources.PresetMedium,
            BuiltInPresets.HardKey => AppResources.PresetHard,
            _ => AppResources.PresetHardcore,
        }
        : preset.Name;

    public static string Movement(bool allowed) => allowed ? AppResources.MovementGentle : AppResources.MovementNone;

    /// <summary>"Dwa palce · delikatny ruch".</summary>
    public static string TouchLine(GameConfig config) =>
        $"{TouchMethod(config.TouchMethod)} · {(config.MovementAllowed ? AppResources.MovementGentleLower : AppResources.MovementNoneLower)}";

    public static string Seconds(int seconds) => string.Format(CultureInfo.CurrentCulture, AppResources.SecondsFormat, seconds);
}

/// <summary>Avatar colours (Party colours) assigned to new players in turn.</summary>
public static class AvatarPalette
{
    public static readonly string[] Colors = ["#FF3D9A", "#22D3EE", "#FF9A3C", "#8B5CF6", "#5B8CFF", "#34D399", "#F472B6", "#FACC15"];

    public static string Next(int existingPlayers) => Colors[existingPlayers % Colors.Length];
}

/// <summary>
/// Colours of zones on the model (zone editor, zone set preview): the slot from <see cref="BodyDataService.ColorSlot"/>
/// (neighbouring zones have different slots), so the borders are visible at a glance (owner's remark after stage 8).
/// </summary>
public static class ZonePalette
{
    private static readonly string[] WarmShades = ["#FF9A3C", "#D9480F", "#FFD43B", "#FF6B6B", "#F76707", "#FFC078", "#C2410C", "#FFA94D"];
    private static readonly string[] CyanShades = ["#22D3EE", "#0E7490", "#A5F3FC", "#0891B2", "#67E8F9", "#155E75", "#06B6D4", "#CFFAFE"];
    private static readonly string[] GreyShades = ["#A7ABB8", "#6F7586", "#D3D6DE", "#8C91A1", "#BEC2CD", "#5E6475", "#B0B4C0", "#7E8494"];

    public const string Removed = "#4A4560";

    /// <summary>Zone that is drawn / belongs to the level.</summary>
    public static string Warm(int slot) => WarmShades[slot % WarmShades.Length];

    /// <summary>Zone picked by hand.</summary>
    public static string Cyan(int slot) => CyanShades[slot % CyanShades.Length];

    /// <summary>Zone that can be tapped but is not drawn.</summary>
    public static string Grey(int slot) => GreyShades[slot % GreyShades.Length];
}
