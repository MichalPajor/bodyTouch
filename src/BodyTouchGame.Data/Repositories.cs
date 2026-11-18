using System.Text.Json;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Persistence;
using BodyTouchGame.Core.Players;
using BodyTouchGame.Core.Serialization;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Data;

internal static class Json
{
    public static string Write<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) => JsonSerializer.Serialize(value, info);

    public static T Read<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
        JsonSerializer.Deserialize(json, info) ?? throw new InvalidDataException($"Invalid JSON column: {json}");

    public static DateTime ToDb(DateTimeOffset value) => value.UtcDateTime;

    public static DateTimeOffset FromDb(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

public sealed class PlayerRepository(BodyTouchDatabase database) : IPlayerRepository
{
    public async Task<IReadOnlyList<Player>> GetAllAsync()
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var rows = await database.Connection.Table<PlayerEntity>().ToListAsync().ConfigureAwait(false);
        return rows.Select(ToDomain).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<Player?> GetAsync(long id)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var row = await database.Connection.FindAsync<PlayerEntity>(id).ConfigureAwait(false);
        return row is null ? null : ToDomain(row);
    }

    public async Task<Player> AddAsync(Player player)
    {
        Ensure(player);
        if (await NameExistsAsync(player.Name).ConfigureAwait(false)) throw new DuplicateNameException(player.Name.Trim());
        var row = ToEntity(player with { Id = 0 });
        await database.Connection.InsertAsync(row).ConfigureAwait(false);
        return ToDomain(row);
    }

    public async Task UpdateAsync(Player player)
    {
        Ensure(player);
        if (await NameExistsAsync(player.Name, player.Id).ConfigureAwait(false)) throw new DuplicateNameException(player.Name.Trim());
        if (await database.Connection.UpdateAsync(ToEntity(player)).ConfigureAwait(false) == 0)
            throw new KeyNotFoundException($"Player {player.Id} not found.");
    }

    public async Task DeleteAsync(long id)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        await database.Connection.RunInTransactionAsync(db =>
        {
            db.Delete<PlayerEntity>(id);
            db.Delete<ExclusionProfileEntity>(id);
        }).ConfigureAwait(false);
    }

    public async Task<bool> NameExistsAsync(string name, long? exceptId = null)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var trimmed = name.Trim();
        var rows = await database.Connection.Table<PlayerEntity>().ToListAsync().ConfigureAwait(false);
        return rows.Any(p => p.Id != exceptId && string.Equals(p.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static void Ensure(Player player)
    {
        var problems = player.Validate();
        if (problems.Count > 0) throw new ArgumentException(string.Join(" ", problems), nameof(player));
    }

    private static Player ToDomain(PlayerEntity e) => new(e.Id, e.Name, e.AvatarColor, e.AvatarEmoji, Json.FromDb(e.CreatedAt));

    private static PlayerEntity ToEntity(Player p) => new()
    {
        Id = p.Id, Name = p.Name.Trim(), AvatarColor = p.AvatarColor, AvatarEmoji = p.AvatarEmoji, CreatedAt = Json.ToDb(p.CreatedAt),
    };
}

public sealed class ExclusionRepository(BodyTouchDatabase database) : IExclusionRepository
{
    public async Task<ExclusionProfile> GetAsync(long playerId)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var row = await database.Connection.FindAsync<ExclusionProfileEntity>(playerId).ConfigureAwait(false);
        return new ExclusionProfile(playerId, row is null ? [] : Json.Read(row.ZoneIdsJson, CoreJsonContext.Default.ListString));
    }

    public async Task SaveAsync(ExclusionProfile profile)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        await database.Connection.InsertOrReplaceAsync(new ExclusionProfileEntity
        {
            PlayerId = profile.PlayerId,
            ZoneIdsJson = Json.Write(profile.ZoneIds.ToList(), CoreJsonContext.Default.ListString),
        }).ConfigureAwait(false);
    }
}

