using DeskNote.App.Services;
using DeskNote.Companion.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>
/// Plain-language instructions for raising the currently selected pet.
/// </summary>
/// <remarks>
/// Every number on this page is read out of <see cref="CompanionBalanceV2"/> and
/// <see cref="CompanionGrowthLadder"/> rather than written into the resource files. A guide that
/// restates the balance in prose goes stale the first time the balance is tuned, and a user who
/// followed a stale guide has been lied to about what their effort was worth.
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
        AppWindow.Resize(new SizeInt32(640, 820));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 540;
            presenter.PreferredMinimumHeight = 560;
        }

        PageTitle.Text = Strings.Get("PetGrowthGuide_Title");
        Introduction.Text = Strings.Get("PetGrowthGuide_Introduction");
        AppBudgetLabel.Text = Strings.Get("Companion_AppScoreLabel");
        CareBudgetLabel.Text = Strings.Get("Companion_CareScoreLabel");
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
        TipsHeader.Text = Strings.Get("PetGrowthGuide_TipsHeader");
        CloseButton.Content = Strings.Get("Ai_Close");

        BuildStages();
        BuildAppActivities();
        BuildCareActions();
        BuildTips();
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

    private void BuildStages()
    {
        foreach (var rung in CompanionGrowthLadder.Rungs)
        {
            var requirement = rung.Stage == CompanionGrowthLadder.FirstStage
                ? Strings.Get("PetGrowthGuide_StageStart")
                : rung.CareDays > 0
                    ? Strings.Format(
                        "PetGrowthGuide_StageRequirementFormat",
                        rung.Experience,
                        rung.CareDays)
                    : Strings.Format("PetGrowthGuide_StageExperienceOnlyFormat", rung.Experience);

            StageRows.Children.Add(TwoColumnRow(
                Strings.Format(
                    "Companion_StageFormat",
                    rung.Stage,
                    Strings.Get($"Companion_StageName{rung.Stage}")),
                requirement,
                emphasise: rung.Stage == CompanionGrowthLadder.FinalStage));
        }
    }

    private void BuildAppActivities()
    {
        var index = 1;
        foreach (var category in Enum.GetValues<AppScoreCategory>())
        {
            AppRows.Children.Add(NumberedRow(
                index++,
                Strings.Get($"Companion_Task{category}"),
                Strings.Get($"PetGrowthGuide_Task{category}Description"),
                Strings.Format(
                    "PetGrowthGuide_ScoreLimitFormat",
                    CompanionBalanceV2.AppPoints(category),
                    CompanionBalanceV2.AppDailyLimit(category))));
        }
    }

    private void BuildCareActions()
    {
        var index = 1;
        foreach (var action in Enum.GetValues<CompanionCareAction>())
        {
            var name = action == CompanionCareAction.SpecialCare
                ? Strings.Get($"Companion_SpecialCare{CompanionSpecialCareCatalog.For(_pet)}")
                : Strings.Get($"Companion_Care{action}");
            var score = Strings.Format(
                "PetGrowthGuide_ScoreLimitFormat",
                CompanionBalanceV2.CarePoints(action),
                CompanionBalanceV2.CareDailyLimit(action));
            if (action == CompanionCareAction.Play)
            {
                score += Strings.Format(
                    "PetGrowthGuide_PlayBonusFormat",
                    CompanionBalanceV2.DifferentPlayBonus);
            }

            CareRows.Children.Add(NumberedRow(
                index++,
                name,
                Strings.Get($"PetGrowthGuide_Care{action}Description"),
                score));
        }
    }

    private void BuildTips()
    {
        foreach (var key in new[]
                 {
                     "PetGrowthGuide_TipSelectedPet",
                     "PetGrowthGuide_TipNeedState",
                     "PetGrowthGuide_TipPassive",
                     "PetGrowthGuide_TipNoPenalty",
                     "PetGrowthGuide_TipDragon",
                 })
        {
            TipRows.Children.Add(BulletRow(Strings.Get(key)));
        }
    }

    private static Grid TwoColumnRow(string name, string value, bool emphasise)
    {
        var row = new Grid { ColumnSpacing = 20 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new TextBlock
        {
            Text = name,
            FontWeight = emphasise ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        };
        var right = new TextBlock
        {
            Text = value,
            Opacity = emphasise ? 1 : 0.7,
            TextAlignment = TextAlignment.Right,
            FontWeight = emphasise ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        };
        Grid.SetColumn(right, 1);
        row.Children.Add(left);
        row.Children.Add(right);
        return row;
    }

    private static Grid NumberedRow(int number, string title, string description, string score)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            VerticalAlignment = VerticalAlignment.Top,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorSecondaryBrush"],
            Child = new TextBlock
            {
                Text = number.ToString(System.Globalization.CultureInfo.CurrentCulture),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            },
        };

        var text = new StackPanel { Spacing = 3 };
        text.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = score, Opacity = 0.72, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(text, 1);

        row.Children.Add(badge);
        row.Children.Add(text);
        return row;
    }

    private static Grid BulletRow(string text)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bullet = new TextBlock { Text = "•" };
        var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(body, 1);
        row.Children.Add(bullet);
        row.Children.Add(body);
        return row;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
