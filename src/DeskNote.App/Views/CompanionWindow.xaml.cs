using System.Globalization;
using DeskNote.App.Services;
using DeskNote.App.Theming;
using DeskNote.Companion.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI;

namespace DeskNote.App.Views;

/// <summary>
/// The pet dashboard: who the pet is, what to do for it today, and how it is doing.
/// </summary>
/// <remarks>
/// <para>
/// The window answers three questions in that order, one block each. The hero band is the pet
/// itself and its progress; the "today" card is the only part meant to be acted on, and it holds
/// the day's mission together with the buttons that satisfy it; the state card is four gauges,
/// coloured by whether they are asking for anything. Traits, the guide and settings are reference
/// material and sit below all three.
/// </para>
/// <para>
/// The previous layout was eight sibling sections of equal weight — four bars, two score cards,
/// eight identical grey buttons, and a mission written as a paragraph six sections away from the
/// buttons that would complete it. Nothing said what to do next, and nothing said this was a game.
/// </para>
/// <para>
/// Colour comes from <see cref="CompanionPalette"/> and is applied in code because almost all of
/// it depends on runtime state. A tile is gold because it is today's mission, grey because its
/// allowance is spent; a gauge is red because the pet is asking for something. That mapping is the
/// design: colour here is information, not decoration.
/// </para>
/// </remarks>
public sealed partial class CompanionWindow : Window
{
    /// <summary>One care button, with the parts of it that change as the day is spent.</summary>
    private sealed record CareTile(
        Button Button,
        TextBlock Title,
        TextBlock Subtitle,
        TextBlock Badge,
        CompanionCareAction Action,
        CompanionPlayKind? Play);

    /// <summary>One app task and two care tasks, which is what a mission always is.</summary>
    private const int MissionTaskCount = 3;

    /// <summary>
    /// How often an open dashboard re-reads the pet.
    /// </summary>
    /// <remarks>
    /// Fullness, cleanliness and mood are projected from the clock, so they are wrong the moment
    /// the window stops asking. A minute is under the resolution of the slowest gauge (mood loses
    /// ten points an hour) and costs one indexed read.
    /// </remarks>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    private readonly Action _showSettings;
    private readonly Func<CareRequest, Task<CompanionCareResult?>> _performCare;
    private readonly Func<CompanionSuggestion, Task> _actOnSuggestion;
    private readonly Func<CompanionSuggestion, Task> _dismissSuggestion;
    private readonly Func<Task> _refreshSnapshot;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _refreshTimer;
    private readonly List<CareTile> _tiles = [];
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
        Func<CompanionSuggestion, Task> dismissSuggestion,
        Func<Task> refreshSnapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(showSettings);
        ArgumentNullException.ThrowIfNull(performCare);
        ArgumentNullException.ThrowIfNull(actOnSuggestion);
        ArgumentNullException.ThrowIfNull(dismissSuggestion);
        ArgumentNullException.ThrowIfNull(refreshSnapshot);

        InitializeComponent();
        _settings = settings;
        _snapshot = snapshot;
        _showSettings = showSettings;
        _performCare = performCare;
        _actOnSuggestion = actOnSuggestion;
        _dismissSuggestion = dismissSuggestion;
        _refreshSnapshot = refreshSnapshot;

