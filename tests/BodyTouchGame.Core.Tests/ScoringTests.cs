using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Scoring;

namespace BodyTouchGame.Core.Tests;

public class ScoringTests
{
    [Theory]
    [InlineData(0, 100)]          // inside the target
    [InlineData(4.9, 100)]        // d < r
    [InlineData(5, 100)]          // d = r: full points
    [InlineData(22.5, 50)]        // half way between r and z
    [InlineData(40, 0)]           // d = z: zero
    [InlineData(55, 0)]           // d > z
    [InlineData(5.18, 99)]        // 99.486 -> 99
    [InlineData(39.8, 1)]         // 0.57 -> 1
    public void BasePointsFollowTheFormula(double distance, int expected) =>
        Assert.Equal(expected, ScoreCalculator.BasePoints(distance, 5, 40));

    [Fact]
    public void UnreachableDistanceGivesZero()
    {
        Assert.Equal(0, ScoreCalculator.BasePoints(double.PositiveInfinity, 5, 40));
        Assert.Equal(0, ScoreCalculator.BasePoints(double.NaN, 5, 40));
    }

    [Fact]
    public void InvalidRangeIsRejected() =>
        Assert.Throws<ArgumentException>(() => ScoreCalculator.BasePoints(10, 40, 40));

    [Theory]
    [InlineData(0, 20)]
    [InlineData(5, 20)]           // full bonus within 5 s
    [InlineData(7.5, 15)]
    [InlineData(10, 10)]
    [InlineData(14.9, 0)]         // 0.2 -> 0
    [InlineData(15, 0)]           // nothing from 15 s
    [InlineData(40, 0)]
    [InlineData(-1, 0)]
    public void SpeedBonusDecreasesLinearlyFrom5To15Seconds(double seconds, int expected) =>
        Assert.Equal(expected, ScoreCalculator.SpeedBonus(seconds));

    [Fact]
    public void BonusIsAddedOnlyWhenEnabledAndThePinScored()
    {
        var withBonus = BuiltInPresets.Medium with { SpeedBonus = true };

        Assert.Equal(new RoundScore(100, 20), ScoreCalculator.Score(2, withBonus, 3));
        Assert.Equal(new RoundScore(100, 0), ScoreCalculator.Score(2, BuiltInPresets.Medium, 3));
        Assert.Equal(new RoundScore(0, 0), ScoreCalculator.Score(80, withBonus, 3));    // a fast miss earns nothing
        Assert.Equal(new RoundScore(0, 0), ScoreCalculator.Score(null, withBonus, 3));  // no pin
        Assert.Equal(120, ScoreCalculator.Score(2, withBonus, 3).Total);
    }

    [Theory]
    [InlineData("Easy", 10, 60, 35, 50)]
    [InlineData("Medium", 5, 40, 22.5, 50)]
    [InlineData("Hard", 3, 25, 14, 50)]
    [InlineData("Hardcore", 1.5, 15, 8.25, 50)]
    public void BuiltInPresetsUseTheirRadii(string key, double r, double z, double halfWay, int expected)
    {
        var config = BuiltInPresets.All.Single(p => p.Key == key).Config;

        Assert.Equal(r, config.FullPointsRadiusCm);
        Assert.Equal(z, config.ZeroPointsDistanceCm);
        Assert.Equal(expected, ScoreCalculator.Score(halfWay, config, 99).Total);
    }
}
