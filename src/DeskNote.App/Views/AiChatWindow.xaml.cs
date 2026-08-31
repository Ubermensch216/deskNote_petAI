using DeskNote.Ai;
using DeskNote.App.Services;
using DeskNote.Core.Ai;
using DeskNote.Core.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private readonly IAiRetriever? _retriever;
    private readonly Func<Guid, Task>? _openNote;

    private CancellationTokenSource? _answering;

    public AiChatWindow(
        LocalAiHost ai,
        Guid? noteId,
        string? noteTitle,
        IAiRetriever? retriever = null,
        Func<Guid, Task>? openNote = null)
    {
        ArgumentNullException.ThrowIfNull(ai);

        InitializeComponent();

        _ai = ai;
        _noteId = noteId;
        _retriever = retriever;
        _openNote = openNote;

        SourcesCaption.Text = Strings.Get("Ai_Sources");

        AppWindow.Title = Strings.Format("Ai_PreviewTitleFormat", Strings.Get("Ai_AskTitle"));
        AppIcon.Apply(this);
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
        SourcesPanel.Visibility = Visibility.Collapsed;
        SourceList.ItemsSource = null;
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
            query = await ShowSourcesAsync(query, _answering.Token).ConfigureAwait(true);

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

    /// <summary>
    /// Resolves the notes this question may read and shows them, before the answer starts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retrieval already happens inside the service; running it here first and handing the result
    /// back as <see cref="AiQuery.ScopedNoteIds"/> means the search is not repeated — the
    /// retriever takes the scoped path and only re-reads those bodies. The cost is a few indexed
    /// reads, and what it buys is that the user can see what the answer is about to be based on
    /// while it is still being written.
    /// </para>
    /// <para>
    /// A failure here is not a failed question. Retrieval that throws leaves the query exactly as
    /// it was, and the service does its own retrieval as before.
    /// </para>
    /// </remarks>
    private async Task<AiQuery> ShowSourcesAsync(AiQuery query, CancellationToken cancellationToken)
    {
        if (_retriever is null)
        {
            return query;
        }

        IReadOnlyList<RetrievedNote> sources;

        try
        {
            sources = await _retriever.RetrieveAsync(query, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Could not resolve the notes behind an answer", ex);
            return query;
        }

        if (sources.Count == 0)
        {
            return query;
        }

        SourceList.ItemsSource = sources
            .Select(note => new AnswerSource(
                note.NoteId,
                string.IsNullOrWhiteSpace(note.Title)
                    ? NoteContent.DeriveTitle(note.Content)
                    : note.Title))
            .ToList();

        SourcesPanel.Visibility = Visibility.Visible;

        return query with { ScopedNoteIds = [.. sources.Select(note => note.NoteId)] };
    }

    private async void OnSourceClicked(object sender, RoutedEventArgs e)
    {
        if (_openNote is null || sender is not HyperlinkButton { Tag: Guid noteId })
        {
            return;
        }

        try
        {
            await _openNote(noteId);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Could not open the note behind an answer", ex);
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}

/// <summary>One note an answer was allowed to read, as the sources list shows it.</summary>
/// <param name="NoteId">The note to open when the row is clicked.</param>
/// <param name="Label">The note's title, or its first line when it has none.</param>
public sealed record AnswerSource(Guid NoteId, string Label);