public sealed class PresetRepository(BodyTouchDatabase database) : IPresetRepository
{
    public async Task<IReadOnlyList<GamePreset>> GetAllAsync()
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var rows = await database.Connection.Table<GamePresetEntity>().ToListAsync().ConfigureAwait(false);
        var order = BuiltInPresets.All.Select((p, i) => (p.Key, i)).ToDictionary(x => x.Key, x => x.i);
        return rows.Select(ToDomain)
            .OrderBy(p => p.IsBuiltIn ? 0 : 1)
            .ThenBy(p => p.IsBuiltIn ? order.GetValueOrDefault(p.Name, int.MaxValue) : 0)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<GamePreset?> GetAsync(long id)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var row = await database.Connection.FindAsync<GamePresetEntity>(id).ConfigureAwait(false);
        return row is null ? null : ToDomain(row);
    }

    public async Task<GamePreset> SaveCustomAsync(GamePreset preset)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        if (preset.IsBuiltIn) throw new InvalidOperationException("Built-in presets cannot be changed.");
        if (string.IsNullOrWhiteSpace(preset.Name)) throw new ArgumentException("A preset needs a name.", nameof(preset));
        var problems = preset.Config.Validate();
        if (problems.Count > 0) throw new ArgumentException(string.Join(" ", problems), nameof(preset));

        var name = preset.Name.Trim();
        var all = await database.Connection.Table<GamePresetEntity>().ToListAsync().ConfigureAwait(false);
        if (all.Any(p => !p.IsBuiltIn && p.Id != preset.Id && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new DuplicateNameException(name);

        var row = new GamePresetEntity { Id = preset.Id, Name = name, IsBuiltIn = false, ConfigJson = Json.Write(preset.Config, CoreJsonContext.Default.GameConfig) };
        if (preset.Id == 0)
            await database.Connection.InsertAsync(row).ConfigureAwait(false);
        else
        {
            var existing = all.FirstOrDefault(p => p.Id == preset.Id) ?? throw new KeyNotFoundException($"Preset {preset.Id} not found.");
            if (existing.IsBuiltIn) throw new InvalidOperationException("Built-in presets cannot be changed.");
            await database.Connection.UpdateAsync(row).ConfigureAwait(false);
        }

        return ToDomain(row);
    }

    public async Task DeleteCustomAsync(long id)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var row = await database.Connection.FindAsync<GamePresetEntity>(id).ConfigureAwait(false);
        if (row is null) return;
        if (row.IsBuiltIn) throw new InvalidOperationException("Built-in presets cannot be deleted.");
        await database.Connection.DeleteAsync(row).ConfigureAwait(false);
    }

    private static GamePreset ToDomain(GamePresetEntity e) =>
        new(e.Id, e.Name, e.IsBuiltIn, Json.Read(e.ConfigJson, CoreJsonContext.Default.GameConfig).Normalized());
}

public sealed class CustomPointRepository(BodyTouchDatabase database) : ICustomPointRepository
{
    public async Task<IReadOnlyList<CustomPoint>> GetAllAsync()
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var rows = await database.Connection.Table<CustomPointEntity>().ToListAsync().ConfigureAwait(false);
        return rows.OrderBy(r => r.Id).Select(ToDomain).ToList();
    }

    public async Task<CustomPoint> SaveAsync(CustomPoint point)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var problems = point.Validate();
        if (problems.Count > 0) throw new ArgumentException(string.Join(" ", problems), nameof(point));
        var row = new CustomPointEntity
        {
            Id = point.Id,
            Name = point.Name.Trim(),
            TriangleIndex = point.Point.TriangleIndex,
            BaryU = point.Point.BaryU,
            BaryV = point.Point.BaryV,
            RadiusCm = point.RadiusCm,
            LevelsJson = Json.Write(point.Levels.Distinct().Order().ToList(), CoreJsonContext.Default.ListZoneLevel),
            VertexIdsJson = Json.Write(point.VertexIds.ToList(), CoreJsonContext.Default.ListInt32),
        };
        if (point.Id == 0) await database.Connection.InsertAsync(row).ConfigureAwait(false);
        else if (await database.Connection.UpdateAsync(row).ConfigureAwait(false) == 0) throw new KeyNotFoundException($"Custom point {point.Id} not found.");
        return ToDomain(row);
    }

    public async Task DeleteAsync(long id)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        await database.Connection.DeleteAsync<CustomPointEntity>(id).ConfigureAwait(false);
    }

    private static CustomPoint ToDomain(CustomPointEntity e) => new(e.Id, e.Name, new SurfacePoint(e.TriangleIndex, e.BaryU, e.BaryV), e.RadiusCm,
        Json.Read(e.LevelsJson, CoreJsonContext.Default.ListZoneLevel), Json.Read(e.VertexIdsJson, CoreJsonContext.Default.ListInt32));
}

