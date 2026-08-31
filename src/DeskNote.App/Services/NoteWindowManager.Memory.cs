using DeskNote.App.Views;
using DeskNote.Core.Ai;
using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.App.Services;

public sealed partial class NoteWindowManager
{
    /// <summary>
    /// Opens the note library, or brings it forward if it is already open.
    /// </summary>
    /// <remarks>
    /// One instance only. A second copy of the library would show a stale list the moment the
    /// first one was used, and neither would obviously be the real one.
    /// </remarks>
    public void ShowLibrary()
    {
        if (_explorer is not null)
        {
            _explorer.Activate();
            return;
        }

        _explorer = new NotesExplorerWindow(
            library,
            notes,
            (noteId, origin) => FocusAsync(noteId, new NoteOpenContext { Origin = origin }),
            hybridSearch);
        _explorer.Closed += (_, _) => _explorer = null;
        _explorer.Activate();
    }

    /// <summary>
    /// Opens a note's stored versions, or replaces the window if another note's are showing.
    /// </summary>
    /// <remarks>
    /// One window for every note rather than one per note: two histories side by side invite a
    /// restore into the wrong note, and the restore is the one action here that writes.
    /// </remarks>
    public void ShowHistory(Guid noteId)
    {
        if (revisions is null)
        {
            return;
        }

        _history?.Close();

        _history = new NoteHistoryWindow(
            noteId,
            revisions,
            notes,
            savePipeline,
            (id, content) =>
            {
                // The desktop window is still holding the text that was just replaced; it has to
                // show what the database now says the note is.
                if (_windows.TryGetValue(id, out var open))
                {
                    open.ReplaceContent(content);
                }

                return Task.CompletedTask;
            });

        _history.Closed += (_, _) => _history = null;
        _history.Activate();

        if (_windows.TryGetValue(noteId, out var source))
        {
            _history.PlaceNear(source.AppWindow);
        }
    }

    /// <summary>
    /// Opens the notes that read like this one.
    /// </summary>
    /// <remarks>
    /// One window at a time, like the history: this is a view onto one note's surroundings, and
    /// two of them open at once stop saying whose surroundings they are.
    /// </remarks>
    public void ShowRelated(Guid noteId)
    {
        if (neighbourhood is null)
        {
            return;
        }

        _related?.Close();
        _related = new RelatedNotesWindow(
            noteId,
            neighbourhood,
            id => FocusAsync(
                id,
                new NoteOpenContext { Origin = NoteOpenOrigin.Related, SourceNoteId = noteId }),
            GroupIntoNotebookAsync);
        _related.Closed += (_, _) => _related = null;
        _related.Activate();

        if (_windows.TryGetValue(noteId, out var source))
        {
            _related.PlaceNear(source.AppWindow);
        }
    }