        AppWindow.Title = Strings.Get("Companion_Title");
        AppIcon.Apply(this);
        AppWindow.Resize(new SizeInt32(420, 820));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = settings.AlwaysVisible;
            presenter.PreferredMinimumWidth = 380;
            presenter.PreferredMinimumHeight = 480;
            presenter.IsMaximizable = false;
        }

        LocalizeChrome();
        BuildTiles();
        ApplyAccessibleName();
        ApplyPalette();
        UpdateSnapshot(snapshot);

        // The palette is resolved per theme, and the window can be open when Windows switches.
        Board.ActualThemeChanged += (_, _) =>
        {
            ApplyPalette();
            UpdateSnapshot(_snapshot);
        };

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = RefreshInterval;
        _refreshTimer.Tick += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        _refreshTimer.Start();

        Activated += OnFirstActivated;
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _growthGuideWindow?.Close();
        };
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e) =>
        await RefreshAsync().ConfigureAwait(true);

    /// <summary>
    /// Asks for the pet as it is now. The answer arrives back through UpdateSnapshot.
    /// </summary>
    /// <remarks>
    /// The window does not read the database itself — it hands the request to the runtime that
    /// owns the companion queue, which is the same path a care action or a note activity takes.
    /// That keeps one writer of the snapshot, and one place where it can fail.
    /// </remarks>
    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;

        try
        {
            await _refreshSnapshot().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Refreshing the companion dashboard failed", ex);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void LocalizeChrome()
    {
        TodayHeader.Text = Strings.Get("Companion_TodayHeader");
        DayScoreLabel.Text = Strings.Get("Companion_DayScoreLabel");
        AppTaskHint.Text = Strings.Get("Companion_AppTaskHint");
        CareHeader.Text = Strings.Get("Companion_CareHeader");
        PlayHeader.Text = Strings.Get("Companion_PlayHeader");

        NeedsHeader.Text = Strings.Get("Companion_NeedsHeader");
        FullnessLabel.Text = Strings.Get("Companion_Fullness");
        CleanlinessLabel.Text = Strings.Get("Companion_Cleanliness");
        MoodNeedLabel.Text = Strings.Get("Companion_MoodNeed");
        BondLabel.Text = Strings.Get("Companion_Bond");

        ExperienceLabel.Text = Strings.Get("Companion_ExperienceHeader");

        TraitHeader.Text = Strings.Get("Companion_TraitHeader");
        CuriosityLabel.Text = Strings.Get("Companion_TraitCuriosity");
        InsightLabel.Text = Strings.Get("Companion_TraitInsight");
        ReliabilityLabel.Text = Strings.Get("Companion_TraitReliability");

        // The corner icons carry no words, so the name they used to show has to be said out
        // loud instead: as the accessible name, and as the tooltip that answers a hovering
        // pointer with the same sentence a screen reader hears.
        Describe(GrowthGuideButton, "PetGrowthGuide_LinkLabel");
        Describe(SettingsButton, "Tray_Settings");
        Describe(RefreshButton, "Companion_Refresh");
        SuggestionOpen.Content = Strings.Get("Companion_SuggestionOpen");
        SuggestionDismiss.Content = Strings.Get("Companion_SuggestionDismiss");
    }

    /// <summary>Names a wordless button, for a screen reader and for a hovering pointer alike.</summary>
    private static void Describe(FrameworkElement element, string key)
    {
        var text = Strings.Get(key);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, text);
        ToolTipService.SetToolTip(element, text);
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

        ShowStagePips(snapshot.Growth.Stage);
        ShowNeeds(snapshot.Needs);
        ShowGrowth(snapshot.Growth);
        ShowToday(snapshot);
        ShowCareTiles(snapshot);

        CuriosityValue.Text = Count(snapshot.Growth.Curiosity);
        InsightValue.Text = Count(snapshot.Growth.Insight);
        ReliabilityValue.Text = Count(snapshot.Growth.Reliability);

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

    /// <summary>Five rungs, drawn: the stage as a distance rather than a number to read.</summary>
    private void ShowStagePips(int stage)
    {
        var reached = new SolidColorBrush(CompanionPalette.HeroInk);
        var pending = new SolidColorBrush(CompanionPalette.HeroTrack);
        var pips = new[] { StagePip1, StagePip2, StagePip3, StagePip4, StagePip5 };

        for (var index = 0; index < pips.Length; index++)
        {
            pips[index].Background = index < stage ? reached : pending;
        }
    }

    private void ShowNeeds(CompanionNeeds needs)
    {
        var dark = IsDark;

        PaintGauge(
            FullnessFill,
            FullnessRest,
            FullnessBar,
            FullnessValue,
            needs.Fullness,
            CompanionPalette.Gauge(
                CompanionPalette.LevelOf(needs.Fullness, CompanionCareRules.FeedThreshold),
                dark));

        PaintGauge(
            CleanlinessFill,
            CleanlinessRest,
            CleanlinessBar,
            CleanlinessValue,
            needs.Cleanliness,
            CompanionPalette.Gauge(
                CompanionPalette.LevelOf(needs.Cleanliness, CompanionCareRules.BasicCareThreshold),
                dark));

        PaintGauge(
            MoodFill,
            MoodRest,
            MoodBar,
            MoodValue,
            needs.Mood,
            CompanionPalette.Gauge(
                CompanionPalette.LevelOf(needs.Mood, CompanionCareRules.PlayThreshold),
                dark));

        // Bond only ever rises, so a "low" red on it would be scolding the user for being new.
        PaintGauge(BondFill, BondRest, BondBar, BondValue, needs.Bond, CompanionPalette.Bond(dark));
    }

    private static void PaintGauge(
        ColumnDefinition fill,
        ColumnDefinition rest,
        Border bar,
        TextBlock value,
        int amount,
        Color color)
    {
        SetBar(fill, rest, amount / 100d);
        bar.Background = new SolidColorBrush(color);
        value.Text = Count(amount);
    }

    private void ShowGrowth(GrowthState growth)
    {
        SetBar(ExperienceFill, ExperienceRest, growth.ExperienceProgress);
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

    /// <summary>
    /// The day's budgets, the mission counter, and the mission's app task.
    /// </summary>
    /// <remarks>
    /// The two budgets stay visibly separate — app work and care cannot substitute for each other,
    /// which is the whole of the 30:70 balance — but they are drawn as one bar split 30:70 so the
    /// day reads as one thing at a glance rather than as two score cards to compare.
    /// </remarks>
    private void ShowToday(CompanionSnapshot snapshot)
    {
        var dark = IsDark;
        var today = snapshot.Today;
        var mission = snapshot.Mission;

        DayScoreValue.Text = Strings.Format(
            "Companion_ScoreFormat",
            today.TotalScore,
            CompanionBalanceV2.DailyMaximum);

        SetBar(AppScoreFill, AppScoreRest, today.AppScore / (double)CompanionBalanceV2.DailyAppMaximum);
        SetBar(CareScoreFill, CareScoreRest, today.CareScore / (double)CompanionBalanceV2.DailyCareMaximum);
        AppScoreBar.Background = new SolidColorBrush(CompanionPalette.AppBudget(dark));
        CareScoreBar.Background = new SolidColorBrush(CompanionPalette.CareBudget(dark));

        AppScoreCaption.Text = Strings.Format(
            "Companion_AppBudgetFormat",
            today.AppScore,
            CompanionBalanceV2.DailyAppMaximum);
        CareScoreCaption.Text = Strings.Format(
            "Companion_CareBudgetFormat",
            today.CareScore,
            CompanionBalanceV2.DailyCareMaximum);

        var appDone = today.Count(mission.App) > 0;
        var done = (appDone ? 1 : 0)
            + (today.Count(mission.FirstCare) > 0 ? 1 : 0)
            + (today.Count(mission.SecondCare) > 0 ? 1 : 0);

        MissionProgressValue.Text = Strings.Format("Companion_MissionProgressFormat", done, MissionTaskCount);
        MissionProgressValue.Foreground = new SolidColorBrush(
            done == MissionTaskCount ? CompanionPalette.Gauge(CompanionGaugeLevel.Good, dark) : CompanionPalette.Mission(dark));

        var filled = new SolidColorBrush(
            done == MissionTaskCount ? CompanionPalette.Gauge(CompanionGaugeLevel.Good, dark) : CompanionPalette.Mission(dark));
        var empty = new SolidColorBrush(CompanionPalette.Track(dark));
        var pips = new[] { MissionPip1, MissionPip2, MissionPip3 };
        for (var index = 0; index < pips.Length; index++)
        {
            pips[index].Background = index < done ? filled : empty;
        }

        ShowAppTask(mission.App, appDone, dark);
    }

    /// <summary>
    /// The mission's app task, stated rather than offered.
    /// </summary>
    /// <remarks>
    /// It has no button because it is not earned on this window: it is earned by writing, tidying
    /// or reusing a note. Saying what it is worth is what keeps it from reading as a chore with no
    /// payoff, and it is painted in the same gold as the care tiles so the three mission tasks are
    /// recognisably one set.
    /// </remarks>
    private void ShowAppTask(AppScoreCategory category, bool done, bool dark)
    {
        var surface = CompanionPalette.Tile(
            done ? CompanionTileState.Spent : CompanionTileState.Mission,
            dark);

        AppTaskChip.Background = new SolidColorBrush(surface.Fill);
        AppTaskChip.BorderBrush = new SolidColorBrush(surface.Border);
        AppTaskText.Foreground = new SolidColorBrush(surface.Ink);
        AppTaskHint.Foreground = new SolidColorBrush(surface.Subtle);
        AppTaskIcon.Opacity = done ? 0.55 : 1;

        AppTaskText.Text = Strings.Get($"Companion_Task{category}");
        AppTaskState.Text = done
            ? $"✓ {Strings.Get("Companion_TaskDone")}"
            : Strings.Format("Companion_PointsFormat", CompanionBalanceV2.AppPoints(category));
        AppTaskState.Foreground = new SolidColorBrush(
            done ? surface.Subtle : CompanionPalette.Mission(dark));
    }

    /// <summary>
    /// Builds the care tiles once. Their state is repainted per snapshot, never rebuilt.
    /// </summary>
    /// <remarks>
    /// Rebuilding would drop keyboard focus every time a care action lands, which is exactly when
    /// a keyboard user is on one of these buttons.
    /// </remarks>
    private void BuildTiles()
    {
        var care = new (CompanionCareAction Action, string Emoji)[]
        {
            (CompanionCareAction.Feed, "\U0001F35A"),
            (CompanionCareAction.BasicCare, "\U0001F6C1"),
            (CompanionCareAction.SpecialCare, "✨"),
            (CompanionCareAction.Rest, "\U0001F634"),
            (CompanionCareAction.Greeting, "\U0001F44B"),
        };

        for (var index = 0; index < care.Length; index++)
        {
            var tile = BuildTile(care[index].Action, null, care[index].Emoji);

            Grid.SetRow(tile.Button, index / 2);
            Grid.SetColumn(tile.Button, index % 2);
            if (index == care.Length - 1)
            {
                Grid.SetColumnSpan(tile.Button, 2);
            }

            CareTileHost.Children.Add(tile.Button);
            _tiles.Add(tile);
        }

        var play = new (CompanionPlayKind Kind, string Emoji)[]
        {
            (CompanionPlayKind.Chase, "\U0001F3C3"),
            (CompanionPlayKind.Puzzle, "\U0001F9E9"),
            (CompanionPlayKind.Toss, "\U0001F3BE"),
        };

        for (var index = 0; index < play.Length; index++)
        {
            var tile = BuildTile(CompanionCareAction.Play, play[index].Kind, play[index].Emoji);
            Grid.SetColumn(tile.Button, index);
            PlayTileHost.Children.Add(tile.Button);
            _tiles.Add(tile);
        }
    }

    private CareTile BuildTile(CompanionCareAction action, CompanionPlayKind? play, string emoji)
    {
        var icon = new TextBlock
        {
            Text = emoji,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 17,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var title = new TextBlock
        {
            FontSize = 13.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var subtitle = new TextBlock { FontSize = 11 };

        var badge = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var button = new Button
        {
            Tag = play is { } kind ? $"{action}.{kind}" : action.ToString(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            MinWidth = 0,
            Padding = play is null ? new Thickness(11, 9, 11, 9) : new Thickness(6, 9, 6, 9),
            Content = play is null
                ? WideContent(icon, title, subtitle, badge)
                : ChipContent(icon, title, subtitle, badge),
        };

        button.Click += OnCareClicked;
        return new CareTile(button, title, subtitle, badge, action, play);
    }

    /// <summary>A care tile: icon, name over its cost or its reason, and a badge on the right.</summary>
    private static FrameworkElement WideContent(
        TextBlock icon,
        TextBlock title,
        TextBlock subtitle,
        TextBlock badge)
    {
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(subtitle);

        var layout = new Grid { ColumnSpacing = 9 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(text, 1);
        Grid.SetColumn(badge, 2);
        layout.Children.Add(icon);
        layout.Children.Add(text);
        layout.Children.Add(badge);
        return layout;
    }

    /// <summary>
    /// A play chip is a third of a row wide, so the same parts are folded into a column.
    /// </summary>
    /// <remarks>
    /// The badge would be a fourth line on a chip this narrow, so it is left out of the tree
    /// entirely and the chip's own colour says whether it is a mission or already spent.
    /// </remarks>
    private static FrameworkElement ChipContent(
        TextBlock icon,
        TextBlock title,
        TextBlock subtitle,
        TextBlock badge)
    {
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.FontSize = 12.5;
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        badge.Visibility = Visibility.Collapsed;

        var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(icon);
        stack.Children.Add(title);
        stack.Children.Add(subtitle);
        return stack;
    }

    private void ShowCareTiles(CompanionSnapshot snapshot)
    {
        var dark = IsDark;

        foreach (var tile in _tiles)
        {
            var state = StateOf(tile, snapshot);
            var surface = CompanionPalette.Tile(state, dark);

            tile.Title.Text = TileName(tile, snapshot);
            tile.Title.Foreground = new SolidColorBrush(surface.Ink);

            tile.Subtitle.Text = state switch
            {
                CompanionTileState.Spent => Strings.Get("Companion_ActionSpent"),
                CompanionTileState.NotNeeded => Strings.Get("Companion_ActionNotNeeded"),
                _ => Strings.Format(
                    "Companion_PointsFormat",
                    CompanionBalanceV2.CarePoints(tile.Action)),
            };
            tile.Subtitle.Foreground = new SolidColorBrush(
                state == CompanionTileState.Mission
                    ? CompanionPalette.Mission(dark)
                    : surface.Subtle);

            tile.Badge.Text = state switch
            {
                CompanionTileState.Mission => "★",
                CompanionTileState.Spent => "✓",
                _ => string.Empty,
            };
            tile.Badge.Foreground = new SolidColorBrush(
                state == CompanionTileState.Mission ? CompanionPalette.Mission(dark) : surface.Subtle);

            Paint(tile.Button, surface, dark);
            tile.Button.IsEnabled = state is CompanionTileState.Ready or CompanionTileState.Mission;

            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                tile.Button,
                $"{tile.Title.Text} · {tile.Subtitle.Text}");
        }
    }

    /// <summary>
    /// Why a tile is or is not available, which the tile then says out loud.
    /// </summary>
    /// <remarks>
    /// A disabled button with no reason is the thing this dashboard did worst: feeding a full pet
    /// and feeding a fed pet looked identical, and both looked like a bug. Spent beats not-needed
    /// because an allowance that is gone stays gone even once the gauge falls again.
    /// </remarks>
    private static CompanionTileState StateOf(CareTile tile, CompanionSnapshot snapshot)
    {
        if (tile.Play is { } kind && snapshot.Today.PlayKinds.Contains(kind))
        {
            return CompanionTileState.Spent;
        }

        if (snapshot.Today.IsDone(tile.Action))
        {
            return CompanionTileState.Spent;
        }

        if (!CompanionCareRules.IsNeeded(tile.Action, snapshot.Needs))
        {
            return CompanionTileState.NotNeeded;
        }

        var mission = snapshot.Mission;
        return mission.FirstCare == tile.Action || mission.SecondCare == tile.Action
            ? CompanionTileState.Mission
            : CompanionTileState.Ready;
    }

    private static string TileName(CareTile tile, CompanionSnapshot snapshot) => tile.Play switch
    {
        { } kind => Strings.Get($"Companion_Play{kind}"),
        _ => CareName(tile.Action, snapshot),
    };

    /// <summary>
    /// Paints one tile, including the states the button template would otherwise repaint itself.
    /// </summary>
    /// <remarks>
    /// The template rebinds background and border from theme resources on every visual state, so a
    /// gold mission tile would turn system grey the moment the pointer touched it. Overriding the
    /// brushes on the button's own resources is what makes hover and press stay in its own colour.
    /// </remarks>
    private static void Paint(Button button, CompanionTileSurface surface, bool dark)
    {
        var fill = new SolidColorBrush(surface.Fill);
        var border = new SolidColorBrush(surface.Border);

        button.Background = fill;
        button.BorderBrush = border;
        button.Resources["ButtonBackground"] = fill;
        button.Resources["ButtonBackgroundDisabled"] = fill;
        button.Resources["ButtonBackgroundPointerOver"] =
            new SolidColorBrush(CompanionPalette.Lift(surface.Fill, dark));
        button.Resources["ButtonBackgroundPressed"] =
            new SolidColorBrush(CompanionPalette.Press(surface.Fill, dark));
        button.Resources["ButtonBorderBrush"] = border;
        button.Resources["ButtonBorderBrushDisabled"] = border;
        button.Resources["ButtonBorderBrushPointerOver"] = border;
        button.Resources["ButtonBorderBrushPressed"] = border;
    }

    /// <summary>Every colour that does not depend on the snapshot.</summary>
    private void ApplyPalette()
    {
        var dark = IsDark;

        Board.Background = new SolidColorBrush(CompanionPalette.Board(dark));

        var (from, to) = CompanionPalette.Hero(dark);
        HeroCard.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = from, Offset = 0 },
                new GradientStop { Color = to, Offset = 1 },
            },
        };

        var heroInk = new SolidColorBrush(CompanionPalette.HeroInk);
        var heroSubtle = new SolidColorBrush(CompanionPalette.HeroSubtle);
        var heroWell = new SolidColorBrush(CompanionPalette.HeroWell);

        AvatarWell.Background = heroWell;
        SpeechBubble.Background = heroWell;
        StagePill.Background = heroWell;
        CompanionName.Foreground = heroInk;
        StageLabel.Foreground = heroInk;
        MoodLabel.Foreground = heroInk;
        RecentReason.Foreground = heroSubtle;
        ExperienceLabel.Foreground = heroInk;
        ExperienceValue.Foreground = heroSubtle;
        CareDaysLabel.Foreground = heroSubtle;
        ExperienceTrack.Background = new SolidColorBrush(CompanionPalette.HeroTrack);
        ExperienceBar.Background = heroInk;

        // On the gradient, so they are painted out of the hero's own whites rather than the card
        // brushes the rest of the window uses.
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var pressed = new SolidColorBrush(Windows.UI.Color.FromArgb(0x52, 0xFF, 0xFF, 0xFF));

        foreach (var button in new[] { GrowthGuideButton, SettingsButton, RefreshButton })
        {
            button.Background = clear;
            button.Foreground = heroInk;
            button.Resources["ButtonBackground"] = clear;
            button.Resources["ButtonBackgroundPointerOver"] = heroWell;
            button.Resources["ButtonBackgroundPressed"] = pressed;
            button.Resources["ButtonBackgroundDisabled"] = clear;
            button.Resources["ButtonForeground"] = heroInk;
            button.Resources["ButtonForegroundPointerOver"] = heroInk;
            button.Resources["ButtonForegroundPressed"] = heroInk;
            button.Resources["ButtonForegroundDisabled"] = heroSubtle;
        }

        var card = new SolidColorBrush(CompanionPalette.Card(dark));
        var cardBorder = new SolidColorBrush(CompanionPalette.CardBorder(dark));
        var ink = new SolidColorBrush(CompanionPalette.Ink(dark));
        var subtle = new SolidColorBrush(CompanionPalette.Subtle(dark));
        var track = new SolidColorBrush(CompanionPalette.Track(dark));

        foreach (var panel in new[] { TodayCard, NeedsCard, TraitCard })
        {
            panel.Background = card;
            panel.BorderBrush = cardBorder;
        }

        foreach (var header in new[] { TodayHeader, NeedsHeader, TraitHeader })
        {
            header.Foreground = ink;
        }

        foreach (var label in new[]
                 {
                     FullnessLabel, CleanlinessLabel, MoodNeedLabel, BondLabel,
                     FullnessValue, CleanlinessValue, MoodValue, BondValue,
                     DayScoreLabel, AppScoreCaption, CareScoreCaption, CareHeader, PlayHeader, CareStatus,
                     CuriosityLabel, InsightLabel, ReliabilityLabel,
                 })
        {
            label.Foreground = subtle;
        }

        foreach (var value in new[] { DayScoreValue, CuriosityValue, InsightValue, ReliabilityValue })
        {
            value.Foreground = ink;
        }

        foreach (var bar in new[]
                 {
                     FullnessTrack, CleanlinessTrack, MoodTrack, BondTrack,
                     AppScoreTrack, CareScoreTrack,
                 })
        {
            bar.Background = track;
        }

        var chip = new SolidColorBrush(CompanionPalette.Tile(CompanionTileState.Spent, dark).Fill);
        foreach (var stat in new[] { CuriosityChip, InsightChip, ReliabilityChip })
        {
            stat.Background = chip;
        }

        SuggestionCard.Background = card;
        SuggestionCard.BorderBrush = new SolidColorBrush(CompanionPalette.Mission(dark));
        SuggestionText.Foreground = ink;
    }

    /// <summary>Sets a bar's fill by weighting two columns, which needs no template to restyle.</summary>
    private static void SetBar(ColumnDefinition fill, ColumnDefinition rest, double fraction)
    {
        var clamped = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1);
        fill.Width = new GridLength(clamped, GridUnitType.Star);
        rest.Width = new GridLength(1 - clamped, GridUnitType.Star);
    }

    private bool IsDark =>
        Board.ActualTheme == ElementTheme.Dark
        || (Board.ActualTheme == ElementTheme.Default
            && Application.Current.RequestedTheme == ApplicationTheme.Dark);

    private static string CareName(CompanionCareAction action, CompanionSnapshot snapshot) =>
        action == CompanionCareAction.SpecialCare
            ? Strings.Get($"Companion_SpecialCare{CompanionSpecialCareCatalog.For(SelectedPet(snapshot))}")
            : Strings.Get($"Companion_Care{action}");

    private static CompanionPetKind SelectedPet(CompanionSnapshot snapshot) =>
        CompanionPetCatalog.FromAssetKey(snapshot.Profile.AppearanceKey);

    private static string Count(int value) =>
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
            // The refreshed snapshot decides which tiles come back, so re-enable from state.
            ShowCareTiles(_snapshot);
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
        foreach (var tile in _tiles)
        {
            tile.Button.IsEnabled = enabled;
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
