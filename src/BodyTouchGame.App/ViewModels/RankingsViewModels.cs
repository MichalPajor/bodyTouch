using System.Collections.ObjectModel;
using System.Globalization;
using BodyTouchGame.App.Bridge;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.App.Services;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Persistence;
using BodyTouchGame.Core.Players;
using BodyTouchGame.Core.Statistics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BodyTouchGame.App.ViewModels;

/// <summary>A chip in a row of choices (preset tabs, players). Id null = "all".</summary>
public sealed partial class ChoiceItem(long? id, string name) : ObservableObject
{
    public long? Id { get; } = id;
    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public sealed record RankingRow(string Place, string Name, string Color, string? Emoji, string Detail, string Points, Color Accent);

/// <summary>Rankings (spec 10, own design D7): a tab per preset, average points per round, threshold 5 rounds.</summary>
public sealed partial class RankingsViewModel(IPresetRepository presets, IPlayerRepository players, HistoryService history) : ObservableObject
{
    private static CultureInfo Culture => AppCulture.Format;
    private static readonly Color[] PlaceAccents = [Color.FromArgb("#FF9A3C"), Color.FromArgb("#22D3EE"), Color.FromArgb("#FF3D9A")];

    private IReadOnlyList<HistoryRound> _history = [];
    private Dictionary<long, Player> _players = [];

    public ObservableCollection<ChoiceItem> Tabs { get; } = [];
    public ObservableCollection<RankingRow> Qualified { get; } = [];
    public ObservableCollection<RankingRow> Below { get; } = [];

    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial bool HasBelow { get; set; }
    [ObservableProperty] public partial bool HasNoQualified { get; set; }

    public string Hint { get; } = string.Format(AppCulture.Format, AppResources.RankingHintFormat, Rankings.MinRounds);
    public string BelowTitle { get; } = string.Format(AppCulture.Format, AppResources.RankingBelowFormat, Rankings.MinRounds);
    public string NoQualifiedText { get; } = string.Format(AppCulture.Format, AppResources.RankingNoQualifiedFormat, Rankings.MinRounds);

    public async Task LoadAsync()
    {
        _history = await history.GetAsync();
        _players = (await players.GetAllAsync()).ToDictionary(p => p.Id);
        var withRounds = Rankings.PresetsWithRounds(_history).ToHashSet();
        var selected = Tabs.FirstOrDefault(t => t.IsSelected)?.Id;
        Tabs.Clear();
        foreach (var preset in await presets.GetAllAsync())
        {
            if (withRounds.Contains(preset.Id)) Tabs.Add(new ChoiceItem(preset.Id, ConfigTexts.PresetName(preset)));
        }

        IsEmpty = Tabs.Count == 0;
        if (IsEmpty)
        {
            Qualified.Clear();
            Below.Clear();
            HasBelow = HasNoQualified = false;
            return;
        }

        Select(Tabs.FirstOrDefault(t => t.Id == selected) ?? Tabs[0]);
    }

