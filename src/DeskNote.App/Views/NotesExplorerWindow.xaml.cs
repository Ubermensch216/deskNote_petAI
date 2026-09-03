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

    /// <summary>
    /// How long edits elsewhere are collected before the list reloads.
    /// </summary>
    /// <remarks>
    /// Autosave fires every few seconds while a note is being typed into, and each save changes
    /// the row's preview and its position in "recently updated". Reloading on each one would make
    /// the list jump under the pointer; a second of quiet means it reloads once, when the typing
    /// stops.
    /// </remarks>
    private static readonly TimeSpan ChangeDebounce = TimeSpan.FromSeconds(1);

    private readonly DispatcherTimer _refreshTimer = new() { Interval = ChangeDebounce };

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
        NotebooksHeader.Text = Strings.Get("Explorer_Notebooks");
        TagsHeader.Text = Strings.Get("Explorer_Tags");
        SearchBox.PlaceholderText = Strings.Get("Explorer_SearchPlaceholder");
        Describe(RefreshButton, "Explorer_Refresh");
        Describe(NewNotebookButton, "Explorer_NewNotebook");
        Describe(RenameNotebookButton, "Explorer_RenameNotebook");
        Describe(DeleteNotebookButton, "Explorer_DeleteNotebook");
        MoveToNotebookButton.Content = Strings.Get("Explorer_MoveToNotebook");
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

        _refreshTimer.Tick += async (_, _) =>
        {
            _refreshTimer.Stop();
            await RefreshAllAsync();
        };

        // Nothing is selected yet, so the notebook commands start out inert rather than looking
        // available for the moment before the first load.
        UpdateNotebookCommands();

        Activated += OnFirstActivated;
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;

        await RefreshAllAsync();
    }

    /// <summary>Reloads the sidebar and the list together.</summary>
    public async Task RefreshAllAsync()
    {
        await RefreshFiltersAsync();
        await RefreshResultsAsync();
    }

    /// <summary>
    /// Reloads shortly, after a note changed somewhere else in the app.
    /// </summary>
    /// <remarks>
    /// Skipped while rows are selected: the reload replaces the list, and taking a selection away
    /// from someone who is halfway through choosing what to delete is worse than showing them a
    /// preview that is a few seconds old. The refresh button is there for that case.
    /// </remarks>
    public void ScheduleRefresh()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(ScheduleRefresh);
            return;
        }

        if (Results.SelectedItems.Count > 0)
        {
            return;
        }

        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        RefreshButton.IsEnabled = false;

        try
        {
            await RefreshAllAsync();
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
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
        UpdateNotebookCommands();
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

    private async void OnNotebookChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || NotebookList.SelectedItem is not NotebookChoice choice)
        {
            return;
        }

        _query = _query with { NotebookId = choice.Id };
        UpdateNotebookCommands();
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
        MoveToNotebookButton.IsEnabled = selectedCount > 0;
        DeleteSelectedButton.IsEnabled = selectedCount > 0;
    }

    /// <summary>A notebook is only actionable when it is a real one — "All" is a filter, not a folder.</summary>
    private void UpdateNotebookCommands()
    {
        var selected = NotebookList.SelectedItem is NotebookChoice { Id: not null };
        RenameNotebookButton.IsEnabled = selected;
        DeleteNotebookButton.IsEnabled = selected;
    }

    private async void OnDeleteSelectedClicked(object sender, RoutedEventArgs e)
    {
        var selected = SelectedNoteIds();
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

    private List<Guid> SelectedNoteIds() => Results.SelectedItems
        .OfType<NoteSummary>()
        .Select(note => note.Id)
        .Distinct()
        .ToList();

    /// <summary>
    /// Builds the destinations at the moment the menu opens.
    /// </summary>
    /// <remarks>
    /// Reading the notebooks here rather than caching them means one made seconds ago in the
    /// sidebar is already somewhere notes can go. "No notebook" comes first because taking a note
    /// out of a folder is as ordinary an act as putting it in one.
    /// </remarks>
    private async void OnMoveFlyoutOpening(object? sender, object e)
    {
        MoveToNotebookFlyout.Items.Clear();

        var unfiled = new MenuFlyoutItem { Text = Strings.Get("Explorer_NoNotebook") };
        unfiled.Click += async (_, _) => await MoveSelectedAsync(null);
        MoveToNotebookFlyout.Items.Add(unfiled);

        try
        {
            var notebooks = await _library.ListNotebooksAsync();
            if (notebooks.Count > 0)
            {
                MoveToNotebookFlyout.Items.Add(new MenuFlyoutSeparator());
            }

            foreach (var notebook in notebooks)
            {
                var item = new MenuFlyoutItem { Text = notebook.Name };
                var target = notebook.Id;
                item.Click += async (_, _) => await MoveSelectedAsync(target);
                MoveToNotebookFlyout.Items.Add(item);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Listing notebooks for a move failed", ex);
        }
    }

    private async Task MoveSelectedAsync(Guid? notebookId)
    {
        var selected = SelectedNoteIds();
        if (selected.Count == 0)
        {
            StatusText.Text = Strings.Get("Explorer_SelectToMove");
            return;
        }

        try
        {
            foreach (var noteId in selected)
            {
                await _notes.SetNotebookAsync(noteId, notebookId);
            }

            await RefreshAllAsync();
            StatusText.Text = Strings.Format("Explorer_MovedFormat", selected.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Moving notes to a notebook failed", ex);
            StatusText.Text = ex.Message;
        }
    }

    private async void OnRenameNotebookClicked(object sender, RoutedEventArgs e)
    {
        if (NotebookList.SelectedItem is not NotebookChoice { Id: { } notebookId } choice)
        {
            return;
        }

        var input = new TextBox { Text = choice.Label, SelectionStart = choice.Label.Length };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Strings.Get("Explorer_RenameNotebook"),
            Content = input,
            PrimaryButtonText = Strings.Get("Explorer_Rename"),
            CloseButtonText = Strings.Get("Explorer_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || string.IsNullOrWhiteSpace(input.Text))
        {
            return;
        }

        try
        {
            await _library.RenameNotebookAsync(notebookId, input.Text);
            await RefreshAllAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Renaming a notebook failed", ex);
            StatusText.Text = ex.Message;
        }
    }

    /// <summary>
    /// Deletes a notebook. The notes inside it are not deleted with it.
    /// </summary>
    /// <remarks>
    /// The dialog says so out loud. With no deleted view left to recover from, a user has every
    /// reason to fear that removing the folder removes the work, and being told otherwise is what
    /// makes the command usable.
    /// </remarks>
    private async void OnDeleteNotebookClicked(object sender, RoutedEventArgs e)
    {
        if (NotebookList.SelectedItem is not NotebookChoice { Id: { } notebookId } choice)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = Strings.Get("Explorer_DeleteNotebook"),
            Content = Strings.Format("Explorer_DeleteNotebookConfirmFormat", choice.Label),
            PrimaryButtonText = Strings.Get("Explorer_Delete"),
            CloseButtonText = Strings.Get("Explorer_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await _library.DeleteNotebookAsync(notebookId);

            if (_query.NotebookId == notebookId)
            {
                _query = _query with { NotebookId = null };
            }

            await RefreshAllAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Deleting a notebook failed", ex);
            StatusText.Text = ex.Message;
        }
    }

    private static void Describe(FrameworkElement element, string key)
    {
        var text = Strings.Get(key);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, text);
        ToolTipService.SetToolTip(element, text);
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
