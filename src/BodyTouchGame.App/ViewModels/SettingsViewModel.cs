using System.Globalization;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BodyTouchGame.App.ViewModels;

/// <summary>Settings screen (mockup 10). Effect switches are saved immediately.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;

    public SettingsViewModel(ISettingsService settings, INavigationService navigation, IAppInfo appInfo, LocalizationService localization)
    {
        _settings = settings;
        _navigation = navigation;
        SoundEnabled = settings.SoundEnabled;
        ConfettiEnabled = settings.ConfettiEnabled;
        VersionText = string.Format(AppCulture.Format, AppResources.VersionFormat, appInfo.VersionString);
        var language = localization.Choice.Length == 0
            ? string.Format(AppCulture.Format, AppResources.LanguageSystem, LocalizationService.SystemLanguage.NativeName)
            : localization.Current.NativeName;
        LanguageText = $"{AppResources.Language}: {language}";
    }

    [ObservableProperty]
    public partial bool SoundEnabled { get; set; }

    [ObservableProperty]
    public partial bool ConfettiEnabled { get; set; }

    public string VersionText { get; }

    public string LanguageText { get; }

    partial void OnSoundEnabledChanged(bool value) => _settings.SoundEnabled = value;
    partial void OnConfettiEnabledChanged(bool value) => _settings.ConfettiEnabled = value;

    [RelayCommand]
    private Task DefaultConfigAsync() => _navigation.GoToAsync($"{Routes.GameConfig}?mode=default");

    [RelayCommand]
    private Task LanguageAsync() => _navigation.GoToAsync(Routes.Language);

    [RelayCommand]
    private Task ZoneEditorAsync() => _navigation.GoToAsync(Routes.ZoneEditor);

    [RelayCommand]
    private Task ExcludedZonesAsync() => _navigation.GoToAsync(Routes.Exclusions);
}
