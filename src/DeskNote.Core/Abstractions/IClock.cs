namespace DeskNote.Core.Abstractions;

/// <summary>
/// Indirection over the system clock so reminder scheduling and revision timestamps are testable
/// without waiting in real time.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Local time, used for reminder due dates the user reads and types.</summary>
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset Now => DateTimeOffset.Now;
}
