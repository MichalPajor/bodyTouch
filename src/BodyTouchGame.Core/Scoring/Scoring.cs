using BodyTouchGame.Core.Config;

namespace BodyTouchGame.Core.Scoring;

public sealed record RoundScore(int BasePoints, int SpeedBonus)
{
    public int Total => BasePoints + SpeedBonus;
}

/// <summary>Points of a round (spec 9).</summary>
public static class ScoreCalculator
{
    public const int MaxPoints = 100;
    public const int MaxSpeedBonus = 20;
    public const double FullBonusSeconds = 5;
    public const double NoBonusSeconds = 15;

    /// <summary>P = 100 · clamp(1 − (d − r) / (z − r), 0, 1), rounded half away from zero.
    /// An unreachable distance (infinity / NaN) gives 0.</summary>
    public static int BasePoints(double distanceCm, double fullPointsRadiusCm, double zeroPointsDistanceCm)
    {
        if (!double.IsFinite(distanceCm)) return 0;
        if (zeroPointsDistanceCm <= fullPointsRadiusCm) throw new ArgumentException("z must be greater than r.");
        var value = 1 - (distanceCm - fullPointsRadiusCm) / (zeroPointsDistanceCm - fullPointsRadiusCm);
        return (int)Math.Round(MaxPoints * Math.Clamp(value, 0, 1), MidpointRounding.AwayFromZero);
    }

    /// <summary>+20 within 5 s from entering Guessing, linearly down to 0 at 15 s.</summary>
    public static int SpeedBonus(double guessSeconds)
    {
        if (!double.IsFinite(guessSeconds) || guessSeconds < 0) return 0;
        var value = MaxSpeedBonus * (NoBonusSeconds - guessSeconds) / (NoBonusSeconds - FullBonusSeconds);
        return (int)Math.Round(Math.Clamp(value, 0, MaxSpeedBonus), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Score of a round. No pin (time ran out) = 0. The speed bonus is added only when enabled and the guess scored
    /// any base points, so a fast random tap is not rewarded.
    /// </summary>
    public static RoundScore Score(double? distanceCm, GameConfig config, double guessSeconds)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (distanceCm is not { } d) return new RoundScore(0, 0);
        var basePoints = BasePoints(d, config.FullPointsRadiusCm, config.ZeroPointsDistanceCm);
        var bonus = config.SpeedBonus && basePoints > 0 ? SpeedBonus(guessSeconds) : 0;
        return new RoundScore(basePoints, bonus);
    }
}
