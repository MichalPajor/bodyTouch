using System.Collections.ObjectModel;
using System.Globalization;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.App.Services;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BodyTouchGame.App.ViewModels;

public sealed partial class PresetItem(GamePreset preset, bool isAvailable) : ObservableObject
{
    public GamePreset Preset { get; } = preset;
    public string Name { get; } = ConfigTexts.PresetName(preset);

    /// <summary>False for presets whose zone levels the model does not have yet (Hard, Hardcore): shown greyed.</summary>
    public bool IsAvailable { get; } = isAvailable;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public sealed record TouchMethodItem(TouchMethod Method, string Name);

/// <summary>
/// New game, step 2 (mockup 3): presets and every <see cref="GameConfig"/> field (fields not in the mockup are in
/// the "Zaawansowane" section, D6). The edited configuration lives in <see cref="GameSetupService"/>.
/// </summary>
public sealed partial class GameConfigViewModel(
    GameSetupService setup,
    BodyDataService bodyData,
    IPresetRepository presets,
    INavigationService navigation,
    IDialogService dialogs) : ObservableObject
{
    private bool _loading;

    public ObservableCollection<PresetItem> Presets { get; } = [];

    public IReadOnlyList<TouchMethodItem> TouchMethods { get; } =
        Enum.GetValues<TouchMethod>().Select(m => new TouchMethodItem(m, ConfigTexts.TouchMethod(m))).ToList();

    [ObservableProperty] public partial string ZonesText { get; set; } = string.Empty;
    [ObservableProperty] public partial TouchMethod TouchMethod { get; set; }
    [ObservableProperty] public partial bool MovementAllowed { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TouchSecondsText))] public partial int TouchSeconds { get; set; }
    [ObservableProperty] public partial int RoundsPerPlayer { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AdvancedGlyph))] public partial bool ShowAdvanced { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsRandom), nameof(ZonesRowOpacity))] public partial bool ChosenByModel { get; set; }
    [ObservableProperty] public partial bool HasGuessLimit { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(GuessSecondsText))] public partial int GuessSeconds { get; set; } = 10;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FullRadiusText))] public partial double FullPointsRadiusCm { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ZeroDistanceText))] public partial double ZeroPointsDistanceCm { get; set; }
    [ObservableProperty] public partial bool SpeedBonus { get; set; }

    public bool IsRandom => !ChosenByModel;
    public double ZonesRowOpacity => ChosenByModel ? 0.45 : 1;
    public string AdvancedGlyph => ShowAdvanced ? "−" : "+";
    public string TouchSecondsText => ConfigTexts.Seconds(TouchSeconds);
    public string GuessSecondsText => ConfigTexts.Seconds(GuessSeconds);
    public string FullRadiusText => string.Format(CultureInfo.CurrentCulture, AppResources.CmFormat, FullPointsRadiusCm);
    public string ZeroDistanceText => string.Format(CultureInfo.CurrentCulture, AppResources.CmFormat, ZeroPointsDistanceCm);

    public async Task LoadAsync()
    {
        _loading = true;
        try
        {
            await bodyData.LoadAsync();
            var catalog = bodyData.Zones!;
            Presets.Clear();
            foreach (var preset in await presets.GetAllAsync())
                Presets.Add(new PresetItem(preset, preset.Config.ZoneSet.Levels.All(catalog.HasLevel)));
            ShowConfig(setup.Config);
        }
        finally
        {
            _loading = false;
        }

        await RefreshZonesTextAsync();
        UpdatePresetSelection();
    }

    [RelayCommand]
    private async Task SelectPresetAsync(PresetItem item)
    {
        if (!item.IsAvailable)
        {
            await dialogs.AlertAsync(item.Name, AppResources.ZonesNotAvailable);
            return;
        }

        setup.Config = item.Preset.Config;
        _loading = true;
        ShowConfig(item.Preset.Config);
        _loading = false;
        await RefreshZonesTextAsync();
        UpdatePresetSelection();
    }

    [RelayCommand]
    private void SelectTouchMethod(TouchMethodItem item) => TouchMethod = item.Method;

    [RelayCommand]
    private void ChangeRounds(string delta) =>
        RoundsPerPlayer = Math.Clamp(RoundsPerPlayer + int.Parse(delta, CultureInfo.InvariantCulture), GameConfig.MinRoundsPerPlayer, GameConfig.MaxRoundsPerPlayer);

    /// <summary>r changes by 0.5 cm; z keeps a gap of at least 1 cm above r.</summary>
    [RelayCommand]
    private void ChangeFullRadius(string delta)
    {
        var value = Math.Clamp(FullPointsRadiusCm + double.Parse(delta, CultureInfo.InvariantCulture), 0, 30);
        FullPointsRadiusCm = value;
        if (ZeroPointsDistanceCm < value + 1) ZeroPointsDistanceCm = value + 1;
    }

    [RelayCommand]
    private void ChangeZeroDistance(string delta) =>
        ZeroPointsDistanceCm = Math.Clamp(ZeroPointsDistanceCm + double.Parse(delta, CultureInfo.InvariantCulture), FullPointsRadiusCm + 1, 150);

    [RelayCommand]
    private void ToggleAdvanced() => ShowAdvanced = !ShowAdvanced;

    [RelayCommand]
    private Task OpenZonesAsync() => ChosenByModel ? Task.CompletedTask : navigation.GoToAsync(Routes.ZoneSet);

    [RelayCommand]
    private async Task SavePresetAsync()
    {
        var name = await dialogs.PromptAsync(AppResources.SavePreset, AppResources.SavePresetMessage, AppResources.Save, 30);
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        try
        {
            var existing = Presets.FirstOrDefault(p => !p.Preset.IsBuiltIn && string.Equals(p.Preset.Name, name, StringComparison.CurrentCultureIgnoreCase));
            var saved = await presets.SaveCustomAsync(new GamePreset(existing?.Preset.Id ?? 0, name, false, setup.Config));
            if (existing is not null) Presets.Remove(existing);
            Presets.Add(new PresetItem(saved, true));
            UpdatePresetSelection();
        }
        catch (DuplicateNameException)
        {
            await dialogs.AlertAsync(AppResources.SavePreset, AppResources.NameTaken);
        }
    }

    [RelayCommand]
    private async Task StartGameAsync()
    {
        var config = setup.Config;
        var problems = new List<string>();
        if (config.TargetSelection == TargetSelection.Random)
        {
            var catalog = bodyData.Zones!;
            if (!config.ZoneSet.Levels.All(catalog.HasLevel)) problems.Add(AppResources.ZonesNotAvailable);
            else
            {
                var pools = await setup.BuildTargetPoolsAsync(catalog);
                problems.AddRange(setup.Players.Where(p => pools[p.Id].Count == 0)
                    .Select(p => string.Format(CultureInfo.CurrentCulture, AppResources.NothingToDrawFormat, p.Name)));
            }
        }

        if (problems.Count > 0)
        {
            await dialogs.AlertAsync(AppResources.CannotStartTitle, string.Join("\n", problems));
            return;
        }

        await navigation.GoToAsync(Routes.Game);
    }

    // ------------------------------------------------------------------ edits → configuration

    partial void OnTouchMethodChanged(TouchMethod value) => Apply(c => c with { TouchMethod = value });
    partial void OnMovementAllowedChanged(bool value) => Apply(c => c with { MovementAllowed = value });
    partial void OnTouchSecondsChanged(int value) => Apply(c => c with { TouchSeconds = value });
    partial void OnRoundsPerPlayerChanged(int value) => Apply(c => c with { RoundsPerPlayer = value });
    partial void OnChosenByModelChanged(bool value)
    {
        Apply(c => c with { TargetSelection = value ? TargetSelection.ChosenByModel : TargetSelection.Random });
        if (!_loading) _ = RefreshZonesTextAsync();
    }
    partial void OnHasGuessLimitChanged(bool value) => Apply(c => c with { GuessSeconds = value ? GuessSeconds : null });
    partial void OnGuessSecondsChanged(int value) => Apply(c => c with { GuessSeconds = HasGuessLimit ? value : null });
    partial void OnFullPointsRadiusCmChanged(double value) => Apply(c => c with { FullPointsRadiusCm = value });
    partial void OnZeroPointsDistanceCmChanged(double value) => Apply(c => c with { ZeroPointsDistanceCm = value });
    partial void OnSpeedBonusChanged(bool value) => Apply(c => c with { SpeedBonus = value });

    private void Apply(Func<GameConfig, GameConfig> change)
    {
        if (_loading) return;
        setup.Config = change(setup.Config);
        UpdatePresetSelection();
    }

    private void ShowConfig(GameConfig config)
    {
        TouchMethod = config.TouchMethod;
        MovementAllowed = config.MovementAllowed;
        TouchSeconds = config.TouchSeconds;
        RoundsPerPlayer = config.RoundsPerPlayer;
        ChosenByModel = config.TargetSelection == TargetSelection.ChosenByModel;
        HasGuessLimit = config.GuessSeconds is not null;
        GuessSeconds = config.GuessSeconds ?? 10;
        FullPointsRadiusCm = config.FullPointsRadiusCm;
        ZeroPointsDistanceCm = config.ZeroPointsDistanceCm;
        SpeedBonus = config.SpeedBonus;
    }

    /// <summary>The preset whose settings match the current ones (the number of rounds is not compared).</summary>
    private void UpdatePresetSelection()
    {
        var config = setup.Config;
        var match = Presets.FirstOrDefault(p => p.Preset.Config with { RoundsPerPlayer = config.RoundsPerPlayer } == config);
        foreach (var item in Presets) item.IsSelected = item == match;
        setup.PresetId = match?.Preset.Id;
        setup.PresetLabel = match?.Name ?? AppResources.PresetCustomConfig;
    }

    public async Task RefreshZonesTextAsync()
    {
        var config = setup.Config;
        if (config.TargetSelection == TargetSelection.ChosenByModel)
        {
            ZonesText = AppResources.TargetChosenByModel;
            return;
        }

        var catalog = bodyData.Zones!;
        var set = config.ZoneSet;
        var parts = set.Levels.Select(ConfigTexts.Level).ToList();
        if (set.ZoneIds.Count > 0) parts.Add(PolishPlural.Zones(set.ZoneIds.Count));
        if (set.CustomPointIds.Count > 0) parts.Add(PolishPlural.Points(set.CustomPointIds.Count));
        var what = string.Join(" + ", parts);
        var preview = await setup.PreviewAsync(catalog, set);
        var count = ZoneSetViewModel.Targets(preview);
        // Without levels the parts already say what is drawn ("2 strefy + 1 punkt"): only the count follows.
        var head = set.Levels.Count > 0 ? $"{what} · {count}" : count;
        ZonesText = preview.ExcludedCount > 0 ? $"{head}, {PolishPlural.Excluded(preview.ExcludedCount)}" : head;
    }
}
