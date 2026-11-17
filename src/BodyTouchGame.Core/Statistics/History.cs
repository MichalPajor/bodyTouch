using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;

namespace BodyTouchGame.Core.Statistics;

// Everything computed from the round history (spec 10): player statistics, rankings per preset, heat map.
// Input: all stored rounds with their session and preset (ISessionRepository.GetAllRoundsAsync).

/// <summary>A stored round with its session and the preset of that session (null = settings matching no preset).</summary>
public sealed record HistoryRound(long SessionId, long? PresetId, RoundResult Round)
{
    /// <summary>Distance with a pin and a reachable target (the rounds that count for average distances).</summary>
    public bool IsMeasured => Round.DistanceCm is { } d && float.IsFinite(d);
}

/// <summary>Statistics of one zone for one player.</summary>
/// <param name="AverageDistanceCm">Over measured rounds; null when none.</param>
/// <param name="AverageBasePoints">Over all rounds (a round without a pin counts as 0 points).</param>
public sealed record ZoneStat(string ZoneId, int Rounds, double? AverageDistanceCm, double AverageBasePoints);

/// <summary>One point of the "average distance over time" chart: one session.</summary>
public sealed record TrendPoint(long SessionId, DateTimeOffset Date, double AverageDistanceCm, int Rounds);

/// <summary>Player statistics (spec 10). Rounds are those the player guessed.</summary>
public sealed record PlayerStats(
    long PlayerId,
    int Rounds,
    double? AverageDistanceCm,
    double AveragePoints,
    ZoneStat? BestZone,
    ZoneStat? WorstZone,
    IReadOnlyList<ZoneStat> Zones,
    IReadOnlyList<TrendPoint> Trend);

/// <summary>A row of the ranking of one preset.</summary>
/// <param name="Rank">1-based place among qualified players (equal averages share a place); 0 when not qualified.</param>
/// <param name="Qualified">At least <see cref="Rankings.MinRounds"/> rounds in this preset.</param>
public sealed record RankingEntry(long PlayerId, int Rank, int Rounds, double AveragePoints, double? AverageDistanceCm, bool Qualified);

/// <summary>Heat of one zone: the colour comes from the average base points (100 = green, 0 = red).</summary>
public sealed record ZoneHeat(string ZoneId, int Rounds, double? AverageDistanceCm, double AverageBasePoints, string Color);

/// <summary>Arrow from a target zone to the zone most often guessed instead of it.</summary>
public sealed record ConfusionArrow(string FromZoneId, string ToZoneId, int Count);

public sealed record HeatmapData(IReadOnlyList<ZoneHeat> Zones, IReadOnlyList<ConfusionArrow> Arrows);

/// <summary>Maps a round's target to the built-in zone it is counted under.</summary>
public static class TargetZones
{
    /// <summary>
    /// Zone targets count under their zone; point targets (custom points, points chosen by the model) under the zone
    /// containing the point on the finest level – the same level the guessed zone is stored on for point targets.
    /// </summary>
    public static string? ZoneOf(Target target, ZoneCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(catalog);
        return target switch
        {
            ZoneTarget zone => zone.ZoneId,
            CustomPointTarget point => catalog.ZoneAt(point.Point.TriangleIndex, catalog.Levels[^1])?.Id,
            ManualPointTarget point => catalog.ZoneAt(point.Point.TriangleIndex, catalog.Levels[^1])?.Id,
            _ => null,
        };
    }
}

public static class PlayerStatistics
{
    public static PlayerStats Compute(long playerId, IEnumerable<HistoryRound> history, ZoneCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(catalog);
        var own = history.Where(h => h.Round.GuesserId == playerId).ToList();
        var measured = own.Where(h => h.IsMeasured).Select(h => (double)h.Round.DistanceCm!.Value).ToList();

        var zones = ZoneStats(own, catalog);
        // Best / worst zone: by average distance (measured rounds only); ties: more rounds first, then id.
        var ranked = zones.Where(z => z.AverageDistanceCm is not null)
            .OrderBy(z => z.AverageDistanceCm).ThenByDescending(z => z.Rounds).ThenBy(z => z.ZoneId, StringComparer.Ordinal).ToList();
        var worst = zones.Where(z => z.AverageDistanceCm is not null)
            .OrderByDescending(z => z.AverageDistanceCm).ThenByDescending(z => z.Rounds).ThenBy(z => z.ZoneId, StringComparer.Ordinal).FirstOrDefault();

        var trend = own.Where(h => h.IsMeasured)
            .GroupBy(h => h.SessionId)
            .Select(g => new TrendPoint(g.Key, g.Min(h => h.Round.CreatedAt), g.Average(h => (double)h.Round.DistanceCm!.Value), g.Count()))
            .OrderBy(p => p.Date).ThenBy(p => p.SessionId)
            .ToList();

        return new PlayerStats(
            playerId,
            own.Count,
            measured.Count > 0 ? measured.Average() : null,
            own.Count > 0 ? own.Average(h => (double)h.Round.Points) : 0,
            ranked.FirstOrDefault(),
            ranked.Count > 1 ? worst : null,
            zones,
            trend);
    }

    /// <summary>Per-zone statistics of the given rounds, ordered by zone id.</summary>
    public static IReadOnlyList<ZoneStat> ZoneStats(IEnumerable<HistoryRound> rounds, ZoneCatalog catalog) =>
        rounds.Select(h => (Zone: TargetZones.ZoneOf(h.Round.Target, catalog), h))
            .Where(x => x.Zone is not null)
            .GroupBy(x => x.Zone!, StringComparer.Ordinal)
            .Select(g =>
            {
                var measured = g.Where(x => x.h.IsMeasured).Select(x => (double)x.h.Round.DistanceCm!.Value).ToList();
                return new ZoneStat(g.Key, g.Count(), measured.Count > 0 ? measured.Average() : null, g.Average(x => (double)x.h.Round.BasePoints));
            })
            .OrderBy(z => z.ZoneId, StringComparer.Ordinal)
            .ToList();
}

