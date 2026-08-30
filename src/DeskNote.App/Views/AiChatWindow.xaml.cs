using DeskNote.App.Services;
using DeskNote.Core.Ai;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>
/// 메모 Q&amp;A: asks a question against one note or the whole library and streams the answer.
/// </summary>
/// <remarks>
/// <para>
/// Streamed rather than delivered whole. The first token arrives long before the last one, and on
/// CPU inference that difference is the difference between an app that is thinking and an app that
/// looks hung.
/// </para>
/// <para>
/// The scope is stated on screen because it changes what the answer means: "이 메모" can only be
/// wrong about one note, while a library-wide question reads whatever the retriever matched.
/// </para>
/// </remarks>
public sealed partial class AiChatWindow : Window
{
    private readonly LocalAiHost _ai;
    private readonly Guid? _noteId;

    private CancellationTokenSource? _answering;

    public AiChatWindow(LocalAiHost ai, Guid? noteId, string? noteTitle)
    {
        ArgumentNullException.ThrowIfNull(ai);

        InitializeComponent();

        _ai = ai;
        _noteId = noteId;

        AppWindow.Title = Strings.Format("Ai_PreviewTitleFormat", Strings.Get("Ai_AskTitle"));
        ActionTitle.Text = Strings.Get("Ai_AskTitle");
        ScopeLabel.Text = noteId is null || string.IsNullOrWhiteSpace(noteTitle)
            ? Strings.Get("Ai_ScopeLibrary")
            : Strings.Format("Ai_ScopeNoteFormat", noteTitle);

        QuestionBox.PlaceholderText = Strings.Get("Ai_AskPlaceholder");
        AskButton.Content = Strings.Get("Ai_Ask");
        CloseButton.Content = Strings.Get("Ai_Close");

        AppWindow.Resize(new SizeInt32(620, 520));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 460;
            presenter.PreferredMinimumHeight = 360;
        }

        Activated += OnFirstActivated;
        Closed += (_, _) => _answering?.Cancel();
    }

    /// <summary>Places the sidecar beside the note it was opened from, rather than on top of it.</summary>
    public void PlaceNear(AppWindow owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        AppWindow.Move(new PointInt32(
            owner.Position.X + owner.Size.Width + 12,
            owner.Position.Y));
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        _ = QuestionBox.Focus(FocusState.Programmatic);
    }

    private void OnQuestionKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            Ask();
        }
    }

    private void OnAskClicked(object sender, RoutedEventArgs e) => Ask();

    private async void Ask()
    {
        var question = QuestionBox.Text.Trim();

        if (question.Length == 0 || _answering is not null)
        {
            return;
        }

        if (!_ai.IsAvailable)
        {
            Status.Text = Strings.Get($"Note_AiUnavailable{_ai.Capability.Availability}");
            return;
        }

        _answering = new CancellationTokenSource();

        AnswerText.Text = string.Empty;
        Progress.IsActive = true;
        Progress.Visibility = Visibility.Visible;
        AskButton.IsEnabled = false;
        Status.Text = Strings.Get("Ai_Running");

        var query = new AiQuery
        {
            Question = question,
            ScopedNoteIds = _noteId is { } id ? [id] : [],
            LanguageTag = Strings.OverrideLocale ?? "ko-KR",
        };

        try
        {
            await foreach (var chunk in _ai.Service.StreamAnswerAsync(query, _answering.Token))
            {
                // Appending as tokens arrive is the point of streaming; the ring goes away with
                // the first one because by then the answer itself is the progress indicator.
                AnswerText.Text += chunk;
                Progress.Visibility = Visibility.Collapsed;
                AnswerScroll.ChangeView(null, AnswerScroll.ScrollableHeight, null, disableAnimation: true);
            }

            Status.Text = Strings.Format("Ai_ModelFormat", _ai.Capability.ModelId ?? string.Empty);
        }
        catch (OperationCanceledException)
        {
            // The window is closing, or a new question replaced this one.
        }
        catch (AiUnavailableException ex)
        {
            Status.Text = Strings.Get($"Note_AiUnavailable{ex.Reason}");
        }
        catch (Exception ex)
        {
            CrashLog.Write("AI question failed", ex);
            Status.Text = ex.Message;
        }
        finally
        {
            Progress.IsActive = false;
            Progress.Visibility = Visibility.Collapsed;
            AskButton.IsEnabled = true;

            _answering?.Dispose();
            _answering = null;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
