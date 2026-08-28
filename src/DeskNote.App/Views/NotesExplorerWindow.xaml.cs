using DeskNote.App.Services;
using DeskNote.Core.Abstractions;
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
    private readonly Func<Guid, Task> _openNote;
    private readonly DispatcherTimer _searchTimer = new() { Interval = SearchDebounce };

    private NoteQuery _query = NoteQuery.Default;
    private bool _suppressFilterEvents;

    public NotesExplorerWindow(INoteLibrary library, INoteRepository notes, Func<Guid, Task> openNote)
    {
        InitializeComponent();

        _library = library;
        _notes = notes;
        _openNote = openNote;

        AppWindow.Title = Strings.Get("Explorer_Title");
        ScopeAll.Content = Strings.Get("Explorer_AllNotes");
        ScopeDeleted.Content = Strings.Get("Explorer_Deleted");
        NotebooksHeader.Text = Strings.Get("Explorer_Notebooks");
        TagsHeader.Text = Strings.Get("Explorer_Tags");
        SearchBox.PlaceholderText = Strings.Get("Explorer_SearchPlaceholder");
        NewNotebookButton.Content = Strings.Get("Explorer_NewNotebook");
        RestoreButton.Content = Strings.Get("Explorer_Restore");
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

        NotebookList.ItemsSource = new List<NotebookChoice> { new(null, Strings.Get("Explorer_All")) }
            .Concat(notebooks.Select(n => new NotebookChoice(n.Id, n.Name)))
            .ToList();
        NotebookList.SelectedIndex = 0;

        TagList.ItemsSource = new List<TagChoice> { new(null, Strings.Get("Explorer_All")) }
            .Concat(tags.Select(t => new TagChoice(
                t.Tag.NormalizedName,
                Strings.Format("Explorer_TagCountFormat", t.Tag.Name, t.NoteCount))))
            .ToList();
        TagList.SelectedIndex = 0;

        _suppressFilterEvents = false;
    }

    private async Task RefreshResultsAsync()
    {
        var text = SearchBox.Text ?? string.Empty;

        var rows = string.IsNullOrWhiteSpace(text)
            ? await _library.ListAsync(_query)
            : await _library.SearchAsync(text, _query);

        Results.ItemsSource = rows;
        RestoreButton.Visibility = _query.OnlyDeleted ? Visibility.Visible : Visibility.Collapsed;

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

        await _openNote(summary.Id);
    }

    private async void OnRestoreClicked(object sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is not NoteSummary summary)
        {
            StatusText.Text = Strings.Get("Explorer_SelectToRestore");
            return;
        }

        await _notes.RestoreAsync(summary.Id);
        await RefreshResultsAsync();
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
