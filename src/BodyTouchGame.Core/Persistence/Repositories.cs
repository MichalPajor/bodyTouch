using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Players;
using BodyTouchGame.Core.Zones;

namespace BodyTouchGame.Core.Persistence;

// Storage ports. Implemented by BodyTouchGame.Data (SQLite); Core and the UI depend only on these interfaces.

/// <summary>A stored session with its rounds (spec 10, 11).</summary>
public sealed record SessionRecord(
    long Id,
    long? PresetId,
    GameConfig Config,
    RoleOrder RoleOrder,
    IReadOnlyList<long> PlayerIds,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<RoundResult> Rounds);

public interface IPlayerRepository
{
    Task<IReadOnlyList<Player>> GetAllAsync();
    Task<Player?> GetAsync(long id);

    /// <summary>Adds a player and returns it with its id. Throws <see cref="DuplicateNameException"/> when the name is taken.</summary>
    Task<Player> AddAsync(Player player);

    Task UpdateAsync(Player player);

    /// <summary>Deletes the player and their exclusion profile (rounds stay for statistics).</summary>
    Task DeleteAsync(long id);

    Task<bool> NameExistsAsync(string name, long? exceptId = null);
}

public interface IExclusionRepository
{
    /// <summary>The player's profile (empty when none was saved).</summary>
    Task<ExclusionProfile> GetAsync(long playerId);

    Task SaveAsync(ExclusionProfile profile);
}

public interface IPresetRepository
{
    /// <summary>Built-in presets first (in display order), then custom ones by name.</summary>
    Task<IReadOnlyList<GamePreset>> GetAllAsync();

    Task<GamePreset?> GetAsync(long id);

    /// <summary>Saves a custom preset (new when Id = 0). Built-in presets cannot be changed.</summary>
    Task<GamePreset> SaveCustomAsync(GamePreset preset);

    Task DeleteCustomAsync(long id);
}

public interface ICustomPointRepository
{
    Task<IReadOnlyList<CustomPoint>> GetAllAsync();
    Task<CustomPoint> SaveAsync(CustomPoint point);
    Task DeleteAsync(long id);
}

public interface ILevelOverrideRepository
{
    Task<IReadOnlyList<LevelOverride>> GetAllAsync();
    Task SetAsync(LevelOverride levelOverride);

    /// <summary>"Restore defaults": removes all overrides.</summary>
    Task ClearAsync();
}

public interface ISessionRepository
{
    /// <summary>Creates a session (rounds are added as they finish) and returns its id.</summary>
    Task<long> CreateAsync(SessionRecord session);

    Task AddRoundAsync(long sessionId, RoundResult round);
    Task EndAsync(long sessionId, DateTimeOffset endedAt);
    Task<SessionRecord?> GetAsync(long id);

    /// <summary>The most recently ended session ("Last game" tile), or null.</summary>
    Task<SessionRecord?> GetLatestEndedAsync();

    /// <summary>All rounds, oldest first (rankings, statistics, heat map).</summary>
    Task<IReadOnlyList<(long SessionId, long? PresetId, RoundResult Round)>> GetAllRoundsAsync();
}

public sealed class DuplicateNameException(string name) : Exception($"The name '{name}' is already used.")
{
    public string Name { get; } = name;
}
