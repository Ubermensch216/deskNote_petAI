using DeskNote.App.Services;
using DeskNote.Companion.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>A glanceable, non-modal projection of deterministic companion state.</summary>
public sealed partial class CompanionWindow : Window
{
    private readonly Action _showSettings;
    private readonly Func<DailyRitualKind, Task> _chooseRitual;
    private CompanionSettings _settings;

    public CompanionWindow(
        CompanionSnapshot snapshot,
        CompanionSettings settings,
        Action showSettings,
        Func<DailyRitualKind, Task> chooseRitual)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(showSettings);
        ArgumentNullException.ThrowIfNull(chooseRitual);

        InitializeComponent();
        _settings = settings;
        _showSettings = showSettings;
        _chooseRitual = chooseRitual;

        AppWindow.Title = Strings.Get("Companion_Title");
        AppIcon.Apply(this);
        AppWindow.Resize(new SizeInt32(350, 440));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = settings.AlwaysVisible;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        CuriosityLabel.Text = Strings.Get("Companion_Curiosity");
        InsightLabel.Text = Strings.Get("Companion_Insight");
        ReliabilityLabel.Text = Strings.Get("Companion_Reliability");
        SettingsButton.Content = Strings.Get("Tray_Settings");
        RitualHeader.Text = Strings.Get("Companion_RitualHeader");
        CaptureRitual.Content = Strings.Get("Companion_RitualCapture");
        RecallRitual.Content = Strings.Get("Companion_RitualRecall");
        ResolveRitual.Content = Strings.Get("Companion_RitualResolve");
        UpdateSnapshot(snapshot);
        Activated += OnFirstActivated;
    }

    public void UpdateSettings(CompanionSettings settings)
    {
        _settings = settings;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = settings.AlwaysVisible;
        }
    }

    public void UpdateSnapshot(CompanionSnapshot snapshot)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => UpdateSnapshot(snapshot));
            return;
        }

        CompanionName.Text = snapshot.Profile.Name;
        CuriosityBar.Value = snapshot.Growth.Curiosity;
        InsightBar.Value = snapshot.Growth.Insight;
        ReliabilityBar.Value = snapshot.Growth.Reliability;
        CuriosityValue.Text = snapshot.Growth.Curiosity.ToString(System.Globalization.CultureInfo.CurrentCulture);
        InsightValue.Text = snapshot.Growth.Insight.ToString(System.Globalization.CultureInfo.CurrentCulture);
        ReliabilityValue.Text = snapshot.Growth.Reliability.ToString(System.Globalization.CultureInfo.CurrentCulture);
        StageLabel.Text = Strings.Format("Companion_StageFormat", snapshot.Growth.AppearanceStage + 1);

        var activityKey = snapshot.LastActivityType is { } type
            ? $"Companion_Reason{type}"
            : "Companion_ReasonWelcome";
        RecentReason.Text = Strings.Get(activityKey);

        var grew = snapshot.LastReward.HasGrowth;
        MoodLabel.Text = grew ? Strings.Get("Companion_MoodGrowth") : Strings.Get("Companion_MoodCalm");
        Face.Text = grew ? "◕ᴗ◕" : "◕‿◕";
        ShowRitual(snapshot.Today);

        // Motion reduction is respected by using an immediate state change. The beta intentionally
        // ships no looping animation; future motion must keep this branch as its static fallback.
        if (_settings.ReduceMotion)
        {
            Face.Opacity = 1;
        }
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var area = display.WorkArea;
        AppWindow.Move(new PointInt32(
            area.X + area.Width - AppWindow.Size.Width - 20,
            area.Y + area.Height - AppWindow.Size.Height - 20));
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e) => _showSettings();

    private async void OnRitualClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }
            || !Enum.TryParse<DailyRitualKind>(tag, out var ritual))
        {
            return;
        }

        RitualChoices.IsEnabled = false;
        try
        {
            await _chooseRitual(ritual).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Choosing a companion ritual failed", ex);
            RitualChoices.IsEnabled = true;
        }
    }

    private void ShowRitual(DailyProgress progress)
    {
        if (progress.ChosenRitual is not { } ritual)
        {
            RitualChoices.Visibility = Visibility.Visible;
            RitualChoices.IsEnabled = true;
            RitualStatus.Visibility = Visibility.Collapsed;
            return;
        }

        RitualChoices.Visibility = Visibility.Collapsed;
        RitualStatus.Visibility = Visibility.Visible;
        RitualStatus.Text = Strings.Format(
            progress.RitualCompleted ? "Companion_RitualDoneFormat" : "Companion_RitualPendingFormat",
            Strings.Get($"Companion_Ritual{ritual}"));
    }
}
