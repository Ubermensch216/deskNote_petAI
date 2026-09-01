using DeskNote.App.Services;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>
/// The note library: every note ever written, searchable, filterable, including deleted ones.
/// </summary>
/// <remarks>
/// Report p5 lists this as P0 alongside the desktop notes themselves. The desktop holds what is
/// currently on the user's mind; this window is where everything else stays findable, which is
/// what stops the sticky layer from becoming a place notes go to be lost.
/// </remarks>
public sealed partial class NotesExplorerWindow : Window
{
    /// <summary>
    /// How long typing pauses before a search runs. Short enough to feel like find-as-you-type,
    /// long enough that a fast typist does not queue a query per keystroke.
    /// </summary>
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(150);

    private readonly INoteLibrary _library;
    private readonly INoteRepository _notes;
    private readonly HybridLibrarySearch? _hybrid;
    private readonly Func<Guid, NoteOpenOrigin, Task> _openNote;
    private readonly Func<IReadOnlyList<Guid>, Task> _deleteNotes;
    private readonly DispatcherTimer _searchTimer = new() { Interval = SearchDebounce };

    private NoteQuery _query = NoteQuery.Default;
    private bool _suppressFilterEvents;

    /// <summary>
    /// Cancels the semantic pass of a search that has been superseded.
    /// </summary>
    /// <remarks>
    /// Embedding a query takes about 0.3 s, which is longer than a fast typist's pause. Without
    /// this, an abandoned query could come back after the next one and reorder the list under a
    /// pointer that was already moving toward a row.
    /// </remarks>
    private CancellationTokenSource? _semanticPass;

