namespace BodyTouchGame.Core.Config;

/// <summary>Who decides the target of a round (spec 5, decision D4).</summary>
public enum TargetSelection
{
    /// <summary>The app draws a target from the zone set (default).</summary>
    Random,

    /// <summary>The model places a pin anywhere on the body; that point is the target.</summary>
    ChosenByModel,
}

public enum TouchMethod
{
    WholeHand,
    TwoFingers,
    OneFinger,
    Tap,
}

public enum ZoneLevel
{
    Easy,
    Medium,
    Hard,
    Hardcore,
}

/// <summary>Order of guesser/model pairs in a session (players screen, spec 5).</summary>
public enum RoleOrder
{
    /// <summary>Every player guesses with every other player as the model (default).</summary>
    AllPairs,

    /// <summary>In a circle: each player guesses with the next player as the model.</summary>
    Circle,
}

/// <summary>Game settings of a session (spec 6). Every field is set independently.</summary>
public sealed record GameConfig(
    TargetSelection TargetSelection,
    ZoneSetSelection ZoneSet,
    TouchMethod TouchMethod,
    bool MovementAllowed,
    int TouchSeconds,
    int? GuessSeconds,
    double FullPointsRadiusCm,
    double ZeroPointsDistanceCm,
    bool SpeedBonus,
    int RoundsPerPlayer)
{
    public const int MinTouchSeconds = 1;
    public const int MaxTouchSeconds = 10;
    public const int MinGuessSeconds = 3;
    public const int MaxGuessSeconds = 30;
    public const int MinRoundsPerPlayer = 1;
    public const int MaxRoundsPerPlayer = 20;

    /// <summary>Problems that make the configuration unusable (empty = valid). Messages are for developers/logs;
    /// the UI prevents invalid values with constrained controls.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(TargetSelection)) problems.Add("Unknown target selection.");
        if (!Enum.IsDefined(TouchMethod)) problems.Add("Unknown touch method.");
        if (TouchSeconds is < MinTouchSeconds or > MaxTouchSeconds)
            problems.Add($"TouchSeconds must be {MinTouchSeconds}..{MaxTouchSeconds}.");
        if (GuessSeconds is { } guess && guess is < MinGuessSeconds or > MaxGuessSeconds)
            problems.Add($"GuessSeconds must be empty or {MinGuessSeconds}..{MaxGuessSeconds}.");
        if (!double.IsFinite(FullPointsRadiusCm) || FullPointsRadiusCm < 0)
            problems.Add("FullPointsRadiusCm must be a non-negative number.");
        if (!double.IsFinite(ZeroPointsDistanceCm) || ZeroPointsDistanceCm <= FullPointsRadiusCm)
            problems.Add("ZeroPointsDistanceCm must be greater than FullPointsRadiusCm.");
        if (RoundsPerPlayer is < MinRoundsPerPlayer or > MaxRoundsPerPlayer)
            problems.Add($"RoundsPerPlayer must be {MinRoundsPerPlayer}..{MaxRoundsPerPlayer}.");
        if (TargetSelection == TargetSelection.Random && ZoneSet.IsEmpty)
            problems.Add("The zone set is empty.");
        return problems;
    }
}

/// <summary>
/// Which targets can be drawn in <see cref="TargetSelection.Random"/> mode: whole levels (any mix),
/// individually chosen built-in zones and custom points. Lists are normalised (distinct, sorted), so two
/// selections with the same content are equal.
/// </summary>
public sealed record ZoneSetSelection
{
    // Parameter types match the properties so System.Text.Json can use this constructor.
    public ZoneSetSelection(IReadOnlyList<ZoneLevel>? levels = null, IReadOnlyList<string>? zoneIds = null, IReadOnlyList<long>? customPointIds = null)
    {
        Levels = (levels ?? []).Distinct().Order().ToArray();
        ZoneIds = (zoneIds ?? []).Distinct().Order(StringComparer.Ordinal).ToArray();
        CustomPointIds = (customPointIds ?? []).Distinct().Order().ToArray();
    }

    public IReadOnlyList<ZoneLevel> Levels { get; }
    public IReadOnlyList<string> ZoneIds { get; }
    public IReadOnlyList<long> CustomPointIds { get; }

    public bool IsEmpty => Levels.Count == 0 && ZoneIds.Count == 0 && CustomPointIds.Count == 0;

    public static ZoneSetSelection ForLevels(params ZoneLevel[] levels) => new(levels);

    public bool Equals(ZoneSetSelection? other) =>
        other is not null && Levels.SequenceEqual(other.Levels) && ZoneIds.SequenceEqual(other.ZoneIds) &&
        CustomPointIds.SequenceEqual(other.CustomPointIds);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var level in Levels) hash.Add(level);
        foreach (var id in ZoneIds) hash.Add(id, StringComparer.Ordinal);
        foreach (var id in CustomPointIds) hash.Add(id);
        return hash.ToHashCode();
    }
}
