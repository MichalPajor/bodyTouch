using SQLite;

namespace BodyTouchGame.Data;

// SQLite tables (spec 11). Dates are stored as UTC ticks (sqlite-net default). Lists and configs are JSON
// written with BodyTouchGame.Core.Serialization.CoreJsonContext.

[Table("SchemaInfo")]
internal sealed class SchemaInfoEntity
{
    [PrimaryKey] public int Id { get; set; } = 1;
    public int Version { get; set; }
}

[Table("Players")]
internal sealed class PlayerEntity
{
    [PrimaryKey, AutoIncrement] public long Id { get; set; }
    [NotNull] public string Name { get; set; } = "";
    [NotNull] public string AvatarColor { get; set; } = "";
    public string? AvatarEmoji { get; set; }
    public DateTime CreatedAt { get; set; }
}

[Table("ExclusionProfiles")]
internal sealed class ExclusionProfileEntity
{
    [PrimaryKey] public long PlayerId { get; set; }
    [NotNull] public string ZoneIdsJson { get; set; } = "[]";
}

[Table("GamePresets")]
internal sealed class GamePresetEntity
{
    [PrimaryKey, AutoIncrement] public long Id { get; set; }

    /// <summary>User-entered name, or the stable key (Easy, Medium, Hard, Hardcore) of a built-in preset.</summary>
    [NotNull] public string Name { get; set; } = "";

    public bool IsBuiltIn { get; set; }
    [NotNull] public string ConfigJson { get; set; } = "";
}

[Table("CustomPoints")]
internal sealed class CustomPointEntity
{
    [PrimaryKey, AutoIncrement] public long Id { get; set; }
    [NotNull] public string Name { get; set; } = "";
    public int TriangleIndex { get; set; }
    public float BaryU { get; set; }
    public float BaryV { get; set; }
    public float RadiusCm { get; set; }
    [NotNull] public string LevelsJson { get; set; } = "[]";
    [NotNull] public string VertexIdsJson { get; set; } = "[]";
}

[Table("LevelOverrides")]
internal sealed class LevelOverrideEntity
{
    [PrimaryKey, AutoIncrement] public long Id { get; set; }
    [NotNull, Indexed(Name = "UX_LevelOverrides_Zone_Level", Order = 1, Unique = true)] public string ZoneId { get; set; } = "";
    [NotNull, Indexed(Name = "UX_LevelOverrides_Zone_Level", Order = 2, Unique = true)] public string Level { get; set; } = "";
    public bool IsIncluded { get; set; }
}

[Table("Sessions")]
internal sealed class SessionEntity
{
    [PrimaryKey, AutoIncrement] public long Id { get; set; }
    public long? PresetId { get; set; }
    [NotNull] public string ConfigJson { get; set; } = "";

    /// <summary>Not in spec 11: the session's players in order (spec 10 "a session has a list of players").</summary>
    [NotNull] public string PlayerIdsJson { get; set; } = "[]";

    /// <summary>Not in spec 11: AllPairs / Circle.</summary>
    [NotNull] public string RoleOrder { get; set; } = "";

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}

[Table("Rounds")]
internal sealed class RoundEntity
{
    [PrimaryKey, AutoIncrement] public long Id { get; set; }
    [Indexed] public long SessionId { get; set; }

    /// <summary>Not in spec 11: 1-based round number within the session.</summary>
    public int RoundNumber { get; set; }

    [Indexed] public long GuesserId { get; set; }
    public long ModelId { get; set; }
    [NotNull] public string TargetKind { get; set; } = "";

    /// <summary>Zone id or custom point id; empty for a manual point.</summary>
    public string? TargetId { get; set; }

    /// <summary>Point of a manual or custom point target (custom points are stored too, so history survives
    /// deleting the point).</summary>
    public int? TargetTriangle { get; set; }

    public float? TargetBaryU { get; set; }
    public float? TargetBaryV { get; set; }

    /// <summary>Not in spec 11: radius and name of a custom point target at the time of the round.</summary>
    public float? TargetRadiusCm { get; set; }

    public string? TargetName { get; set; }
    public int? PinTriangle { get; set; }
    public float? PinBaryU { get; set; }
    public float? PinBaryV { get; set; }
    public float? DistanceCm { get; set; }
    public string? GuessedZoneId { get; set; }

    /// <summary>Total points (base + speed bonus).</summary>
    public int Points { get; set; }

    /// <summary>Not in spec 11: score breakdown.</summary>
    public int BasePoints { get; set; }

    public int SpeedBonus { get; set; }
    public double GuessSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
}
