using System.Collections.ObjectModel;
using System.Globalization;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.App.Services;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Persistence;
using BodyTouchGame.Core.Players;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BodyTouchGame.App.ViewModels;

public sealed partial class PlayerItem(Player player, string stats) : ObservableObject
{
    public Player Player { get; } = player;
    public string Name => Player.Name;
    public string Color => Player.AvatarColor;
    public string Stats { get; } = stats;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>New game, step 1 (mockup 2): who plays and in which order.</summary>
public sealed partial class PlayersSelectViewModel(
    IPlayerRepository players, ISessionRepository sessions, GameSetupService setup, INavigationService navigation, IDialogService dialogs)
    : ObservableObject
{
    public ObservableCollection<PlayerItem> Players { get; } = [];

    [ObservableProperty]
    public partial string SelectedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OrderText { get; set; } = string.Empty;

    public async Task LoadAsync()
    {
        var all = await players.GetAllAsync();
        var rounds = await sessions.GetAllRoundsAsync();
        // The page reappears also after a dialog closes: keep the selection made on the screen, otherwise start
        // from the players of the previous game.
        var previouslySelected = Players.Count > 0
            ? Players.Where(p => p.IsSelected).Select(p => p.Player.Id).ToHashSet()
            : setup.Players.Select(p => p.Id).ToHashSet();
        Players.Clear();
        foreach (var player in all)
        {
            var own = rounds.Where(r => r.Round.GuesserId == player.Id).Select(r => r.Round).ToList();
            var measured = own.Where(r => r.DistanceCm is { } d && float.IsFinite(d)).Select(r => (double)r.DistanceCm!.Value).ToList();
            var stats = own.Count == 0
                ? AppResources.PlayerNoRounds
                : measured.Count == 0
                    ? PolishPlural.Rounds(own.Count)
                    : string.Format(CultureInfo.CurrentCulture, AppResources.PlayerStatsFormat, PolishPlural.Rounds(own.Count), measured.Average());
            Players.Add(new PlayerItem(player, stats) { IsSelected = previouslySelected.Contains(player.Id) });
        }

        UpdateTexts();
    }

    [RelayCommand]
    private void Toggle(PlayerItem item)
    {
        if (!item.IsSelected && SelectedCount >= RoleScheduler.MaxPlayers) return;
        item.IsSelected = !item.IsSelected;
        UpdateTexts();
    }

    [RelayCommand]
    private async Task AddPlayerAsync()
    {
        var name = await dialogs.PromptAsync(AppResources.AddPlayerTitle, AppResources.AddPlayerMessage, AppResources.Add, Player.MaxNameLength);
        if (name is null) return;
        name = name.Trim();
        if (name.Length == 0)
        {
            await dialogs.AlertAsync(AppResources.AddPlayerTitle, AppResources.NameEmpty);
            return;
        }

        try
        {
            var all = await players.GetAllAsync();
            var added = await players.AddAsync(new Player(0, name, AvatarPalette.Next(all.Count), null, DateTimeOffset.UtcNow));
            Players.Add(new PlayerItem(added, AppResources.PlayerNoRounds) { IsSelected = SelectedCount < RoleScheduler.MaxPlayers });
            UpdateTexts();
        }
        catch (DuplicateNameException)
        {
            await dialogs.AlertAsync(AppResources.AddPlayerTitle, AppResources.NameTaken);
        }
    }

    [RelayCommand]
    private void ToggleOrder()
    {
        setup.RoleOrder = setup.RoleOrder == RoleOrder.AllPairs ? RoleOrder.Circle : RoleOrder.AllPairs;
        UpdateTexts();
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private Task NextAsync()
    {
        setup.Players.Clear();
        setup.Players.AddRange(Players.Where(p => p.IsSelected).Select(p => p.Player));
        return navigation.GoToAsync(Routes.GameConfig);
    }

    private int SelectedCount => Players.Count(p => p.IsSelected);

    private bool CanContinue() => SelectedCount is >= RoleScheduler.MinPlayers and <= RoleScheduler.MaxPlayers;

    private void UpdateTexts()
    {
        SelectedText = SelectedCount < RoleScheduler.MinPlayers
            ? AppResources.SelectPlayersHint
            : string.Format(CultureInfo.CurrentCulture, AppResources.SelectedPlayersFormat, SelectedCount);
        OrderText = setup.RoleOrder == RoleOrder.AllPairs ? AppResources.OrderAllPairs : AppResources.OrderCircle;
        NextCommand.NotifyCanExecuteChanged();
    }
}
