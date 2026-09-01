using System.Globalization;
using DeskNote.App.Services;
using DeskNote.Companion.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>The pet dashboard: what it needs, what today cost, and every care action.</summary>
public sealed partial class CompanionWindow : Window
{
    private readonly Action _showSettings;
    private readonly Func<CareRequest, Task<CompanionCareResult?>> _performCare;
    private readonly Func<CompanionSuggestion, Task> _actOnSuggestion;
    private readonly Func<CompanionSuggestion, Task> _dismissSuggestion;
    private CompanionSettings _settings;
    private CompanionSnapshot _snapshot;
    private CompanionSuggestion? _suggestion;
    private PetGrowthGuideWindow? _growthGuideWindow;
    private string? _avatarAssetKey;
    private int _avatarStage = -1;
    private CompanionPetKind? _avatarPet;

    public CompanionWindow(
        CompanionSnapshot snapshot,
        CompanionSettings settings,
        Action showSettings,
        Func<CareRequest, Task<CompanionCareResult?>> performCare,
        Func<CompanionSuggestion, Task> actOnSuggestion,
        Func<CompanionSuggestion, Task> dismissSuggestion)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(showSettings);
        ArgumentNullException.ThrowIfNull(performCare);
        ArgumentNullException.ThrowIfNull(actOnSuggestion);
        ArgumentNullException.ThrowIfNull(dismissSuggestion);

        InitializeComponent();
        _settings = settings;
        _snapshot = snapshot;
        _showSettings = showSettings;
        _performCare = performCare;
        _actOnSuggestion = actOnSuggestion;
        _dismissSuggestion = dismissSuggestion;

