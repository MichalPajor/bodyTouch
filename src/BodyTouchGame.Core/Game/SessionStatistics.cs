using BodyTouchGame.Core.Players;
using BodyTouchGame.Core.Targets;

namespace BodyTouchGame.Core.Game;

/// <param name="Rank">1 = winner; players with the same total share a rank.</param>
/// <param name="AverageDistanceCm">Over rounds with a pin and a reachable target; null if none.</param>
public sealed record PlayerStanding(long PlayerId, int Rank, int TotalPoints, int Rounds, double? AverageDistanceCm);

/// <summary>Session summary screen data (spec 10, mockup 9).</summary>
/// <param name="AverageDistanceCm">"Average miss" over all measured rounds.</param>
/// <param name="BestHit">Round with the smallest distance.</param>
/// <param name="BiggestMiss">Round with the largest (finite) distance.</param>
/// <param name="HardestZoneId">Built-in zone with the largest average distance among zone targets.</param>
public sealed record SessionSummary(
    IReadOnlyList<PlayerStanding> Standings,
    double? AverageDistanceCm,
    RoundResult? BestHit,
    RoundResult? BiggestMiss,
    string? HardestZoneId);

public static class SessionStatistics
{
    public static SessionSummary Compute(IReadOnlyList<Player> players, IReadOnlyList<RoundResult> rounds)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(rounds);

        static bool Measured(RoundResult r) => r.DistanceCm is { } d && float.IsFinite(d);

        var rows = players.Select(p =>
            {
                var own = rounds.Where(r => r.GuesserId == p.Id).ToList();
                var measured = own.Where(Measured).Select(r => (double)r.DistanceCm!.Value).ToList();
                return (Player: p, Total: own.Sum(r => r.Points), Rounds: own.Count, Avg: measured.Count > 0 ? measured.Average() : (double?)null);
            })
            .OrderByDescending(x => x.Total)
            .ThenBy(x => x.Avg ?? double.MaxValue)
            .ThenBy(x => x.Player.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var standings = new List<PlayerStanding>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var rank = i > 0 && rows[i].Total == rows[i - 1].Total ? standings[i - 1].Rank : i + 1;
            standings.Add(new PlayerStanding(rows[i].Player.Id, rank, rows[i].Total, rows[i].Rounds, rows[i].Avg));
        }

        var measuredRounds = rounds.Where(Measured).ToList();
        var best = measuredRounds.OrderBy(r => r.DistanceCm).ThenByDescending(r => r.Points).ThenBy(r => r.RoundNumber).FirstOrDefault();
        var worst = measuredRounds.OrderByDescending(r => r.DistanceCm).ThenBy(r => r.RoundNumber).FirstOrDefault();
        var hardest = measuredRounds
            .Where(r => r.Target is ZoneTarget)
            .GroupBy(r => ((ZoneTarget)r.Target).ZoneId, StringComparer.Ordinal)
            .Select(g => (Zone: g.Key, Avg: g.Average(r => r.DistanceCm!.Value)))
            .OrderByDescending(x => x.Avg)
            .ThenBy(x => x.Zone, StringComparer.Ordinal)
            .Select(x => x.Zone)
            .FirstOrDefault();

        return new SessionSummary(
            standings,
            measuredRounds.Count > 0 ? measuredRounds.Average(r => r.DistanceCm!.Value) : null,
            best,
            worst,
            hardest);
    }
}
