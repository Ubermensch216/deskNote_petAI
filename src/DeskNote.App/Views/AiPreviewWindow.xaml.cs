using DeskNote.App.Services;
using DeskNote.Core.Ai;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>
/// Shows what the model proposes next to what the note says now, and applies it only if asked.
/// </summary>
/// <remarks>
/// <para>
/// The window opens the moment an action starts rather than when the answer arrives. On CPU
/// inference an action takes tens of seconds, and a menu that appears to do nothing for half a
/// minute is indistinguishable from one that is broken — so the wait, and the way out of it, are
/// both on screen from the first frame.
/// </para>
/// <para>
/// Model output never reaches the note by itself (report p7). Until <see cref="Applied"/> is
/// raised the note is untouched, and closing this window is a complete answer of "no".
/// </para>
/// </remarks>
public sealed partial class AiPreviewWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private string? _proposed;

    public AiPreviewWindow(string actionName)
    {
        InitializeComponent();

        AppWindow.Title = Strings.Format("Ai_PreviewTitleFormat", actionName);
        AppIcon.Apply(this);
        ActionTitle.Text = actionName;
        Attribution.Text = Strings.Get("Ai_LocalOnly");
        RunningLabel.Text = Strings.Get("Ai_Running");
        OriginalCaption.Text = Strings.Get("Ai_Original");
        ProposedCaption.Text = Strings.Get("Ai_Proposed");
        CancelButton.Content = Strings.Get("Ai_Cancel");
        ApplyButton.Content = Strings.Get("Ai_Apply");

        AppWindow.Resize(new SizeInt32(760, 520));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.PreferredMinimumWidth = 560;
            presenter.PreferredMinimumHeight = 380;
        }

        Closed += (_, _) => _cancellation.Cancel();
    }

    /// <summary>Raised when the user accepts the proposal. Carries the text to write into the note.</summary>
    public event EventHandler<string>? Applied;

    /// <summary>Cancelled when the window closes, so a run nobody is waiting for stops costing CPU.</summary>
    public CancellationToken CancellationToken => _cancellation.Token;

    /// <summary>Places the preview over the note it belongs to, without covering it entirely.</summary>
    public void PlaceNear(AppWindow owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var x = owner.Position.X + (owner.Size.Width / 2) - (AppWindow.Size.Width / 2);
        var y = owner.Position.Y + (owner.Size.Height / 2) - (AppWindow.Size.Height / 2);

        AppWindow.Move(new PointInt32(x, y));
    }

    public void ShowResult(AiTextResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _proposed = result.Proposed;

        RunningPanel.Visibility = Visibility.Collapsed;
        Progress.IsActive = false;
        ComparePanel.Visibility = Visibility.Visible;

        OriginalText.Text = result.Original;
        ProposedText.Text = result.Proposed;

        // Naming the model is not decoration: it is what keeps generated text from being mistaken
        // for the user's own writing once it is sitting in the note (report p7).
        Attribution.Text = Strings.Format("Ai_ModelFormat", result.ModelId ?? string.Empty);
        Elapsed.Text = Strings.Format("Ai_ElapsedFormat", result.Elapsed.TotalSeconds.ToString("F1"));

        ApplyButton.IsEnabled = result.Proposed.Length > 0;
        CancelButton.Content = Strings.Get("Ai_Discard");
    }

    /// <summary>Reports a failure in place, so the user can read what happened before closing.</summary>
    public void ShowFailure(string message)
    {
        RunningPanel.Visibility = Visibility.Collapsed;
        Progress.IsActive = false;
        ComparePanel.Visibility = Visibility.Collapsed;

        Failure.Title = Strings.Get("Ai_FailedTitle");
        Failure.Message = message;
        Failure.IsOpen = true;

        ApplyButton.IsEnabled = false;
        CancelButton.Content = Strings.Get("Ai_Close");
    }

    private void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        if (_proposed is null)
        {
            return;
        }

        // The window stays open until the note confirms the write. If the note moved on while the
        // model was thinking, the subscriber says so here rather than silently dropping the text.
        ApplyButton.IsEnabled = false;
        Applied?.Invoke(this, _proposed);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Close();
}
