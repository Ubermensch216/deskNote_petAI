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
/// The manual for raising the currently selected pet.
/// </summary>
/// <remarks>
/// <para>
/// Every number on this page is read out of <see cref="CompanionBalanceV2"/> and
/// <see cref="CompanionGrowthLadder"/> rather than written into the resource files. A guide that
/// restates the balance in prose goes stale the first time the balance is tuned, and a user who
/// followed a stale guide has been lied to about what their effort was worth.
/// </para>
/// <para>
/// It is drawn in the dashboard's own language — the same hero band, indigo for app work, teal for
/// care, gold for what you are aiming at — because a user arrives here from the pet window, and a
/// help page in system greys reads as a different application. Where the page used to state a
/// number it now shows one: the day's budget is a 30:70 bar, the five stages are five stops on a
/// rail, and each activity's points sit in their own column instead of in a third line of grey
/// text under the description.
/// </para>
/// </remarks>
public sealed partial class PetGrowthGuideWindow : Window
{
    private readonly CompanionPetKind _pet;

    public PetGrowthGuideWindow(CompanionPetKind pet)
    {
        InitializeComponent();
        _pet = pet;

        AppWindow.Title = Strings.Get("PetGrowthGuide_WindowTitle");
        AppIcon.Apply(this);
        AppWindow.Resize(new SizeInt32(660, 860));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 560;
            presenter.PreferredMinimumHeight = 560;
        }

        PageTitle.Text = Strings.Get("PetGrowthGuide_Title");
        Lede.Text = Strings.Get("PetGrowthGuide_Lede");
        BudgetHeader.Text = Strings.Format(
            "PetGrowthGuide_DayTotalFormat",
            CompanionBalanceV2.DailyMaximum);
        AppBudgetLabel.Text = Strings.Get("PetGrowthGuide_AppShort");
        CareBudgetLabel.Text = Strings.Get("PetGrowthGuide_CareShort");
        AppBudgetValue.Text = Strings.Format(
            "PetGrowthGuide_PointsPerDayFormat",
            CompanionBalanceV2.DailyAppMaximum);
        CareBudgetValue.Text = Strings.Format(
            "PetGrowthGuide_PointsPerDayFormat",
            CompanionBalanceV2.DailyCareMaximum);
        StagesHeader.Text = Strings.Get("PetGrowthGuide_StagesHeader");
        CareDayNote.Text = Strings.Format(
            "PetGrowthGuide_CareDayNoteFormat",
            CompanionBalanceV2.CareDayThreshold,
            CompanionBalanceV2.DailyCareMaximum);
        AppHeader.Text = Strings.Get("PetGrowthGuide_AppHeader");
        CareHeader.Text = Strings.Get("PetGrowthGuide_CareHeader");
        AppHeaderChipText.Text = Strings.Format(
            "PetGrowthGuide_PointsPerDayFormat",
            CompanionBalanceV2.DailyAppMaximum);
        CareHeaderChipText.Text = Strings.Format(
            "PetGrowthGuide_PointsPerDayFormat",
            CompanionBalanceV2.DailyCareMaximum);
        TipsHeader.Text = Strings.Get("PetGrowthGuide_TipsHeader");
        CloseButton.Content = Strings.Get("Ai_Close");

        AvatarSpriteStrip.Source = new BitmapImage(
            new Uri($"ms-appx:///Assets/Companion/{_pet.AssetKey()}-walk.png"));

        Render();

