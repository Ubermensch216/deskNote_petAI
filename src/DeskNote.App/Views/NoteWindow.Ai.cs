using System.Globalization;
using DeskNote.App.Services;
using DeskNote.Core.Ai;
using DeskNote.Core.Models;
using DeskNote.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskNote.App.Views;

public sealed partial class NoteWindow
{
    /// <summary>
    /// Updates what the AI section offers, from a probe the host ran in the background.
    /// </summary>
    /// <remarks>
    /// Called whenever availability changes, not only at startup: Ollama can be started or stopped
    /// while notes are open, and the menu is expected to follow rather than to be right only for
    /// as long as nothing outside the app moved.
    /// </remarks>
    public void SetAiCapability(AiCapability capability)
    {
        _aiCapability = capability;
        UpdateAiState();
    }

    private void UpdateAiState()
    {
        var ready = _ai is not null && _aiCapability.IsAvailable;

        foreach (var chip in new[]
                 {
                     AiSummarize, AiOrganize, AiExtractTasks, AiSuggestTags, AiAsk, AiRelated,
                     AiTitle, RemindCustom,
                     AiRewriteConcise, AiRewriteFormal, AiRewriteFriendly, AiRewriteReport,
                 })
        {
            chip.IsEnabled = ready;
        }

        // When AI works, the line names the model and where it runs — the note owner should be
        // able to tell local inference from a service without leaving the note. When it does not,
        // the line is the reason, because "greyed out with no explanation" is the state that makes
        // people think the app is broken.
        UpdateSelectionActions();

        AiStatus.Text = ready
            ? Strings.Format(
                "Note_AiReadyFormat",
                _aiCapability.ModelId ?? string.Empty,
                Strings.Get($"Note_AiAccelerator{_aiCapability.Accelerator}"))
            : Strings.Get($"Note_AiUnavailable{_aiCapability.Availability}");
    }

