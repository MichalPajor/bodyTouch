using System.Globalization;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.Core.Persistence;
using BodyTouchGame.Core.Players;
using BodyTouchGame.Core.Statistics;

namespace BodyTouchGame.App.Services;

/// <summary>All stored rounds with their session preset (input of statistics, rankings and the heat map).</summary>
public sealed class HistoryService(ISessionRepository sessions)
{
    public async Task<IReadOnlyList<HistoryRound>> GetAsync() =>
        (await sessions.GetAllRoundsAsync()).Select(r => new HistoryRound(r.SessionId, r.PresetId, r.Round)).ToList();

    /// <summary>"3 rundy · śr. 12,4 cm" or "Bez rozegranych rund" (player lists).</summary>
    public static string ShortStats(long playerId, IEnumerable<HistoryRound> history)
    {
        var own = history.Where(h => h.Round.GuesserId == playerId).ToList();
        if (own.Count == 0) return AppResources.PlayerNoRounds;
        var measured = own.Where(h => h.IsMeasured).Select(h => (double)h.Round.DistanceCm!.Value).ToList();
        return measured.Count == 0
            ? PolishPlural.Rounds(own.Count)
            : string.Format(CultureInfo.CurrentCulture, AppResources.PlayerStatsFormat, PolishPlural.Rounds(own.Count), measured.Average());
    }
}

/// <summary>Adding and renaming players with the app's dialogs (unique names, spec 10).</summary>
public sealed class PlayerEditor(IPlayerRepository players, IDialogService dialogs)
{
    public static readonly string[] Emojis = ["😎", "🦊", "🐱", "🐼", "🐸", "🦄", "🐙", "🔥", "⭐", "🍕", "👻", "🎉"];

    /// <summary>Asks for a name and adds the player; null when cancelled or the name is invalid / taken.</summary>
    public async Task<Player?> AddAsync()
    {
        var name = await AskNameAsync(AppResources.AddPlayerTitle, AppResources.Add, string.Empty);
        if (name is null) return null;
        try
        {
            var all = await players.GetAllAsync();
            return await players.AddAsync(new Player(0, name, AvatarPalette.Next(all.Count), null, DateTimeOffset.UtcNow));
        }
        catch (DuplicateNameException)
        {
            await dialogs.AlertAsync(AppResources.AddPlayerTitle, AppResources.NameTaken);
            return null;
        }
    }

    /// <summary>Asks for a new name; returns the updated player or null when cancelled / unchanged / taken.</summary>
    public async Task<Player?> RenameAsync(Player player)
    {
        var name = await AskNameAsync(AppResources.RenameTitle, AppResources.Save, player.Name);
        if (name is null || name == player.Name) return null;
        return await SaveAsync(player with { Name = name }, AppResources.RenameTitle);
    }

    public Task<Player?> SetAvatarAsync(Player player, string color, string? emoji) =>
        SaveAsync(player with { AvatarColor = color, AvatarEmoji = string.IsNullOrEmpty(emoji) ? null : emoji }, AppResources.ChangeAvatar);

    private async Task<Player?> SaveAsync(Player player, string title)
    {
        try
        {
            await players.UpdateAsync(player);
            return player;
        }
        catch (DuplicateNameException)
        {
            await dialogs.AlertAsync(title, AppResources.NameTaken);
            return null;
        }
    }

    private async Task<string?> AskNameAsync(string title, string accept, string initial)
    {
        var name = (await dialogs.PromptAsync(title, AppResources.AddPlayerMessage, accept, Player.MaxNameLength, initial))?.Trim();
        if (name is null) return null;
        if (name.Length > 0) return name;
        await dialogs.AlertAsync(title, AppResources.NameEmpty);
        return null;
    }
}
