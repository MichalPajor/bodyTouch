using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Targets;

namespace BodyTouchGame.Core.Tests;

public class GameSessionTests
{
    private static readonly GameState[] SecretStates = [GameState.Touching, GameState.Curtain, GameState.Guessing];

    /// <summary>Plays the current round from WhoIsNext to Reveal.</summary>
    private static RoundResult PlayRound(GameSession session, FakeClock clock, int pinTriangle = 3, double guessSeconds = 4)
    {
        session.Continue();
        session.StartTouch();
        session.TouchFinished();
        session.ReadyToGuess();
        clock.Advance(guessSeconds);
        session.PlacePin(TestData.OnTriangle(pinTriangle));
        return session.Submit();
    }

    [Fact]
    public void FullSessionFollowsTheStateMachine()
    {
        var (session, clock, _) = TestData.Session(TestData.Config(rounds: 2), [TestData.Ola, TestData.Kuba]);
        var states = new List<GameState>();
        session.StateChanged += (_, e) => states.Add(e.Current);

        session.Start();
        for (var round = 0; round < 4; round++)
        {
            PlayRound(session, clock);
            session.NextRound();
        }

        Assert.Equal(GameState.SessionSummary, session.State);
        Assert.Equal(4, session.Results.Count);
        Assert.NotNull(session.StartedAt);
        Assert.NotNull(session.EndedAt);
        GameState[] oneRound = [GameState.Draw, GameState.Touching, GameState.Curtain, GameState.Guessing, GameState.Reveal];
        GameState[] expected = [GameState.WhoIsNext, .. oneRound, GameState.WhoIsNext, .. oneRound, GameState.WhoIsNext, .. oneRound,
            GameState.WhoIsNext, .. oneRound, GameState.SessionSummary];
        Assert.Equal(expected, states);
    }

    [Fact]
    public void RolesFollowTheSchedule()
    {
        var (session, clock, _) = TestData.Session(TestData.Config(rounds: 1), [TestData.Ola, TestData.Kuba, TestData.Marta]);
        session.Start();
        var guessers = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            guessers.Add(session.Guesser.Id);
            Assert.NotEqual(session.Guesser.Id, session.Model.Id);
            PlayRound(session, clock);
            session.NextRound();
        }

