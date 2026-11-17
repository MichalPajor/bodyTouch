using System.Collections.ObjectModel;
using System.Globalization;
using BodyTouchGame.App.Bridge;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.App.Services;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Persistence;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;
using BodyTouchGame.Geometry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BodyTouchGame.App.ViewModels;

public sealed partial class CustomPointItem(CustomPoint point, string detail) : ObservableObject
{
    public CustomPoint Point { get; } = point;
    public string Name => Point.Name;
    public string Detail { get; } = detail;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// Zone editor (spec 7, items 1–3): custom points (add, move, radius, levels, delete) and the level overlay of the
/// built-in zones (add a zone to a level or remove it, "Przywróć domyślne"). Built-in data is never changed.
/// </summary>
public sealed partial class ZoneEditorViewModel(
    ICustomPointRepository customPoints,
    ILevelOverrideRepository overrides,
    BodyDataService bodyData,
    IDialogService dialogs) : ObservableObject
{
    public const string LevelColor = "#FF9A3C";
    public const string RemovedColor = "#4A4560";
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    private BodyViewBridge? _bridge;
    private Task _modelLoaded = Task.CompletedTask;
    private IReadOnlyList<LevelOverride> _overrides = [];
    private CustomPoint? _editing;

    public ObservableCollection<CustomPointItem> Points { get; } = [];
    public ObservableCollection<LevelItem> PointLevels { get; } = [];
    public ObservableCollection<LevelItem> TargetLevels { get; } = [];
    public ObservableCollection<LevelItem> TapLevels { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsLevelsTab), nameof(ShowPointList))] public partial bool IsPointsTab { get; set; } = true;
    public bool IsLevelsTab => !IsPointsTab;

    // ---- custom points
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowPointList), nameof(CanDelete))] public partial bool IsEditing { get; set; }
    public bool ShowPointList => IsPointsTab && !IsEditing;
    public bool CanDelete => IsEditing && _editing is not null;
    [ObservableProperty] public partial bool HasNoPoints { get; set; }
    [ObservableProperty] public partial string EditName { get; set; } = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RadiusText))] public partial int EditRadius { get; set; } = (int)CustomPoint.DefaultRadiusCm;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PinHint))] public partial SurfacePoint? EditPin { get; set; }
    public string RadiusText => string.Format(Culture, AppResources.CmFormat, EditRadius);
    public string PinHint => EditPin is null ? AppResources.PointTapHint : AppResources.PointMoveHint;

    // ---- level overlay
    [ObservableProperty] public partial string LevelSummary { get; set; } = string.Empty;

    public async Task LoadAsync(BodyViewBridge bridge, Task modelLoaded)
    {
        if (_bridge is null)
        {
            _bridge = bridge;
            _modelLoaded = modelLoaded;
            bridge.PinPlaced += OnPinPlaced;
            bridge.ZoneTapped += OnZoneTapped;
        }

        await bodyData.LoadAsync();
        var catalog = bodyData.Zones!;
        if (TargetLevels.Count == 0)
        {
            foreach (var level in Enum.GetValues<ZoneLevel>())
            {
                var available = catalog.HasLevel(level);
                PointLevels.Add(new LevelItem(level, true));
                TargetLevels.Add(new LevelItem(level, available) { IsSelected = level == ZoneLevel.Easy });
                TapLevels.Add(new LevelItem(level, available) { IsSelected = level == ZoneLevel.Medium });
            }
        }

        await ReloadPointsAsync();
        _overrides = await overrides.GetAllAsync();
        await ShowTabAsync();
    }

    [RelayCommand]
    private Task ShowPointsAsync()
    {
        IsPointsTab = true;
        return ShowTabAsync();
    }

    [RelayCommand]
    private Task ShowLevelsAsync()
    {
        IsPointsTab = false;
        IsEditing = false;
        _editing = null;
        return ShowTabAsync();
    }

    // ------------------------------------------------------------------ custom points

    [RelayCommand]
    private async Task AddPointAsync()
    {
        _editing = null;
        EditName = string.Empty;
        EditRadius = (int)CustomPoint.DefaultRadiusCm;
        EditPin = null;
        foreach (var l in PointLevels) l.IsSelected = l.Level is ZoneLevel.Hard or ZoneLevel.Hardcore;
        IsEditing = true;
        OnPropertyChanged(nameof(CanDelete));
        await ShowPointsOnModelAsync();
    }

    [RelayCommand]
    private async Task EditPointAsync(CustomPointItem item)
    {
        _editing = item.Point;
        EditName = item.Point.Name;
        EditRadius = (int)Math.Round(item.Point.RadiusCm);
        EditPin = item.Point.Point;
        foreach (var l in PointLevels) l.IsSelected = item.Point.Levels.Contains(l.Level);
        IsEditing = true;
        OnPropertyChanged(nameof(CanDelete));
        await ShowPointsOnModelAsync(turnCamera: true);
    }

    [RelayCommand]
    private void TogglePointLevel(LevelItem item) => item.IsSelected = !item.IsSelected;

    [RelayCommand]
    private async Task CancelEditAsync()
    {
        IsEditing = false;
        _editing = null;
        await ShowTabAsync();
    }

    [RelayCommand]
    private async Task SavePointAsync()
    {
        var name = EditName.Trim();
        if (name.Length == 0)
        {
            await dialogs.AlertAsync(AppResources.TabCustomPoints, AppResources.PointNeedsName);
            return;
        }

        if (EditPin is not { } pin)
        {
            await dialogs.AlertAsync(AppResources.TabCustomPoints, AppResources.PointNeedsPin);
            return;
        }

        var levels = PointLevels.Where(l => l.IsSelected).Select(l => l.Level).ToList();
        // The point's zone (graph vertices within the radius) is computed once, here (spec 7).
        var vertices = await Task.Run(() => bodyData.Distances!.VerticesWithin(pin, EditRadius));
        var point = new CustomPoint(_editing?.Id ?? 0, name, pin, EditRadius, levels, vertices.ToList());
        await customPoints.SaveAsync(point);
        IsEditing = false;
        _editing = null;
        await ReloadPointsAsync();
        await ShowTabAsync();
    }

    [RelayCommand]
    private async Task DeletePointAsync()
    {
        if (_editing is null) return;
        var message = string.Format(Culture, AppResources.DeletePointMessageFormat, _editing.Name);
        if (!await dialogs.ConfirmAsync(AppResources.DeletePointTitle, message, AppResources.Delete, AppResources.Cancel)) return;
        await customPoints.DeleteAsync(_editing.Id);
        IsEditing = false;
        _editing = null;
        await ReloadPointsAsync();
        await ShowTabAsync();
    }

    partial void OnEditRadiusChanged(int value)
    {
        if (IsEditing) _ = ShowEditedPinAsync(turnCamera: false);
    }

    private void OnPinPlaced(object? sender, PinPlacedMessage message)
    {
        if (!IsPointsTab || !IsEditing) return;
        EditPin = new SurfacePoint(message.TriangleIndex, message.BaryU, message.BaryV);
        _ = ShowEditedPinAsync(turnCamera: false);
    }

    private async Task ReloadPointsAsync()
    {
        var all = await customPoints.GetAllAsync();
        Points.Clear();
        foreach (var p in all.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var levels = p.Levels.Count == 0 ? AppResources.PointNoLevels : string.Join(", ", p.Levels.Order().Select(ConfigTexts.Level));
            Points.Add(new CustomPointItem(p, string.Format(Culture, AppResources.PointDetailFormat,
                string.Format(Culture, AppResources.CmFormat, p.RadiusCm), levels)));
        }

        HasNoPoints = Points.Count == 0;
    }

    private async Task ShowPointsOnModelAsync(bool turnCamera = false)
    {
        if (_bridge is null) return;
        await _modelLoaded;
        await _bridge.SetZoneColorsAsync(new());
        await _bridge.SetModeAsync(IsEditing ? "Draw" : "View", new ViewModeOptions(AllowTargetPin: IsEditing));
        var others = Points.Where(p => p.Point.Id != _editing?.Id || !IsEditing)
            .Select(p => new CustomPointDto(p.Point.Id, p.Point.Point.TriangleIndex, p.Point.Point.BaryU, p.Point.Point.BaryV, p.Point.RadiusCm, false))
            .ToList();
        await _bridge.SetCustomPointsAsync(others);
        if (IsEditing && EditPin is not null) await ShowEditedPinAsync(turnCamera);
        else await _bridge.ClearTargetAsync();
    }

    /// <summary>The edited point as an orange, draggable pin with its radius highlighted.</summary>
    private async Task ShowEditedPinAsync(bool turnCamera)
    {
        if (_bridge is null || EditPin is not { } pin) return;
        await _modelLoaded;
        await _bridge.FocusTargetAsync(new TargetDto("Point", null, pin.TriangleIndex, pin.BaryU, pin.BaryV, EditRadius), true, turnCamera);
    }

    // ------------------------------------------------------------------ level overlay

    [RelayCommand]
    private async Task SelectTargetLevelAsync(LevelItem item)
    {
        if (!item.IsAvailable) return;
        foreach (var l in TargetLevels) l.IsSelected = l == item;
        await ShowLevelsOnModelAsync();
    }

    [RelayCommand]
    private async Task SelectTapLevelAsync(LevelItem item)
    {
        if (!item.IsAvailable) return;
        foreach (var l in TapLevels) l.IsSelected = l == item;
        await ShowLevelsOnModelAsync();
    }

    [RelayCommand]
    private async Task RestoreDefaultsAsync()
    {
        if (!await dialogs.ConfirmAsync(AppResources.RestoreDefaultsTitle, AppResources.RestoreDefaultsMessage, AppResources.Restore, AppResources.Cancel)) return;
        await overrides.ClearAsync();
        _overrides = [];
        await ShowLevelsOnModelAsync();
    }

    private ZoneLevel TargetLevel => TargetLevels.First(l => l.IsSelected).Level;
    private ZoneLevel TapLevel => TapLevels.First(l => l.IsSelected).Level;

    private void OnZoneTapped(object? sender, ZoneTappedMessage message) => MainThread.BeginInvokeOnMainThread(async () =>
    {
        if (IsPointsTab) return;
        var catalog = bodyData.Zones!;
        if (!catalog.TryGet(message.ZoneId, out var zone) || !zone.Drawable) return;
        var level = TargetLevel;
        var member = Effective(zone).Contains(level);
        await overrides.SetAsync(new LevelOverride(zone.Id, level, !member));
        _overrides = await overrides.GetAllAsync();
        await ShowLevelsOnModelAsync();
    });

    private HashSet<ZoneLevel> Effective(ZoneDefinition zone) =>
        TargetPoolBuilder.EffectiveLevels(zone, _overrides.GroupBy(o => o.ZoneId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal));

    private async Task ShowLevelsOnModelAsync()
    {
        if (_bridge is null) return;
        var catalog = bodyData.Zones!;
        var level = TargetLevel;
        var map = _overrides.GroupBy(o => o.ZoneId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var colors = new Dictionary<string, string>();
        int count = 0, added = 0, removed = 0;
        foreach (var zone in catalog.Zones.Where(z => z.Drawable))
        {
            var member = TargetPoolBuilder.EffectiveLevels(zone, map).Contains(level);
            if (member)
            {
                colors[zone.Id] = LevelColor;
                count++;
                if (zone.Level != level) added++;
            }
            else if (zone.Level == level)
            {
                colors[zone.Id] = RemovedColor;
                removed++;
            }
        }

        LevelSummary = string.Format(Culture, AppResources.LevelSummaryFormat, ConfigTexts.Level(level), PolishPlural.Zones(count)) +
                       (added + removed > 0 ? string.Format(Culture, AppResources.LevelChangesFormat, added, removed) : string.Empty);
        await _modelLoaded;
        await _bridge.ClearTargetAsync();
        await _bridge.SetCustomPointsAsync([]);
        await _bridge.SetModeAsync("Editor", new ViewModeOptions(false, TapLevel.ToString()));
        await _bridge.SetZoneColorsAsync(colors);
    }

    private Task ShowTabAsync() => IsPointsTab ? ShowPointsOnModelAsync() : ShowLevelsOnModelAsync();
}