public sealed class LevelOverrideRepository(BodyTouchDatabase database) : ILevelOverrideRepository
{
    public async Task<IReadOnlyList<LevelOverride>> GetAllAsync()
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var rows = await database.Connection.Table<LevelOverrideEntity>().ToListAsync().ConfigureAwait(false);
        return rows.OrderBy(r => r.ZoneId, StringComparer.Ordinal).ThenBy(r => r.Level, StringComparer.Ordinal)
            .Select(r => new LevelOverride(r.ZoneId, Enum.Parse<ZoneLevel>(r.Level), r.IsIncluded)).ToList();
    }

    public async Task SetAsync(LevelOverride levelOverride)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var level = levelOverride.Level.ToString();
        await database.Connection.RunInTransactionAsync(db =>
        {
            var row = db.Table<LevelOverrideEntity>().FirstOrDefault(r => r.ZoneId == levelOverride.ZoneId && r.Level == level);
            if (row is null) db.Insert(new LevelOverrideEntity { ZoneId = levelOverride.ZoneId, Level = level, IsIncluded = levelOverride.IsIncluded });
            else
            {
                row.IsIncluded = levelOverride.IsIncluded;
                db.Update(row);
            }
        }).ConfigureAwait(false);
    }

    public async Task ClearAsync()
    {
        await database.InitializeAsync().ConfigureAwait(false);
        await database.Connection.DeleteAllAsync<LevelOverrideEntity>().ConfigureAwait(false);
    }
}

