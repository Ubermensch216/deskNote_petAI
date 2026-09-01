using DeskNote.App.Services;
using DeskNote.Companion.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>A glanceable, non-modal projection of deterministic companion state.</summary>
public sealed partial class CompanionWindow : Window
{
    private readonly Action _showSettings;
    private readonly Func<DailyRitualKind, Task> _chooseRitual;
    private readonly Func<CompanionSuggestion, Task> _actOnSuggestion;
    private readonly Func<CompanionSuggestion, Task> _dismissSuggestion;
    private CompanionSettings _settings;
    private CompanionSuggestion? _suggestion;
    private string? _avatarAssetKey;
    private int _avatarStage = -1;
    private CompanionPetKind? _avatarPet;

    public CompanionWindow(
        CompanionSnapshot snapshot,
        CompanionSettings settings,
        Action showSettings,
        Func<DailyRitualKind, Task> chooseRitual,
        Func<CompanionSuggestion, Task> actOnSuggestion,
        Func<CompanionSuggestion, Task> dismissSuggestion)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(showSettings);
        ArgumentNullException.ThrowIfNull(chooseRitual);
        ArgumentNullException.ThrowIfNull(actOnSuggestion);
        ArgumentNullException.ThrowIfNull(dismissSuggestion);

        InitializeComponent();
        _settings = settings;
        _showSettings = showSettings;
        _chooseRitual = chooseRitual;
        _actOnSuggestion = actOnSuggestion;
        _dismissSuggestion = dismissSuggestion;

        AppWindow.Title = Strings.Get("Companion_Title");
        AppIcon.Apply(this);
        AppWindow.Resize(new SizeInt32(380, 520));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = settings.AlwaysVisible;
            presenter.PreferredMinimumWidth = 320;
            presenter.PreferredMinimumHeight = 420;
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
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            AvatarViewport,
            Strings.Get("Companion_AccessibleName"));
        SuggestionOpen.Content = Strings.Get("Companion_SuggestionOpen");
        SuggestionDismiss.Content = Strings.Get("Companion_SuggestionDismiss");
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

        if (_avatarAssetKey is { } assetKey)
        {
            CompanionName.Text = $"{PetName(assetKey)} · {_settings.PetName}";
        }
    }

    public void UpdateSnapshot(CompanionSnapshot snapshot)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => UpdateSnapshot(snapshot));
            return;
        }

        CompanionName.Text = $"{PetName(snapshot.Profile.AppearanceKey)} · {_settings.PetName}";
        SetAvatar(snapshot.Profile.AppearanceKey, snapshot.Growth.AppearanceStage);
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
        ShowRitual(snapshot.Today);
    }

    private void SetAvatar(string assetKey, int appearanceStage)
    {
        var normalizedAssetKey = assetKey switch
        {
            "rabbit" or "cat" or "dog" or "fennec" or "otter" or "dragon" or "monkey" => assetKey,
            _ => "rabbit",
        };

        if (!string.Equals(_avatarAssetKey, normalizedAssetKey, StringComparison.Ordinal))
        {
            _avatarAssetKey = normalizedAssetKey;
            AvatarSpriteStrip.Source = new BitmapImage(
                new Uri($"ms-appx:///Assets/Companion/{normalizedAssetKey}-walk.png"));
        }

        var pet = CompanionPetCatalog.FromAssetKey(normalizedAssetKey);
        var normalizedStage = Math.Clamp(
            appearanceStage,
            0,
            CompanionGrowthAppearanceCatalog.FinalStage);
        if (_avatarPet == pet && _avatarStage == normalizedStage)
        {
            return;
        }

        _avatarPet = pet;
        _avatarStage = normalizedStage;
        var appearance = CompanionGrowthAppearanceCatalog.For(pet, normalizedStage);
        AvatarGrowthTransform.ScaleX = appearance.WidthScale;
        AvatarGrowthTransform.ScaleY = appearance.HeightScale;
    }

    public void ShowSuggestion(CompanionSuggestion suggestion)
    {
        _suggestion = suggestion;
        SuggestionText.Text = Strings.Get($"Companion_Suggestion{suggestion.Type}");
        SuggestionCard.Visibility = Visibility.Visible;
        SuggestionOpen.IsEnabled = true;
        SuggestionDismiss.IsEnabled = true;
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

        SetRitualChoicesEnabled(false);
        try
        {
            await _chooseRitual(ritual).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Choosing a companion ritual failed", ex);
            SetRitualChoicesEnabled(true);
        }
    }

    private void ShowRitual(DailyProgress progress)
    {
        if (progress.ChosenRitual is not { } ritual)
        {
            RitualChoices.Visibility = Visibility.Visible;
            SetRitualChoicesEnabled(true);
            RitualStatus.Visibility = Visibility.Collapsed;
            return;
        }

        RitualChoices.Visibility = Visibility.Collapsed;
        RitualStatus.Visibility = Visibility.Visible;
        RitualStatus.Text = Strings.Format(
            progress.RitualCompleted ? "Companion_RitualDoneFormat" : "Companion_RitualPendingFormat",
            Strings.Get($"Companion_Ritual{ritual}"));
    }

    private void SetRitualChoicesEnabled(bool enabled)
    {
        CaptureRitual.IsEnabled = enabled;
        RecallRitual.IsEnabled = enabled;
        ResolveRitual.IsEnabled = enabled;
    }

    private static string PetName(string assetKey) => Strings.Get(assetKey switch
    {
        "rabbit" => "Settings_PetRabbit",
        "cat" => "Settings_PetCat",
        "dog" => "Settings_PetDog",
        "fennec" => "Settings_PetFennecFox",
        "otter" => "Settings_PetOtter",
        "dragon" => "Settings_PetDragon",
        "monkey" => "Settings_PetMonkey",
        _ => "Settings_PetRabbit",
    });

    private async void OnSuggestionOpened(object sender, RoutedEventArgs e) =>
        await FinishSuggestionAsync(_actOnSuggestion).ConfigureAwait(true);

    private async void OnSuggestionDismissed(object sender, RoutedEventArgs e) =>
        await FinishSuggestionAsync(_dismissSuggestion).ConfigureAwait(true);

    private async Task FinishSuggestionAsync(Func<CompanionSuggestion, Task> action)
    {
        if (_suggestion is not { } suggestion)
        {
            return;
        }

        SuggestionOpen.IsEnabled = false;
        SuggestionDismiss.IsEnabled = false;
        try
        {
            await action(suggestion).ConfigureAwait(true);
            _suggestion = null;
            SuggestionCard.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Handling a companion suggestion failed", ex);
            SuggestionOpen.IsEnabled = true;
            SuggestionDismiss.IsEnabled = true;
        }
    }
}
