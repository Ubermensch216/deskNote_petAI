using DeskNote.App.Services;
using DeskNote.Core.Abstractions;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>What the palette will do with the line that was typed.</summary>
public enum PaletteAction
{
    Search = 0,
    Ask = 1,
    Remind = 2,
    NewNote = 3,
}

/// <summary>
/// One line in, everything the app can do with it.
/// </summary>
/// <remarks>
/// <para>
/// <c>Ctrl+Space</c> used to open 메모 Q&amp;A and nothing else, which made the app's only global
/// AI key a single feature's shortcut. A line of text is enough to mean four different things —
/// find this, ask this, remind me this, write this down — and the palette is where the app finds
/// out which.
/// </para>
/// <para>
/// Search never waits for the model. Results come from hybrid search on every keystroke, so the
/// common case — "where is that note" — is answered in milliseconds. The model is asked what the
/// line means at the same time, and when it answers it only highlights a button. A palette that
/// paused a second before showing anything would be worse than the shortcut it replaced, however
/// good the routing was.
/// </para>
/// </remarks>
public sealed partial class CommandPaletteWindow : Window
{
    /// <summary>Matches the library's own find-as-you-type pause.</summary>
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// How long the line rests before the model is asked what it means.
    /// </summary>
    /// <remarks>
    /// Longer than the search debounce on purpose. Search is free and should keep up with typing;
    /// classification costs a model call, and asking after every pause would queue one per word.
    /// </remarks>
    private static readonly TimeSpan IntentDebounce = TimeSpan.FromMilliseconds(600);

    private readonly HybridLibrarySearch? _search;
    private readonly INoteLibrary _library;
    private readonly LocalAiHost? _ai;
    private readonly Func<Guid, Task> _openNote;
    private readonly Action<PaletteAction, string> _run;

    private readonly DispatcherTimer _searchTimer = new() { Interval = SearchDebounce };
    private readonly DispatcherTimer _intentTimer = new() { Interval = IntentDebounce };

    private CancellationTokenSource? _pass;

    public CommandPaletteWindow(
        INoteLibrary library,
        HybridLibrarySearch? search,
        LocalAiHost? ai,
        Func<Guid, Task> openNote,
        Action<PaletteAction, string> run)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(openNote);
        ArgumentNullException.ThrowIfNull(run);

        InitializeComponent();

        _library = library;
        _search = search;
        _ai = ai;
        _openNote = openNote;
        _run = run;

        AppWindow.Title = Strings.Get("Palette_Title");
        InputBox.PlaceholderText = Strings.Get("Palette_Placeholder");
        AskButton.Content = Strings.Get("Note_AiAsk");
        RemindButton.Content = Strings.Get("Remind_Title");
        NewNoteButton.Content = Strings.Get("Note_NewNote");
        Hint.Text = Strings.Get("Palette_Hint");

        // Nothing is possible with an empty line, so the actions start off.
        SetActionsEnabled(false);

