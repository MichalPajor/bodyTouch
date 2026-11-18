using System.Collections.ObjectModel;
using System.Globalization;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BodyTouchGame.App.ViewModels;

public sealed record LanguageItem(string Code, string Name, bool IsSelected);

/// <summary>Settings → Język: the phone language or one of the supported languages; applied immediately.</summary>
public sealed partial class LanguageViewModel(LocalizationService localization) : ObservableObject
{
    public ObservableCollection<LanguageItem> Languages { get; } = [];

    public void Load()
    {
        Languages.Clear();
        var choice = localization.Choice;
        Languages.Add(new LanguageItem(string.Empty,
            string.Format(AppCulture.Format, AppResources.LanguageSystem, LocalizationService.SystemLanguage.NativeName), choice.Length == 0));
        foreach (var language in LocalizationService.Languages)
            Languages.Add(new LanguageItem(language.Code, language.NativeName, choice == language.Code));
    }

    [RelayCommand]
    private async Task SelectAsync(LanguageItem item)
    {
        if (item.IsSelected) return;
        localization.Choice = item.Code;
        localization.Apply();
        // Open screens were built with the old texts: rebuild them and come back to Settings.
        await App.ReloadShellAsync($"//{Routes.Start}/{Routes.Settings}");
    }
}
