using DeskNote.Core.Abstractions;
using DeskNote.Core.Models;
using DeskNote.Core.Services;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DeskNote.App.Services;

/// <summary>
/// Watches for due reminders and raises Windows toasts for them.
/// </summary>
/// <remarks>
/// <para>
/// Polls rather than arming a timer per reminder: a single cheap indexed query every half minute
/// survives sleep, hibernation and clock changes, where a long-armed timer silently does not fire
/// at all after a resume.
/// </para>
/// <para>
/// The half-minute cadence bounds how late a reminder can be, which is the right trade for a note
/// app — being 30 seconds late is unnoticeable, missing it entirely is not.
/// </para>
/// </remarks>
public sealed class ReminderService(
    IReminderRepository reminders,
    INoteRepository notes,
    IClock clock,
    DispatcherQueue dispatcher) : IDisposable
{
    /// <summary>Tag used on every DeskNote toast, so the note id can be recovered on activation.</summary>
    public const string NoteIdArgument = "noteId";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly DispatcherQueueTimer _timer = dispatcher.CreateTimer();
    private bool _running;

    /// <summary>Raised when the user clicks a reminder toast, with the note it belongs to.</summary>
    public event EventHandler<Guid>? NoteRequested;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _timer.Interval = PollInterval;
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();

        AppNotificationManager.Default.NotificationInvoked += OnToastInvoked;
        AppNotificationManager.Default.Register();

        // Catch anything that came due while the app was closed, without waiting a full interval.
        _ = CheckAsync();
    }

    /// <summary>
    /// Brings up the note behind a clicked toast.
    /// </summary>
    /// <remarks>
    /// Activation arrives on a background thread, so the hop onto the UI queue is required before
    /// anything touches a window.
    /// </remarks>
    private void OnToastInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (!args.Arguments.TryGetValue(NoteIdArgument, out var raw) || !Guid.TryParse(raw, out var noteId))
        {
            return;
        }

        dispatcher.TryEnqueue(() => NoteRequested?.Invoke(this, noteId));
    }

    public void Dispose()
    {
        _timer.Stop();

        if (_running)
        {
            AppNotificationManager.Default.NotificationInvoked -= OnToastInvoked;
            AppNotificationManager.Default.Unregister();
        }

        _running = false;
    }

    /// <summary>Fires any reminders that are due and moves repeating ones to their next occurrence.</summary>
    public async Task CheckAsync()
    {
        try
        {
            var now = clock.UtcNow;
            var due = await reminders.ListDueAsync(now).ConfigureAwait(true);

            foreach (var reminder in due)
            {
                var note = await notes.GetAsync(reminder.NoteId).ConfigureAwait(true);
                if (note is null || note.IsDeleted)
                {
                    continue;
                }

                Show(reminder, note);

                var outcome = ReminderScheduler.Resolve(reminder, now);
                if (outcome.NextDueAt is { } next)
                {
                    await reminders.RescheduleAsync(reminder.Id, next).ConfigureAwait(true);
                }
                else
                {
                    await reminders.MarkNotifiedAsync(reminder.Id, now).ConfigureAwait(true);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A reminder that cannot be shown must not take the note windows with it.
            CrashLog.Write("Reminder check failed", ex);
        }
    }

    private static void Show(Reminder reminder, Note note)
    {
        var heading = string.IsNullOrWhiteSpace(note.Title)
            ? FirstLine(note.Content)
            : note.Title;

        var body = string.IsNullOrWhiteSpace(note.Content) ? "메모 알림" : Excerpt(note.Content);

        var toast = new AppNotificationBuilder()
            .AddArgument(NoteIdArgument, note.Id.ToString())
            .AddText(string.IsNullOrWhiteSpace(heading) ? "DeskNote" : heading)
            .AddText(body)
            .BuildNotification();

        AppNotificationManager.Default.Show(toast);
    }

    private static string FirstLine(string content) =>
        content.Split('\n').FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? "DeskNote";

    private static string Excerpt(string content)
    {
        const int MaxLength = 120;
        var flattened = content.Replace('\n', ' ').Trim();
        return flattened.Length <= MaxLength ? flattened : flattened[..MaxLength] + "…";
    }
}