public sealed class SessionRepository(BodyTouchDatabase database) : ISessionRepository
{
    public async Task<long> CreateAsync(SessionRecord session)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var row = new SessionEntity
        {
            PresetId = session.PresetId,
            ConfigJson = Json.Write(session.Config, CoreJsonContext.Default.GameConfig),
            PlayerIdsJson = Json.Write(session.PlayerIds.ToList(), CoreJsonContext.Default.ListInt64),
            RoleOrder = session.RoleOrder.ToString(),
            StartedAt = Json.ToDb(session.StartedAt),
            EndedAt = session.EndedAt is { } ended ? Json.ToDb(ended) : null,
        };
        await database.Connection.InsertAsync(row).ConfigureAwait(false);
        foreach (var round in session.Rounds) await AddRoundAsync(row.Id, round).ConfigureAwait(false);
        return row.Id;
    }

    public async Task AddRoundAsync(long sessionId, RoundResult round)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        await database.Connection.InsertAsync(ToEntity(sessionId, round)).ConfigureAwait(false);
    }

    public async Task EndAsync(long sessionId, DateTimeOffset endedAt)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var row = await database.Connection.FindAsync<SessionEntity>(sessionId).ConfigureAwait(false)
                  ?? throw new KeyNotFoundException($"Session {sessionId} not found.");
        row.EndedAt = Json.ToDb(endedAt);
        await database.Connection.UpdateAsync(row).ConfigureAwait(false);
    }

    public async Task<SessionRecord?> GetAsync(long id)
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var row = await database.Connection.FindAsync<SessionEntity>(id).ConfigureAwait(false);
        return row is null ? null : await LoadAsync(row).ConfigureAwait(false);
    }

    public async Task<SessionRecord?> GetLatestEndedAsync()
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var row = await database.Connection.Table<SessionEntity>().Where(s => s.EndedAt != null)
            .OrderByDescending(s => s.EndedAt).FirstOrDefaultAsync().ConfigureAwait(false);
        return row is null ? null : await LoadAsync(row).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(long SessionId, long? PresetId, RoundResult Round)>> GetAllRoundsAsync()
    {
        await database.InitializeAsync().ConfigureAwait(false);
        var sessions = (await database.Connection.Table<SessionEntity>().ToListAsync().ConfigureAwait(false)).ToDictionary(s => s.Id);
        var rounds = await database.Connection.Table<RoundEntity>().ToListAsync().ConfigureAwait(false);
        return rounds.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Select(r => (r.SessionId, sessions.TryGetValue(r.SessionId, out var s) ? s.PresetId : null, ToDomain(r)))
            .ToList();
    }

    private async Task<SessionRecord> LoadAsync(SessionEntity s)
    {
        var rounds = await database.Connection.Table<RoundEntity>().Where(r => r.SessionId == s.Id).ToListAsync().ConfigureAwait(false);
        return new SessionRecord(
            s.Id,
            s.PresetId,
            Json.Read(s.ConfigJson, CoreJsonContext.Default.GameConfig),
            Enum.Parse<RoleOrder>(s.RoleOrder),
            Json.Read(s.PlayerIdsJson, CoreJsonContext.Default.ListInt64),
            Json.FromDb(s.StartedAt),
            s.EndedAt is { } ended ? Json.FromDb(ended) : null,
            rounds.OrderBy(r => r.RoundNumber).ThenBy(r => r.Id).Select(ToDomain).ToList());
    }

    private static RoundEntity ToEntity(long sessionId, RoundResult r)
    {
        var e = new RoundEntity
        {
            SessionId = sessionId,
            RoundNumber = r.RoundNumber,
            GuesserId = r.GuesserId,
            ModelId = r.ModelId,
            TargetKind = r.Target.Kind.ToString(),
            PinTriangle = r.Pin?.TriangleIndex,
            PinBaryU = r.Pin?.BaryU,
            PinBaryV = r.Pin?.BaryV,
            DistanceCm = r.DistanceCm,
            GuessedZoneId = r.GuessedZoneId,
            Points = r.Points,
            BasePoints = r.BasePoints,
            SpeedBonus = r.SpeedBonus,
            GuessSeconds = r.GuessSeconds,
            CreatedAt = Json.ToDb(r.CreatedAt),
        };
        switch (r.Target)
        {
            case ZoneTarget zone:
                e.TargetId = zone.ZoneId;
                break;
            case CustomPointTarget point:
                e.TargetId = point.PointId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                e.TargetName = point.Name;
                e.TargetRadiusCm = point.RadiusCm;
                SetPoint(e, point.Point);
                break;
            case ManualPointTarget manual:
                SetPoint(e, manual.Point);
                break;
        }

        return e;
    }

    private static void SetPoint(RoundEntity e, SurfacePoint p)
    {
        e.TargetTriangle = p.TriangleIndex;
        e.TargetBaryU = p.BaryU;
        e.TargetBaryV = p.BaryV;
    }

    private static RoundResult ToDomain(RoundEntity e)
    {
        SurfacePoint TargetPoint() => new(e.TargetTriangle ?? 0, e.TargetBaryU ?? 0, e.TargetBaryV ?? 0);
        Target target = Enum.Parse<TargetKind>(e.TargetKind) switch
        {
            TargetKind.Zone => new ZoneTarget(e.TargetId ?? ""),
            TargetKind.CustomPoint => new CustomPointTarget(long.Parse(e.TargetId ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                e.TargetName ?? "", TargetPoint(), e.TargetRadiusCm ?? 0),
            _ => new ManualPointTarget(TargetPoint()),
        };
        SurfacePoint? pin = e.PinTriangle is { } t ? new SurfacePoint(t, e.PinBaryU ?? 0, e.PinBaryV ?? 0) : null;
        return new RoundResult(e.RoundNumber, e.GuesserId, e.ModelId, target, pin, e.DistanceCm, e.GuessedZoneId,
            e.BasePoints, e.SpeedBonus, e.GuessSeconds, Json.FromDb(e.CreatedAt), []);
    }
}