public static class Rankings
{
    /// <summary>Minimum number of rounds in a preset to appear in its ranking (spec 10).</summary>
    public const int MinRounds = 5;

    /// <summary>Presets that have at least one round, in the order of first appearance (callers sort for display).</summary>
    public static IReadOnlyList<long> PresetsWithRounds(IEnumerable<HistoryRound> history) =>
        history.Where(h => h.PresetId is not null).Select(h => h.PresetId!.Value).Distinct().ToList();

    /// <summary>
    /// Ranking of one preset: qualified players (≥ <see cref="MinRounds"/> rounds) sorted by average points per round
    /// (descending), then average distance (ascending), then number of rounds (descending); players with equal
    /// averages share a place. Players below the threshold follow with rank 0, sorted the same way.
    /// Only players in <paramref name="playerIds"/> are listed (deleted players drop out).
    /// </summary>
    public static IReadOnlyList<RankingEntry> Compute(IEnumerable<HistoryRound> history, long presetId, IReadOnlySet<long> playerIds)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(playerIds);
        var rows = history.Where(h => h.PresetId == presetId && playerIds.Contains(h.Round.GuesserId))
            .GroupBy(h => h.Round.GuesserId)
            .Select(g =>
            {
                var measured = g.Where(h => h.IsMeasured).Select(h => (double)h.Round.DistanceCm!.Value).ToList();
                return (Player: g.Key, Rounds: g.Count(), Avg: Math.Round(g.Average(h => (double)h.Round.Points), 2),
                    Dist: measured.Count > 0 ? measured.Average() : (double?)null);
            })
            .OrderByDescending(r => r.Rounds >= MinRounds)
            .ThenByDescending(r => r.Avg)
            .ThenBy(r => r.Dist ?? double.MaxValue)
            .ThenByDescending(r => r.Rounds)
            .ThenBy(r => r.Player)
            .ToList();

        var result = new List<RankingEntry>(rows.Count);
        var rank = 0;
        double? previousAvg = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var qualified = r.Rounds >= MinRounds;
            if (qualified && r.Avg != previousAvg)
            {
                rank = i + 1;
                previousAvg = r.Avg;
            }

            result.Add(new RankingEntry(r.Player, qualified ? rank : 0, r.Rounds, r.Avg, r.Dist, qualified));
        }

        return result;
    }
}

public static class Heatmap
{
    /// <summary>Minimum number of identical mistakes to draw an arrow (spec 10).</summary>
    public const int MinArrowCount = 3;

    /// <summary>
    /// Heat map of one player (as the guesser), optionally limited to one preset (null = all rounds).
    /// Zones: every zone that was a target, coloured by the average base points. Arrows: for each target zone the
    /// zone most often guessed instead of it, when that happened at least <see cref="MinArrowCount"/> times.
    /// </summary>
    public static HeatmapData Compute(IEnumerable<HistoryRound> history, ZoneCatalog catalog, long playerId, long? presetId)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(catalog);
        var rounds = history.Where(h => h.Round.GuesserId == playerId && (presetId is null || h.PresetId == presetId)).ToList();

        var zones = PlayerStatistics.ZoneStats(rounds, catalog)
            .Select(z => new ZoneHeat(z.ZoneId, z.Rounds, z.AverageDistanceCm, z.AverageBasePoints, ColorFor(z.AverageBasePoints)))
            .ToList();

        var arrows = rounds
            .Select(h => (From: TargetZones.ZoneOf(h.Round.Target, catalog), To: h.Round.GuessedZoneId))
            .Where(x => x.From is not null && x.To is not null && x.From != x.To)
            .GroupBy(x => (x.From!, x.To!))
            .Select(g => new ConfusionArrow(g.Key.Item1, g.Key.Item2, g.Count()))
            .GroupBy(a => a.FromZoneId, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(a => a.Count).ThenBy(a => a.ToZoneId, StringComparer.Ordinal).First())
            .Where(a => a.Count >= MinArrowCount)
            .OrderBy(a => a.FromZoneId, StringComparer.Ordinal)
            .ToList();

        return new HeatmapData(zones, arrows);
    }

    private static readonly (double Points, (int R, int G, int B) Color)[] Scale =
    [
        (0, (0xEF, 0x44, 0x44)),     // red – misses the most
        (50, (0xFA, 0xCC, 0x15)),    // yellow
        (100, (0x22, 0xC5, 0x5E)),   // green – hits on average
    ];

    /// <summary>Colour of the average base points: 0 → red, 50 → yellow, 100 → green (linear in between).</summary>
    public static string ColorFor(double averageBasePoints)
    {
        var p = Math.Clamp(averageBasePoints, 0, 100);
        var i = p <= 50 ? 0 : 1;
        var (p0, c0) = Scale[i];
        var (p1, c1) = Scale[i + 1];
        var t = (p - p0) / (p1 - p0);
        static int Lerp(int a, int b, double t) => (int)Math.Round(a + (b - a) * t);
        return $"#{Lerp(c0.R, c1.R, t):X2}{Lerp(c0.G, c1.G, t):X2}{Lerp(c0.B, c1.B, t):X2}";
    }
}
