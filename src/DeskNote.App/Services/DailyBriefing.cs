using System.Globalization;
using System.Text;
using DeskNote.App.Views;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.App.Services;

/// <summary>
/// The day gathered into one note.
/// </summary>
/// <remarks>
/// <para>
/// The product's own order is 메모 → 기억 → AI → 자동화, and this is the first thing on the
/// automation end: nobody asks for it a second time, it just shows up having read everything.
/// </para>
/// <para>
/// It is also the one place where a 25-second model call costs nothing. Every other AI action in
/// this app is a person waiting; a briefing is assembled while they are reading the first line of
/// it, and the wait is a progress ring on a window they opened deliberately.
/// </para>
/// <para>
/// The result is an ordinary note. That is not a shortcut — it means the briefing is searchable,
/// editable, revisable and deletable by everything the app already does, and it needs no surface
/// of its own to live on.
/// </para>
/// <para>
/// Reminders are read from the library rows rather than from the reminder store: a summary row
/// already carries the note's next unfired reminder, so asking twice would be two queries for one
/// answer.
/// </para>
/// </remarks>
public sealed class DailyBriefing(
    INoteLibrary library,
    INoteRepository notes,
    IClock clock,
    LocalAiHost? ai = null)
{
    /// <summary>How far back "recently" reaches when gathering what the day should mention.</summary>
    private static readonly TimeSpan RecentWindow = TimeSpan.FromDays(1);

    /// <summary>How far ahead a reminder counts as part of today.</summary>
    private static readonly TimeSpan DueWindow = TimeSpan.FromDays(1);

    /// <summary>Notes read into the briefing. Past this it stops being a briefing.</summary>
    private const int MaxNotes = 12;

    /// <summary>
    /// Gathers the day and, when a model is available, writes it up.
    /// </summary>
    /// <remarks>
    /// The gathered facts are the product; the model only phrases them. With no model the briefing
    /// is still produced, as the plain list the facts already are — which is the same rule the
    /// rest of the app follows: AI improves the note layer, it is never load-bearing for it.
    /// </remarks>
    public async Task<string> ComposeAsync(CancellationToken cancellationToken = default)
    {
        var facts = await GatherAsync(cancellationToken).ConfigureAwait(false);

        if (facts.IsEmpty)
        {
            return Heading() + "\n\n" + Strings.Get("Briefing_Nothing");
        }

        var plain = facts.ToPlainText();

        if (ai is not { IsAvailable: true })
        {
            return plain;
        }

        try
        {
            var context = new Core.Ai.NoteContext
            {
                NoteId = Guid.Empty,
                Content = plain,
                LanguageTag = Strings.OverrideLocale ?? "ko-KR",
            };

            var written = await ai.Service.OrganizeAsync(context, cancellationToken).ConfigureAwait(false);

            // The model tidies the gathered facts; it does not get to replace them. An empty or
            // shrunken answer means the run went wrong, and the list is better than the loss.
            return string.IsNullOrWhiteSpace(written.Proposed) ? plain : written.Proposed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Composing the briefing with the model failed", ex);
            return plain;
        }
    }

    /// <summary>Everything the briefing is allowed to mention, read straight from the store.</summary>
    private async Task<BriefingFacts> GatherAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;

        var recent = await library
            .ListAsync(
                NoteQuery.Default with { Limit = MaxNotes, Sort = NoteSort.UpdatedDescending },
                cancellationToken)
            .ConfigureAwait(false);

        // An empty note has nothing to brief about, and one of them is always this briefing's own
        // note — created a moment ago and still blank while these facts are being gathered.
        var touched = recent
            .Where(note => now - note.UpdatedAt <= RecentWindow)
            .Where(note => !string.IsNullOrWhiteSpace(note.Preview))
            .ToList();

        var due = new List<(DateTimeOffset At, string Title)>();

        foreach (var note in recent)
        {
            if (note.NextReminderAt is not { } at
                || at - now > DueWindow
                || string.IsNullOrWhiteSpace(note.Preview))
            {
                continue;
            }

            due.Add((at, NoteRowFormat.TitleOrFallback(note.Title, note.Preview)));
        }

        // Unfinished checklist items across the notes that were touched, which is the closest
        // thing this app has to "what is still open".
        var open = new List<string>();

        foreach (var note in touched.Where(n => n.ChecklistTotal > n.ChecklistDone))
        {
            if (await notes.GetAsync(note.Id, cancellationToken).ConfigureAwait(false) is not { } full)
            {
                continue;
            }

            open.AddRange(ChecklistParser
                .Parse(full.Content)
                .Where(item => !item.IsDone)
                .Select(item => item.Text));
        }

        return new BriefingFacts(now, touched, due, open);
    }

    private string Heading() =>
        "# " + Strings.Format(
            "Briefing_TitleFormat",
            clock.Now.ToLocalTime().ToString("d", CultureInfo.CurrentCulture));

    /// <summary>What the day actually contains, before anything phrases it.</summary>
    private sealed record BriefingFacts(
        DateTimeOffset Now,
        IReadOnlyList<NoteSummary> Touched,
        IReadOnlyList<(DateTimeOffset At, string Title)> Due,
        IReadOnlyList<string> Open)
    {
        public bool IsEmpty => Touched.Count == 0 && Due.Count == 0 && Open.Count == 0;

        /// <summary>
        /// The facts as a note, in the app's own notation.
        /// </summary>
        /// <remarks>
        /// This is both the fallback when there is no model and the input when there is one. The
        /// same text serving both is what keeps the two versions saying the same things.
        /// </remarks>
        public string ToPlainText()
        {
            var builder = new StringBuilder();

            builder
                .Append("# ")
                .Append(Strings.Format(
                    "Briefing_TitleFormat",
                    Now.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)))
                .Append('\n');

            if (Due.Count > 0)
            {
                builder.Append('\n').Append(Strings.Get("Briefing_Due")).Append('\n');

                foreach (var (at, title) in Due.OrderBy(item => item.At))
                {
                    builder
                        .Append("- ")
                        .Append(at.ToLocalTime().ToString("t", CultureInfo.CurrentCulture))
                        .Append(" · ")
                        .Append(title)
                        .Append('\n');
                }
            }

            if (Open.Count > 0)
            {
                builder.Append('\n').Append(Strings.Get("Briefing_Open")).Append('\n');

                foreach (var item in Open.Take(MaxNotes))
                {
                    builder.Append("- [ ] ").Append(item).Append('\n');
                }
            }

            if (Touched.Count > 0)
            {
                builder.Append('\n').Append(Strings.Get("Briefing_Touched")).Append('\n');

                foreach (var note in Touched)
                {
                    builder
                        .Append("- ")
                        .Append(NoteRowFormat.TitleOrFallback(note.Title, note.Preview))
                        .Append('\n');
                }
            }

            return builder.ToString().TrimEnd();
        }
    }
}
