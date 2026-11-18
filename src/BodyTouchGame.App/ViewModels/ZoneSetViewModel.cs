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
/// Zone set of the game (mockup 3 "Strefy", spec 6): any mix of levels, zones picked by tapping the model and
/// custom points. Preview: orange = drawn through a level, cyan = picked by hand, violet markers = custom points,
/// grey = excluded by the selected players.
/// </summary>
public sealed partial class ZoneSetViewModel(GameSetupService setup, BodyDataService bodyData, INavigationService navigation) : ObservableObject
{
    private static CultureInfo Culture => AppCulture.Format;

    private BodyViewBridge? _bridge;
    private Task _modelLoaded = Task.CompletedTask;

    public ObservableCollection<LevelItem> Levels { get; } = [];
    public ObservableCollection<LevelItem> TapLevels { get; } = [];
    public ObservableCollection<ChoiceItem> Points { get; } = [];

    [ObservableProperty] public partial string CountText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ManualText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasManual { get; set; }
    [ObservableProperty] public partial bool HasPoints { get; set; }

    private ZoneSetSelection Selection => setup.Config.ZoneSet;

    public async Task LoadAsync(BodyViewBridge bridge, Task modelLoaded)
    {
        if (_bridge is null)
        {
            _bridge = bridge;
            _modelLoaded = modelLoaded;
            bridge.ZoneTapped += OnZoneTapped;
        }

        await bodyData.LoadAsync();
        var catalog = bodyData.Zones!;
        Levels.Clear();
        TapLevels.Clear();
        foreach (var level in Enum.GetValues<ZoneLevel>())
        {
            Levels.Add(new LevelItem(level, catalog.HasLevel(level)) { IsSelected = Selection.Levels.Contains(level) });
            TapLevels.Add(new LevelItem(level, catalog.HasLevel(level)) { IsSelected = level == ZoneLevel.Medium });
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ToggleAsync(LevelItem item)
    {
        if (!item.IsAvailable) return;
        var levels = Selection.Levels.ToList();
        if (!levels.Remove(item.Level)) levels.Add(item.Level);
        await ApplyAsync(new ZoneSetSelection(levels, Selection.ZoneIds, Selection.CustomPointIds));
        item.IsSelected = Selection.Levels.Contains(item.Level);
    }

    [RelayCommand]
    private async Task SelectTapLevelAsync(LevelItem item)
    {
        if (!item.IsAvailable) return;
        foreach (var l in TapLevels) l.IsSelected = l == item;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task TogglePointAsync(ChoiceItem item)
    {
        var ids = Selection.CustomPointIds.ToList();
        if (!ids.Remove(item.Id!.Value)) ids.Add(item.Id.Value);
        await ApplyAsync(new ZoneSetSelection(Selection.Levels, Selection.ZoneIds, ids));
    }

    [RelayCommand]
    private Task ClearManualAsync() => ApplyAsync(new ZoneSetSelection(Selection.Levels, [], Selection.CustomPointIds));

    [RelayCommand]
    private Task DoneAsync() => navigation.GoBackAsync();

    private void OnZoneTapped(object? sender, ZoneTappedMessage message) => MainThread.BeginInvokeOnMainThread(async () =>
    {
        if (!bodyData.Zones!.TryGet(message.ZoneId, out var zone) || !zone.Drawable) return;
        var ids = Selection.ZoneIds.ToList();
        if (!ids.Remove(zone.Id)) ids.Add(zone.Id);
        await ApplyAsync(new ZoneSetSelection(Selection.Levels, ids, Selection.CustomPointIds));
    });

    /// <summary>Saves the selection unless it would become empty (something must be drawable).</summary>
    private async Task ApplyAsync(ZoneSetSelection selection)
    {
        if (!selection.IsEmpty) setup.Config = setup.Config with { ZoneSet = selection };
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var catalog = bodyData.Zones!;
        var selection = Selection;
        foreach (var l in Levels) l.IsSelected = selection.Levels.Contains(l.Level);
        var preview = await setup.PreviewAsync(catalog, selection);

        CountText = preview.ExcludedCount > 0
            ? string.Format(Culture, AppResources.ZoneSetCountFormat, Targets(preview), PolishPlural.Excluded(preview.ExcludedCount))
            : string.Format(Culture, AppResources.ZoneSetCountOnlyFormat, Targets(preview));
        HasManual = selection.ZoneIds.Count > 0;
        ManualText = string.Format(Culture, AppResources.ManualZonesFormat,
            string.Join(", ", selection.ZoneIds.Select(id => catalog.TryGet(id, out var z) ? ZoneNames.Of(z) : id)));

        Points.Clear();
        foreach (var p in preview.AllPoints.OrderBy(p => p.Name, StringComparer.Create(AppCulture.Format, ignoreCase: true)))
            Points.Add(new ChoiceItem(p.Id, p.Name) { IsSelected = selection.CustomPointIds.Contains(p.Id) || p.Levels.Any(selection.Levels.Contains) });
        HasPoints = Points.Count > 0;

        if (_bridge is null) return;
        await _modelLoaded;
        var manual = selection.ZoneIds.ToHashSet(StringComparer.Ordinal);
        var colors = new Dictionary<string, string>();
        foreach (var id in preview.DrawableZones)
        {
            var index = bodyData.ColorSlot(id);
            colors[id] = manual.Contains(id) ? ZonePalette.Cyan(index) : ZonePalette.Warm(index);
        }

        foreach (var id in preview.ExcludedZones) colors[id] = ZonePalette.Removed;
        await _bridge.SetModeAsync("Editor", new ViewModeOptions(false, TapLevels.First(l => l.IsSelected).Level.ToString()));
        await _bridge.SetZoneColorsAsync(colors);
        await _bridge.SetCustomPointsAsync(preview.AllPoints
            .Select(p => new CustomPointDto(p.Id, p.Point.TriangleIndex, p.Point.BaryU, p.Point.BaryV, p.RadiusCm, preview.DrawablePoints.Contains(p.Id)))
            .ToList());
    }

    /// <summary>"38 stref" or "38 stref + 2 punkty".</summary>
    public static string Targets(ZoneSetPreview preview) => (preview.DrawableZones.Count, preview.DrawablePoints.Count) switch
    {
        (var zones, 0) => PolishPlural.Zones(zones),
        (0, var points) => PolishPlural.Points(points),
        var (zones, points) => $"{PolishPlural.Zones(zones)} + {PolishPlural.Points(points)}",
    };
}