    public NotesExplorerWindow(
        INoteLibrary library,
        INoteRepository notes,
        Func<Guid, NoteOpenOrigin, Task> openNote,
        Func<IReadOnlyList<Guid>, Task> deleteNotes,
        HybridLibrarySearch? hybrid = null)
    {
        InitializeComponent();

        _library = library;
        _notes = notes;
        _hybrid = hybrid;
        _openNote = openNote;
        _deleteNotes = deleteNotes;

        AppWindow.Title = Strings.Get("Explorer_Title");
        AppIcon.Apply(this);
        ScopeAll.Content = Strings.Get("Explorer_AllNotes");
        ScopeDeleted.Content = Strings.Get("Explorer_Deleted");
        NotebooksHeader.Text = Strings.Get("Explorer_Notebooks");
        TagsHeader.Text = Strings.Get("Explorer_Tags");
        SearchBox.PlaceholderText = Strings.Get("Explorer_SearchPlaceholder");
        NewNotebookButton.Content = Strings.Get("Explorer_NewNotebook");
        RestoreButton.Content = Strings.Get("Explorer_Restore");
        SelectAllButton.Content = Strings.Get("Explorer_SelectAll");
        ClearSelectionButton.Content = Strings.Get("Explorer_ClearSelection");
        DeleteSelectedButton.Content = Strings.Get("Explorer_DeleteSelected");
        AppWindow.Resize(new SizeInt32(920, 620));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 640;
            presenter.PreferredMinimumHeight = 420;
        }

        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            await RefreshResultsAsync();
        };

        Activated += OnFirstActivated;
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;

        _suppressFilterEvents = true;
        ScopeList.SelectedIndex = 0;
        _suppressFilterEvents = false;

        await RefreshFiltersAsync();
        await RefreshResultsAsync();
    }

    /// <summary>Reloads the sidebar. Called on open and after anything changes the notebook or tag sets.</summary>
    public async Task RefreshFiltersAsync()
    {
        var notebooks = await _library.ListNotebooksAsync();
        var tags = await _library.ListTagsAsync();

        _suppressFilterEvents = true;

        var notebookChoices = new List<NotebookChoice> { new(null, Strings.Get("Explorer_All")) }
            .Concat(notebooks.Select(n => new NotebookChoice(n.Id, n.Name)))
            .ToList();
        NotebookList.ItemsSource = notebookChoices;
        var notebookIndex = notebookChoices.FindIndex(n => n.Id == _query.NotebookId);
        NotebookList.SelectedIndex = notebookIndex >= 0 ? notebookIndex : 0;
        if (notebookIndex < 0)
        {
            _query = _query with { NotebookId = null };
        }

        var tagChoices = new List<TagChoice> { new(null, Strings.Get("Explorer_All")) }
            .Concat(tags.Select(t => new TagChoice(
                t.Tag.NormalizedName,
                Strings.Format("Explorer_TagCountFormat", t.Tag.Name, t.NoteCount))))
            .ToList();
        TagList.ItemsSource = tagChoices;
        var tagIndex = tagChoices.FindIndex(t => t.NormalizedName == _query.TagNormalizedName);
        TagList.SelectedIndex = tagIndex >= 0 ? tagIndex : 0;
        if (tagIndex < 0)
        {
            _query = _query with { TagNormalizedName = null };
        }

        _suppressFilterEvents = false;
    }

    /// <summary>
    /// Renders the keyword answer, then improves it with the semantic one.
    /// </summary>
    /// <remarks>
    /// Two passes rather than one slower pass. Keyword results are ready immediately and are what
    /// most searches wanted; the semantic pass arrives about 0.3 s later and only redraws the list
    /// when it actually changes the order. A user who found their note on the first pass never
    /// learns there was a second.
    /// </remarks>
    private async Task RefreshResultsAsync()
    {
        _semanticPass?.Cancel();
        _semanticPass?.Dispose();
        _semanticPass = null;

        var text = SearchBox.Text ?? string.Empty;

        var rows = string.IsNullOrWhiteSpace(text)
            ? await _library.ListAsync(_query)
            : await _library.SearchAsync(text, _query);

        Show(rows, text);

        if (_hybrid is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _semanticPass = new CancellationTokenSource();
        var pass = _semanticPass;

        try
        {
            var fused = await _hybrid.FuseAsync(text, _query, rows, cancellationToken: pass.Token);

            // The query that started this pass may no longer be the one on screen.
            if (fused is not null && !pass.IsCancellationRequested && (SearchBox.Text ?? string.Empty) == text)
            {
                Show(fused, text);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query; its results are the ones that matter.
        }
        catch (Exception ex)
        {
            // Semantic search is the optional half. Losing it leaves the keyword results standing.
            CrashLog.Write("Semantic search pass failed", ex);
        }
    }

    private void Show(IReadOnlyList<NoteSummary> rows, string text)
    {
        Results.ItemsSource = rows;
        UpdateSelectionState();

        StatusText.Text = rows.Count switch
        {
            0 when !string.IsNullOrWhiteSpace(text) => Strings.Format("Explorer_NoMatchesFormat", text),
            0 => Strings.Get("Explorer_NoNotes"),
            _ => Strings.Format("Explorer_CountFormat", rows.Count),
        };
    }

    /// <remarks>
    /// Restarting the timer on each keystroke is what makes this a debounce rather than a delay:
    /// only the pause after the last character runs a query.
    /// </remarks>
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async void OnScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || ScopeList.SelectedItem is not ListViewItem { Tag: string scope })
        {
            return;
        }

        _query = _query with { OnlyDeleted = scope == "deleted" };
        await RefreshResultsAsync();
    }

    private async void OnNotebookChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || NotebookList.SelectedItem is not NotebookChoice choice)
        {
            return;
        }

        _query = _query with { NotebookId = choice.Id };
        await RefreshResultsAsync();
    }

    private async void OnTagChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || TagList.SelectedItem is not TagChoice choice)
        {
            return;
        }

        _query = _query with { TagNormalizedName = choice.NormalizedName };
        await RefreshResultsAsync();
    }

    /// <remarks>
    /// A single click opens the note. The library exists to get back to a note, so making that the
    /// primary gesture — rather than select-then-open — is what keeps it a way through rather than
    /// a destination.
    /// </remarks>
    private async void OnResultClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not NoteSummary summary || summary.IsDeleted)
        {
            return;
        }

        var origin = string.IsNullOrWhiteSpace(SearchBox.Text)
            ? NoteOpenOrigin.Library
            : NoteOpenOrigin.Search;

        await _openNote(summary.Id, origin);
    }

    private void OnResultSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectionState();

    private void OnSelectAllClicked(object sender, RoutedEventArgs e) => Results.SelectAll();

    private void OnClearSelectionClicked(object sender, RoutedEventArgs e) =>
        Results.SelectedItems.Clear();

    private void UpdateSelectionState()
    {
        var selectedCount = Results.SelectedItems.Count;
        SelectionText.Text = selectedCount == 0
            ? Strings.Get("Explorer_SelectNotes")
            : Strings.Format("Explorer_SelectedFormat", selectedCount);
        SelectAllButton.IsEnabled = Results.Items.Count > 0 && selectedCount < Results.Items.Count;
        ClearSelectionButton.IsEnabled = selectedCount > 0;
        DeleteSelectedButton.Visibility = _query.OnlyDeleted
            ? Visibility.Collapsed
            : Visibility.Visible;
        DeleteSelectedButton.IsEnabled = selectedCount > 0 && !_query.OnlyDeleted;
        RestoreButton.Visibility = _query.OnlyDeleted ? Visibility.Visible : Visibility.Collapsed;
        RestoreButton.IsEnabled = selectedCount > 0;
    }

    private async void OnDeleteSelectedClicked(object sender, RoutedEventArgs e)
    {
        var selected = Results.SelectedItems
            .OfType<NoteSummary>()
            .Where(note => !note.IsDeleted)
            .Select(note => note.Id)
            .Distinct()
            .ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = Strings.Get("Explorer_SelectToDelete");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Strings.Get("Explorer_DeleteConfirmTitle"),
            Content = Strings.Format("Explorer_DeleteConfirmFormat", selected.Count),
            PrimaryButtonText = Strings.Get("Explorer_DeleteSelected"),
            CloseButtonText = Strings.Get("Explorer_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        DeleteSelectedButton.IsEnabled = false;
        try
        {
            await _deleteNotes(selected);
            await RefreshFiltersAsync();
            await RefreshResultsAsync();
            StatusText.Text = Strings.Format("Explorer_DeletedFormat", selected.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Bulk note deletion failed", ex);
            StatusText.Text = ex.Message;
            UpdateSelectionState();
        }
    }

    private async void OnRestoreClicked(object sender, RoutedEventArgs e)
    {
        var selected = Results.SelectedItems
            .OfType<NoteSummary>()
            .Where(note => note.IsDeleted)
            .Select(note => note.Id)
            .Distinct()
            .ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = Strings.Get("Explorer_SelectToRestore");
            return;
        }

        RestoreButton.IsEnabled = false;
        foreach (var noteId in selected)
        {
            await _notes.RestoreAsync(noteId);
        }

        await RefreshFiltersAsync();
        await RefreshResultsAsync();
        StatusText.Text = Strings.Format("Explorer_RestoredFormat", selected.Count);
    }

    private async void OnNewNotebookClicked(object sender, RoutedEventArgs e)
    {
        var input = new TextBox { PlaceholderText = Strings.Get("Explorer_NotebookName") };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Strings.Get("Explorer_NewNotebook"),
            Content = input,
            PrimaryButtonText = Strings.Get("Explorer_Create"),
            CloseButtonText = Strings.Get("Explorer_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text))
        {
            return;
        }

        await _library.CreateNotebookAsync(input.Text);
        await RefreshFiltersAsync();
        await RefreshResultsAsync();
    }
}
