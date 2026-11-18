using System.Collections.ObjectModel;
using System.Globalization;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.App.Services;
using BodyTouchGame.Core.Persistence;
using BodyTouchGame.Core.Players;
using BodyTouchGame.Core.Statistics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BodyTouchGame.App.ViewModels;

public sealed record PlayerListItem(Player Player, string Stats)
{
    public string Name => Player.Name;
    public string Color => Player.AvatarColor;
    public string? Emoji => Player.AvatarEmoji;
}

/// <summary>Players screen (spec 13 item 6, own design D7): list, add, open the profile.</summary>
public sealed partial class PlayersViewModel(IPlayerRepository players, HistoryService history, PlayerEditor editor, INavigationService navigation)
    : ObservableObject
{
    public ObservableCollection<PlayerListItem> Players { get; } = [];

    [ObservableProperty] public partial bool IsEmpty { get; set; }

    public async Task LoadAsync()
    {
        var all = await players.GetAllAsync();
        var rounds = await history.GetAsync();
        Players.Clear();
        foreach (var player in all) Players.Add(new PlayerListItem(player, HistoryService.ShortStats(player.Id, rounds)));
        IsEmpty = Players.Count == 0;
    }

    [RelayCommand]
    private Task OpenAsync(PlayerListItem item) => navigation.GoToAsync($"{Routes.Player}?id={item.Player.Id}");

    [RelayCommand]
    private async Task AddPlayerAsync()
    {
        if (await editor.AddAsync() is not null) await LoadAsync();
    }
}

public sealed record AvatarColorOption(string Color, bool IsSelected);

public sealed record AvatarEmojiOption(string? Emoji, string Text, bool IsSelected);

/// <summary>Player profile: avatar, name, statistics (spec 10), chart, heat map, exclusions, delete.</summary>
public sealed partial class PlayerProfileViewModel(
    IPlayerRepository players,
    HistoryService history,
    BodyDataService bodyData,
    PlayerEditor editor,
    GameSetupService setup,
    INavigationService navigation,
    IDialogService dialogs) : ObservableObject, IQueryAttributable
{
    private static CultureInfo Culture => AppCulture.Format;
    private long _playerId;

    [ObservableProperty] public partial Player? Player { get; set; }
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Color { get; set; } = "#FF3D9A";
    [ObservableProperty] public partial string? Emoji { get; set; }
    [ObservableProperty] public partial bool ShowAvatarEditor { get; set; }
    [ObservableProperty] public partial IReadOnlyList<AvatarColorOption> ColorOptions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<AvatarEmojiOption> EmojiOptions { get; set; } = [];

    [ObservableProperty] public partial string RoundsText { get; set; } = string.Empty;
    [ObservableProperty] public partial string AverageDistanceText { get; set; } = string.Empty;
    [ObservableProperty] public partial string AveragePointsText { get; set; } = string.Empty;
    [ObservableProperty] public partial string BestZoneText { get; set; } = string.Empty;
    [ObservableProperty] public partial string WorstZoneText { get; set; } = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasTrend), nameof(HasNoTrend))] public partial IReadOnlyList<double> Trend { get; set; } = [];

    public bool HasTrend => Trend.Count >= 2;
    public bool HasNoTrend => !HasTrend;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && long.TryParse(id?.ToString(), out var value)) _playerId = value;
    }

    public async Task LoadAsync()
    {
        var player = await players.GetAsync(_playerId);
        if (player is null)
        {
            await navigation.GoBackAsync();
            return;
        }

        ShowPlayer(player);
        await bodyData.LoadAsync();
        var catalog = bodyData.Zones!;
        var stats = PlayerStatistics.Compute(player.Id, await history.GetAsync(), catalog);
        RoundsText = stats.Rounds.ToString(Culture);
        AverageDistanceText = stats.AverageDistanceCm is { } d ? string.Format(Culture, AppResources.DistanceFormat, d) : AppResources.Unreachable;
        AveragePointsText = stats.Rounds > 0 ? string.Format(Culture, AppResources.AvgPointsFormat, stats.AveragePoints) : AppResources.Unreachable;
        BestZoneText = ZoneText(stats.BestZone);
        WorstZoneText = ZoneText(stats.WorstZone);
        Trend = stats.Trend.Select(p => p.AverageDistanceCm).ToList();

        string ZoneText(ZoneStat? zone) => zone is null
            ? AppResources.Unreachable
            : string.Format(Culture, AppResources.ZoneStatFormat, catalog.TryGet(zone.ZoneId, out var z) ? ZoneNames.Of(z) : zone.ZoneId, zone.AverageDistanceCm);
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (Player is null) return;
        if (await editor.RenameAsync(Player) is { } updated) Updated(updated);
    }

    [RelayCommand]
    private void ToggleAvatarEditor() => ShowAvatarEditor = !ShowAvatarEditor;

    [RelayCommand]
    private async Task SetColorAsync(AvatarColorOption option)
    {
        if (Player is null) return;
        if (await editor.SetAvatarAsync(Player, option.Color, Player.AvatarEmoji) is { } updated) Updated(updated);
    }

    [RelayCommand]
    private async Task SetEmojiAsync(AvatarEmojiOption option)
    {
        if (Player is null) return;
        if (await editor.SetAvatarAsync(Player, Player.AvatarColor, option.Emoji) is { } updated) Updated(updated);
    }

    [RelayCommand]
    private Task OpenHeatmapAsync() => navigation.GoToAsync($"{Routes.Heatmap}?player={_playerId}");

    [RelayCommand]
    private Task OpenExclusionsAsync() => navigation.GoToAsync($"{Routes.Exclusions}?player={_playerId}");

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Player is null) return;
        var message = string.Format(Culture, AppResources.DeletePlayerMessageFormat, Player.Name);
        if (!await dialogs.ConfirmAsync(AppResources.DeletePlayerTitle, message, AppResources.Delete, AppResources.Cancel)) return;
        await players.DeleteAsync(Player.Id);
        setup.Players.RemoveAll(p => p.Id == Player.Id);
        await navigation.GoBackAsync();
    }

    private void Updated(Player player)
    {
        ShowPlayer(player);
        // Keep the players of the game being prepared in sync (name / avatar).
        var index = setup.Players.FindIndex(p => p.Id == player.Id);
        if (index >= 0) setup.Players[index] = player;
    }

    private void ShowPlayer(Player player)
    {
        Player = player;
        Name = player.Name;
        Color = player.AvatarColor;
        Emoji = player.AvatarEmoji;
        ColorOptions = AvatarPalette.Colors.Select(c => new AvatarColorOption(c, string.Equals(c, player.AvatarColor, StringComparison.OrdinalIgnoreCase))).ToList();
        EmojiOptions = new[] { new AvatarEmojiOption(null, AppResources.NoEmoji, player.AvatarEmoji is null) }
            .Concat(PlayerEditor.Emojis.Select(e => new AvatarEmojiOption(e, e, e == player.AvatarEmoji))).ToList();
    }
}
