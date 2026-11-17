using System.Collections.ObjectModel;
using System.Globalization;
using BodyTouchGame.App.Bridge;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.App.Services;
using BodyTouchGame.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BodyTouchGame.App.ViewModels;

public sealed partial class LevelItem(ZoneLevel level, bool isAvailable) : ObservableObject
{
    public ZoneLevel Level { get; } = level;
    public string Name { get; } = ConfigTexts.Level(level);
    public bool IsAvailable { get; } = isAvailable;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// Zone set of the game (from mockup 3 "Strefy"): which levels are drawn, with a model preview — orange zones can be
/// drawn, grey ones are excluded by the selected players.
/// </summary>
public sealed partial class ZoneSetViewModel(GameSetupService setup, BodyDataService bodyData, INavigationService navigation) : ObservableObject
{
    public const string DrawableColor = "#FF9A3C";
    public const string ExcludedColor = "#4A4560";

    private BodyViewBridge? _bridge;
    private Task _modelLoaded = Task.CompletedTask;

    public ObservableCollection<LevelItem> Levels { get; } = [];

    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    public async Task LoadAsync(BodyViewBridge bridge, Task modelLoaded)
    {
        _bridge = bridge;
        _modelLoaded = modelLoaded;
        await bodyData.LoadAsync();
        var catalog = bodyData.Zones!;
        var selected = setup.Config.ZoneSet.Levels;
        Levels.Clear();
        foreach (var level in Enum.GetValues<ZoneLevel>())
            Levels.Add(new LevelItem(level, catalog.HasLevel(level)) { IsSelected = selected.Contains(level) });
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ToggleAsync(LevelItem item)
    {
        if (!item.IsAvailable) return;
        if (item.IsSelected && Levels.Count(l => l.IsSelected) == 1) return;   // at least one level
        item.IsSelected = !item.IsSelected;
        var levels = Levels.Where(l => l.IsSelected).Select(l => l.Level).ToArray();
        setup.Config = setup.Config with { ZoneSet = new ZoneSetSelection(levels, setup.Config.ZoneSet.ZoneIds, setup.Config.ZoneSet.CustomPointIds) };
        await RefreshAsync();
    }

    [RelayCommand]
    private Task DoneAsync() => navigation.GoBackAsync();

    private async Task RefreshAsync()
    {
        var catalog = bodyData.Zones!;
        var selection = setup.Config.ZoneSet;
        var (drawable, excluded) = await setup.PreviewZonesAsync(catalog, selection);
        var (total, excludedCount) = await setup.CountTargetsAsync(catalog, selection);
        CountText = excludedCount > 0
            ? string.Format(CultureInfo.CurrentCulture, AppResources.ZoneSetCountFormat, PolishPlural.Zones(total - excludedCount), PolishPlural.Excluded(excludedCount))
            : string.Format(CultureInfo.CurrentCulture, AppResources.ZoneSetCountOnlyFormat, PolishPlural.Zones(total));

        if (_bridge is null) return;
        await _modelLoaded;
        var colors = new Dictionary<string, string>();
        foreach (var id in drawable) colors[id] = DrawableColor;
        foreach (var id in excluded) colors[id] = ExcludedColor;
        await _bridge.SetZoneColorsAsync(colors);
    }
}
