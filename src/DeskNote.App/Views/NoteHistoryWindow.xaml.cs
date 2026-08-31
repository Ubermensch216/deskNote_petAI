using System.Globalization;
using DeskNote.App.Services;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;
using DeskNote.Core.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>
/// The versions a note has been through, and the way back to one of them.
/// </summary>
/// <remarks>
/// <para>
/// The store has been keeping these since the first release — every five minutes of typing, and
/// always before anything replaced the body wholesale — with no way to look at them. Report p15
/// asks that an AI rewrite preserve the original, and the writing half of that was already true;
/// this is the reading half, without which the guarantee was only a row in a table.
/// </para>
/// <para>
/// It matters more as AI does more. <c>Ctrl+Z</c> only survives while the window is open, so
/// before this window the real answer to "the model rewrote my note and I closed it" was that the
/// text was gone. Every automation added after this one is safer for it existing.
/// </para>
/// </remarks>
public sealed partial class NoteHistoryWindow : Window
{
    private readonly Guid _noteId;
    private readonly INoteRevisionStore _revisions;
    private readonly INoteRepository _notes;
    private readonly NoteSavePipeline _pipeline;
    private readonly Func<Guid, string, Task> _restored;

    public NoteHistoryWindow(
        Guid noteId,
        INoteRevisionStore revisions,
        INoteRepository notes,
        NoteSavePipeline pipeline,
        Func<Guid, string, Task> restored)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(restored);

        InitializeComponent();

        _noteId = noteId;
        _revisions = revisions;
        _notes = notes;
        _pipeline = pipeline;
        _restored = restored;

        AppWindow.Title = Strings.Format("Ai_PreviewTitleFormat", Strings.Get("History_Title"));
        AppIcon.Apply(this);
        HistoryTitle.Text = Strings.Get("History_Title");
        HistoryHint.Text = Strings.Get("History_Hint");
        RestoreButton.Content = Strings.Get("History_Restore");
        CloseButton.Content = Strings.Get("Ai_Close");

        AppWindow.Resize(new SizeInt32(760, 560));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 560;
            presenter.PreferredMinimumHeight = 400;
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
            var stored = await _revisions.ListAsync(_noteId);

            RevisionList.ItemsSource = stored.Select(Describe).ToList();

            Status.Text = stored.Count == 0
                ? Strings.Get("History_Empty")
                : Strings.Format("Explorer_CountFormat", stored.Count);

            if (stored.Count > 0)
            {
                RevisionList.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Could not read note history", ex);
            Status.Text = ex.Message;
        }
    }

    /// <summary>
    /// One row: when it was replaced, and by what.
    /// </summary>
    /// <remarks>
    /// The origin line names the AI action when there was one. "AI · 요약" and "직접 편집" are
    /// different kinds of loss to recover from, and which one it was is the first thing someone
    /// scanning this list is looking for.
    /// </remarks>
    private static RevisionChoice Describe(NoteRevision revision)
    {
        var origin = revision.Source == RevisionSource.Ai
            ? Strings.Format(
                "History_ByAiFormat",
                string.IsNullOrWhiteSpace(revision.ActionName)
                    ? Strings.Get("Note_Ai")
                    : Strings.Get($"Note_Ai{revision.ActionName}"))
            : Strings.Get("History_ByUser");

        return new RevisionChoice(
            revision.Id,
            revision.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            origin,
            revision.Content);
    }

    private void OnRevisionSelected(object sender, SelectionChangedEventArgs e)
    {
        var chosen = RevisionList.SelectedItem as RevisionChoice;

        RevisionBody.Text = chosen?.Content ?? string.Empty;
        RestoreButton.IsEnabled = chosen is not null;
    }

    /// <remarks>
    /// The restore goes through the same pipeline as any other write, so the text being replaced
    /// is checkpointed first. Restoring the wrong version is therefore itself undoable — which is
    /// what makes it safe to press this button while unsure.
    /// </remarks>
    private async void OnRestoreClicked(object sender, RoutedEventArgs e)
    {
        if (RevisionList.SelectedItem is not RevisionChoice chosen)
        {
            return;
        }

        RestoreButton.IsEnabled = false;

        try
        {
            if (await _notes.GetAsync(_noteId) is null)
            {
                Status.Text = Strings.Get("History_NoteGone");
                return;
            }

            await _pipeline.SaveAsync(
                _noteId,
                NoteContent.DeriveTitle(chosen.Content),
                chosen.Content,
                RevisionReason.BulkReplace,
                RevisionSource.User,
                actionName: null);

            // The window on the desktop is still showing the text that was just replaced.
            await _restored(_noteId, chosen.Content);

            Close();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Restoring a revision failed", ex);
            Status.Text = ex.Message;
            RestoreButton.IsEnabled = true;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}

/// <summary>One stored version, as the history list shows it.</summary>
/// <param name="Id">The revision row.</param>
/// <param name="When">Local timestamp, in the reader's culture.</param>
/// <param name="Origin">Who replaced the text: the user, or a named AI action.</param>
/// <param name="Content">The body to restore.</param>
public sealed record RevisionChoice(Guid Id, string When, string Origin, string Content);
