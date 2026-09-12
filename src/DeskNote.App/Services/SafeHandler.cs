namespace DeskNote.App.Services;

/// <summary>
/// Turns a callback into an event handler that cannot take the application down with it.
/// </summary>
/// <remarks>
/// <para>
/// An <c>async</c> lambda attached to an <see cref="EventHandler"/> is an <c>async void</c> method:
/// once it is past its first <c>await</c> there is no caller left to catch anything it throws, and
/// the exception goes straight to <c>Application.UnhandledException</c>. Wiring a note window's
/// events that way meant a failed database write while closing a note ended the process — and with
/// it the unsaved text that the failing write was trying to store.
/// </para>
/// <para>
/// The description is what gets logged, so it reads as the user's gesture ("Closing a note")
/// rather than the method that implemented it. Cancellation is not a failure and is not logged:
/// it is how shutdown tells pending work to stop.
/// </para>
/// </remarks>
internal static class SafeHandler
{
    /// <summary>Wraps an asynchronous handler that does not need the event's arguments.</summary>
    public static EventHandler Async(string description, Func<Task> handler) =>
        (_, _) => Run(description, handler);

    /// <summary>Wraps an asynchronous handler that takes the event's argument.</summary>
    public static EventHandler<T> Async<T>(string description, Func<T, Task> handler) =>
        (_, argument) => Run(description, () => handler(argument));

    /// <summary>Wraps an asynchronous handler that takes the sender as well as the argument.</summary>
    public static EventHandler<T> AsyncWithSender<T>(string description, Func<object?, T, Task> handler) =>
        (sender, argument) => Run(description, () => handler(sender, argument));

    /// <summary>
    /// Wraps a synchronous handler.
    /// </summary>
    /// <remarks>
    /// These throw back into whoever raised the event, which in WinUI is itself usually an
    /// <c>async void</c> handler — so opening a window that fails to construct ends the process
    /// just as surely as a failed await does.
    /// </remarks>
    public static EventHandler Sync(string description, Action handler) =>
        (_, _) => Run(description, handler);

    /// <summary>Wraps a synchronous handler that takes the sender.</summary>
    public static EventHandler SyncWithSender(string description, Action<object?> handler) =>
        (sender, _) => Run(description, () => handler(sender));

    /// <summary>Wraps a synchronous handler that takes the event's argument.</summary>
    public static EventHandler<T> Sync<T>(string description, Action<T> handler) =>
        (_, argument) => Run(description, () => handler(argument));

    private static void Run(string description, Action handler)
    {
        try
        {
            handler();
        }
        catch (OperationCanceledException)
        {
            // Shutdown, or a gesture the user replaced with another.
        }
        catch (Exception ex)
        {
            CrashLog.Write($"{description} failed", ex);
        }
    }

    private static async void Run(string description, Func<Task> handler)
    {
        try
        {
            await handler().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, or a gesture the user replaced with another.
        }
        catch (Exception ex)
        {
            CrashLog.Write($"{description} failed", ex);
        }
    }
}
