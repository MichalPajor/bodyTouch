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

    public SettingsViewModel(ISettingsService settings, INavigationService navigation, IAppInfo appInfo)
    {
        _settings = settings;
        _navigation = navigation;
        SoundEnabled = settings.SoundEnabled;
        VibrationEnabled = settings.VibrationEnabled;
        ConfettiEnabled = settings.ConfettiEnabled;
        VersionText = string.Format(CultureInfo.CurrentCulture, AppResources.VersionFormat, appInfo.VersionString);
    }

    [ObservableProperty]
    public partial bool SoundEnabled { get; set; }

    [ObservableProperty]
    public partial bool VibrationEnabled { get; set; }

    [ObservableProperty]
    public partial bool ConfettiEnabled { get; set; }

    public string VersionText { get; }

    partial void OnSoundEnabledChanged(bool value) => _settings.SoundEnabled = value;
    partial void OnVibrationEnabledChanged(bool value) => _settings.VibrationEnabled = value;
    partial void OnConfettiEnabledChanged(bool value) => _settings.ConfettiEnabled = value;

    // Screens of later stages: visible but disabled.
    [RelayCommand(CanExecute = nameof(IsAvailableLater))]
    private void DefaultConfig() { }

    [RelayCommand(CanExecute = nameof(IsAvailableLater))]
    private void ZoneEditor() { }

    [RelayCommand]
    private Task ExcludedZonesAsync() => _navigation.GoToAsync(Routes.Exclusions);

    [RelayCommand(CanExecute = nameof(IsAvailableLater))]
    private void ExportData() { }

    [RelayCommand(CanExecute = nameof(IsAvailableLater))]
    private void ImportData() { }

    private static bool IsAvailableLater() => false;
}
