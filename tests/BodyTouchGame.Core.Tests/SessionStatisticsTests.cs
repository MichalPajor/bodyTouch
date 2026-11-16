using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Targets;

namespace BodyTouchGame.Core.Tests;

public class SessionStatisticsTests
{
    private static RoundResult Round(int number, long guesser, float? distance, int points, string zone = "head") =>
        new(number, guesser, guesser == 1 ? 2 : 1, new ZoneTarget(zone), distance is null ? null : TestData.OnTriangle(0), distance,
            "head", points, 0, 3, DateTimeOffset.UnixEpoch, []);

    [Fact]
    public void StandingsAreSortedByTotalPointsWithSharedRanks()
    {
        RoundResult[] rounds =
        [
            Round(1, 1, 5, 90), Round(2, 2, 20, 40), Round(3, 3, 10, 70),
            Round(4, 1, 8, 80), Round(5, 2, 2, 100), Round(6, 3, 30, 100),
        ];

        var summary = SessionStatistics.Compute([TestData.Ola, TestData.Kuba, TestData.Marta], rounds);

        Assert.Equal([(1L, 1, 170), (3L, 1, 170), (2L, 3, 140)], summary.Standings.Select(s => (s.PlayerId, s.Rank, s.TotalPoints)));
        Assert.Equal(6.5, summary.Standings[0].AverageDistanceCm!.Value, 6);   // Ola: tie broken by the smaller average distance
    }

    [Fact]
    public void BestHitBiggestMissAverageAndHardestZone()
    {
        RoundResult[] rounds =
        [
            Round(1, 1, 4, 90, "leg-l.knee"),
            Round(2, 2, 0, 100, "head.face"),
            Round(3, 1, 38, 0, "leg-l.foot"),
            Round(4, 2, null, 0, "head.face"),                       // timeout: no distance
            Round(5, 1, float.PositiveInfinity, 0, "arm-l.hand"),    // unreachable: ignored in distance statistics
            Round(6, 2, 22, 30, "leg-l.foot"),
        ];

        var summary = SessionStatistics.Compute([TestData.Ola, TestData.Kuba], rounds);

        Assert.Equal(2, summary.BestHit!.RoundNumber);
        Assert.Equal(3, summary.BiggestMiss!.RoundNumber);
        Assert.Equal((4 + 0 + 38 + 22) / 4.0, summary.AverageDistanceCm!.Value, 6);
        Assert.Equal("leg-l.foot", summary.HardestZoneId);       // average 30 cm
    }

    [Fact]
    public void EmptySessionHasNoStatistics()
    {
        var summary = SessionStatistics.Compute([TestData.Ola, TestData.Kuba], []);

        Assert.All(summary.Standings, s => Assert.Equal(0, s.TotalPoints));
        Assert.Null(summary.AverageDistanceCm);
        Assert.Null(summary.BestHit);
        Assert.Null(summary.HardestZoneId);
    }
}