        // Every row is painted at build time, so a theme switch rebuilds them. There is no state
        // on this page to lose by doing that.
        Board.ActualThemeChanged += (_, _) => Render();
    }

    public void PlaceNear(AppWindow source)
    {
        var display = DisplayArea.GetFromWindowId(source.Id, DisplayAreaFallback.Nearest);
        var area = display.WorkArea;
        var x = Math.Clamp(
            source.Position.X - AppWindow.Size.Width - 12,
            area.X,
            Math.Max(area.X, area.X + area.Width - AppWindow.Size.Width));
        var y = Math.Clamp(
            source.Position.Y,
            area.Y,
            Math.Max(area.Y, area.Y + area.Height - AppWindow.Size.Height));
        AppWindow.Move(new PointInt32(x, y));
    }

    private bool IsDark =>
        Board.ActualTheme == ElementTheme.Dark
        || (Board.ActualTheme == ElementTheme.Default
            && Application.Current.RequestedTheme == ApplicationTheme.Dark);

    private void Render()
    {
        ApplyPalette();
        BuildStages();
        BuildAppActivities();
        BuildCareActions();
        BuildTips();
    }

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

        AvatarWell.Background = new SolidColorBrush(CompanionPalette.HeroWell);
        PageTitle.Foreground = new SolidColorBrush(CompanionPalette.HeroInk);
        Lede.Foreground = new SolidColorBrush(CompanionPalette.HeroSubtle);

        var card = new SolidColorBrush(CompanionPalette.Card(dark));
        var cardBorder = new SolidColorBrush(CompanionPalette.CardBorder(dark));
        var ink = new SolidColorBrush(CompanionPalette.Ink(dark));
        var subtle = new SolidColorBrush(CompanionPalette.Subtle(dark));
        var app = CompanionPalette.AppBudget(dark);
        var care = CompanionPalette.CareBudget(dark);

        foreach (var panel in new[] { BudgetCard, StagesCard, AppCard, CareCard, TipsCard })
        {
            panel.Background = card;
            panel.BorderBrush = cardBorder;
        }

        foreach (var header in new[]
                 {
                     BudgetHeader, StagesHeader, AppHeader, CareHeader, TipsHeader,
                     AppBudgetValue, CareBudgetValue,
                 })
        {
            header.Foreground = ink;
        }

        foreach (var label in new[] { AppBudgetLabel, CareBudgetLabel })
        {
            label.Foreground = subtle;
        }

        AppBudgetBar.Background = new SolidColorBrush(app);
        CareBudgetBar.Background = new SolidColorBrush(care);
        AppAccent.Background = new SolidColorBrush(app);
        CareAccent.Background = new SolidColorBrush(care);

        AppHeaderChip.Background = new SolidColorBrush(Wash(app, dark));
        AppHeaderChipText.Foreground = new SolidColorBrush(app);
        CareHeaderChip.Background = new SolidColorBrush(Wash(care, dark));
        CareHeaderChipText.Foreground = new SolidColorBrush(care);

        StageRail.Background = new SolidColorBrush(CompanionPalette.Track(dark));

        var note = CompanionPalette.Tile(CompanionTileState.Mission, dark);
        CareDayNoteBox.Background = new SolidColorBrush(note.Fill);
        CareDayNote.Foreground = new SolidColorBrush(note.Ink);
    }

    /// <summary>
    /// The five stages as five stops on one rail.
    /// </summary>
    /// <remarks>
    /// The requirements were five rows of "3단계 · 성장기 | 경험치 600 · 돌봄 7일", which is a table
    /// asking to be read. Drawn, the distance between where the pet is and what it becomes is the
    /// thing you see first, and the final stop is gold because it is what the page is selling.
    /// </remarks>
    private void BuildStages()
    {
        var dark = IsDark;
        var ink = new SolidColorBrush(CompanionPalette.Ink(dark));
        var subtle = new SolidColorBrush(CompanionPalette.Subtle(dark));
        var stop = CompanionPalette.AppBudget(dark);
        var goal = CompanionPalette.Mission(dark);

        // The rail is the first child and stays there; the stops are rebuilt on top of it.
        while (StageTrack.Children.Count > 1)
        {
            StageTrack.Children.RemoveAt(StageTrack.Children.Count - 1);
        }

        foreach (var rung in CompanionGrowthLadder.Rungs)
        {
            var final = rung.Stage == CompanionGrowthLadder.FinalStage;

            var node = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(final ? goal : stop),
                Child = new TextBlock
                {
                    Text = rung.Stage.ToString(CultureInfo.CurrentCulture),
                    FontSize = 15,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var name = new TextBlock
            {
                Text = Strings.Get($"Companion_StageName{rung.Stage}"),
                FontSize = 12.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = final ? new SolidColorBrush(goal) : ink,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };

            var stack = new StackPanel { Spacing = 5 };
            stack.Children.Add(node);
            stack.Children.Add(name);

            foreach (var line in Requirements(rung))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = line,
                    FontSize = 11,
                    Foreground = subtle,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                });
            }

            Grid.SetColumn(stack, rung.Stage - CompanionGrowthLadder.FirstStage);
            StageTrack.Children.Add(stack);
        }
    }

    /// <summary>What a rung costs, one short line each so it fits under a stop.</summary>
    private static IEnumerable<string> Requirements(GrowthRung rung)
    {
        if (rung.Stage == CompanionGrowthLadder.FirstStage)
        {
            yield return Strings.Get("PetGrowthGuide_StageStart");
            yield break;
        }

        yield return Strings.Format("PetGrowthGuide_StageExperienceOnlyFormat", rung.Experience);

        if (rung.CareDays > 0)
        {
            yield return Strings.Format("PetGrowthGuide_StageCareDaysFormat", rung.CareDays);
        }
    }

    private void BuildAppActivities()
    {
        var accent = CompanionPalette.AppBudget(IsDark);
        AppRows.Children.Clear();

        foreach (var category in Enum.GetValues<AppScoreCategory>())
        {
            if (AppRows.Children.Count > 0)
            {
                AppRows.Children.Add(Separator());
            }

            AppRows.Children.Add(ActivityRow(
                Icon(category),
                Strings.Get($"Companion_Task{category}"),
                Strings.Get($"PetGrowthGuide_Task{category}Description"),
                CompanionBalanceV2.AppPoints(category),
                CompanionBalanceV2.AppDailyLimit(category),
                bonus: null,
                accent));
        }
    }

    private void BuildCareActions()
    {
        var accent = CompanionPalette.CareBudget(IsDark);
        CareRows.Children.Clear();

        foreach (var action in Enum.GetValues<CompanionCareAction>())
        {
            if (CareRows.Children.Count > 0)
            {
                CareRows.Children.Add(Separator());
            }

            var name = action == CompanionCareAction.SpecialCare
                ? Strings.Get($"Companion_SpecialCare{CompanionSpecialCareCatalog.For(_pet)}")
                : Strings.Get($"Companion_Care{action}");

            var bonus = action == CompanionCareAction.Play
                ? Strings.Format("PetGrowthGuide_PlayBonusFormat", CompanionBalanceV2.DifferentPlayBonus)
                : null;

            CareRows.Children.Add(ActivityRow(
                Icon(action),
                name,
                Strings.Get($"PetGrowthGuide_Care{action}Description"),
                CompanionBalanceV2.CarePoints(action),
                CompanionBalanceV2.CareDailyLimit(action),
                bonus,
                accent));
        }
    }

    /// <summary>
    /// One earning: what it is, what it takes, and what it pays.
    /// </summary>
    /// <remarks>
    /// The points used to be a third line of grey text under the description, which is where a
    /// reader stops looking. In their own right-hand column they can be compared down the card,
    /// which is the question this page is actually asked: what is worth doing.
    /// </remarks>
    private Grid ActivityRow(
        string emoji,
        string title,
        string description,
        int points,
        int dailyLimit,
        string? bonus,
        Color accent)
    {
        var dark = IsDark;
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 11, 0, 11) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(11),
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Wash(accent, dark)),
            Child = new TextBlock
            {
                Text = emoji,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var text = new StackPanel { Spacing = 3 };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(CompanionPalette.Ink(dark)),
        });
        text.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(CompanionPalette.Subtle(dark)),
        });

        if (bonus is not null)
        {
            text.Children.Add(new TextBlock
            {
                Text = bonus,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(CompanionPalette.Mission(dark)),
            });
        }

        var score = new StackPanel { Spacing = 1, MinWidth = 72 };
        score.Children.Add(new TextBlock
        {
            Text = Strings.Format("Companion_PointsFormat", points),
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Right,
            Foreground = new SolidColorBrush(accent),
        });
        score.Children.Add(new TextBlock
        {
            Text = Strings.Format("PetGrowthGuide_DailyLimitFormat", dailyLimit),
            FontSize = 11,
            TextAlignment = TextAlignment.Right,
            Foreground = new SolidColorBrush(CompanionPalette.Subtle(dark)),
        });

        Grid.SetColumn(text, 1);
        Grid.SetColumn(score, 2);
        row.Children.Add(icon);
        row.Children.Add(text);
        row.Children.Add(score);
        return row;
    }

    private Border Separator() => new()
    {
        Height = 1,
        Background = new SolidColorBrush(CompanionPalette.CardBorder(IsDark)),
    };

    private void BuildTips()
    {
        var dark = IsDark;
        TipRows.Children.Clear();

        foreach (var key in new[]
                 {
                     "PetGrowthGuide_TipSelectedPet",
                     "PetGrowthGuide_TipNeedState",
                     "PetGrowthGuide_TipPassive",
                     "PetGrowthGuide_TipNoPenalty",
                     "PetGrowthGuide_TipDragon",
                 })
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 6, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(
                    key == "PetGrowthGuide_TipDragon"
                        ? CompanionPalette.Mission(dark)
                        : CompanionPalette.CareBudget(dark)),
            };

            var body = new TextBlock
            {
                Text = Strings.Get(key),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(CompanionPalette.Subtle(dark)),
            };

            Grid.SetColumn(body, 1);
            row.Children.Add(dot);
            row.Children.Add(body);
            TipRows.Children.Add(row);
        }
    }

    /// <summary>The accent at card strength: enough to tint a chip, not enough to shout.</summary>
    private static Color Wash(Color accent, bool dark) =>
        Color.FromArgb((byte)(dark ? 0x33 : 0x1F), accent.R, accent.G, accent.B);

    private static string Icon(AppScoreCategory category) => category switch
    {
        AppScoreCategory.Capture => "\U0001F4DD",
        AppScoreCategory.Refine => "✏️",
        AppScoreCategory.Organize => "\U0001F5C2️",
        AppScoreCategory.Resolve => "✅",
        AppScoreCategory.Reuse => "\U0001F50D",
        _ => "\U0001F916",
    };

    private static string Icon(CompanionCareAction action) => action switch
    {
        CompanionCareAction.Feed => "\U0001F35A",
        CompanionCareAction.BasicCare => "\U0001F6C1",
        CompanionCareAction.SpecialCare => "✨",
        CompanionCareAction.Play => "\U0001F3BE",
        CompanionCareAction.Greeting => "\U0001F44B",
        _ => "\U0001F634",
    };

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
