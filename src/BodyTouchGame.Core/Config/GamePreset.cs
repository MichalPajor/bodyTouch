namespace BodyTouchGame.Core.Config;

/// <summary>
/// A named configuration. Built-in presets have a fixed <see cref="BuiltInKey"/> (the UI shows a localised name
/// for it); custom presets have a user-entered <see cref="Name"/>.
/// </summary>
public sealed record GamePreset(long Id, string Name, bool IsBuiltIn, GameConfig Config)
{
    /// <summary>For built-in presets the name is the stable key (Easy, Medium, Hard, Hardcore).</summary>
    public string? BuiltInKey => IsBuiltIn ? Name : null;
}

/// <summary>The four built-in presets (spec 6). All use <see cref="TargetSelection.Random"/>.</summary>
public static class BuiltInPresets
{
    public const string EasyKey = "Easy";
    public const string MediumKey = "Medium";
    public const string HardKey = "Hard";
    public const string HardcoreKey = "Hardcore";
    public const int DefaultRoundsPerPlayer = 3;

    public static GameConfig Easy { get; } = Create(ZoneLevel.Easy, TouchMethod.WholeHand, movement: true, touchSeconds: 6, r: 10, z: 60);
    public static GameConfig Medium { get; } = Create(ZoneLevel.Medium, TouchMethod.TwoFingers, movement: true, touchSeconds: 5, r: 5, z: 40);
    // Touch time at least 5 s (range 5..30 s since stage 8): Hard 4 s -> 5 s, Hardcore 2 s -> 5 s.
    public static GameConfig Hard { get; } = Create(ZoneLevel.Hard, TouchMethod.OneFinger, movement: true, touchSeconds: 5, r: 3, z: 25);
    public static GameConfig Hardcore { get; } = Create(ZoneLevel.Hardcore, TouchMethod.Tap, movement: false, touchSeconds: 5, r: 1.5, z: 15);

    /// <summary>Key and configuration in display order.</summary>
    public static IReadOnlyList<(string Key, GameConfig Config)> All { get; } =
    [
        (EasyKey, Easy),
        (MediumKey, Medium),
        (HardKey, Hard),
        (HardcoreKey, Hardcore),
    ];

    private static GameConfig Create(ZoneLevel level, TouchMethod method, bool movement, int touchSeconds, double r, double z) =>
        new(TargetSelection.Random, ZoneSetSelection.ForLevels(level), method, movement, touchSeconds,
            GuessSeconds: null, FullPointsRadiusCm: r, ZeroPointsDistanceCm: z, SpeedBonus: false,
            RoundsPerPlayer: DefaultRoundsPerPlayer);
}
