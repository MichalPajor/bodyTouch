using System.Globalization;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.App.Services;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BodyTouchGame.App.ViewModels;

/// <summary>Start screen (mockup 1): last game tile and the main menu.</summary>
public sealed partial class StartViewModel(ISessionRepository sessions, IPlayerRepository players, INavigationService navigation) : ObservableObject
{
    [ObservableProperty]
    public partial string? LastGameText { get; set; }

    [ObservableProperty]
    public partial bool HasLastGame { get; set; }

    /// <summary>Refreshes the "Last game" tile from the most recently ended session.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var last = await sessions.GetLatestEndedAsync();
            if (last is null || last.Rounds.Count == 0)
            {
                HasLastGame = false;
                return;
            }

            var allPlayers = (await players.GetAllAsync()).ToDictionary(p => p.Id);
            var sessionPlayers = last.PlayerIds.Where(allPlayers.ContainsKey).Select(id => allPlayers[id]).ToList();
            var summary = SessionStatistics.Compute(sessionPlayers, last.Rounds);
            var winners = summary.Standings.Where(s => s.Rank == 1).ToList();
            if (winners.Count == 0)
            {
                HasLastGame = false;
                return;
            }

            var names = string.Join(", ", winners.Select(w => allPlayers[w.PlayerId].Name));
            LastGameText = string.Format(AppCulture.Format, winners.Count == 1 ? AppResources.LastGameWinner : AppResources.LastGameTie,
                names, winners[0].TotalPoints);
            HasLastGame = true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[BTG] last game: {e.Message}");
            HasLastGame = false;
        }
    }

    [RelayCommand]
    private Task PlayAsync() => navigation.GoToAsync(Routes.PlayersSelect);

    [RelayCommand]
    private Task PlayersAsync() => navigation.GoToAsync(Routes.Players);

    [RelayCommand]
    private Task RankingsAsync() => navigation.GoToAsync(Routes.Rankings);

    [RelayCommand]
    private Task HowToPlayAsync() => navigation.GoToAsync(Routes.Tutorial);

    [RelayCommand]
    private Task SettingsAsync() => navigation.GoToAsync(Routes.Settings);

}
