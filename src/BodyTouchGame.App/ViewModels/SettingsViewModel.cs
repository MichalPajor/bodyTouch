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

    private readonly PremiumState _premium;
    private readonly PurchaseService _purchases;
    private readonly AdService _ads;
    private readonly IDialogService _dialogs;

    public SettingsViewModel(ISettingsService settings, INavigationService navigation, LocalizationService localization,
        PremiumState premium, PurchaseService purchases, AdService ads, IDialogService dialogs)
    {
        _premium = premium;
        _purchases = purchases;
        _ads = ads;
        _dialogs = dialogs;
        _settings = settings;
        _navigation = navigation;
        SoundEnabled = settings.SoundEnabled;
        ConfettiEnabled = settings.ConfettiEnabled;
        var language = localization.Choice.Length == 0
            ? string.Format(AppCulture.Format, AppResources.LanguageSystem, LocalizationService.SystemLanguage.NativeName)
            : localization.Current.NativeName;
        LanguageText = $"{AppResources.Language}: {language}";
    }

    [ObservableProperty]
    public partial bool SoundEnabled { get; set; }

    [ObservableProperty]
    public partial bool ConfettiEnabled { get; set; }

    public string LanguageText { get; }

    public bool AdsRemoved => _premium.AdsRemoved;
    public bool CanBuy => !_premium.AdsRemoved;

    /// <summary>"Usuń reklamy · 9,99 zł" (price from Google Play when known).</summary>
    public string RemoveAdsText => _purchases.Price is { } price ? string.Format(AppCulture.Format, AppResources.RemoveAdsFormat, price) : AppResources.RemoveAds;

    [RelayCommand]
    private async Task RemoveAdsAsync()
    {
        if (await _purchases.BuyAsync()) await _dialogs.AlertAsync(AppResources.RemoveAds, AppResources.PurchaseThanks);
        else if (_purchases.Price is null) await _dialogs.AlertAsync(AppResources.RemoveAds, AppResources.StoreUnavailable);
        RefreshShop();
    }

    [RelayCommand]
    private async Task RestorePurchaseAsync()
    {
        if (!await _purchases.RefreshAsync()) await _dialogs.AlertAsync(AppResources.RestorePurchase, AppResources.StoreUnavailable);
        else await _dialogs.AlertAsync(AppResources.RestorePurchase, _premium.AdsRemoved ? AppResources.PurchaseRestored : AppResources.PurchaseNotFound);
        RefreshShop();
    }

    [RelayCommand]
    private void AdPrivacy()
    {
        if (Platform.CurrentActivity is { } activity) _ads.ShowPrivacyOptions(activity);
    }

    [RelayCommand]
    private Task RateAppAsync() => ReviewService.OpenStorePageAsync();

    public void RefreshShop()
    {
        OnPropertyChanged(nameof(AdsRemoved));
        OnPropertyChanged(nameof(CanBuy));
        OnPropertyChanged(nameof(RemoveAdsText));
    }

    partial void OnSoundEnabledChanged(bool value) => _settings.SoundEnabled = value;
    partial void OnConfettiEnabledChanged(bool value) => _settings.ConfettiEnabled = value;

    [RelayCommand]
    private Task DefaultConfigAsync() => _navigation.GoToAsync($"{Routes.GameConfig}?mode=default");

    [RelayCommand]
    private Task HowToPlayAsync() => _navigation.GoToAsync(Routes.Tutorial);

    [RelayCommand]
    private Task LanguageAsync() => _navigation.GoToAsync(Routes.Language);

    [RelayCommand]
    private Task ZoneEditorAsync() => _navigation.GoToAsync(Routes.ZoneEditor);

    [RelayCommand]
    private Task ExcludedZonesAsync() => _navigation.GoToAsync(Routes.Exclusions);
}