    /// <summary>
    /// Writes today's briefing into a new note and opens it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A note rather than a window. The briefing is text about the user's own notes, and making it
    /// one more note means it is searchable, editable and deletable by everything that already
    /// exists — and that a briefing nobody wanted is dismissed by the gesture people already know.
    /// </para>
    /// <para>
    /// The note is created and shown first, then filled in. Composing takes a model call, and a
    /// tray click that appears to do nothing for half a minute reads as a broken menu item.
    /// </para>
    /// </remarks>
    public async Task ShowBriefingAsync(CancellationToken cancellationToken = default)
    {
        if (briefing is null || _composingBriefing)
        {
            return;
        }

        _composingBriefing = true;

        try
        {
            var note = await CreateAsync(NoteSizePreset.Large).ConfigureAwait(true);

            if (_windows.TryGetValue(note.Id, out var window))
            {
                window.ReplaceContent(Strings.Get("Briefing_Composing"));
            }

            var text = await briefing.ComposeAsync(cancellationToken).ConfigureAwait(true);

            // Written through the pipeline rather than the editor, so the placeholder is replaced
            // in storage too and the note is not left holding "composing" if the app closes now.
            await savePipeline.SaveAsync(
                note.Id,
                NoteContent.DeriveTitle(text),
                text,
                RevisionReason.BulkReplace,
                RevisionSource.Ai,
                nameof(DailyBriefing),
                cancellationToken).ConfigureAwait(true);

            if (_windows.TryGetValue(note.Id, out var filled))
            {
                filled.ReplaceContent(text);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Composing the daily briefing failed", ex);
        }
        finally
        {
            _composingBriefing = false;
        }
    }

    /// <summary>
    /// Files a note and its closest neighbours into a notebook the model names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first thing in this app to call <see cref="INoteRepository.SetNotebookAsync"/>. Until
    /// now notebooks could be created and filtered by and never put anything in — a drawer with no
    /// way to open it — because nothing knew which notes belonged together. The vectors do.
    /// </para>
    /// <para>
    /// The model does one job: name the group. Which notes belong is a question the embeddings
    /// answer better and in milliseconds, and asking a small model to both choose and name would
    /// put the expensive, unreliable half in charge of the part that moves the user's notes.
    /// </para>
    /// </remarks>
    private async Task<string?> GroupIntoNotebookAsync(Guid noteId, IReadOnlyList<Guid> neighbours)
    {
        var members = new List<Guid>(neighbours) { noteId };

        var name = await NameForAsync(members).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var notebook = await library.CreateNotebookAsync(name).ConfigureAwait(true);

        foreach (var member in members)
        {
            await notes.SetNotebookAsync(member, notebook.Id).ConfigureAwait(true);
        }

        // The sidebar is showing a notebook list that no longer matches the database.
        if (_explorer is not null)
        {
            await _explorer.RefreshFiltersAsync().ConfigureAwait(true);
        }

        return notebook.Name;
    }

    /// <summary>
    /// A name for a set of notes, from their titles.
    /// </summary>
    /// <remarks>
    /// Titles rather than bodies: naming a group needs to know what the notes are about, which the
    /// titles already say, and sending several full bodies would turn a short call into a long one
    /// for no better answer. With no model the notes are still filed — under the first note's
    /// title, which is a worse name and an honest one.
    /// </remarks>
    private async Task<string?> NameForAsync(IReadOnlyList<Guid> members)
    {
        var titles = new List<string>(members.Count);

        foreach (var id in members)
        {
            if (await notes.GetAsync(id).ConfigureAwait(true) is { } note)
            {
                titles.Add(NoteContent.DeriveTitle(note.Content));
            }
        }

        titles.RemoveAll(string.IsNullOrWhiteSpace);

        if (titles.Count == 0)
        {
            return null;
        }

        if (ai is not { IsAvailable: true })
        {
            return titles[0];
        }

        try
        {
            var suggested = await ai.Service.SuggestTitleAsync(new Core.Ai.NoteContext
            {
                NoteId = Guid.Empty,
                Content = string.Join('\n', titles),
                LanguageTag = Strings.OverrideLocale ?? "ko-KR",
            }).ConfigureAwait(true);

            return string.IsNullOrWhiteSpace(suggested) ? titles[0] : suggested;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Naming a notebook failed; using the first note's title", ex);
            return titles[0];
        }
    }

    /// <summary>
    /// Opens 메모 Q&amp;A, scoped to one note or to the whole library.
    /// </summary>
    /// <remarks>
    /// One sidecar at a time, re-scoped rather than duplicated: two chat windows answering about
    /// different notes would leave the user guessing which one their next question went to.
    /// </remarks>
    public void ShowChat(Guid? noteId)
    {
        if (ai is null)
        {
            return;
        }

        var source = noteId is { } id && _windows.TryGetValue(id, out var window) ? window : null;

        _chat?.Close();
        _chat = new AiChatWindow(
            ai,
            noteId,
            source?.CurrentTitle,
            retriever,
            id => FocusAsync(
                id,
                new NoteOpenContext { Origin = NoteOpenOrigin.AiEvidence, SourceNoteId = noteId }));
        _chat.Closed += (_, _) => _chat = null;
        _chat.Activate();

        if (source is not null)
        {
            _chat.PlaceNear(source.AppWindow);
        }
    }

    /// <summary>Opens 메모 Q&amp;A for the note the user was last in.</summary>
    public void ShowChatForActiveNote() => ShowChat(ActiveNote?.NoteId);

    /// <summary>
    /// Opens the command palette, the app's one global entry point for a line of text.
    /// </summary>
    /// <remarks>
    /// The palette decides nothing itself. It collects a line and an action and hands both back
    /// here, so creating a note, opening a question or composing a reminder still happen in the
    /// one place that owns those gestures.
    /// </remarks>
    public void ShowPalette()
    {
        _palette?.Close();

        _palette = new CommandPaletteWindow(
            library,
            hybridSearch,
            ai,
            noteId => FocusAsync(
                noteId,
                new NoteOpenContext { Origin = NoteOpenOrigin.Search }),
            RunPaletteAction);

        _palette.Closed += (_, _) => _palette = null;
        _palette.Activate();
        _palette.CenterOnScreen();
    }

    private async void RunPaletteAction(PaletteAction action, string text)
    {
        try
        {
            switch (action)
            {
                case PaletteAction.Ask:
                    ShowChat(ActiveNote?.NoteId);
                    break;

                case PaletteAction.Remind:
                    // Needs a note to hang the reminder on. The one the user was last in is the
                    // note they were thinking about; with none open, the line becomes its own note
                    // so the reminder has something to point at.
                    var target = ActiveNote?.NoteId
                        ?? (await CreateAsync().ConfigureAwait(true)).Id;

                    ShowReminderComposer(target);
                    break;

                case PaletteAction.Briefing:
                    await ShowBriefingAsync().ConfigureAwait(true);
                    break;

                case PaletteAction.NewNote:
                    var created = await CreateAsync().ConfigureAwait(true);

                    if (_windows.TryGetValue(created.Id, out var window))
                    {
                        window.InsertAtCaret(text);
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Palette action {action} failed", ex);
        }
    }
}
