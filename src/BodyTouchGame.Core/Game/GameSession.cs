using System.Numerics;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Players;
using BodyTouchGame.Core.Scoring;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Core.Game;

/// <summary>States of a round (spec 5).</summary>
public enum GameState
{
    Setup,
    WhoIsNext,
    Draw,
    Touching,
    Curtain,
    Guessing,
    Reveal,
    SessionSummary,
}

/// <summary>Everything a session needs before it starts.</summary>
/// <param name="TargetPools">Drawable targets per model player id (built with that player's exclusions by
/// <see cref="TargetPoolBuilder"/>); required in <see cref="TargetSelection.Random"/> mode.</param>
public sealed record SessionSetup(
    IReadOnlyList<Player> Players,
    GameConfig Config,
    RoleOrder RoleOrder,
    long? PresetId,
    IReadOnlyDictionary<long, IReadOnlyList<Target>> TargetPools);

/// <summary>Result of a finished round.</summary>
/// <param name="Pin">Null when the guess time ran out before a pin was placed.</param>
/// <param name="DistanceCm">Null without a pin; infinity when unreachable.</param>
/// <param name="GuessedZoneId">Zone under the pin on the target's level (for points: the finest level available).</param>
public sealed record RoundResult(
    int RoundNumber,
    long GuesserId,
    long ModelId,
    Target Target,
    SurfacePoint? Pin,
    float? DistanceCm,
    string? GuessedZoneId,
    int BasePoints,
    int SpeedBonus,
    double GuessSeconds,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Vector3> Path)
{
    public int Points => BasePoints + SpeedBonus;
}

public sealed class GameStateChangedEventArgs(GameState previous, GameState current) : EventArgs
{
    public GameState Previous { get; } = previous;
    public GameState Current { get; } = current;
}

