using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Game;

namespace BodyTouchGame.Core.Tests;

public class RoleSchedulerTests
{
    [Theory]
    [InlineData(RoleOrder.AllPairs)]
    [InlineData(RoleOrder.Circle)]
    public void TwoPlayersSwapRolesEveryRound(RoleOrder order)
    {
        var pairs = RoleScheduler.Build([1, 2], order, roundsPerPlayer: 3);

        Assert.Equal(6, pairs.Count);
        for (var i = 0; i < pairs.Count; i++)
        {
            Assert.Equal(i % 2 == 0 ? 1 : 2, pairs[i].GuesserId);
            Assert.Equal(i % 2 == 0 ? 2 : 1, pairs[i].ModelId);
        }
    }

    [Theory]
    [InlineData(RoleOrder.AllPairs, 3, 3)]
    [InlineData(RoleOrder.AllPairs, 8, 2)]
    [InlineData(RoleOrder.Circle, 4, 3)]
    [InlineData(RoleOrder.Circle, 8, 1)]
    public void EveryPlayerGuessesRoundsPerPlayerTimesNeverWithThemselves(RoleOrder order, int players, int rounds)
    {
        var ids = Enumerable.Range(1, players).Select(i => (long)i).ToList();

        var pairs = RoleScheduler.Build(ids, order, rounds);

        Assert.Equal(players * rounds, pairs.Count);
        Assert.All(pairs, p => Assert.NotEqual(p.GuesserId, p.ModelId));
        Assert.All(ids, id => Assert.Equal(rounds, pairs.Count(p => p.GuesserId == id)));
        Assert.All(ids, id => Assert.Equal(rounds, pairs.Count(p => p.ModelId == id)));
        for (var i = 1; i < pairs.Count; i++) Assert.NotEqual(pairs[i - 1].GuesserId, pairs[i].GuesserId);
    }

    [Fact]
    public void AllPairsCoversEveryPairOverEnoughRounds()
    {
        long[] ids = [1, 2, 3, 4];

        var pairs = RoleScheduler.Build(ids, RoleOrder.AllPairs, roundsPerPlayer: 3);

        var distinct = pairs.Select(p => (p.GuesserId, p.ModelId)).Distinct().Count();
        Assert.Equal(4 * 3, distinct);   // every ordered pair exactly once
    }

    [Fact]
    public void CircleAlwaysUsesTheNextPlayerAsModel()
    {
        var pairs = RoleScheduler.Build([1, 2, 3], RoleOrder.Circle, roundsPerPlayer: 2);

        Assert.Equal([(1L, 2L), (2L, 3L), (3L, 1L), (1L, 2L), (2L, 3L), (3L, 1L)], pairs.Select(p => (p.GuesserId, p.ModelId)));
    }

    [Fact]
    public void MockupSessionHasNineRounds() =>   // mockup: 3 players, 3 rounds per player -> "Runda 3 z 9"
        Assert.Equal(9, RoleScheduler.Build([1, 2, 3], RoleOrder.AllPairs, 3).Count);

    [Fact]
    public void InvalidInputsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => RoleScheduler.Build([1], RoleOrder.AllPairs, 1));
        Assert.Throws<ArgumentException>(() => RoleScheduler.Build(Enumerable.Range(1, 9).Select(i => (long)i).ToList(), RoleOrder.AllPairs, 1));
        Assert.Throws<ArgumentException>(() => RoleScheduler.Build([1, 1], RoleOrder.AllPairs, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoleScheduler.Build([1, 2], RoleOrder.AllPairs, 0));
    }
}
