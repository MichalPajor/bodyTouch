using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Statistics;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Core.Tests;

public class HistoryStatisticsTests
{
    private static readonly Zones.ZoneCatalog Catalog = TestData.Catalog();
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 18, 0, 0, TimeSpan.Zero);

    // Triangles of the test body (see TestData): 0-1 head.face, 3-4 arm-l.hand, 5 arm-l.elbow, 6-7 leg-l.knee, 8 leg-l.foot.
    private const int Face = 0, Hand = 3, Elbow = 5, Knee = 6, Foot = 8;

    private static int _number;

    /// <summary>A round of <paramref name="guesser"/>: target zone, distance (null = no pin), guessed zone and base points.</summary>
    private static HistoryRound R(long session, long? preset, long guesser, Target target, float? distance, string? guessed, int basePoints,
        int bonus = 0, double minutes = 0) =>
        new(session, preset, new RoundResult(++_number, guesser, guesser == 1 ? 2 : 1, target,
            distance is null ? null : new SurfacePoint(Face, 0.3f, 0.3f), distance, guessed, basePoints, bonus, 3,
            T0.AddMinutes(minutes), []));

    private static ZoneTarget Z(string id) => new(id);

    [Fact]
    public void PlayerStatsCountOnlyOwnGuessesAndAverageMeasuredDistances()
    {
        HistoryRound[] history =
        [
            R(1, 10, guesser: 1, Z("leg-l.knee"), 2, "leg-l.knee", 100),
            R(1, 10, guesser: 1, Z("leg-l.knee"), 6, "leg-l.knee", 80),
            R(1, 10, guesser: 1, Z("arm-l.hand"), 30, "head.face", 20),
            R(1, 10, guesser: 1, Z("arm-l.hand"), null, null, 0),                      // time up: counts as a round, not in distances
            R(1, 10, guesser: 1, Z("head.face"), float.PositiveInfinity, "arm-l.hand", 0),   // unreachable: not in distances
            R(1, 10, guesser: 2, Z("head.face"), 1, "head.face", 100),                 // someone else's round
        ];

        var stats = PlayerStatistics.Compute(1, history, Catalog);

        Assert.Equal(5, stats.Rounds);
        Assert.Equal((2 + 6 + 30) / 3.0, stats.AverageDistanceCm!.Value, 6);
        Assert.Equal((100 + 80 + 20 + 0 + 0) / 5.0, stats.AveragePoints, 6);
        Assert.Equal("leg-l.knee", stats.BestZone!.ZoneId);
        Assert.Equal(4, stats.BestZone.AverageDistanceCm!.Value, 6);
        Assert.Equal("arm-l.hand", stats.WorstZone!.ZoneId);
        Assert.Equal(2, stats.WorstZone.Rounds);
        Assert.Equal(10, stats.WorstZone.AverageBasePoints, 6);   // (20 + 0) / 2
    }

    [Fact]
    public void PlayerWithoutRoundsHasEmptyStats()
    {
        var stats = PlayerStatistics.Compute(7, [R(1, 10, 1, Z("head.face"), 1, "head.face", 100)], Catalog);

        Assert.Equal(0, stats.Rounds);
        Assert.Null(stats.AverageDistanceCm);
        Assert.Null(stats.BestZone);
        Assert.Null(stats.WorstZone);
        Assert.Empty(stats.Trend);
    }

    [Fact]
    public void WorstZoneNeedsAtLeastTwoMeasuredZones()
    {
        var stats = PlayerStatistics.Compute(1, [R(1, 10, 1, Z("head.face"), 5, "head.face", 100)], Catalog);

        Assert.Equal("head.face", stats.BestZone!.ZoneId);
        Assert.Null(stats.WorstZone);
    }

    [Fact]
    public void TrendHasOnePointPerSessionInTimeOrder()
    {
        HistoryRound[] history =
        [
            R(2, 10, 1, Z("head.face"), 10, "head.face", 90, minutes: 60),
            R(2, 10, 1, Z("head.face"), 20, "head.face", 50, minutes: 61),
            R(1, 10, 1, Z("head.face"), 40, "head.face", 0, minutes: 0),
            R(3, 10, 1, Z("head.face"), null, null, 0, minutes: 120),      // no measured round: no point
        ];

        var trend = PlayerStatistics.Compute(1, history, Catalog).Trend;

        Assert.Equal([1L, 2L], trend.Select(p => p.SessionId));
        Assert.Equal(40, trend[0].AverageDistanceCm, 6);
        Assert.Equal(15, trend[1].AverageDistanceCm, 6);
        Assert.Equal(2, trend[1].Rounds);
    }

    [Fact]
    public void PointTargetsCountUnderTheZoneOnTheFinestLevel()
    {
        var manual = new ManualPointTarget(new SurfacePoint(Knee, 0.2f, 0.2f));
        var custom = new CustomPointTarget(5, "Pieprzyk", new SurfacePoint(Elbow, 0.2f, 0.2f), 2);

        Assert.Equal("leg-l.knee", TargetZones.ZoneOf(manual, Catalog));
        Assert.Equal("arm-l.elbow", TargetZones.ZoneOf(custom, Catalog));
        Assert.Equal("head", TargetZones.ZoneOf(Z("head"), Catalog));
    }

    [Fact]
    public void RankingSortsQualifiedPlayersByAveragePointsAndSharesPlaces()
    {
        var history = new List<HistoryRound>();
        void Add(long player, int count, int points, float distance, long preset = 10)
        {
            for (var i = 0; i < count; i++) history.Add(R(1, preset, player, Z("head.face"), distance, "head.face", points));
        }

        Add(1, 5, 80, 5);     // avg 80, 5 rounds
        Add(2, 6, 90, 3);     // avg 90
        Add(3, 5, 80, 4);     // avg 80 – shares 2nd place with player 1, listed first (smaller distance)
        Add(4, 4, 100, 0);    // only 4 rounds: below the threshold
        Add(5, 9, 100, 0, preset: 11);   // other preset
        Add(6, 5, 50, 9);     // deleted player

        var ranking = Rankings.Compute(history, 10, new HashSet<long> { 1, 2, 3, 4, 5 });

        Assert.Equal([2L, 3L, 1L, 4L], ranking.Select(r => r.PlayerId));
        Assert.Equal([1, 2, 2, 0], ranking.Select(r => r.Rank));
        Assert.Equal([true, true, true, false], ranking.Select(r => r.Qualified));
        Assert.Equal(90, ranking[0].AveragePoints);
        Assert.Equal(4, ranking[3].Rounds);
    }

    [Fact]
    public void RankingUsesTotalPointsIncludingSpeedBonus()
    {
        var history = Enumerable.Range(0, 5).Select(_ => R(1, 10, 1, Z("head.face"), 2, "head.face", 90, bonus: 10))
            .Concat(Enumerable.Range(0, 5).Select(_ => R(1, 10, 2, Z("head.face"), 1, "head.face", 95)))
            .ToList();

        var ranking = Rankings.Compute(history, 10, new HashSet<long> { 1, 2 });

        Assert.Equal(1L, ranking[0].PlayerId);
        Assert.Equal(100, ranking[0].AveragePoints);
    }

    [Fact]
    public void RoundsWithoutPresetAreNotInAnyRanking()
    {
        var history = Enumerable.Range(0, 6).Select(_ => R(1, null, 1, Z("head.face"), 2, "head.face", 90)).ToList();

        Assert.Empty(Rankings.PresetsWithRounds(history));
        Assert.Empty(Rankings.Compute(history, 10, new HashSet<long> { 1 }));
    }

    [Theory]
    [InlineData(100, "#22C55E")]
    [InlineData(0, "#EF4444")]
    [InlineData(50, "#FACC15")]
    [InlineData(150, "#22C55E")]
    [InlineData(-5, "#EF4444")]
    [InlineData(25, "#F4882C")]   // halfway between red and yellow
    public void HeatColourGoesFromRedThroughYellowToGreen(double points, string expected) =>
        Assert.Equal(expected, Heatmap.ColorFor(points));

    [Fact]
    public void HeatmapColoursTargetZonesAndDrawsArrowsForRepeatedMistakes()
    {
        var history = new List<HistoryRound>();
        // Knee: always hit (green).
        for (var i = 0; i < 4; i++) history.Add(R(1, 10, 1, Z("leg-l.knee"), 1, "leg-l.knee", 100));
        // Hand: 0 points; guessed the elbow 3 times and the face once → arrow hand → elbow (3).
        for (var i = 0; i < 3; i++) history.Add(R(1, 10, 1, Z("arm-l.hand"), 30, "arm-l.elbow", 0));
        history.Add(R(1, 10, 1, Z("arm-l.hand"), 40, "head.face", 0));
        // Foot: guessed the knee only twice → no arrow.
        for (var i = 0; i < 2; i++) history.Add(R(1, 10, 1, Z("leg-l.foot"), 15, "leg-l.knee", 50));
        // Another preset and another player: ignored.
        for (var i = 0; i < 5; i++) history.Add(R(2, 11, 1, Z("head.face"), 30, "arm-l.hand", 0));
        for (var i = 0; i < 5; i++) history.Add(R(3, 10, 2, Z("head.face"), 30, "arm-l.hand", 0));

        var map = Heatmap.Compute(history, Catalog, playerId: 1, presetId: 10);

        Assert.Equal(["arm-l.hand", "leg-l.foot", "leg-l.knee"], map.Zones.Select(z => z.ZoneId));
        Assert.Equal("#EF4444", map.Zones[0].Color);
        Assert.Equal("#FACC15", map.Zones[1].Color);
        Assert.Equal("#22C55E", map.Zones[2].Color);
        var arrow = Assert.Single(map.Arrows);
        Assert.Equal(new ConfusionArrow("arm-l.hand", "arm-l.elbow", 3), arrow);

        var all = Heatmap.Compute(history, Catalog, playerId: 1, presetId: null);
        Assert.Contains(all.Zones, z => z.ZoneId == "head.face");
        Assert.Contains(all.Arrows, a => a is { FromZoneId: "head.face", ToZoneId: "arm-l.hand", Count: 5 });
    }

    [Fact]
    public void HeatmapCountsChosenPointsUnderTheirZone()
    {
        var target = new ManualPointTarget(new SurfacePoint(Foot, 0.2f, 0.2f));
        var history = Enumerable.Range(0, 3).Select(_ => R(1, null, 1, target, 20, "leg-l.knee", 40)).ToList();

        var map = Heatmap.Compute(history, Catalog, 1, null);

        Assert.Equal("leg-l.foot", Assert.Single(map.Zones).ZoneId);
        Assert.Equal(new ConfusionArrow("leg-l.foot", "leg-l.knee", 3), Assert.Single(map.Arrows));
    }
}