/// <summary>Thrown when a session cannot start (e.g. a model has nothing left to draw after exclusions).</summary>
public sealed class GameSetupException(IReadOnlyList<string> problems)
    : Exception("The session cannot start: " + string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// The round state machine of one session (spec 5):
/// Setup → WhoIsNext → Draw → Touching → Curtain → Guessing → Reveal → (WhoIsNext | SessionSummary).
/// The UI calls the transition methods; calling one in the wrong state throws <see cref="InvalidOperationException"/>.
/// Timers live in the UI: it calls <see cref="TouchFinished"/> and <see cref="GuessTimeExpired"/>.
/// <para>Target secrecy: <see cref="VisibleTarget"/> is non-null only in Draw and Reveal; after
/// <see cref="Interrupt"/> (app in background / screen off) in Touching, Curtain or Guessing the state is always
/// Curtain.</para>
/// </summary>
public sealed class GameSession
{
    private readonly SessionSetup _setup;
    private readonly IDistanceCalculator _distances;
    private readonly ZoneCatalog _catalog;
    private readonly IRandomSource _random;
    private readonly IClock _clock;
    private readonly IReadOnlyList<RolePair> _schedule;
    private readonly Dictionary<long, Target> _lastTargetByGuesser = [];
    private readonly List<RoundResult> _results = [];
    private Target? _target;
    private DateTimeOffset _guessStartedAt;

    public GameSession(SessionSetup setup, IDistanceCalculator distances, ZoneCatalog catalog, IRandomSource random, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _setup = setup;
        _distances = distances ?? throw new ArgumentNullException(nameof(distances));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _schedule = RoleScheduler.Build(setup.Players.Select(p => p.Id).ToList(), setup.RoleOrder, setup.Config.RoundsPerPlayer);
    }

    public event EventHandler<GameStateChangedEventArgs>? StateChanged;

    public GameState State { get; private set; } = GameState.Setup;
    public GameConfig Config => _setup.Config;
    public IReadOnlyList<Player> Players => _setup.Players;
    public long? PresetId => _setup.PresetId;
    public RoleOrder RoleOrder => _setup.RoleOrder;

    /// <summary>0-based index of the current (or next) round.</summary>
    public int RoundIndex { get; private set; }

    public int RoundCount => _schedule.Count;
    public RolePair CurrentPair => _schedule[Math.Min(RoundIndex, _schedule.Count - 1)];
    public Player Guesser => PlayerById(CurrentPair.GuesserId);
    public Player Model => PlayerById(CurrentPair.ModelId);

    /// <summary>The target, only while it may be shown (Draw, Reveal); null in every other state.</summary>
    public Target? VisibleTarget => State is GameState.Draw or GameState.Reveal ? _target : null;

    /// <summary>The guesser's pin in Guessing (null until placed).</summary>
    public SurfacePoint? Pin { get; private set; }

    public IReadOnlyList<RoundResult> Results => _results;
    public RoundResult? LastResult => _results.Count > 0 ? _results[^1] : null;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    public SessionSummary Summary => SessionStatistics.Compute(_setup.Players, _results);

    /// <summary>Setup → WhoIsNext. Validates the configuration and that every model has something to draw.</summary>
    public void Start()
    {
        Require(GameState.Setup);
        var problems = _setup.Config.Validate().ToList();
        if (_setup.Config.TargetSelection == TargetSelection.Random)
        {
            foreach (var player in _setup.Players)
            {
                if (!_setup.TargetPools.TryGetValue(player.Id, out var pool) || pool.Count == 0)
                    problems.Add($"No target can be drawn for model '{player.Name}'.");
            }
        }

        if (problems.Count > 0) throw new GameSetupException(problems);
        StartedAt = _clock.Now;
        MoveTo(GameState.WhoIsNext);
    }

    /// <summary>WhoIsNext → Draw. In Random mode the target is drawn here.</summary>
    public void Continue()
    {
        Require(GameState.WhoIsNext);
        _target = null;
        if (Config.TargetSelection == TargetSelection.Random)
        {
            _lastTargetByGuesser.TryGetValue(CurrentPair.GuesserId, out var previous);
            _target = TargetDrawer.Draw(_setup.TargetPools[CurrentPair.ModelId], previous, _random)
                      ?? throw new InvalidOperationException("Nothing to draw.");
        }

        MoveTo(GameState.Draw);
    }

    /// <summary>Draw, ChosenByModel mode: the model places (or moves) the target pin.</summary>
    public void SetManualTarget(SurfacePoint point)
    {
        Require(GameState.Draw);
        if (Config.TargetSelection != TargetSelection.ChosenByModel)
            throw new InvalidOperationException("The target is chosen by the model only in ChosenByModel mode.");
        ValidatePoint(point);
        _target = new ManualPointTarget(point);
    }

    /// <summary>Whether "Start touch" is available (a target exists).</summary>
    public bool CanStartTouch => State == GameState.Draw && _target is not null;

    /// <summary>Draw → Touching.</summary>
    public void StartTouch()
    {
        Require(GameState.Draw);
        if (_target is null) throw new InvalidOperationException("The model has not chosen the target yet.");
        MoveTo(GameState.Touching);
    }

    /// <summary>Touching → Curtain (touch time is over).</summary>
    public void TouchFinished()
    {
        Require(GameState.Touching);
        MoveTo(GameState.Curtain);
    }

    /// <summary>Curtain → Guessing ("I'm ready"). The guess time starts now.</summary>
    public void ReadyToGuess()
    {
        Require(GameState.Curtain);
        Pin = null;
        _guessStartedAt = _clock.Now;
        MoveTo(GameState.Guessing);
    }

    /// <summary>Guessing: place or move the pin.</summary>
    public void PlacePin(SurfacePoint pin)
    {
        Require(GameState.Guessing);
        ValidatePoint(pin);
        Pin = pin;
    }

    /// <summary>Guessing → Reveal ("Confirm"); requires a pin.</summary>
    public RoundResult Submit()
    {
        Require(GameState.Guessing);
        if (Pin is null) throw new InvalidOperationException("Place the pin first.");
        return Finish();
    }

    /// <summary>Guessing → Reveal when the optional guess time runs out; without a pin the round scores 0.</summary>
    public RoundResult GuessTimeExpired()
    {
        Require(GameState.Guessing);
        return Finish();
    }

    /// <summary>Reveal → WhoIsNext, or SessionSummary after the last round.</summary>
    public void NextRound()
    {
        Require(GameState.Reveal);
        RoundIndex++;
        _target = null;
        Pin = null;
        if (RoundIndex >= RoundCount)
        {
            EndedAt = _clock.Now;
            MoveTo(GameState.SessionSummary);
        }
        else
        {
            MoveTo(GameState.WhoIsNext);
        }
    }

    /// <summary>
    /// "Abort round" (decision D5): from Draw, Touching, Curtain or Guessing back to WhoIsNext with the same pair.
    /// Nothing is recorded and the guesser's last target does not change.
    /// </summary>
    public void AbortRound()
    {
        Require(GameState.Draw, GameState.Touching, GameState.Curtain, GameState.Guessing);
        _target = null;
        Pin = null;
        MoveTo(GameState.WhoIsNext);
    }

    /// <summary>
    /// The app went to the background or the screen was turned off. In Touching, Curtain and Guessing the session
    /// returns to Curtain (the target is never shown after coming back; the pin and guess time are reset).
    /// Other states are unchanged.
    /// </summary>
    public void Interrupt()
    {
        if (State is not (GameState.Touching or GameState.Curtain or GameState.Guessing)) return;
        Pin = null;
        if (State != GameState.Curtain) MoveTo(GameState.Curtain);
    }

    private RoundResult Finish()
    {
        var target = _target ?? throw new InvalidOperationException("No target.");
        var now = _clock.Now;
        var seconds = Math.Max(0, (now - _guessStartedAt).TotalSeconds);

        float? distance = null;
        string? guessedZone = null;
        IReadOnlyList<Vector3> path = [];
        if (Pin is { } pin)
        {
            var measured = _distances.Measure(pin, target);
            distance = measured.DistanceCm;
            path = measured.Path;
            guessedZone = GuessedZone(pin, target);
        }

        var score = ScoreCalculator.Score(distance, Config, seconds);
        var result = new RoundResult(RoundIndex + 1, CurrentPair.GuesserId, CurrentPair.ModelId, target, Pin, distance, guessedZone,
            score.BasePoints, score.SpeedBonus, seconds, now, path);
        _results.Add(result);
        _lastTargetByGuesser[CurrentPair.GuesserId] = target;
        MoveTo(GameState.Reveal);
        return result;
    }

    private string? GuessedZone(SurfacePoint pin, Target target)
    {
        var level = target is ZoneTarget zone ? _catalog.Get(zone.ZoneId).Level : _catalog.Levels.LastOrDefault();
        return _catalog.ZoneAt(pin.TriangleIndex, level)?.Id;
    }

    private void ValidatePoint(SurfacePoint point)
    {
        if ((uint)point.TriangleIndex >= (uint)_catalog.TriangleCount)
            throw new ArgumentOutOfRangeException(nameof(point), "Triangle index out of range.");
        if (!float.IsFinite(point.BaryU) || !float.IsFinite(point.BaryV))
            throw new ArgumentException("Barycentric coordinates must be finite.", nameof(point));
    }

    private Player PlayerById(long id) => _setup.Players.First(p => p.Id == id);

    private void Require(params GameState[] allowed)
    {
        if (Array.IndexOf(allowed, State) < 0)
            throw new InvalidOperationException($"Not allowed in state {State} (expected {string.Join(" or ", allowed)}).");
    }

    private void MoveTo(GameState next)
    {
        var previous = State;
        State = next;
        StateChanged?.Invoke(this, new GameStateChangedEventArgs(previous, next));
    }
}
