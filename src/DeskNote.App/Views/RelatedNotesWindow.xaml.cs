using System.Globalization;
using DeskNote.App.Services;
using DeskNote.App.Theming;
using DeskNote.Core.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>
/// The notes that read like this one.
/// </summary>
/// <remarks>
/// <para>
/// The product principle is 메모 → 기억 → AI → 자동화, and this is the 기억 step actually
/// arriving: until now the app stored what you wrote well and never brought it back to you. The
/// embeddings that made 메모 Q&amp;A possible already knew which notes belong together; nothing
/// was asking them.
/// </para>
/// <para>
/// It costs no generation. Finding these is a cosine over vectors that are already stored, so the
/// window fills in milliseconds rather than in the tens of seconds every other AI action needs.
/// </para>
/// </remarks>
public sealed partial class RelatedNotesWindow : Window
{
    /// <summary>
    /// Fewest neighbours worth offering a notebook for.
    /// </summary>
    /// <remarks>
    /// Two notes are a coincidence. Three that all read alike is a subject, and a subject is what
    /// a notebook is for — offering one for every pair would fill the sidebar with folders of two.
    /// </remarks>
    private const int MinimumToGroup = 3;

    private readonly Guid _noteId;
    private readonly NoteNeighbourhood _neighbourhood;
    private readonly Func<Guid, Task> _openNote;
    private readonly Func<Guid, IReadOnlyList<Guid>, Task<string?>>? _group;

    private IReadOnlyList<Guid> _groupable = [];

    public RelatedNotesWindow(
        Guid noteId,
        NoteNeighbourhood neighbourhood,
        Func<Guid, Task> openNote,
        Func<Guid, IReadOnlyList<Guid>, Task<string?>>? group = null)
    {
        ArgumentNullException.ThrowIfNull(neighbourhood);
        ArgumentNullException.ThrowIfNull(openNote);

        InitializeComponent();

        _noteId = noteId;
        _neighbourhood = neighbourhood;
        _openNote = openNote;
        _group = group;

        GroupButton.Content = Strings.Get("Related_Group");

        AppWindow.Title = Strings.Format("Ai_PreviewTitleFormat", Strings.Get("Related_Title"));
        AppIcon.Apply(this);
        RelatedTitle.Text = Strings.Get("Related_Title");
        RelatedHint.Text = Strings.Get("Related_Hint");
        CloseButton.Content = Strings.Get("Ai_Close");

        AppWindow.Resize(new SizeInt32(560, 480));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 420;
            presenter.PreferredMinimumHeight = 320;
        }

        Activated += OnFirstActivated;
    }

    /// <summary>Places the window beside the note it belongs to, rather than over it.</summary>
    public void PlaceNear(AppWindow owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        AppWindow.Move(new PointInt32(
            owner.Position.X + owner.Size.Width + 12,
            owner.Position.Y));
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var related = await _neighbourhood.RelatedAsync(_noteId);

            Progress.IsActive = false;
            Progress.Visibility = Visibility.Collapsed;

            if (related.Count == 0)
            {
                // Two different reasons produce an empty list, and the user can act on only one of
                // them: no model means "install one", no neighbours means "there is nothing here".
                Notice.Message = Strings.Get("Related_None");
                Notice.IsOpen = true;
                return;
            }

            RelatedList.ItemsSource = related.Select(RelatedRow.From).ToList();
            RelatedList.Visibility = Visibility.Visible;

            var duplicates = related.Count(item => item.IsNearDuplicate);

            Status.Text = duplicates > 0
                ? Strings.Format("Related_CountWithDuplicatesFormat", related.Count, duplicates)
                : Strings.Format("Explorer_CountFormat", related.Count);

            await OfferGroupingAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Could not find related notes", ex);

            Progress.IsActive = false;
            Progress.Visibility = Visibility.Collapsed;
            Status.Text = ex.Message;
        }
    }

    /// <summary>
    /// Shows the notebook offer when enough neighbours are close enough to file together.
    /// </summary>
    /// <remarks>
    /// A separate, stricter read than the list above it. Being worth reading and being worth
    /// moving into a folder are different bars, and this is the one that moves the user's notes.
    /// </remarks>
    private async Task OfferGroupingAsync()
    {
        if (_group is null)
        {
            return;
        }

        try
        {
            var group = await _neighbourhood.GroupAsync(_noteId);

            if (group.Count + 1 < MinimumToGroup)
            {
                return;
            }

            _groupable = [.. group.Select(note => note.Id)];
            GroupButton.Content = Strings.Format("Related_GroupFormat", group.Count + 1);
            GroupButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Could not work out a notebook for these notes", ex);
        }
    }

    private async void OnGroupClicked(object sender, RoutedEventArgs e)
    {
        if (_group is null || _groupable.Count == 0)
        {
            return;
        }

        GroupButton.IsEnabled = false;

        try
        {
            var name = await _group(_noteId, _groupable);

            Status.Text = string.IsNullOrWhiteSpace(name)
                ? Strings.Get("Related_GroupFailed")
                : Strings.Format("Related_GroupedFormat", name, _groupable.Count + 1);

            GroupButton.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Filing these notes into a notebook failed", ex);
            Status.Text = ex.Message;
            GroupButton.IsEnabled = true;
        }
    }

    private async void OnRelatedClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not RelatedRow row)
        {
            return;
        }

        try
        {
            await _openNote(row.NoteId);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Could not open a related note", ex);
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}

/// <summary>One row in the related-notes list.</summary>
public sealed record RelatedRow(
    Guid NoteId,
    string Title,
    string Preview,
    Brush Color,
    string Badge,
    Brush BadgeBackground,
    Brush BadgeForeground)
{
    public static RelatedRow From(RelatedNote related)
    {
        ArgumentNullException.ThrowIfNull(related);

        var isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        var note = related.Note;

        // A near-duplicate says something a percentage does not, so it gets the word and the
        // colour; everything else just states how close it was and stays quiet.
        var duplicate = related.IsNearDuplicate;

        var badge = duplicate
            ? Strings.Get("Related_NearDuplicate")
            : Math.Round(related.Similarity * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        var surface = NotePalette.Resolve(NoteColors.Default, isDark);
        var ink = surface.Ink;

        return new RelatedRow(
            note.Id,
            NoteRowFormat.TitleOrFallback(note.Title, note.Preview),
            note.Preview.Replace('\n', ' ').Trim(),
            NoteRowFormat.ColorBrush(note.ColorKey),
            badge,
            new SolidColorBrush(duplicate ? surface.Background : Microsoft.UI.Colors.Transparent),
            new SolidColorBrush(duplicate
                ? ink
                : Windows.UI.Color.FromArgb(0x99, ink.R, ink.G, ink.B)));
    }
}
