using System.Collections.ObjectModel;
using System.Globalization;
using BodyTouchGame.App.Bridge;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.App.Services;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Game;
using BodyTouchGame.Core.Persistence;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;
using BodyTouchGame.Geometry;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BodyTouchGame.App.ViewModels;

public sealed record ProgressSegment(Color Color, double Width);

public sealed record PodiumItem(string Name, string Color, string? Emoji, string Points, int Place, double BarHeight);

/// <summary>
/// The game screen: drives <see cref="GameSession"/> (Core) through the round states, sends the view commands of
/// <see cref="ViewDirector"/> to the 3D view, runs the timers and saves the session. No game rules here.
/// </summary>
public sealed partial class GameViewModel(
    GameSetupService setup,
    BodyDataService bodyData,
    ISessionRepository sessions,
    FeedbackService feedback,
    ISettingsService settings,
    AdService ads,
    ReviewService review,
    INavigationService navigation,
    IDialogService dialogs) : ObservableObject
{
    private static IDispatcher Dispatcher => Application.Current!.Dispatcher;

    private static CultureInfo Culture => AppCulture.Format;
    // Finished rounds in Party colours; the current round is orange (Primary), so orange is not among them.
    private static readonly Color[] PartyColors = [Color.FromArgb("#FF3D9A"), Color.FromArgb("#22D3EE"), Color.FromArgb("#8B5CF6")];

    private readonly SemaphoreSlim _viewLock = new(1, 1);
    private readonly Random _random = new();
    private GameSession? _session;
    private ZoneCatalog? _catalog;
    private BodyViewBridge? _bridge;
    private Task _modelLoaded = Task.CompletedTask;
    private long _sessionId;
    private IDispatcherTimer? _timer;
    private DateTime _timerStart;
    private int _lastTickSecond;

    /// <summary>Raised when the result is a perfect hit and confetti is enabled (the page plays the burst).</summary>
    public event EventHandler? ConfettiRequested;

    /// <summary>Path animation of the result in the 3D view (body-view.js REVEAL_PATH_MS): sound and confetti
    /// come when the path reaches the target.</summary>
    private static readonly TimeSpan RevealPathDuration = TimeSpan.FromMilliseconds(1300);
    private bool _ended;

    // ---- state flags (which panel is visible)
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWhoIsNext), nameof(IsDraw), nameof(IsTouching), nameof(IsCurtain), nameof(IsGuessing),
         nameof(IsReveal), nameof(IsSummary), nameof(ShowBody), nameof(ShowHeader), nameof(IsGuessLayout), nameof(IsDrawLayout), nameof(ShowProgress), nameof(ShowOverlay))]
    public partial GameState State { get; set; } = GameState.Setup;

    public bool IsWhoIsNext => State == GameState.WhoIsNext;
    public bool IsDraw => State == GameState.Draw;
    public bool IsTouching => State == GameState.Touching;
    public bool IsCurtain => State == GameState.Curtain;
    public bool IsGuessing => State == GameState.Guessing;

    /// <summary>
    /// The screen under the touch countdown and the curtain already has the Guessing layout (title, pin panel), so the
    /// 3D view keeps its size when the guesser's screen appears – a size change re-framed the model in front of the
    /// players ("jump" after "Gotowe!", owner's remark). Nothing of the target is in this layout.
    /// </summary>
    public bool IsGuessLayout => State is GameState.Touching or GameState.Curtain or GameState.Guessing;

    /// <summary>
    /// The same for the draw: under the "Kto teraz" screen the Draw layout (header with the pair, tiles, button) is
    /// already in place, so "Dalej" does not resize the 3D view. The target itself appears only in Draw.
    /// </summary>
    public bool IsDrawLayout => State is GameState.WhoIsNext or GameState.Draw;
    public bool IsReveal => State == GameState.Reveal;
    public bool IsSummary => State == GameState.SessionSummary;
    public bool ShowBody => State is GameState.Draw or GameState.Guessing or GameState.Reveal;
    public bool ShowHeader => State is GameState.Reveal || IsDrawLayout || IsGuessLayout;
    public bool ShowProgress => State is GameState.Reveal || IsDrawLayout;
    public bool ShowOverlay => !ShowBody;

    // ---- header
    [ObservableProperty] public partial string Header { get; set; } = string.Empty;
    [ObservableProperty] public partial string SubHeader { get; set; } = string.Empty;
    public ObservableCollection<ProgressSegment> Progress { get; } = [];

    // ---- who is next / tiles
    [ObservableProperty] public partial string GuesserName { get; set; } = string.Empty;
    [ObservableProperty] public partial string GuesserColor { get; set; } = "#22D3EE";
    [ObservableProperty] public partial string? GuesserEmoji { get; set; }
    [ObservableProperty] public partial string ModelName { get; set; } = string.Empty;
    [ObservableProperty] public partial string ModelColor { get; set; } = "#FF3D9A";
    [ObservableProperty] public partial string? ModelEmoji { get; set; }
    [ObservableProperty] public partial string WhoIsNextHint { get; set; } = string.Empty;
    [ObservableProperty] public partial string TouchText { get; set; } = string.Empty;
    [ObservableProperty] public partial string MovementText { get; set; } = string.Empty;
    [ObservableProperty] public partial string TimeText { get; set; } = string.Empty;

    // ---- draw
    [ObservableProperty] public partial bool IsChosenByModel { get; set; }
    [ObservableProperty] public partial string TargetTitle { get; set; } = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TargetNameSize))] public partial string TargetName { get; set; } = string.Empty;

    /// <summary>Long names of fine zones ("Prawy nadgarstek – od wewnątrz") get a smaller title.</summary>
    public double TargetNameSize => TargetName.Length > 26 ? 21 : TargetName.Length > 16 ? 25 : 30;

    /// <summary>ChosenByModel before the model has tapped: a hint instead of the target name.</summary>
    [ObservableProperty] public partial bool ShowTargetHint { get; set; }

    // ---- touching
    [ObservableProperty] public partial string CountdownText { get; set; } = string.Empty;
    [ObservableProperty] public partial double CountdownProgress { get; set; } = 1;
    [ObservableProperty] public partial string TouchLine { get; set; } = string.Empty;

    // ---- curtain
    [ObservableProperty] public partial string CurtainHint { get; set; } = string.Empty;

    // ---- guessing
    [ObservableProperty] public partial bool HasGuessTimer { get; set; }
    [ObservableProperty] public partial string GuessTimerText { get; set; } = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PinText))] public partial string PinZoneText { get; set; } = string.Empty;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ConfirmCommand)), NotifyPropertyChangedFor(nameof(PinText))] public partial bool HasPin { get; set; }

    /// <summary>Under "Twoja pinezka": the zone name (Easy/Medium sets only) or a hint before the pin is placed.</summary>
    public string PinText => HasPin ? PinZoneText : AppResources.PinNotPlaced;

    // ---- reveal
    [ObservableProperty] public partial string DistanceText { get; set; } = string.Empty;
    [ObservableProperty] public partial string PointsText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CommentText { get; set; } = string.Empty;
    [ObservableProperty] public partial string LegendPin { get; set; } = string.Empty;
    [ObservableProperty] public partial string LegendTarget { get; set; } = string.Empty;
    [ObservableProperty] public partial string TotalText { get; set; } = string.Empty;
    [ObservableProperty] public partial string NextButtonText { get; set; } = string.Empty;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(NextRoundCommand))] public partial bool CanLeaveReveal { get; set; }

    // ---- summary
    public ObservableCollection<PodiumItem> Podium { get; } = [];
    [ObservableProperty] public partial string BestHitText { get; set; } = string.Empty;
    [ObservableProperty] public partial string AverageMissText { get; set; } = string.Empty;
    [ObservableProperty] public partial string HardestZoneText { get; set; } = string.Empty;
    [ObservableProperty] public partial string BiggestMissText { get; set; } = string.Empty;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(StartTouchCommand))] public partial bool CanStartTouch { get; set; }

    // ------------------------------------------------------------------ start

    /// <summary>Creates the session (players and configuration from <see cref="GameSetupService"/>) and starts it.</summary>
    public async Task InitializeAsync(BodyViewBridge bridge, Task modelLoaded)
    {
        if (_session is not null) return;
        _bridge = bridge;
        _modelLoaded = modelLoaded;
        bridge.PinPlaced += OnPinPlaced;

        await bodyData.LoadAsync();
        _catalog = bodyData.Zones!;
        var pools = setup.Config.TargetSelection == TargetSelection.Random
            ? await setup.BuildTargetPoolsAsync(_catalog)
            : new Dictionary<long, IReadOnlyList<Target>>();
        var players = setup.Players.ToList();
        _session = new GameSession(new SessionSetup(players, setup.Config, setup.RoleOrder, setup.PresetId, pools),
            bodyData.Distances!, _catalog, new SystemRandomSource(), SystemClock.Instance);
        _session.StateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(OnStateChanged);
        IsChosenByModel = setup.Config.TargetSelection == TargetSelection.ChosenByModel;

        try
        {
            _session.Start();
        }
        catch (GameSetupException e)
        {
            await dialogs.AlertAsync(AppResources.CannotStartTitle, string.Join("\n", e.Problems));
            await navigation.GoBackAsync();
            return;
        }

        _sessionId = await sessions.CreateAsync(new SessionRecord(0, setup.PresetId, setup.Config, setup.RoleOrder,
            players.Select(p => p.Id).ToList(), _session.StartedAt!.Value, null, []));
    }

    // ------------------------------------------------------------------ transitions (commands)

    [RelayCommand]
    private void Continue() => _session?.Continue();

    [RelayCommand(CanExecute = nameof(CanStartTouch))]
    private async Task StartTouchAsync()
    {
        if (_session?.CanStartTouch != true) return;
        _session.StartTouch();
        await feedback.TouchStartAsync();
        StartTimer(_session.Config.TouchSeconds, OnTouchTick, OnTouchFinished);
    }

    [RelayCommand]
    private void Ready() => _session?.ReadyToGuess();

    [RelayCommand(CanExecute = nameof(HasPin))]
    private async Task ConfirmAsync()
    {
        if (_session?.State != GameState.Guessing || _session.Pin is null) return;
        StopTimer();
        await SaveRoundAsync(_session.Submit());
    }

    [RelayCommand(CanExecute = nameof(CanLeaveReveal))]
    private async Task NextRoundAsync()
    {
        if (_session?.State != GameState.Reveal) return;
        // A full-screen ad, when due, only here: between rounds (never in Touching, Curtain or Guessing).
        if (_session.RoundIndex + 1 < _session.RoundCount) await ads.ShowIfDueAsync();
        if (_session.State != GameState.Reveal) return;
        _session.NextRound();
        if (_session.State == GameState.SessionSummary) await EndSessionAsync();
    }

    /// <summary>"Przerwij rundę" (D5), with confirmation.</summary>
    [RelayCommand]
    private async Task AbortRoundAsync()
    {
        if (_session?.State is not (GameState.Draw or GameState.Touching or GameState.Curtain or GameState.Guessing)) return;
        var touching = _session.State == GameState.Touching;
        if (!await dialogs.ConfirmAsync(AppResources.AbortRoundTitle, AppResources.AbortRoundMessage, AppResources.Abort, AppResources.Cancel)) return;
        if (_session.State is not (GameState.Draw or GameState.Touching or GameState.Curtain or GameState.Guessing)) return;
        StopTimer();
        if (touching) await feedback.TouchEndAsync();
        _session.AbortRound();
    }

    [RelayCommand]
    private Task MenuAsync() => navigation.GoHomeAsync();

    [RelayCommand]
    private Task NewGameAsync() => navigation.GoToAsync("//" + Routes.Start + "/" + Routes.PlayersSelect);

    /// <summary>Android back button: abort the round in its middle, otherwise offer to end the game.</summary>
    public async Task HandleBackAsync()
    {
        if (_session is null || _session.State == GameState.SessionSummary)
        {
            await navigation.GoHomeAsync();
            return;
        }

        if (_session.State is GameState.Draw or GameState.Touching or GameState.Curtain or GameState.Guessing)
        {
            await AbortRoundAsync();
            return;
        }

        if (!await dialogs.ConfirmAsync(AppResources.EndGameTitle, AppResources.EndGameMessage, AppResources.End, AppResources.Cancel)) return;
        StopTimer();
        if (_session.Results.Count > 0 && !_ended)
        {
            _ended = true;
            await sessions.EndAsync(_sessionId, DateTimeOffset.UtcNow);
        }

        await navigation.GoHomeAsync();
    }

    /// <summary>The app went to the background or the screen turned off (spec 5: always the curtain afterwards).</summary>
    public void OnAppStopped()
    {
        if (_session is null) return;
        if (_session.State is GameState.Touching or GameState.Guessing) StopTimer();
        _session.Interrupt();
    }

    // ------------------------------------------------------------------ events

    private void OnPinPlaced(object? sender, PinPlacedMessage message)
    {
        if (_session is null || _catalog is null) return;
        var point = new SurfacePoint(message.TriangleIndex, message.BaryU, message.BaryV);
        if (_session.State == GameState.Draw && IsChosenByModel)
        {
            _session.SetManualTarget(point);
            TargetName = ZoneName(point, _catalog.Levels.Last()) ?? AppResources.ManualPointName;
            ShowTargetHint = false;
            CanStartTouch = _session.CanStartTouch;
        }
        else if (_session.State == GameState.Guessing)
        {
            _session.PlacePin(point);
            HasPin = true;
            PinZoneText = ShowZoneUnderPin() ? ZoneName(point, FinestSelectedLevel()) ?? string.Empty : string.Empty;
        }
    }

    private void OnStateChanged()
    {
        if (_session is null) return;
        State = _session.State;
        UpdateHeader();
        CanStartTouch = _session.CanStartTouch;
        // The guess timer (and the empty pin panel) is laid out already behind the countdown and the curtain.
        HasGuessTimer = IsGuessLayout && _session.Config.GuessSeconds is not null;
        if (State is GameState.Touching or GameState.Curtain)
        {
            HasPin = false;
            PinZoneText = string.Empty;
            if (_session.Config.GuessSeconds is { } preview) GuessTimerText = FormatClock(preview);
        }

        switch (State)
        {
            case GameState.WhoIsNext: ShowWhoIsNext(); break;
            case GameState.Draw: ShowDraw(); break;
            case GameState.Touching:
                CountdownText = _session.Config.TouchSeconds.ToString(Culture);
                CountdownProgress = 1;
                TouchLine = ConfigTexts.TouchLine(_session.Config);
                break;
            case GameState.Curtain:
                StopTimer();
                CurtainHint = string.Format(Culture, AppResources.CurtainHintFormat, _session.Guesser.Name);
                break;
            case GameState.Guessing:
                HasPin = false;
                PinZoneText = string.Empty;
                if (_session.Config.GuessSeconds is { } limit)
                {
                    GuessTimerText = FormatClock(limit);
                    StartTimer(limit, OnGuessTick, OnGuessTimeUp);
                }

                break;
            case GameState.Reveal: ShowReveal(); break;
            case GameState.SessionSummary: ShowSummary(); break;
        }

        _ = ApplyViewAsync(ViewDirector.For(_session), State);
    }

    /// <summary>Sends the view commands in order; in Curtain/Guessing checks that the view holds no target.</summary>
    private async Task ApplyViewAsync(IReadOnlyList<ViewCommand> commands, GameState state)
    {
        if (_bridge is null) return;
        await _viewLock.WaitAsync();
        try
        {
            await _modelLoaded;
            await _bridge.ExecuteAsync(commands);
            if (state is GameState.Curtain or GameState.Guessing or GameState.Touching)
            {
                var debug = await _bridge.GetDebugStateAsync();
                Console.WriteLine($"[BTG] secrecy {state}: view hasTarget={debug?.HasTarget} mode={debug?.Mode} size={debug?.Width}x{debug?.Height} resizes={debug?.Resizes}");
                if (debug?.HasTarget == true) Console.WriteLine($"[BTG] SECRECY VIOLATION in {state}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[BTG] view commands failed in {state}: {e.Message}");
        }
        finally
        {
            _viewLock.Release();
        }
    }

    // ------------------------------------------------------------------ per-state texts

    private void UpdateHeader()
    {
        var s = _session!;
        var round = Math.Min(s.RoundIndex + 1, s.RoundCount);
        Header = State switch
        {
            GameState.Touching => string.Format(Culture, AppResources.RoundTouchingFormat, round, s.RoundCount, s.Model.Name),
            GameState.Guessing => string.Format(Culture, AppResources.RoundGuessingFormat, round, s.RoundCount, s.Guesser.Name),
            _ => string.Format(Culture, AppResources.RoundHeaderFormat, round, s.RoundCount, setup.PresetLabel),
        };
        SubHeader = State switch
        {
            GameState.WhoIsNext or GameState.Draw => string.Format(Culture, AppResources.PairFormat, s.Model.Name, s.Guesser.Name),
            GameState.Reveal => string.Format(Culture, AppResources.ResultForFormat, s.Guesser.Name),
            _ => string.Empty,
        };

        Progress.Clear();
        var width = s.RoundCount <= 9 ? 22 : Math.Max(6, 200.0 / s.RoundCount - 4);
        for (var i = 0; i < s.RoundCount; i++)
        {
            var color = i < s.RoundIndex ? PartyColors[i % PartyColors.Length] : i == s.RoundIndex ? Color.FromArgb("#FF9A3C") : Color.FromArgb("#2E2650");
            Progress.Add(new ProgressSegment(color, width));
        }
    }

    private void ShowWhoIsNext()
    {
        var s = _session!;
        GuesserName = s.Guesser.Name;
        GuesserColor = s.Guesser.AvatarColor;
        GuesserEmoji = s.Guesser.AvatarEmoji;
        ModelName = s.Model.Name;
        ModelColor = s.Model.AvatarColor;
        ModelEmoji = s.Model.AvatarEmoji;
        WhoIsNextHint = string.Format(Culture, AppResources.WhoIsNextHintFormat, s.Guesser.Name, s.Model.Name);
        TouchText = ConfigTexts.TouchMethod(s.Config.TouchMethod);
        MovementText = ConfigTexts.Movement(s.Config.MovementAllowed);
        TimeText = ConfigTexts.Seconds(s.Config.TouchSeconds);
    }

    private void ShowDraw()
    {
        var s = _session!;
        if (IsChosenByModel)
        {
            TargetTitle = AppResources.ChooseTarget;
            TargetName = string.Empty;
            ShowTargetHint = true;
        }
        else
        {
            TargetTitle = AppResources.YourTarget;
            TargetName = TargetLabel(s.VisibleTarget!, capitalised: true);
            ShowTargetHint = false;
        }
    }

    private void ShowReveal()
    {
        var s = _session!;
        var result = s.LastResult!;
        DistanceText = result.DistanceCm switch
        {
            null => AppResources.TimeUp,
            { } d when !float.IsFinite(d) => AppResources.Unreachable,
            { } d => string.Format(Culture, AppResources.DistanceFormat, d),
        };
        PointsText = string.Format(Culture, AppResources.PointsFormat, result.Points);
        var targetName = TargetLabel(result.Target, capitalised: false);
        var guessed = result.GuessedZoneId is { } g && _catalog!.TryGet(g, out var zone) ? Lower(ZoneNames.Of(zone)) : AppResources.NoPin;
        CommentText = Comment(RevealComments.KindOf(result), targetName, guessed);
        LegendPin = string.Format(Culture, AppResources.LegendPinFormat, guessed);
        LegendTarget = string.Format(Culture, AppResources.LegendTargetFormat, targetName);

        var standing = s.Summary.Standings.First(p => p.PlayerId == result.GuesserId);
        TotalText = string.Format(Culture, AppResources.TotalFormat, standing.TotalPoints, standing.Rank);
        NextButtonText = s.RoundIndex + 1 >= s.RoundCount ? AppResources.ShowSummary : AppResources.NextRound;

        var kind = RevealComments.KindOf(result);
        Dispatcher.StartTimer(result.Pin is null ? TimeSpan.FromMilliseconds(200) : RevealPathDuration, () =>
        {
            if (_session?.State != GameState.Reveal) return false;
            _ = feedback.PlayAsync(kind switch
            {
                RevealCommentKind.Perfect => SoundEffect.RevealPerfect,
                RevealCommentKind.Close or RevealCommentKind.Warm or RevealCommentKind.Far => SoundEffect.RevealPoints,
                _ => SoundEffect.RevealMiss,
            });
            if (kind == RevealCommentKind.Perfect && settings.ConfettiEnabled) ConfettiRequested?.Invoke(this, EventArgs.Empty);
            return false;
        });

        // The result stays on screen for at least 3 s (spec 13).
        CanLeaveReveal = false;
        Dispatcher.StartTimer(TimeSpan.FromSeconds(3), () =>
        {
            CanLeaveReveal = true;
            return false;
        });
    }

    private void ShowSummary()
    {
        _ = feedback.PlayAsync(SoundEffect.GameOver);
        var s = _session!;
        var summary = s.Summary;
        var names = s.Players.ToDictionary(p => p.Id, p => p.Name);
        Header = string.Format(Culture, AppResources.SummaryHeaderFormat, PolishPlural.Rounds(s.Results.Count), setup.PresetLabel);

        Podium.Clear();
        var top = summary.Standings.Take(3).ToList();
        int[] order = top.Count switch { 1 => [0], 2 => [1, 0], _ => [1, 0, 2] };
        string[] placeColors = ["#FF9A3C", "#22D3EE", "#FF3D9A"];
        double[] heights = [160, 110, 80];
        foreach (var i in order)
        {
            var standing = top[i];
            var place = Math.Clamp(standing.Rank, 1, 3);
            Podium.Add(new PodiumItem(names[standing.PlayerId], placeColors[place - 1], s.Players.First(p => p.Id == standing.PlayerId).AvatarEmoji,
                string.Format(Culture, AppResources.PointsShortFormat, standing.TotalPoints), standing.Rank, heights[place - 1]));
        }

        BestHitText = summary.BestHit is { } best
            ? string.Format(Culture, AppResources.BestHitFormat, names[best.GuesserId], best.DistanceCm, TargetLabel(best.Target, capitalised: false))
            : AppResources.Unreachable;
        AverageMissText = summary.AverageDistanceCm is { } avg ? string.Format(Culture, AppResources.DistanceFormat, avg) : AppResources.Unreachable;
        HardestZoneText = summary.HardestZoneId is { } hardest && _catalog!.TryGet(hardest, out var zone) ? ZoneNames.Of(zone) : AppResources.Unreachable;
        BiggestMissText = summary.BiggestMiss is { } worst
            ? string.Format(Culture, AppResources.BiggestMissFormat, names[worst.GuesserId], worst.DistanceCm)
            : AppResources.Unreachable;
    }

    // ------------------------------------------------------------------ timers

    private void StartTimer(int seconds, Action<double> tick, Func<Task> finished)
    {
        StopTimer();
        _timerStart = DateTime.UtcNow;
        _lastTickSecond = seconds;
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += async (_, _) =>
        {
            var remaining = seconds - (DateTime.UtcNow - _timerStart).TotalSeconds;
            if (remaining <= 0)
            {
                StopTimer();
                await finished();
                return;
            }

            tick(remaining / seconds);
            var second = (int)Math.Ceiling(remaining);
            if (_session?.State == GameState.Touching)
            {
                CountdownText = second.ToString(Culture);
                // Ticks in the last 3 seconds of touching.
                if (second <= 3 && second != _lastTickSecond) _ = feedback.PlayAsync(SoundEffect.Tick);
            }

            _lastTickSecond = second;
            if (_session?.State == GameState.Guessing) GuessTimerText = FormatClock(remaining);
        };
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void OnTouchTick(double fraction) => CountdownProgress = fraction;

    private async Task OnTouchFinished()
    {
        if (_session?.State != GameState.Touching) return;
        CountdownText = "0";
        CountdownProgress = 0;
        await feedback.TouchEndAsync();
        _session.TouchFinished();
    }

    private static void OnGuessTick(double fraction) { }

    private async Task OnGuessTimeUp()
    {
        if (_session?.State != GameState.Guessing) return;
        await SaveRoundAsync(_session.GuessTimeExpired());
    }

    // ------------------------------------------------------------------ persistence

    private async Task SaveRoundAsync(RoundResult result)
    {
        ads.RoundFinished();
        try
        {
            await sessions.AddRoundAsync(_sessionId, result);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[BTG] saving round failed: {e.Message}");
        }
    }

    private async Task EndSessionAsync()
    {
        if (_ended) return;
        _ended = true;
        await sessions.EndAsync(_sessionId, _session!.EndedAt ?? DateTimeOffset.UtcNow);
        // Let the summary and the fanfare play first, then (3rd game, then every 10th) the review request.
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        await review.GameCompletedAsync();
    }

    // ------------------------------------------------------------------ helpers

    private string TargetLabel(Target target, bool capitalised)
    {
        var name = target switch
        {
            ZoneTarget zone => ZoneNames.Of(_catalog!.Get(zone.ZoneId)),
            CustomPointTarget point => point.Name,
            ManualPointTarget manual => ZoneName(manual.Point, _catalog!.Levels.Last()) ?? AppResources.ManualPointName,
            _ => string.Empty,
        };
        return capitalised ? name : Lower(name);
    }

    private string? ZoneName(SurfacePoint point, ZoneLevel level) => _catalog!.ZoneAt(point.TriangleIndex, level) is { } zone ? ZoneNames.Of(zone) : null;

    /// <summary>Zone name under the pin only when the zone set is Easy/Medium (spec 12).</summary>
    private bool ShowZoneUnderPin() => IsChosenByModel || SelectedLevels().All(l => l is ZoneLevel.Easy or ZoneLevel.Medium);

    /// <summary>Finest level of the zone set; a set of custom points only names the pin's zone on the Medium level.</summary>
    private ZoneLevel FinestSelectedLevel()
    {
        var levels = SelectedLevels().Where(_catalog!.HasLevel).ToList();
        if (levels.Count > 0) return levels.Max();
        return IsChosenByModel ? _catalog!.Levels.Last() : ZoneLevel.Medium;
    }

    /// <summary>Levels of the zone set, including the levels of zones picked by hand.</summary>
    private IEnumerable<ZoneLevel> SelectedLevels()
    {
        var set = _session!.Config.ZoneSet;
        return set.Levels.Concat(set.ZoneIds.Where(id => _catalog!.TryGet(id, out _)).Select(id => _catalog!.Get(id).Level)).Distinct();
    }

    private string Comment(RevealCommentKind kind, string target, string guessed)
    {
        var pool = kind switch
        {
            RevealCommentKind.Perfect => AppResources.CommentPerfect,
            RevealCommentKind.Close => AppResources.CommentClose,
            RevealCommentKind.Warm => AppResources.CommentWarm,
            RevealCommentKind.Far => AppResources.CommentFar,
            RevealCommentKind.Miss => AppResources.CommentMiss,
            _ => AppResources.CommentTimeUp,
        };
        var options = pool.Split('|');
        return string.Format(Culture, options[_random.Next(options.Length)], target, guessed);
    }

    private static string Lower(string text) => string.IsNullOrEmpty(text) ? text : char.ToLower(text[0], Culture) + text[1..];

    private static string FormatClock(double seconds)
    {
        var total = (int)Math.Ceiling(seconds);
        return $"{total / 60}:{total % 60:00}";
    }
}
