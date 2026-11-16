using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Players;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Core.Tests;

/// <summary>Small synthetic body: 2 levels, 12 triangles.</summary>
internal static class TestData
{
    // index: zone
    //  0 head (Easy)          4 head.face (Medium)
    //  1 arm-l (Easy)         5 head.scalp (Medium)
    //  2 leg-l (Easy)         6 arm-l.hand (Medium)
    //  3 pelvis (Easy, hidden) 7 arm-l.elbow (Medium)
    //                         8 leg-l.knee (Medium)
    //                         9 leg-l.foot (Medium)
    //                        10 pelvis.all (Medium, hidden)
    public static readonly IReadOnlyList<ZoneDefinition> Zones =
    [
        new("head", ZoneLevel.Easy, null, "Głowa", ZoneSide.Center),
        new("arm-l", ZoneLevel.Easy, null, "Lewa ręka", ZoneSide.Left),
        new("leg-l", ZoneLevel.Easy, null, "Lewa noga", ZoneSide.Left),
        new("pelvis", ZoneLevel.Easy, null, "Miednica", ZoneSide.Center, Drawable: false),
        new("head.face", ZoneLevel.Medium, "head", "Twarz", ZoneSide.Center),
        new("head.scalp", ZoneLevel.Medium, "head", "Czubek i tył głowy", ZoneSide.Center),
        new("arm-l.hand", ZoneLevel.Medium, "arm-l", "Lewa dłoń", ZoneSide.Left),
        new("arm-l.elbow", ZoneLevel.Medium, "arm-l", "Lewy łokieć", ZoneSide.Left),
        new("leg-l.knee", ZoneLevel.Medium, "leg-l", "Lewe kolano", ZoneSide.Left),
        new("leg-l.foot", ZoneLevel.Medium, "leg-l", "Lewa stopa", ZoneSide.Left),
        new("pelvis.all", ZoneLevel.Medium, "pelvis", "Miednica", ZoneSide.Center, Drawable: false),
    ];

    // triangle:          0  1  2  3  4  5  6  7  8  9 10 11
    private static readonly int[] Easy = [0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3];
    private static readonly int[] Medium = [4, 4, 5, 6, 6, 7, 8, 8, 9, 10, 10, 10];

    public static ZoneCatalog Catalog() =>
        new(Zones, new Dictionary<ZoneLevel, int[]> { [ZoneLevel.Easy] = Easy, [ZoneLevel.Medium] = Medium }, Easy.Length);

    public static Player Ola => new(1, "Ola", "#FF3D9A", null, DateTimeOffset.UnixEpoch);
    public static Player Kuba => new(2, "Kuba", "#22D3EE", null, DateTimeOffset.UnixEpoch);
    public static Player Marta => new(3, "Marta", "#8B5CF6", null, DateTimeOffset.UnixEpoch);

    public static SurfacePoint OnTriangle(int t) => new(t, 0.3f, 0.3f);

    public static GameConfig Config(TargetSelection selection = TargetSelection.Random, int rounds = 1, bool speedBonus = false,
        params ZoneLevel[] levels) =>
        BuiltInPresets.Medium with
        {
            TargetSelection = selection,
            RoundsPerPlayer = rounds,
            SpeedBonus = speedBonus,
            ZoneSet = ZoneSetSelection.ForLevels(levels.Length > 0 ? levels : [ZoneLevel.Medium]),
        };

    /// <summary>A session with all players' pools built from the config (no exclusions).</summary>
    public static (GameSession Session, FakeClock Clock, FakeDistances Distances) Session(
        GameConfig config, IReadOnlyList<Player> players, RoleOrder order = RoleOrder.AllPairs,
        IReadOnlyDictionary<long, IReadOnlyList<string>>? exclusions = null, IRandomSource? random = null)
    {
        var catalog = Catalog();
        var builder = new TargetPoolBuilder(catalog);
        var pools = players.ToDictionary(p => p.Id,
            p => builder.Build(config.ZoneSet, exclusions?.GetValueOrDefault(p.Id) ?? []));
        var clock = new FakeClock();
        var distances = new FakeDistances();
        var session = new GameSession(new SessionSetup(players, config, order, PresetId: null, pools), distances, catalog,
            random ?? new SequenceRandom(0), clock);
        return (session, clock, distances);
    }
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
}

/// <summary>Returns the given values in turn (modulo the range).</summary>
internal sealed class SequenceRandom(params int[] values) : IRandomSource
{
    private int _next;

    public int Next(int maxExclusive) => values.Length == 0 ? 0 : values[_next++ % values.Length] % maxExclusive;
}

internal sealed class FakeDistances : IDistanceCalculator
{
    public float Distance { get; set; } = 10;
    public List<(SurfacePoint Pin, Target Target)> Calls { get; } = [];

    public DistanceResult Measure(SurfacePoint pin, Target target)
    {
        Calls.Add((pin, target));
        return new DistanceResult(Distance, Distance == 0, [new System.Numerics.Vector3(0, 0, 0)]);
    }
}
