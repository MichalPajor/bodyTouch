using System.Globalization;
using System.Text.Json;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.Core.Zones;

namespace BodyTouchGame.App.Services;

/// <summary>A language of the app: resource culture and the culture used for numbers and sorting.</summary>
public sealed record AppLanguage(string Code, string NativeName, string FormatCulture);

/// <summary>
/// App language (stage 10, D11): the phone's language when supported (otherwise English) or the one chosen in
/// Settings. Applying it sets the thread cultures and <see cref="AppResources.Culture"/>; screens created afterwards
/// use it, and <see cref="App.ReloadShellAsync"/> rebuilds the open screens without restarting the app.
/// </summary>
public sealed class LocalizationService(IPreferences preferences)
{
    private const string LanguageKey = "settings.language";

    public static readonly IReadOnlyList<AppLanguage> Languages =
    [
        new("pl", "Polski", "pl-PL"),
        new("en", "English", "en-US"),
        new("de", "Deutsch", "de-DE"),
        new("it", "Italiano", "it-IT"),
        new("fr", "Français", "fr-FR"),
        new("es", "Español", "es-ES"),
        new("pt", "Português (Brasil)", "pt-BR"),
        new("tr", "Türkçe", "tr-TR"),
        new("cs", "Čeština", "cs-CZ"),
        new("sk", "Slovenčina", "sk-SK"),
        new("uk", "Українська", "uk-UA"),
        new("nl", "Nederlands", "nl-NL"),
    ];

    /// <summary>The phone's culture, captured before the app changes the thread cultures.</summary>
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;

    /// <summary>Language of the phone, if the app supports it; otherwise English.</summary>
    public static AppLanguage SystemLanguage =>
        Languages.FirstOrDefault(l => l.Code == SystemCulture.TwoLetterISOLanguageName) ?? Languages.First(l => l.Code == "en");

    /// <summary>Chosen language code, or empty for "Język telefonu".</summary>
    public string Choice
    {
        get => preferences.Get(LanguageKey, string.Empty);
        set => preferences.Set(LanguageKey, value);
    }

    public AppLanguage Current => Languages.FirstOrDefault(l => l.Code == Choice) ?? SystemLanguage;

    /// <summary>Sets the cultures for the current language (call at start-up and after a change).</summary>
    public void Apply()
    {
        var language = Current;
        var format = new CultureInfo(language.FormatCulture);
        var ui = new CultureInfo(language.Code);
        CultureInfo.DefaultThreadCurrentCulture = format;
        CultureInfo.DefaultThreadCurrentUICulture = ui;
        CultureInfo.CurrentCulture = format;
        CultureInfo.CurrentUICulture = ui;
        AppResources.Culture = ui;
        AppCulture.Format = format;
        AppCulture.Ui = ui;
        ZoneNames.Language = language.Code;
    }
}

/// <summary>
/// The app's cultures, kept in static fields: a culture set on the thread inside an async method (the language
/// change runs in a command) is undone by .NET when the method returns, so formatting must not rely on
/// <see cref="CultureInfo.CurrentCulture"/>.
/// </summary>
public static class AppCulture
{
    /// <summary>Numbers, dates and sorting.</summary>
    public static CultureInfo Format { get; set; } = CultureInfo.CurrentCulture;

    /// <summary>Texts (resources, plural rules).</summary>
    public static CultureInfo Ui { get; set; } = CultureInfo.CurrentUICulture;
}

/// <summary>
/// Names of body zones in the app language (Resources/Raw/zone_names/&lt;code&gt;.json, id → name). Polish names come
/// from the zone data itself (<see cref="ZoneDefinition.NamePl"/>), which is also the fallback for missing entries.
/// </summary>
public static class ZoneNames
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Loaded = [];
    private static readonly Lock LoadLock = new();
    private static string _language = "pl";

    public static string Language
    {
        get => _language;
        set => _language = value;
    }

    public static string Of(ZoneDefinition zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (_language == "pl") return zone.NamePl;
        var names = Names(_language);
        return names.TryGetValue(zone.Id, out var name) && !string.IsNullOrWhiteSpace(name) ? name : zone.NamePl;
    }

    private static IReadOnlyDictionary<string, string> Names(string language)
    {
        lock (LoadLock)
        {
            if (Loaded.TryGetValue(language, out var names)) return names;
            try
            {
                using var stream = FileSystem.OpenAppPackageFileAsync($"zone_names/{language}.json").GetAwaiter().GetResult();
                names = JsonSerializer.Deserialize(stream, ZoneNamesJsonContext.Default.DictionaryStringString) ?? [];
            }
            catch (Exception e)
            {
                Console.WriteLine($"[BTG] zone names {language}: {e.Message}");
                names = new Dictionary<string, string>();
            }

            Loaded[language] = names;
            return names;
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class ZoneNamesJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
