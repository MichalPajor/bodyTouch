using System.Globalization;
using BodyTouchGame.App.Resources.Strings;
using BodyTouchGame.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BodyTouchGame.App.ViewModels;

public sealed record TutorialStep(string Title, string Text);

/// <summary>
/// Animated tutorial (stage 11): 7 scenes of a round played by two 2D characters (Resources/Raw/tutorial), with a
/// title and a description under each. Shown once on the first start and from "Jak grać?" any time.
/// </summary>
public sealed partial class TutorialViewModel(IPreferences preferences, INavigationService navigation) : ObservableObject
{
    public const string SeenKey = "tutorial.seen";

    public IReadOnlyList<TutorialStep> Steps { get; } =
    [
        new(AppResources.TutorialTitle1, AppResources.TutorialText1),
        new(AppResources.TutorialTitle2, AppResources.TutorialText2),
        new(AppResources.TutorialTitle3, AppResources.TutorialText3),
        new(AppResources.TutorialTitle4, AppResources.TutorialText4),
        new(AppResources.TutorialTitle5, AppResources.TutorialText5),
        new(AppResources.TutorialTitle6, AppResources.TutorialText6),
        new(AppResources.TutorialTitle7, AppResources.TutorialText7),
    ];

    /// <summary>Raised when the scene changes; the page tells the animation view.</summary>
    public event EventHandler<int>? SceneChanged;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title), nameof(Text), nameof(StepText), nameof(IsLast), nameof(NextText), nameof(Dots))]
    public partial int Index { get; set; }

    public string Title => Steps[Index].Title;
    public string Text => Steps[Index].Text;
    public string StepText => string.Format(AppCulture.Format, AppResources.TutorialStepFormat, Index + 1, Steps.Count);
    public bool IsLast => Index == Steps.Count - 1;
    public string NextText => IsLast ? AppResources.TutorialStart : AppResources.Next;

    /// <summary>Progress dots: the current one is wide and orange.</summary>
    public IReadOnlyList<bool> Dots => Steps.Select((_, i) => i == Index).ToList();

    public static bool WasSeen(IPreferences preferences) => preferences.Get(SeenKey, false);

    [RelayCommand]
    private Task NextAsync()
    {
        if (IsLast) return FinishAsync();
        Index++;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void Previous()
    {
        if (Index > 0) Index--;
    }

    [RelayCommand]
    private Task FinishAsync()
    {
        preferences.Set(SeenKey, true);
        return navigation.GoBackAsync();
    }

    partial void OnIndexChanged(int value) => SceneChanged?.Invoke(this, value);
}