    [RelayCommand]
    private void Select(ChoiceItem tab)
    {
        foreach (var t in Tabs) t.IsSelected = t == tab;
        var entries = Rankings.Compute(_history, tab.Id!.Value, _players.Keys.ToHashSet());
        Qualified.Clear();
        Below.Clear();
        foreach (var e in entries)
        {
            var player = _players[e.PlayerId];
            var distance = e.AverageDistanceCm is { } d ? string.Format(Culture, AppResources.DistanceFormat, d) : AppResources.Unreachable;
            var row = new RankingRow(
                e.Qualified ? e.Rank.ToString(Culture) : "–",
                player.Name, player.AvatarColor, player.AvatarEmoji,
                string.Format(Culture, AppResources.RankingDetailFormat, PolishPlural.Rounds(e.Rounds), distance),
                string.Format(Culture, AppResources.AvgPointsFormat, e.AveragePoints),
                e.Qualified && e.Rank <= 3 ? PlaceAccents[e.Rank - 1] : Color.FromArgb("#2E2650"));
            (e.Qualified ? Qualified : Below).Add(row);
        }

        HasBelow = Below.Count > 0;
        HasNoQualified = Qualified.Count == 0;
    }
}

/// <summary>
/// Heat map (spec 10): zones the player had as targets, coloured by the average base points (green = hits,
/// red = misses), and arrows to the zone most often guessed instead (≥ 3 times). Per player and per preset.
/// </summary>
public sealed partial class HeatmapViewModel(IPlayerRepository players, IPresetRepository presets, HistoryService history, BodyDataService bodyData)
    : ObservableObject, IQueryAttributable
{
    private static CultureInfo Culture => AppCulture.Format;
    private BodyViewBridge? _bridge;
    private Task _modelLoaded = Task.CompletedTask;
    private IReadOnlyList<HistoryRound> _history = [];
    private Dictionary<string, ZoneHeat> _heat = [];
    private long? _requestedPlayer;

    public ObservableCollection<ChoiceItem> Players { get; } = [];
    public ObservableCollection<ChoiceItem> Presets { get; } = [];

    [ObservableProperty] public partial bool ShowArrows { get; set; } = true;
    [ObservableProperty] public partial string InfoText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasPlayers { get; set; } = true;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("player", out var id) && long.TryParse(id?.ToString(), out var value)) _requestedPlayer = value;
    }

    public async Task LoadAsync(BodyViewBridge bridge, Task modelLoaded)
    {
        if (_bridge is null)
        {
            _bridge = bridge;
            _modelLoaded = modelLoaded;
            bridge.ZoneTapped += OnZoneTapped;
        }

        await bodyData.LoadAsync();
        _history = await history.GetAsync();
        var selected = Players.FirstOrDefault(p => p.IsSelected)?.Id ?? _requestedPlayer;
        Players.Clear();
        foreach (var player in await players.GetAllAsync()) Players.Add(new ChoiceItem(player.Id, player.Name));
        HasPlayers = Players.Count > 0;
        if (!HasPlayers) return;
        await SelectPlayerAsync(Players.FirstOrDefault(p => p.Id == selected) ?? Players[0]);
    }

    [RelayCommand]
    private async Task SelectPlayerAsync(ChoiceItem item)
    {
        foreach (var p in Players) p.IsSelected = p == item;
        // Presets in which this player has rounds, after "Wszystkie".
        var withRounds = _history.Where(h => h.Round.GuesserId == item.Id && h.PresetId is not null).Select(h => h.PresetId!.Value).ToHashSet();
        var selected = Presets.FirstOrDefault(p => p.IsSelected)?.Id;
        Presets.Clear();
        Presets.Add(new ChoiceItem(null, AppResources.AllPresets));
        foreach (var preset in await presets.GetAllAsync())
        {
            if (withRounds.Contains(preset.Id)) Presets.Add(new ChoiceItem(preset.Id, ConfigTexts.PresetName(preset)));
        }

        await SelectPresetAsync(Presets.FirstOrDefault(p => p.Id == selected) ?? Presets[0]);
    }

    [RelayCommand]
    private async Task SelectPresetAsync(ChoiceItem item)
    {
        foreach (var p in Presets) p.IsSelected = p == item;
        await RefreshAsync();
    }

    partial void OnShowArrowsChanged(bool value) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        var player = Players.FirstOrDefault(p => p.IsSelected);
        if (player is null || _bridge is null) return;
        var preset = Presets.FirstOrDefault(p => p.IsSelected)?.Id;
        var map = Heatmap.Compute(_history, bodyData.Zones!, player.Id!.Value, preset);
        _heat = map.Zones.ToDictionary(z => z.ZoneId, StringComparer.Ordinal);
        InfoText = map.Zones.Count == 0 ? AppResources.HeatmapNoData : AppResources.HeatmapHint;

        await _modelLoaded;
        await _bridge.SetModeAsync("Heatmap", new ViewModeOptions(false, bodyData.Zones!.Levels[^1].ToString()));
        await _bridge.SetZoneColorsAsync(map.Zones.ToDictionary(z => z.ZoneId, z => z.Color));
        if (ShowArrows) await _bridge.SetArrowsAsync(map.Arrows.Select(a => new ArrowDto(a.FromZoneId, a.ToZoneId, a.Count)).ToList());
        else await _bridge.ClearArrowsAsync();
        var debug = await _bridge.GetDebugStateAsync();
        Console.WriteLine($"[BTG] heatmap player={player.Name} preset={preset?.ToString() ?? "all"} zones={map.Zones.Count} " +
                          $"arrows={map.Arrows.Count} drawn={debug?.Arrows} " +
                          string.Join(" ", map.Zones.Select(z => $"{z.ZoneId}:{z.AverageBasePoints:0}pt{z.Color}")) + " | " +
                          string.Join(" ", map.Arrows.Select(a => $"{a.FromZoneId}->{a.ToZoneId}x{a.Count}")));
    }

    /// <summary>A tap on the model: statistics of the tapped zone, or of the coarser zone containing it.</summary>
    private void OnZoneTapped(object? sender, ZoneTappedMessage message)
    {
        var catalog = bodyData.Zones;
        if (catalog is null || !catalog.TryGet(message.ZoneId, out var zone)) return;
        var candidates = new[] { zone }.Concat(catalog.Ancestors(zone.Id));
        var found = candidates.Select(z => (Zone: z, Heat: _heat.GetValueOrDefault(z.Id))).FirstOrDefault(x => x.Heat is not null);
        InfoText = found.Heat is { } heat
            ? string.Format(Culture, AppResources.HeatZoneFormat, ZoneNames.Of(found.Zone), PolishPlural.Rounds(heat.Rounds),
                heat.AverageDistanceCm is { } d ? string.Format(Culture, AppResources.DistanceFormat, d) : AppResources.Unreachable,
                heat.AverageBasePoints)
            : string.Format(Culture, AppResources.HeatZoneNoneFormat, ZoneNames.Of(zone));
    }
}
