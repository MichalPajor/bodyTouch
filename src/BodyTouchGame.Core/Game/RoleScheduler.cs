using BodyTouchGame.Core.Config;

namespace BodyTouchGame.Core.Game;

/// <summary>Who guesses and who is the model in a round.</summary>
public sealed record RolePair(long GuesserId, long ModelId);

/// <summary>
/// Order of roles for a whole session (spec 5). Every player guesses <c>roundsPerPlayer</c> times, so a session has
/// players × roundsPerPlayer rounds and the guesser changes every round.
/// <list type="bullet">
/// <item><see cref="RoleOrder.AllPairs"/>: in pass k each player guesses with the player (1 + k mod (n−1)) places
/// further as the model, so over n−1 passes everybody plays with everybody.</item>
/// <item><see cref="RoleOrder.Circle"/>: each player always guesses with the next player as the model.</item>
/// </list>
/// With 2 players both orders swap the roles every round.
/// </summary>
public static class RoleScheduler
{
    public const int MinPlayers = 2;
    public const int MaxPlayers = 8;

    public static IReadOnlyList<RolePair> Build(IReadOnlyList<long> playerIds, RoleOrder order, int roundsPerPlayer)
    {
        ArgumentNullException.ThrowIfNull(playerIds);
        var n = playerIds.Count;
        if (n is < MinPlayers or > MaxPlayers) throw new ArgumentException($"A session needs {MinPlayers}-{MaxPlayers} players.", nameof(playerIds));
        if (playerIds.Distinct().Count() != n) throw new ArgumentException("Players must be distinct.", nameof(playerIds));
        ArgumentOutOfRangeException.ThrowIfLessThan(roundsPerPlayer, 1);

        var pairs = new List<RolePair>(n * roundsPerPlayer);
        for (var pass = 0; pass < roundsPerPlayer; pass++)
        {
            var offset = order switch
            {
                RoleOrder.AllPairs => 1 + pass % (n - 1),
                RoleOrder.Circle => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(order)),
            };
            for (var i = 0; i < n; i++) pairs.Add(new RolePair(playerIds[i], playerIds[(i + offset) % n]));
        }

        return pairs;
    }
}