        AppWindow.Resize(new SizeInt32(620, 460));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 460;
            presenter.PreferredMinimumHeight = 300;
        }

        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            await SearchAsync();
        };

        _intentTimer.Tick += async (_, _) =>
        {
            _intentTimer.Stop();
            await ClassifyAsync();
        };

        Activated += OnFirstActivated;
        Closed += (_, _) => _pass?.Cancel();
    }

    /// <summary>Centres the palette on the display it opens on, the way a launcher appears.</summary>
    public void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

        AppWindow.Move(new PointInt32(
            area.X + ((area.Width - AppWindow.Size.Width) / 2),
            area.Y + ((area.Height - AppWindow.Size.Height) / 3)));
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        _ = InputBox.Focus(FocusState.Programmatic);
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        var text = InputBox.Text.Trim();

        SetActionsEnabled(text.Length > 0);
        ClearRecommendation();

        _searchTimer.Stop();
        _intentTimer.Stop();

        if (text.Length == 0)
        {
            Results.ItemsSource = null;
            Hint.Visibility = Visibility.Visible;
            return;
        }

        _searchTimer.Start();

        if (_ai?.IsAvailable == true)
        {
            _intentTimer.Start();
        }
    }

    /// <remarks>
    /// Enter opens the top result, because "find a note" is what most lines typed here are for.
    /// Escape closes, which is what a launcher is expected to do.
    /// </remarks>
    private async void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape:
                e.Handled = true;
                Close();
                break;

            case Windows.System.VirtualKey.Enter:
                e.Handled = true;

                if ((Results.SelectedItem ?? Results.Items.FirstOrDefault()) is PaletteRow row)
                {
                    await OpenAsync(row.NoteId);
                }

                break;
        }
    }

    private async Task SearchAsync()
    {
        var text = InputBox.Text.Trim();

        if (text.Length == 0)
        {
            return;
        }

        _pass?.Cancel();
        _pass?.Dispose();
        _pass = new CancellationTokenSource();
        var pass = _pass;

        try
        {
            var rows = await _library.SearchAsync(text, NoteQuery.Default, 20, pass.Token);
            Show(rows);

            // The semantic pass improves the order a moment later, exactly as in the library.
            if (_search is not null)
            {
                var fused = await _search.FuseAsync(text, NoteQuery.Default, rows, 20, pass.Token);

                if (fused is not null && !pass.IsCancellationRequested && InputBox.Text.Trim() == text)
                {
                    Show(fused);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer line.
        }
        catch (Exception ex)
        {
            CrashLog.Write("Palette search failed", ex);
        }
    }

    private void Show(IReadOnlyList<NoteSummary> rows)
    {
        Results.ItemsSource = rows.Select(PaletteRow.From).ToList();
        Hint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Hint.Text = rows.Count == 0 ? Strings.Get("Explorer_NoNotes") : Strings.Get("Palette_Hint");
    }

    /// <summary>
    /// Asks the model what the line is for, and highlights the matching button.
    /// </summary>
    /// <remarks>
    /// The answer is advisory. Nothing waits for it, nothing moves because of it, and a failure is
    /// swallowed — the buttons are all there either way, and the user can always ignore the
    /// highlight and press a different one.
    /// </remarks>
    private async Task ClassifyAsync()
    {
        if (_ai is not { IsAvailable: true })
        {
            return;
        }

        var text = InputBox.Text.Trim();

        if (text.Length == 0)
        {
            return;
        }

        try
        {
            // Reusing reminder parsing as the classifier: a line that resolves to a real moment is
            // a reminder, and one that does not is not. That is a sharper signal than asking a
            // small model to pick a label, and it is a call the app already knows how to make.
            var parsed = await _ai.Service.ParseReminderAsync(text, DateTimeOffset.Now);

            if (parsed is not null && InputBox.Text.Trim() == text)
            {
                Recommend(RemindButton);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Advisory only; the palette works exactly as well without it.
            CrashLog.Write("Palette intent classification failed", ex);
        }
    }

    private void Recommend(Button button)
    {
        ClearRecommendation();
        button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
    }

    private void ClearRecommendation()
    {
        foreach (var button in new[] { AskButton, RemindButton, NewNoteButton })
        {
            button.ClearValue(FrameworkElement.StyleProperty);
        }
    }

    private void SetActionsEnabled(bool enabled)
    {
        AskButton.IsEnabled = enabled && _ai?.IsAvailable == true;
        RemindButton.IsEnabled = enabled && _ai?.IsAvailable == true;
        NewNoteButton.IsEnabled = enabled;
    }

    private async void OnResultClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PaletteRow row)
        {
            await OpenAsync(row.NoteId);
        }
    }

    private async Task OpenAsync(Guid noteId)
    {
        try
        {
            await _openNote(noteId);
            Close();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Could not open a note from the palette", ex);
        }
    }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }

    private void OnAskClicked(object sender, RoutedEventArgs e) => Run(PaletteAction.Ask);

    private void OnRemindClicked(object sender, RoutedEventArgs e) => Run(PaletteAction.Remind);

    private void OnNewNoteClicked(object sender, RoutedEventArgs e) => Run(PaletteAction.NewNote);

    private void Run(PaletteAction action)
    {
        var text = InputBox.Text.Trim();

        if (text.Length == 0)
        {
            return;
        }

        _run(action, text);
        Close();
    }
}

/// <summary>One search result in the palette.</summary>
public sealed record PaletteRow(Guid NoteId, string Title, string Preview, Brush Color)
{
    public static PaletteRow From(NoteSummary note)
    {
        ArgumentNullException.ThrowIfNull(note);

        return new PaletteRow(
            note.Id,
            NoteRowFormat.TitleOrFallback(note.Title, note.Preview),
            note.Preview.Replace('\n', ' ').Trim(),
            NoteRowFormat.ColorBrush(note.ColorKey));
    }
}