        AppWindow.Title = Strings.Get("Companion_Title");
        AppIcon.Apply(this);
        AppWindow.Resize(new SizeInt32(420, 780));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = settings.AlwaysVisible;
            presenter.PreferredMinimumWidth = 380;
            presenter.PreferredMinimumHeight = 480;
            presenter.IsMaximizable = false;
        }

        NeedsHeader.Text = Strings.Get("Companion_NeedsHeader");
        FullnessLabel.Text = Strings.Get("Companion_Fullness");
        CleanlinessLabel.Text = Strings.Get("Companion_Cleanliness");
        MoodNeedLabel.Text = Strings.Get("Companion_MoodNeed");
        BondLabel.Text = Strings.Get("Companion_Bond");
        ExperienceLabel.Text = Strings.Get("Companion_ExperienceHeader");
        AppScoreLabel.Text = Strings.Get("Companion_AppScoreLabel");
        CareScoreLabel.Text = Strings.Get("Companion_CareScoreLabel");
        CareHeader.Text = Strings.Get("Companion_CareHeader");
        PlayHeader.Text = Strings.Get("Companion_PlayHeader");
        FeedButton.Content = Strings.Get("Companion_CareFeed");
        BasicCareButton.Content = Strings.Get("Companion_CareBasicCare");
        RestButton.Content = Strings.Get("Companion_CareRest");
        GreetingButton.Content = Strings.Get("Companion_CareGreeting");
        ChasePlayButton.Content = Strings.Get("Companion_PlayChase");
        PuzzlePlayButton.Content = Strings.Get("Companion_PlayPuzzle");
        TossPlayButton.Content = Strings.Get("Companion_PlayToss");
        MissionHeader.Text = Strings.Get("Companion_MissionHeader");
        MissionNote.Text = Strings.Get("Companion_MissionNote");
        TraitHeader.Text = Strings.Get("Companion_TraitHeader");
        SettingsButton.Content = Strings.Get("Tray_Settings");
        GrowthGuideLabel.Text = Strings.Get("PetGrowthGuide_LinkLabel");
        GrowthGuideButton.Content = Strings.Get("PetGrowthGuide_Open");
        ApplyAccessibleName();
        SuggestionOpen.Content = Strings.Get("Companion_SuggestionOpen");
        SuggestionDismiss.Content = Strings.Get("Companion_SuggestionDismiss");
        UpdateSnapshot(snapshot);
        Activated += OnFirstActivated;
        Closed += (_, _) => _growthGuideWindow?.Close();
    }

    public void UpdateSettings(CompanionSettings settings)
    {
        _settings = settings;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = settings.AlwaysVisible;
        }

        ApplyAccessibleName();
        UpdateSnapshot(_snapshot);
    }

    private void ApplyAccessibleName() =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            AvatarViewport,
            Strings.Format("Companion_AccessibleNameFormat", _settings.PetName));

    public void UpdateSnapshot(CompanionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => UpdateSnapshot(snapshot));
            return;
        }

        _snapshot = snapshot;
        CompanionName.Text = $"{PetName(snapshot.Profile.AppearanceKey)} · {_settings.PetName}";
        SetAvatar(snapshot.Profile.AppearanceKey, snapshot.Growth.AppearanceStage);
        StageLabel.Text = Strings.Format(
            "Companion_StageFormat",
            snapshot.Growth.Stage,
            Strings.Get($"Companion_StageName{snapshot.Growth.Stage}"));

        ShowNeeds(snapshot.Needs);
        ShowGrowth(snapshot.Growth);
        ShowToday(snapshot.Today);
        ShowCareButtons(snapshot);
        ShowMission(snapshot);

        TraitSummary.Text = Strings.Format(
            "Companion_TraitSummaryFormat",
            snapshot.Growth.Curiosity,
            snapshot.Growth.Insight,
            snapshot.Growth.Reliability);

        var activityKey = snapshot.LastActivityType is { } type
            ? $"Companion_Reason{type}"
            : "Companion_ReasonWelcome";
        RecentReason.Text = Strings.Get(activityKey);
        MoodLabel.Text = Strings.Get(MoodKey(snapshot.Needs));
    }

    /// <summary>The line under the name, chosen by whichever need is loudest right now.</summary>
    private static string MoodKey(CompanionNeeds needs)
    {
        if (needs.Fullness <= CompanionCareRules.FeedThreshold)
        {
            return "Companion_MoodHungry";
        }

        if (needs.Cleanliness <= CompanionCareRules.BasicCareThreshold)
        {
            return "Companion_MoodUnkempt";
        }

        return needs.Mood <= CompanionCareRules.PlayThreshold
            ? "Companion_MoodBored"
            : "Companion_MoodCalm";
    }

    private void ShowNeeds(CompanionNeeds needs)
    {
        FullnessBar.Value = needs.Fullness;
        CleanlinessBar.Value = needs.Cleanliness;
        MoodBar.Value = needs.Mood;
        BondBar.Value = needs.Bond;
        FullnessValue.Text = Percent(needs.Fullness);
        CleanlinessValue.Text = Percent(needs.Cleanliness);
        MoodValue.Text = Percent(needs.Mood);
        BondValue.Text = Percent(needs.Bond);
    }

    private void ShowGrowth(GrowthState growth)
    {
        ExperienceBar.Value = growth.ExperienceProgress;
        CareDaysLabel.Visibility = Visibility.Visible;

        if (CompanionGrowthLadder.NextRung(growth.Stage) is { } next)
        {
            ExperienceValue.Text = Strings.Format(
                "Companion_ExperienceFormat",
                growth.Experience,
                next.Experience);

            // Reaching stage two costs no care days, and "0/0일" is noise rather than guidance.
            if (next.CareDays <= 0)
            {
                CareDaysLabel.Visibility = Visibility.Collapsed;
                return;
            }

            CareDaysLabel.Text = growth.CareDaysRemaining > 0
                ? Strings.Format(
                    "Companion_CareDaysRemainingFormat",
                    growth.CareDays,
                    next.CareDays,
                    growth.CareDaysRemaining)
                : Strings.Format("Companion_CareDaysMetFormat", growth.CareDays, next.CareDays);
            return;
        }

        ExperienceValue.Text = Strings.Format("Companion_ExperienceMaxFormat", growth.Experience);
        CareDaysLabel.Text = Strings.Format("Companion_CareDaysTotalFormat", growth.CareDays);
    }

    private void ShowToday(DailyProgress today)
    {
        AppScoreValue.Text = Strings.Format(
            "Companion_ScoreFormat",
            today.AppScore,
            CompanionBalanceV2.DailyAppMaximum);
        CareScoreValue.Text = Strings.Format(
            "Companion_ScoreFormat",
            today.CareScore,
            CompanionBalanceV2.DailyCareMaximum);
    }

    private void ShowCareButtons(CompanionSnapshot snapshot)
    {
        SpecialCareButton.Content = Strings.Get(
            $"Companion_SpecialCare{CompanionSpecialCareCatalog.For(SelectedPet(snapshot))}");

        FeedButton.IsEnabled = snapshot.CanPerform(CompanionCareAction.Feed);
        BasicCareButton.IsEnabled = snapshot.CanPerform(CompanionCareAction.BasicCare);
        SpecialCareButton.IsEnabled = snapshot.CanPerform(CompanionCareAction.SpecialCare);
        RestButton.IsEnabled = snapshot.CanPerform(CompanionCareAction.Rest);
        GreetingButton.IsEnabled = snapshot.CanPerform(CompanionCareAction.Greeting);

        // Every play button shares one allowance, and a kind already played today is spent.
        var canPlay = snapshot.CanPerform(CompanionCareAction.Play);
        var played = snapshot.Today.PlayKinds;
        ChasePlayButton.IsEnabled = canPlay && !played.Contains(CompanionPlayKind.Chase);
        PuzzlePlayButton.IsEnabled = canPlay && !played.Contains(CompanionPlayKind.Puzzle);
        TossPlayButton.IsEnabled = canPlay && !played.Contains(CompanionPlayKind.Toss);
    }

    private void ShowMission(CompanionSnapshot snapshot)
    {
        var mission = snapshot.Mission;
        var today = snapshot.Today;
        MissionAppTask.Text = MissionLine(
            today.Count(mission.App) > 0,
            Strings.Get($"Companion_Task{mission.App}"));
        MissionFirstCare.Text = MissionLine(
            today.Count(mission.FirstCare) > 0,
            CareName(mission.FirstCare, snapshot));
        MissionSecondCare.Text = MissionLine(
            today.Count(mission.SecondCare) > 0,
            CareName(mission.SecondCare, snapshot));
    }

    private static string MissionLine(bool done, string text) => $"{(done ? "✓" : "·")} {text}";

    private static string CareName(CompanionCareAction action, CompanionSnapshot snapshot) =>
        action == CompanionCareAction.SpecialCare
            ? Strings.Get($"Companion_SpecialCare{CompanionSpecialCareCatalog.For(SelectedPet(snapshot))}")
            : Strings.Get($"Companion_Care{action}");

    private static CompanionPetKind SelectedPet(CompanionSnapshot snapshot) =>
        CompanionPetCatalog.FromAssetKey(snapshot.Profile.AppearanceKey);

    private static string Percent(int value) =>
        value.ToString(CultureInfo.CurrentCulture);

    private async void OnCareClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !TryParseCare(tag, out var request))
        {
            return;
        }

        SetCareEnabled(false);
        try
        {
            var result = await _performCare(request).ConfigureAwait(true);
            ShowCareOutcome(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write($"Care action {request.Action} failed", ex);
        }
        finally
        {
            // The refreshed snapshot decides which buttons come back, so re-enable from state.
            ShowCareButtons(_snapshot);
        }
    }

    private void ShowCareOutcome(CompanionCareResult? result)
    {
        if (result is null)
        {
            CareStatus.Visibility = Visibility.Collapsed;
            return;
        }

        CareStatus.Visibility = Visibility.Visible;
        CareStatus.Text = result.Refusal switch
        {
            CareRefusal.DailyLimitReached => Strings.Get("Companion_CareDone"),
            CareRefusal.NotNeededYet => Strings.Get("Companion_CareNotNeeded"),
            _ => Strings.Get("Companion_CareThanks"),
        };
    }

    private static bool TryParseCare(string tag, out CareRequest request)
    {
        var parts = tag.Split('.');
        if (!Enum.TryParse<CompanionCareAction>(parts[0], out var action))
        {
            request = default;
            return false;
        }

        if (parts.Length > 1 && Enum.TryParse<CompanionPlayKind>(parts[1], out var kind))
        {
            request = new CareRequest(action, kind);
            return true;
        }

        request = new CareRequest(action);
        return true;
    }

    private void SetCareEnabled(bool enabled)
    {
        foreach (var button in new[]
                 {
                     FeedButton, BasicCareButton, SpecialCareButton, RestButton,
                     GreetingButton, ChasePlayButton, PuzzlePlayButton, TossPlayButton,
                 })
        {
            button.IsEnabled = enabled;
        }
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
            Math.Max(area.Y, area.Y + area.Height - AppWindow.Size.Height - 20)));
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e) => _showSettings();

    private void OnGrowthGuideClicked(object sender, RoutedEventArgs e)
    {
        if (_growthGuideWindow is not null)
        {
            _growthGuideWindow.Activate();
            return;
        }

        var window = new PetGrowthGuideWindow(SelectedPet(_snapshot));
        _growthGuideWindow = window;
        window.Closed += (_, _) => _growthGuideWindow = null;
        window.Activate();
        window.PlaceNear(AppWindow);
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
