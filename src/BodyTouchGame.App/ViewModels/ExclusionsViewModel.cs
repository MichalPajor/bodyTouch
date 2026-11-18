using System.Collections.ObjectModel;
using System.Globalization;
using BodyTouchGame.App.Bridge;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.App.Services;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Persistence;
using BodyTouchGame.Core.Players;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BodyTouchGame.App.ViewModels;

public sealed partial class ExclusionPlayerItem(Player player) : ObservableObject
{
    public Player Player { get; } = player;
    public string Name => Player.Name;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// Excluded zones per player (Settings → Strefy wykluczone, spec 7 item 4): a tap on the model (Editor mode)
/// excludes or restores the zone of the chosen level. An excluded zone also covers the zones inside it.
/// </summary>
public sealed partial class ExclusionsViewModel(IPlayerRepository players, IExclusionRepository exclusions, BodyDataService bodyData)
    : ObservableObject, IQueryAttributable
{
    private long? _requestedPlayer;

    public const string ExcludedColor = "#4A4560";
    public const string CoveredColor = "#5E5878";

    private BodyViewBridge? _bridge;
    private Task _modelLoaded = Task.CompletedTask;
    private ExclusionProfile? _profile;

    public ObservableCollection<ExclusionPlayerItem> Players { get; } = [];
    public ObservableCollection<LevelItem> Levels { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNoPlayers))] public partial bool HasPlayers { get; set; }
    [ObservableProperty] public partial string ExcludedText { get; set; } = string.Empty;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ClearCommand))] public partial bool HasExclusions { get; set; }

    public bool HasNoPlayers => !HasPlayers;

    private ZoneLevel CurrentLevel => Levels.FirstOrDefault(l => l.IsSelected)?.Level ?? ZoneLevel.Easy;

    /// <summary>"player" = id of the player to show first (from the player profile).</summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("player", out var id) && long.TryParse(id?.ToString(), out var value)) _requestedPlayer = value;
    }

    public async Task LoadAsync(BodyViewBridge bridge, Task modelLoaded)
    {
        _bridge = bridge;
        _modelLoaded = modelLoaded;
        bridge.ZoneTapped -= OnZoneTapped;
        bridge.ZoneTapped += OnZoneTapped;
        await bodyData.LoadAsync();

        var catalog = bodyData.Zones!;
        Levels.Clear();
        foreach (var level in Enum.GetValues<ZoneLevel>())
            Levels.Add(new LevelItem(level, catalog.HasLevel(level)) { IsSelected = level == ZoneLevel.Easy });

        var previous = Players.FirstOrDefault(p => p.IsSelected)?.Player.Id ?? _requestedPlayer;
        Players.Clear();
        foreach (var player in await players.GetAllAsync()) Players.Add(new ExclusionPlayerItem(player));
        HasPlayers = Players.Count > 0;
        if (!HasPlayers) return;
        await SelectPlayerAsync(Players.FirstOrDefault(p => p.Player.Id == previous) ?? Players[0]);
    }

    [RelayCommand]
    private async Task SelectPlayerAsync(ExclusionPlayerItem item)
    {
        foreach (var p in Players) p.IsSelected = p == item;
        _profile = await exclusions.GetAsync(item.Player.Id);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SelectLevelAsync(LevelItem item)
    {
        if (!item.IsAvailable) return;
        foreach (var l in Levels) l.IsSelected = l == item;
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(HasExclusions))]
    private async Task ClearAsync()
    {
        if (_profile is null) return;
        _profile = new ExclusionProfile(_profile.PlayerId);
        await exclusions.SaveAsync(_profile);
        await RefreshAsync();
    }

    private void OnZoneTapped(object? sender, ZoneTappedMessage message) => MainThread.BeginInvokeOnMainThread(async () =>
    {
        if (_profile is null) return;
        var ids = _profile.ZoneIds.ToList();
        if (!ids.Remove(message.ZoneId)) ids.Add(message.ZoneId);
        _profile = new ExclusionProfile(_profile.PlayerId, ids);
        await exclusions.SaveAsync(_profile);
        await RefreshAsync();
    });

    private async Task RefreshAsync()
    {
        var catalog = bodyData.Zones!;
        var excluded = _profile?.ZoneIds ?? [];
        HasExclusions = excluded.Count > 0;
        ExcludedText = excluded.Count == 0
            ? AppResources.ExcludedNone
            : string.Format(AppCulture.Format, AppResources.ExcludedListFormat,
                string.Join(", ", excluded.Select(id => catalog.TryGet(id, out var z) ? ZoneNames.Of(z) : id)));

        if (_bridge is null) return;
        await _modelLoaded;
        // Excluded zones dark grey; zones lying inside an excluded zone lighter grey.
        var colors = new Dictionary<string, string>();
        foreach (var id in excluded)
        {
            if (!catalog.TryGet(id, out _)) continue;
            foreach (var inner in catalog.Descendants(id)) colors.TryAdd(inner.Id, CoveredColor);
        }

        foreach (var id in excluded) colors[id] = ExcludedColor;
        await _bridge.SetModeAsync("Editor", new ViewModeOptions(false, CurrentLevel.ToString()));
        await _bridge.SetZoneColorsAsync(colors);
    }
}
