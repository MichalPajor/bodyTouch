using System.Numerics;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Geometry;

namespace BodyTouchGame.Core.Game;

/// <summary>Interaction mode of the 3D view (spec 12).</summary>
public enum ViewMode
{
    /// <summary>Look only, taps ignored.</summary>
    View,

    /// <summary>The model sees the target; with <see cref="SetViewMode.AllowTargetPin"/> the model places the target pin.</summary>
    Draw,

    /// <summary>The guesser places and moves the pin.</summary>
    Guess,

    /// <summary>Result animation, taps ignored.</summary>
    Reveal,

    /// <summary>Tapping a zone reports it (exclusions, zone editor).</summary>
    Editor,
}

/// <summary>A command for the 3D view, executed by the app through the bridge.</summary>
public abstract record ViewCommand;

public sealed record SetViewMode(ViewMode Mode, bool AllowTargetPin = false) : ViewCommand;

/// <summary>Rotate to the target and highlight it (pulsing).</summary>
public sealed record FocusTarget(Target Target) : ViewCommand;

/// <summary>Remove the target (highlight, target pin) from the view and from JS memory.</summary>
public sealed record ClearTarget : ViewCommand;

/// <summary>Remove the guesser's pin and the result path.</summary>
public sealed record ClearPin : ViewCommand;

public sealed record ResetCamera(string View) : ViewCommand;

/// <summary>Result: pin (if any), target, surface path and distance. The only command besides
/// <see cref="FocusTarget"/> that carries the target.</summary>
public sealed record ShowReveal(SurfacePoint? Pin, Target Target, IReadOnlyList<Vector3> Path, float? DistanceCm) : ViewCommand;

/// <summary>
/// Decides what the 3D view shows in each state (spec 5): the target is sent to the view only in Draw (to the
/// model) and Reveal (to everybody). Every other state starts by clearing the target, so the view never holds it
/// in Touching, Curtain or Guessing – including after <see cref="GameSession.Interrupt"/>.
/// </summary>
public static class ViewDirector
{
    public static IReadOnlyList<ViewCommand> For(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.State switch
        {
            GameState.Draw when session.Config.TargetSelection == TargetSelection.ChosenByModel =>
                [new ClearTarget(), new ClearPin(), new ResetCamera("Front"), new SetViewMode(ViewMode.Draw, AllowTargetPin: true)],
            GameState.Draw =>
                [new ClearTarget(), new ClearPin(), new SetViewMode(ViewMode.Draw), new FocusTarget(session.VisibleTarget!)],
            GameState.Guessing =>
                [new ClearTarget(), new ClearPin(), new ResetCamera("Front"), new SetViewMode(ViewMode.Guess)],
            GameState.Reveal when session.LastResult is { } result =>
                [new ClearTarget(), new SetViewMode(ViewMode.Reveal), new ShowReveal(result.Pin, result.Target, result.Path, result.DistanceCm)],
            // Behind the touch countdown and the curtain the view is already put into its Guessing state (front view,
            // no pin), so nothing jumps when the guesser's screen appears (owner's remark after stage 8).
            GameState.Touching or GameState.Curtain =>
                [new ClearTarget(), new ClearPin(), new ResetCamera("Front"), new SetViewMode(ViewMode.View)],
            // Behind "Kto teraz" the camera returns to the front view (from the result's pose), so every draw starts its
            // turn to the target from the same place and nothing moves when the screen appears.
            GameState.WhoIsNext =>
                [new ClearTarget(), new ClearPin(), new ResetCamera("Front"), new SetViewMode(ViewMode.View)],
            _ => [new ClearTarget(), new ClearPin(), new SetViewMode(ViewMode.View)],
        };
    }
}

/// <summary>Kind of comment shown with the result (texts come from the app resources).</summary>
public enum RevealCommentKind
{
    /// <summary>No pin: the guess time ran out.</summary>
    TimeUp,

    /// <summary>Distance 0 – the pin is in the target.</summary>
    Perfect,

    /// <summary>At least 75 base points (close to the target or within the full-points radius).</summary>
    Close,

    /// <summary>At least 40 base points.</summary>
    Warm,

    /// <summary>Some points.</summary>
    Far,

    /// <summary>No points: a different part of the body ("It was the knee. The pin: the ear.").</summary>
    Miss,
}

public static class RevealComments
{
    public static RevealCommentKind KindOf(RoundResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Pin is null || result.DistanceCm is null) return RevealCommentKind.TimeUp;
        if (result.DistanceCm <= 0) return RevealCommentKind.Perfect;
        return result.BasePoints switch
        {
            >= 75 => RevealCommentKind.Close,
            >= 40 => RevealCommentKind.Warm,
            > 0 => RevealCommentKind.Far,
            _ => RevealCommentKind.Miss,
        };
    }
}
