using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Targets;

namespace BodyTouchGame.Core.Tests;

/// <summary>
/// Target secrecy at the view level (spec 5): a simulated 3D view applies the commands of every state; it must never
/// hold the target in Touching, Curtain or Guessing.
/// </summary>
public class ViewDirectorTests
{
    /// <summary>What the JS view would hold after applying the commands.</summary>
    private sealed class FakeView
    {
        public Target? Target { get; private set; }
        public bool HasPin { get; private set; }
        public ViewMode Mode { get; private set; }
        public bool AllowTargetPin { get; private set; }

        public void Apply(IEnumerable<ViewCommand> commands)
        {
            foreach (var command in commands)
            {
                switch (command)
                {
                    case ClearTarget: Target = null; break;
                    case ClearPin: HasPin = false; break;
                    case SetViewMode m: Mode = m.Mode; AllowTargetPin = m.AllowTargetPin; break;
                    case FocusTarget f: Target = f.Target; break;
                    case ShowReveal r: Target = r.Target; HasPin = r.Pin is not null; break;
                }
            }
        }

        /// <summary>The model placed the target pin in Draw (ChosenByModel): JS holds it as the target.</summary>
        public void ModelPlacedTargetPin(Target target) => Target = target;

        public void GuesserPlacedPin() => HasPin = true;
    }

    private static readonly GameState[] Secret = [GameState.Touching, GameState.Curtain, GameState.Guessing];

    [Theory]
    [InlineData(TargetSelection.Random)]
    [InlineData(TargetSelection.ChosenByModel)]
    public void ViewNeverHoldsTheTargetInSecretStatesDuringAWholeSessionWithInterrupts(TargetSelection selection)
    {
        var (session, clock, _) = TestData.Session(TestData.Config(selection, rounds: 2), [TestData.Ola, TestData.Kuba, TestData.Marta]);
        var view = new FakeView();
        var checks = 0;
        session.StateChanged += (_, _) =>
        {
            view.Apply(ViewDirector.For(session));
            if (Array.IndexOf(Secret, session.State) >= 0)
            {
                Assert.Null(view.Target);
                checks++;
            }
        };

        session.Start();
        var round = 0;
        while (session.State != GameState.SessionSummary)
        {
            session.Continue();
            if (selection == TargetSelection.ChosenByModel)
            {
                Assert.True(view.AllowTargetPin);
                session.SetManualTarget(TestData.OnTriangle(round % 9));
                view.ModelPlacedTargetPin(session.VisibleTarget!);
            }
            else
            {
                Assert.Equal(session.VisibleTarget, view.Target);       // the model sees the target in Draw
            }

            session.StartTouch();
            if (round % 3 == 0) session.Interrupt();                     // app goes to background while touching
            else session.TouchFinished();
            session.ReadyToGuess();
            if (round % 3 == 1) { session.Interrupt(); session.ReadyToGuess(); }   // background while guessing
            session.PlacePin(TestData.OnTriangle(3));
            view.GuesserPlacedPin();
            session.Submit();
            Assert.NotNull(view.Target);                                 // revealed to everybody
            Assert.True(view.HasPin);
            session.NextRound();
            round++;
        }

        Assert.True(checks >= 3 * 6);
        Assert.Null(view.Target);
    }

    [Fact]
    public void SecretStatesClearTargetAndCarryNoTarget()
    {
        var (session, clock, _) = TestData.Session(TestData.Config(), [TestData.Ola, TestData.Kuba]);
        session.Start();
        session.Continue();
        var states = new List<(GameState, IReadOnlyList<ViewCommand>)>();
        session.StateChanged += (_, _) => states.Add((session.State, ViewDirector.For(session)));

        session.StartTouch();
        session.TouchFinished();
        session.ReadyToGuess();

        Assert.Equal([GameState.Touching, GameState.Curtain, GameState.Guessing], states.Select(s => s.Item1));
        Assert.All(states, s =>
        {
            Assert.IsType<ClearTarget>(s.Item2[0]);                      // clearing comes first
            Assert.DoesNotContain(s.Item2, c => c is FocusTarget or ShowReveal);
        });
        Assert.Contains(new SetViewMode(ViewMode.Guess), states[2].Item2);
        // The camera is reset already behind the countdown and the curtain, not when the guesser's screen appears.
        Assert.Contains(new ResetCamera("Front"), states[0].Item2);
        Assert.Contains(new ResetCamera("Front"), states[1].Item2);
        Assert.Contains(new ClearPin(), states[1].Item2);
    }

    [Fact]
    public void DrawFocusesTheTargetAndRevealShowsTheResult()
    {
        var (session, clock, _) = TestData.Session(TestData.Config(), [TestData.Ola, TestData.Kuba]);
        session.Start();
        session.Continue();

        var draw = ViewDirector.For(session);
        Assert.Contains(new FocusTarget(session.VisibleTarget!), draw);
        Assert.Contains(new SetViewMode(ViewMode.Draw), draw);

        session.StartTouch();
        session.TouchFinished();
        session.ReadyToGuess();
        session.PlacePin(TestData.OnTriangle(2));
        var result = session.Submit();

        var reveal = ViewDirector.For(session);
        var show = Assert.Single(reveal.OfType<ShowReveal>());
        Assert.Equal(result.Target, show.Target);
        Assert.Equal(result.Pin, show.Pin);
    }

    [Theory]
    [InlineData(null, 0, RevealCommentKind.TimeUp)]
    [InlineData(0f, 100, RevealCommentKind.Perfect)]
    [InlineData(3f, 100, RevealCommentKind.Close)]
    [InlineData(8f, 86, RevealCommentKind.Close)]
    [InlineData(15f, 70, RevealCommentKind.Warm)]
    [InlineData(20f, 40, RevealCommentKind.Warm)]
    [InlineData(25f, 39, RevealCommentKind.Far)]
    [InlineData(30f, 20, RevealCommentKind.Far)]
    [InlineData(60f, 0, RevealCommentKind.Miss)]
    public void RevealCommentDependsOnTheResult(float? distance, int points, RevealCommentKind expected)
    {
        var result = new RoundResult(1, 1, 2, new ZoneTarget("head"), distance is null ? null : TestData.OnTriangle(0), distance, null,
            points, 0, 3, DateTimeOffset.UnixEpoch, []);

        Assert.Equal(expected, RevealComments.KindOf(result));
    }
}