    private void OnAiActionChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AiTextAction action })
        {
            AiFlyout.Hide();
            RunAi(action, RewriteStyle.Concise);
        }
    }

    private void OnAiRewriteChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RewriteStyle style })
        {
            AiFlyout.Hide();
            RunAi(AiTextAction.Rewrite, style);
        }
    }

    private void OnAiAskChipClicked(object sender, RoutedEventArgs e)
    {
        AiFlyout.Hide();
        AskRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnAiRelatedClicked(object sender, RoutedEventArgs e)
    {
        AiFlyout.Hide();
        RelatedRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRemindCustomClicked(object sender, RoutedEventArgs e)
    {
        MoreFlyout.Hide();
        CustomReminderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnAiTitleClicked(object sender, RoutedEventArgs e)
    {
        AiFlyout.Hide();
        RunAiTitle();
    }

    /// <summary>
    /// Proposes a heading line for a note whose first line makes a poor name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The note has no title field; its name is the first line with the markup stripped, so a note
    /// starting with <c>- [ ] 김대리 확인</c> is filed under that. Adding a heading line is how a
    /// note gets a name in this app's own notation, and it stays the user's text rather than a
    /// hidden field only the library can see.
    /// </para>
    /// <para>
    /// Presented through the same preview as a rewrite, because it is one: the proposal is the
    /// whole note with a line in front. That means the diff, the apply, the revision and the
    /// conflict check all work here without knowing this action exists.
    /// </para>
    /// </remarks>
    private async void RunAiTitle()
    {
        if (_ai is null || !_aiCapability.IsAvailable || string.IsNullOrWhiteSpace(ContentBox.Text))
        {
            return;
        }

        var original = ContentBox.Text;

        var preview = new AiPreviewWindow(Strings.Get("Note_AiSuggestTitle"));
        preview.Applied += (_, proposed) =>
        {
            if (TryApplyAiText(0, original.Length, original, proposed, "SuggestTitle"))
            {
                preview.Close();
            }
            else
            {
                preview.ShowFailure(Strings.Get("Ai_NoteChanged"));
            }
        };

        preview.Activate();
        preview.PlaceNear(AppWindow);

        var context = new NoteContext
        {
            NoteId = NoteId,
            Content = original,
            Title = CurrentTitle,
            LanguageTag = Strings.OverrideLocale ?? "ko-KR",
        };

        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            var title = await _ai.Service.SuggestTitleAsync(context, preview.CancellationToken);

            if (string.IsNullOrWhiteSpace(title))
            {
                preview.ShowFailure(Strings.Get("Ai_NothingFound"));
                return;
            }

            preview.ShowResult(new AiTextResult
            {
                Original = original,
                Proposed = WithHeading(original, title),
                ModelId = _aiCapability.ModelId,
                Elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started),
            });
        }
        catch (OperationCanceledException)
        {
            // The window is already gone: cancellation only happens when it closed.
        }
        catch (AiUnavailableException ex)
        {
            SetAiCapability(AiCapability.Unavailable(ex.Reason));
            preview.ShowFailure(Strings.Get($"Note_AiUnavailable{ex.Reason}"));
        }
        catch (Exception ex)
        {
            CrashLog.Write("Suggesting a title failed", ex);
            preview.ShowFailure(ex.Message);
        }
    }

    /// <summary>
    /// The note with <paramref name="title"/> as its heading.
    /// </summary>
    /// <remarks>
    /// A note that already opens with a heading has that line replaced rather than a second one
    /// added: two headings in a row is not a note with a better name, it is a note with a stutter.
    /// </remarks>
    private static string WithHeading(string content, string title)
    {
        var heading = "# " + title.TrimStart('#', ' ');
        var lines = NoteContent.NormalizeLineEndings(content).Split('\n');

        if (lines.Length > 0 && MarkdownEditing.StyleOf(lines[0]) is
            LineStyle.Heading1 or LineStyle.Heading2 or LineStyle.Heading3)
        {
            lines[0] = heading;
            return string.Join('\n', lines);
        }

        return heading + "\n" + content;
    }

    /// <summary>
    /// Shows the two selection actions while there is a selection to act on.
    /// </summary>
    /// <remarks>
    /// Visibility rather than opacity: at rest these must take no width at all, because the format
    /// bar already uses most of what a 보통 note has. They also stay hidden with no model, so the
    /// bar never grows to hold buttons that would refuse to run.
    /// </remarks>
    private void UpdateSelectionActions()
    {
        var offered = ContentBox.SelectionLength > 0
            && _ai is not null
            && _aiCapability.IsAvailable;

        var visibility = offered ? Visibility.Visible : Visibility.Collapsed;

        SelectionSeparator.Visibility = visibility;
        SelectionSummarize.Visibility = visibility;
        SelectionConcise.Visibility = visibility;
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e) => UpdateSelectionActions();

    private void OnSelectionSummarizeClicked(object sender, RoutedEventArgs e) =>
        RunAi(AiTextAction.Summarize, RewriteStyle.Concise);

    private void OnSelectionConciseClicked(object sender, RoutedEventArgs e) =>
        RunAi(AiTextAction.Rewrite, RewriteStyle.Concise);

    private void OnAiListChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AiListAction action })
        {
            AiFlyout.Hide();
            RunAiList(action);
        }
    }

    /// <summary>
    /// Asks the model for a list of proposals — tasks or tags — and confirms them one by one.
    /// </summary>
    /// <remarks>
    /// Unlike the text actions this always reads the whole note. A tag describes the note, and a
    /// deadline mentioned three lines above the selection is still this note's deadline.
    /// </remarks>
    private async void RunAiList(AiListAction action)
    {
        if (_ai is null || !_aiCapability.IsAvailable || string.IsNullOrWhiteSpace(ContentBox.Text))
        {
            return;
        }

        var context = new NoteContext
        {
            NoteId = NoteId,
            Content = ContentBox.Text,
            Title = CurrentTitle,
            LanguageTag = Strings.OverrideLocale ?? "ko-KR",
        };

        var window = new AiProposalWindow(Strings.Get($"Note_Ai{action}"));
        window.Activate();
        window.PlaceNear(AppWindow);

        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            if (action == AiListAction.ExtractTasks)
            {
                var tasks = await _ai.Service.ExtractTasksAsync(context, window.CancellationToken);

                window.Applied += (_, selected) =>
                {
                    ApplyTasks(selected.Select(index => tasks[index]).ToList());
                    window.Close();
                };

                window.ShowProposals(
                    [.. tasks.Select(DescribeTask)],
                    _aiCapability.ModelId,
                    System.Diagnostics.Stopwatch.GetElapsedTime(started));
            }
            else
            {
                var carried = TagParser.Parse(ContentBox.Text);

                // Asked of the vectors before the model. A note about the same thing as three
                // tagged notes almost always wants one of their tags, and finding that out is a
                // cosine over stored vectors rather than the 45 seconds the model takes to invent
                // one. The model is the fallback for a note with no neighbours — a new subject,
                // or a library that has not been indexed yet.
                var nearby = _neighbourhood is null
                    ? []
                    : await _neighbourhood.NearbyTagsAsync(
                        NoteId,
                        [.. carried.Select(tag => tag.Name)],
                        cancellationToken: window.CancellationToken);

                var tags = nearby.Count > 0
                    ? [.. nearby.Select(name => new SuggestedTag(name, 1.0))]
                    : await _ai.Service.SuggestTagsAsync(context, window.CancellationToken);

                // A tag the note already carries is not a proposal; offering it would only invite
                // the user to accept a duplicate.
                var fresh = tags
                    .Where(tag => !carried
                        .Any(existing => string.Equals(
                            existing.NormalizedName,
                            Tag.Normalize(tag.Name),
                            StringComparison.Ordinal)))
                    .ToList();

                window.Applied += (_, selected) =>
                {
                    ApplyTags(selected.Select(index => fresh[index].Name).ToList());
                    window.Close();
                };

                // A neighbour's tag has no confidence to report — it is a fact about other notes,
                // not a guess — so it says where it came from instead of showing a fabricated 100%.
                var fromNeighbours = nearby.Count > 0;

                window.ShowProposals(
                    [.. fresh.Select(tag => new AiProposal(
                        "#" + tag.Name,
                        fromNeighbours
                            ? Strings.Get("Ai_TagFromRelated")
                            : Strings.Format("Ai_ConfidenceFormat", (int)Math.Round(tag.Confidence * 100))))],
                    fromNeighbours ? null : _aiCapability.ModelId,
                    System.Diagnostics.Stopwatch.GetElapsedTime(started));
            }
        }
        catch (OperationCanceledException)
        {
            // The window closed; nothing left to show the result to.
        }
        catch (AiUnavailableException ex)
        {
            SetAiCapability(AiCapability.Unavailable(ex.Reason));
            window.ShowFailure(Strings.Get($"Note_AiUnavailable{ex.Reason}"));
        }
        catch (Exception ex)
        {
            CrashLog.Write($"AI action {action} failed", ex);
            window.ShowFailure(ex.Message);
        }
    }

    /// <summary>Renders a task the way the confirmation list should read it.</summary>
    private static AiProposal DescribeTask(ExtractedTask task)
    {
        var parts = new List<string>();

        if (task.DueAt is { } due)
        {
            parts.Add(Strings.Format("Ai_TaskDueFormat", due.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
        }

        if (!string.IsNullOrWhiteSpace(task.Assignee))
        {
            parts.Add(Strings.Format("Ai_TaskAssigneeFormat", task.Assignee));
        }

        if (task.Priority != TaskPriority.Normal)
        {
            parts.Add(Strings.Get($"Ai_Priority{task.Priority}"));
        }

        return new AiProposal(task.Title, parts.Count == 0 ? null : string.Join(" · ", parts));
    }

    /// <summary>
    /// Writes confirmed tasks into the note as checklist items, and schedules the ones with a date.
    /// </summary>
    /// <remarks>
    /// The body is where a checklist lives — <c>checklist_items</c> is only a projection of it — so
    /// appending Markdown is what actually creates the tasks. A deadline becomes a reminder on this
    /// note; it is not written into the line, because the line is the user's text and a timestamp
    /// pasted into it is not something they would have typed.
    /// </remarks>
    private void ApplyTasks(IReadOnlyList<ExtractedTask> tasks)
    {
        if (tasks.Count == 0)
        {
            return;
        }

        AppendLines(tasks.Select(task => "- [ ] " + task.Title), nameof(AiListAction.ExtractTasks));

        foreach (var due in tasks.Select(task => task.DueAt).OfType<DateTimeOffset>())
        {
            ReminderAtRequested?.Invoke(this, due);
        }
    }

    /// <summary>Appends confirmed tags to the note, which is where tags actually live.</summary>
    private void ApplyTags(IReadOnlyList<string> names)
    {
        if (names.Count > 0)
        {
            AppendLines(
                [string.Join(" ", names.Select(name => "#" + name))],
                nameof(AiListAction.SuggestTags));
        }
    }

    /// <summary>
    /// Adds lines to the end of the note as one undoable edit.
    /// </summary>
    /// <remarks>
    /// Goes through the editor rather than the text property so Ctrl+Z removes everything that was
    /// just applied in a single press, the same as any other block edit.
    /// </remarks>
    private void AppendLines(IEnumerable<string> lines, string? actionName = null)
    {
        var current = ContentBox.Text;
        var separator = current.Length == 0 || current.EndsWith('\n') ? string.Empty : "\n";
        var block = string.Join("\n", lines);

        if (block.Length == 0)
        {
            return;
        }

        var appended = current + separator + block;

        // Suppressed for an AI append for the same reason as an AI rewrite: the write is the
        // model's, and the debounced path would file it as the user's typing.
        _suppressChangeEvents = actionName is not null;
        ApplyEditorState(new NoteTextState(appended, appended.Length, 0));
        _suppressChangeEvents = false;

        if (actionName is null)
        {
            RaiseTextChanged();
        }
        else
        {
            RaiseAiApplied(actionName);
        }
    }

    /// <summary>
    /// Runs one AI action against the selection, or the whole note when nothing is selected.
    /// </summary>
    /// <remarks>
    /// The span is captured now and re-checked at apply time. Inference takes tens of seconds on
    /// CPU, which is long enough for the user to keep typing, and text written meanwhile must not
    /// be overwritten by a proposal that never saw it.
    /// </remarks>
    private async void RunAi(AiTextAction action, RewriteStyle style)
    {
        if (_ai is null || !_aiCapability.IsAvailable)
        {
            return;
        }

        var text = ContentBox.Text;
        var start = ContentBox.SelectionLength > 0 ? ContentBox.SelectionStart : 0;
        var length = ContentBox.SelectionLength > 0 ? ContentBox.SelectionLength : text.Length;
        var target = text.Substring(start, length);

        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        // The name the history will show. A rewrite is identified by its tone, because "재작성"
        // alone does not say which of the four the note was put through.
        var actionName = action == AiTextAction.Rewrite ? $"Rewrite{style}" : action.ToString();

        var preview = new AiPreviewWindow(Strings.Get($"Note_Ai{action}"));
        preview.Applied += (_, proposed) =>
        {
            if (TryApplyAiText(start, length, target, proposed, actionName))
            {
                preview.Close();
            }
            else
            {
                preview.ShowFailure(Strings.Get("Ai_NoteChanged"));
            }
        };

        preview.Activate();
        preview.PlaceNear(AppWindow);

        var context = new NoteContext
        {
            NoteId = NoteId,
            Content = text,
            SelectedText = ContentBox.SelectionLength > 0 ? target : null,
            Title = CurrentTitle,
            LanguageTag = Strings.OverrideLocale ?? "ko-KR",
        };

        try
        {
            var result = action switch
            {
                AiTextAction.Summarize => await _ai.Service.SummarizeAsync(context, preview.CancellationToken),
                AiTextAction.Organize => await _ai.Service.OrganizeAsync(context, preview.CancellationToken),
                _ => await _ai.Service.RewriteAsync(context, style, preview.CancellationToken),
            };

            preview.ShowResult(result);
        }
        catch (OperationCanceledException)
        {
            // The window is already gone: cancellation only happens when it closed.
        }
        catch (AiUnavailableException ex)
        {
            SetAiCapability(AiCapability.Unavailable(ex.Reason));
            preview.ShowFailure(Strings.Get($"Note_AiUnavailable{ex.Reason}"));
        }
        catch (Exception ex)
        {
            CrashLog.Write($"AI action {action} failed", ex);
            preview.ShowFailure(ex.Message);
        }
    }

    /// <summary>
    /// Writes an accepted proposal into the note, or refuses if the text it was made from is gone.
    /// </summary>
    /// <remarks>
    /// The edit goes through the same path as a formatting command, so Ctrl+Z undoes an applied
    /// suggestion in one press and the autosave pipeline records it as an ordinary revision — an
    /// AI edit is not a special kind of write, it is just a write the user approved.
    /// </remarks>
    private bool TryApplyAiText(int start, int length, string original, string proposed, string actionName)
    {
        var current = ContentBox.Text;

        if (start + length > current.Length
            || !string.Equals(current.Substring(start, length), original, StringComparison.Ordinal))
        {
            return false;
        }

        // Applied through the editor, so Ctrl+Z still takes it back in one press, but with the
        // ordinary save suppressed: this write goes to storage as an AI replacement rather than
        // as another keystroke, and the debounced path cannot label it that way.
        _suppressChangeEvents = true;
        ApplyEditorState(new NoteTextState(
            current.Remove(start, length).Insert(start, proposed),
            start,
            proposed.Length));
        _suppressChangeEvents = false;

        RaiseAiApplied(actionName);
        return true;
    }

    private void RaiseAiApplied(string actionName) =>
        AiApplied?.Invoke(this, new AiEdit(CurrentTitle, ContentBox.Text, actionName));
}
