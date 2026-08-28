namespace DeskNote.Core.Ai;

public enum AiAvailability
{
    Available = 0,

    /// <summary>No local model has been downloaded yet. The note app is fully usable regardless.</summary>
    ModelNotInstalled = 1,

    /// <summary>The AI worker process is not running or the named pipe could not be reached.</summary>
    WorkerUnavailable = 2,

    /// <summary>The user turned local AI off in settings.</summary>
    Disabled = 3,
}

/// <summary>Where inference actually ran, resolved at runtime rather than assumed from "has GPU".</summary>
public enum AiAccelerator
{
    Unknown = 0,
    Cpu = 1,
    Gpu = 2,
    Npu = 3,
}

/// <summary>
/// Result of probing the local AI stack. Probing happens in the background after notes are
/// restored; the app must never block startup on it (report p14: "앱 시작 = 모델 시작" 금지).
/// </summary>
public sealed record AiCapability(
    AiAvailability Availability,
    string? ProviderName = null,
    string? ModelId = null,
    AiAccelerator Accelerator = AiAccelerator.Unknown,
    long EstimatedMemoryBytes = 0)
{
    public bool IsAvailable => Availability == AiAvailability.Available;

    public static AiCapability Unavailable(AiAvailability reason) => new(reason);
}

/// <summary>Thrown when an AI action is invoked while <see cref="AiCapability.IsAvailable"/> is false.</summary>
public sealed class AiUnavailableException(AiAvailability reason)
    : InvalidOperationException($"Local AI is unavailable: {reason}.")
{
    public AiAvailability Reason { get; } = reason;
}
