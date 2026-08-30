using DeskNote.App.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>One thing the model proposes, with the detail a user needs to judge it.</summary>
/// <param name="Label">The proposal itself: a task title, a tag name.</param>
/// <param name="Detail">Supporting text shown under the label, e.g. a due date and an owner.</param>
public sealed record AiProposal(string Label, string? Detail = null);

/// <summary>
/// Confirms structured proposals — extracted tasks, suggested tags — before any of them lands.
/// </summary>
/// <remarks>
/// <para>
/// The difference between this and the text preview is that a structured proposal is a list of
/// independent claims: the model may be right about three tasks and wrong about the fourth.
/// So each row is its own checkbox rather than the whole answer being one accept-or-discard.
/// </para>
/// <para>
/// Everything starts checked. The model was asked for things that are actually in the note, and
/// making the user tick four boxes to accept a correct answer would cost more than it protects.
/// </para>
/// </remarks>
public sealed partial class AiProposalWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<CheckBox> _rows = [];

    public AiProposalWindow(string actionName)
    {
        InitializeComponent();

        AppWindow.Title = Strings.Format("Ai_PreviewTitleFormat", actionName);
        ActionTitle.Text = actionName;
        Attribution.Text = Strings.Get("Ai_LocalOnly");
        RunningLabel.Text = Strings.Get("Ai_Running");
        CancelButton.Content = Strings.Get("Ai_Cancel");
        ApplyButton.Content = Strings.Get("Ai_Apply");

        AppWindow.Resize(new SizeInt32(520, 480));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.PreferredMinimumWidth = 420;
            presenter.PreferredMinimumHeight = 320;
        }

        Closed += (_, _) => _cancellation.Cancel();
    }

    /// <summary>Raised with the indexes the user kept, in the order they were proposed.</summary>
    public event EventHandler<IReadOnlyList<int>>? Applied;

    public CancellationToken CancellationToken => _cancellation.Token;

    /// <summary>Places the window over the note it belongs to.</summary>
    public void PlaceNear(AppWindow owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var x = owner.Position.X + (owner.Size.Width / 2) - (AppWindow.Size.Width / 2);
        var y = owner.Position.Y + (owner.Size.Height / 2) - (AppWindow.Size.Height / 2);

        AppWindow.Move(new PointInt32(x, y));
    }

    public void ShowProposals(IReadOnlyList<AiProposal> proposals, string? modelId, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(proposals);

        StopRunning();

        if (proposals.Count == 0)
        {
            // "The model found nothing" is an answer, not a failure: a note with no deadlines in it
            // should produce no tasks, and saying so is more useful than an empty list.
            Notice.Title = Strings.Get("Ai_NothingFoundTitle");
            Notice.Message = Strings.Get("Ai_NothingFound");
            Notice.Severity = InfoBarSeverity.Informational;
            Notice.IsOpen = true;
            CancelButton.Content = Strings.Get("Ai_Close");
            return;
        }

        foreach (var proposal in proposals)
        {
            var row = new CheckBox { IsChecked = true, MinWidth = 0 };

            var stack = new StackPanel { Spacing = 1 };
            stack.Children.Add(new TextBlock { Text = proposal.Label, TextWrapping = TextWrapping.Wrap });

            if (!string.IsNullOrWhiteSpace(proposal.Detail))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = proposal.Detail,
                    Opacity = 0.65,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            row.Content = stack;
            row.Checked += (_, _) => UpdateApplyState();
            row.Unchecked += (_, _) => UpdateApplyState();

            _rows.Add(row);
            ProposalList.Children.Add(row);
        }

        ProposalScroll.Visibility = Visibility.Visible;
        Attribution.Text = Strings.Format("Ai_ModelFormat", modelId ?? string.Empty);
        Elapsed.Text = Strings.Format("Ai_ElapsedFormat", elapsed.TotalSeconds.ToString("F1"));
        CancelButton.Content = Strings.Get("Ai_Discard");

        UpdateApplyState();
    }

    public void ShowFailure(string message)
    {
        StopRunning();

        Notice.Title = Strings.Get("Ai_FailedTitle");
        Notice.Message = message;
        Notice.Severity = InfoBarSeverity.Warning;
        Notice.IsOpen = true;

        ApplyButton.IsEnabled = false;
        CancelButton.Content = Strings.Get("Ai_Close");
    }

    private void StopRunning()
    {
        RunningPanel.Visibility = Visibility.Collapsed;
        Progress.IsActive = false;
    }

    /// <summary>Applying nothing is not an action; unchecking every row is the same as cancelling.</summary>
    private void UpdateApplyState() =>
        ApplyButton.IsEnabled = _rows.Any(row => row.IsChecked == true);

    private void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        var selected = _rows
            .Select((row, index) => (row, index))
            .Where(pair => pair.row.IsChecked == true)
            .Select(pair => pair.index)
            .ToList();

        if (selected.Count == 0)
        {
            return;
        }

        ApplyButton.IsEnabled = false;
        Applied?.Invoke(this, selected);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Close();
}