        Assert.Equal([1L, 2L, 3L], guessers);
    }

    [Fact]
    public void TargetIsVisibleOnlyInDrawAndReveal()
    {
        var (session, clock, _) = TestData.Session(TestData.Config(), [TestData.Ola, TestData.Kuba]);
        session.Start();
        Assert.Null(session.VisibleTarget);                       // WhoIsNext

        session.Continue();
        var target = session.VisibleTarget;
        Assert.NotNull(target);                                   // Draw
        session.StartTouch();
        Assert.Null(session.VisibleTarget);                       // Touching
        session.TouchFinished();
        Assert.Null(session.VisibleTarget);                       // Curtain
        session.ReadyToGuess();
        Assert.Null(session.VisibleTarget);                       // Guessing
        session.PlacePin(TestData.OnTriangle(4));
        session.Submit();
        Assert.Equal(target, session.VisibleTarget);              // Reveal
        session.NextRound();
        Assert.Null(session.VisibleTarget);                       // WhoIsNext
    }

    [Theory]
    [InlineData(GameState.Touching)]
    [InlineData(GameState.Curtain)]
    [InlineData(GameState.Guessing)]
    public void InterruptInSecretStatesAlwaysReturnsToCurtainWithoutTarget(GameState state)
    {
        var (session, clock, _) = TestData.Session(TestData.Config(), [TestData.Ola, TestData.Kuba]);
        session.Start();
        session.Continue();
        session.StartTouch();
        if (state != GameState.Touching) session.TouchFinished();
        if (state == GameState.Guessing)
        {
            session.ReadyToGuess();
            session.PlacePin(TestData.OnTriangle(2));
        }

        Assert.Equal(state, session.State);
        session.Interrupt();

        Assert.Equal(GameState.Curtain, session.State);
        Assert.Null(session.VisibleTarget);
        Assert.Null(session.Pin);

        // After "I'm ready" the guess starts over: fresh timer, no pin.
        session.ReadyToGuess();
        Assert.Null(session.Pin);
        Assert.Throws<InvalidOperationException>(() => session.Submit());
    }

    [Theory]
    [InlineData(GameState.WhoIsNext)]
    [InlineData(GameState.Draw)]
    [InlineData(GameState.Reveal)]
    public void InterruptElsewhereChangesNothing(GameState state)
    {
        var (session, clock, _) = TestData.Session(TestData.Config(), [TestData.Ola, TestData.Kuba]);
        session.Start();
        if (state != GameState.WhoIsNext) session.Continue();
        if (state == GameState.Reveal)
        {
            session.StartTouch();
            session.TouchFinished();
            session.ReadyToGuess();
            session.PlacePin(TestData.OnTriangle(2));
            session.Submit();
        }

        var target = session.VisibleTarget;
        session.Interrupt();

        Assert.Equal(state, session.State);
        Assert.Equal(target, session.VisibleTarget);
    }

    [Fact]
    public void SecretStatesNeverExposeTheTargetEvenAfterRepeatedInterrupts()
    {
        var (session, clock, _) = TestData.Session(TestData.Config(), [TestData.Ola, TestData.Kuba]);
        var leaks = 0;
        session.StateChanged += (_, e) =>
        {
            if (Array.IndexOf(SecretStates, e.Current) >= 0 && session.VisibleTarget is not null) leaks++;
        };

        session.Start();
        session.Continue();
        session.StartTouch();
        session.Interrupt();
        session.Interrupt();
        session.ReadyToGuess();
        session.Interrupt();
        session.ReadyToGuess();
        session.PlacePin(TestData.OnTriangle(3));
        session.Submit();

        Assert.Equal(0, leaks);
        Assert.Equal(GameState.Reveal, session.State);
    }

    [Theory]
    [InlineData(GameState.Draw)]
    [InlineData(GameState.Touching)]
    [InlineData(GameState.Curtain)]
    [InlineData(GameState.Guessing)]
    public void AbortRoundReturnsToWhoIsNextWithTheSamePairAndRecordsNothing(GameState state)
    {
        var (session, clock, _) = TestData.Session(TestData.Config(), [TestData.Ola, TestData.Kuba]);
        session.Start();
        var pair = session.CurrentPair;
        session.Continue();
        if (state != GameState.Draw) session.StartTouch();
        if (state is GameState.Curtain or GameState.Guessing) session.TouchFinished();
        if (state == GameState.Guessing) session.ReadyToGuess();

        session.AbortRound();

        Assert.Equal(GameState.WhoIsNext, session.State);
        Assert.Equal(pair, session.CurrentPair);
        Assert.Equal(0, session.RoundIndex);
        Assert.Empty(session.Results);
        Assert.Null(session.VisibleTarget);
    }

    [Fact]
    public void InvalidTransitionsThrow()
    {
        var (session, clock, _) = TestData.Session(TestData.Config(), [TestData.Ola, TestData.Kuba]);

        Assert.Throws<InvalidOperationException>(() => session.Continue());
        session.Start();
        Assert.Throws<InvalidOperationException>(() => session.Start());
        Assert.Throws<InvalidOperationException>(() => session.StartTouch());
        Assert.Throws<InvalidOperationException>(() => session.PlacePin(TestData.OnTriangle(0)));
        Assert.Throws<InvalidOperationException>(() => session.AbortRound());
        session.Continue();
        Assert.Throws<InvalidOperationException>(() => session.ReadyToGuess());
        Assert.Throws<InvalidOperationException>(() => session.NextRound());
        Assert.Throws<InvalidOperationException>(() => session.SetManualTarget(TestData.OnTriangle(0)));   // Random mode
    }

    [Fact]
    public void ResultHasDistanceScoreTimeAndGuessedZone()
    {
        var (session, clock, distances) = TestData.Session(TestData.Config(speedBonus: true), [TestData.Ola, TestData.Kuba]);
        distances.Distance = 22.5f;
        session.Start();

        var result = PlayRound(session, clock, pinTriangle: 5, guessSeconds: 10);

        Assert.Equal(1, result.RoundNumber);
        Assert.Equal(22.5f, result.DistanceCm);
        Assert.Equal(50, result.BasePoints);       // Medium: r = 5, z = 40
        Assert.Equal(10, result.SpeedBonus);       // 10 s -> +10
        Assert.Equal(60, result.Points);
        Assert.Equal(10, result.GuessSeconds, 3);
        Assert.Equal("arm-l.elbow", result.GuessedZoneId);   // Medium zone of triangle 5
        Assert.Single(distances.Calls);
        Assert.Equal(TestData.OnTriangle(5), distances.Calls[0].Pin);
    }

    [Fact]
    public void GuessTimeoutWithoutPinScoresZero()
    {
        var (session, clock, distances) = TestData.Session(TestData.Config() with { GuessSeconds = 10 }, [TestData.Ola, TestData.Kuba]);
        session.Start();
        session.Continue();
        session.StartTouch();
        session.TouchFinished();
        session.ReadyToGuess();
        clock.Advance(10);

        var result = session.GuessTimeExpired();

        Assert.Null(result.Pin);
        Assert.Null(result.DistanceCm);
        Assert.Equal(0, result.Points);
        Assert.Empty(distances.Calls);
        Assert.Equal(GameState.Reveal, session.State);
    }

    [Fact]
    public void GuessTimeoutWithPinIsScoredNormally()
    {
        var (session, clock, distances) = TestData.Session(TestData.Config(), [TestData.Ola, TestData.Kuba]);
        distances.Distance = 0;
        session.Start();
        session.Continue();
        session.StartTouch();
        session.TouchFinished();
        session.ReadyToGuess();
        session.PlacePin(TestData.OnTriangle(1));

        Assert.Equal(100, session.GuessTimeExpired().Points);
    }

    [Fact]
    public void TheSameGuesserNeverGetsTheSameTargetTwiceInARow()
    {
        // Two players: each guesses every second round; the random source always proposes index 0.
        var (session, clock, _) = TestData.Session(TestData.Config(rounds: 6), [TestData.Ola, TestData.Kuba], random: new SequenceRandom(0));
        session.Start();
        var targets = new Dictionary<long, List<Target>>();
        while (session.State != GameState.SessionSummary)
        {
            var guesser = session.Guesser.Id;
            var result = PlayRound(session, clock);
            if (!targets.TryGetValue(guesser, out var list)) targets[guesser] = list = [];
            list.Add(result.Target);
            session.NextRound();
        }

        foreach (var list in targets.Values)
            for (var i = 1; i < list.Count; i++) Assert.NotEqual(list[i - 1], list[i]);
    }

    [Fact]
    public void DrawUsesThePoolOfTheCurrentModel()
    {
        // Kuba (model in round 1) excluded everything except the left leg.
        var exclusions = new Dictionary<long, IReadOnlyList<string>> { [2] = ["head", "arm-l", "leg-l.foot"] };
        var (session, clock, _) = TestData.Session(TestData.Config(), [TestData.Ola, TestData.Kuba], exclusions: exclusions,
            random: new SequenceRandom(0, 1, 2, 3, 4, 5));
        session.Start();

        Assert.Equal(2, session.Model.Id);
        session.Continue();

        Assert.Equal(new ZoneTarget("leg-l.knee"), session.VisibleTarget);
    }

    [Fact]
    public void SessionCannotStartWhenAModelHasNothingToDraw()
    {
        var exclusions = new Dictionary<long, IReadOnlyList<string>> { [1] = ["head", "arm-l", "leg-l"] };
        var (session, _, _) = TestData.Session(TestData.Config(), [TestData.Ola, TestData.Kuba], exclusions: exclusions);

        var error = Assert.Throws<GameSetupException>(() => session.Start());

        Assert.Contains(error.Problems, p => p.Contains("Ola"));
        Assert.Equal(GameState.Setup, session.State);
    }

    [Fact]
    public void SessionCannotStartWithAnInvalidConfig()
    {
        var (session, _, _) = TestData.Session(TestData.Config() with { TouchSeconds = 0 }, [TestData.Ola, TestData.Kuba]);

        Assert.Throws<GameSetupException>(() => session.Start());
    }

    [Fact]
    public void ChosenByModelRequiresThePinBeforeTouchingAndHidesIt()
    {
        var (session, clock, distances) = TestData.Session(TestData.Config(TargetSelection.ChosenByModel),
            [TestData.Ola, TestData.Kuba]);
        session.Start();
        session.Continue();

        Assert.False(session.CanStartTouch);
        Assert.Null(session.VisibleTarget);
        Assert.Throws<InvalidOperationException>(() => session.StartTouch());

        session.SetManualTarget(TestData.OnTriangle(7));
        session.SetManualTarget(TestData.OnTriangle(9));     // the model may move the pin; also inside a hidden zone
        Assert.True(session.CanStartTouch);
        Assert.Equal(new ManualPointTarget(TestData.OnTriangle(9)), session.VisibleTarget);

        session.StartTouch();
        Assert.Null(session.VisibleTarget);
        session.TouchFinished();
        session.ReadyToGuess();
        session.PlacePin(TestData.OnTriangle(6));
        var result = session.Submit();

        Assert.Equal(new ManualPointTarget(TestData.OnTriangle(9)), result.Target);
        Assert.Equal(new ManualPointTarget(TestData.OnTriangle(9)), distances.Calls.Single().Target);
        Assert.Equal("leg-l.knee", result.GuessedZoneId);    // finest level for point targets
    }

    [Fact]
    public void ChosenByModelStartsWithoutTargetPools()
    {
        var config = TestData.Config(TargetSelection.ChosenByModel) with { ZoneSet = new ZoneSetSelection() };
        var catalog = TestData.Catalog();
        var session = new GameSession(new SessionSetup([TestData.Ola, TestData.Kuba], config, RoleOrder.AllPairs, null,
            new Dictionary<long, IReadOnlyList<Target>>()), new FakeDistances(), catalog, new SequenceRandom(0), new FakeClock());

        session.Start();

        Assert.Equal(GameState.WhoIsNext, session.State);
    }

    [Fact]
    public void PinOutsideTheMeshIsRejected()
    {
        var (session, clock, _) = TestData.Session(TestData.Config(), [TestData.Ola, TestData.Kuba]);
        session.Start();
        session.Continue();
        session.StartTouch();
        session.TouchFinished();
        session.ReadyToGuess();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.PlacePin(TestData.OnTriangle(99)));
        Assert.Throws<ArgumentException>(() => session.PlacePin(new Geometry.SurfacePoint(1, float.NaN, 0)));
    }
}
