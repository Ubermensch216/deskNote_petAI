using DeskNote.Core.Abstractions;

namespace DeskNote.Core.Ai;

/// <summary>
/// The AI service used until the worker exists (and whenever the user disables local AI).
/// It always reports unavailable, which keeps every AI affordance in the UI disabled while the
/// note application itself stays 100% functional.
/// </summary>
public sealed class NullAiService(AiAvailability reason = AiAvailability.ModelNotInstalled) : ILocalAiService
{
    private readonly AiCapability _capability = AiCapability.Unavailable(reason);

    public Task<AiCapability> ProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_capability);

    /// <summary>Does nothing, and says so by completing — warming is best effort by contract.</summary>
    public Task WarmAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<AiTextResult> SummarizeAsync(NoteContext context, CancellationToken cancellationToken = default) =>
        throw Unavailable();

    public Task<AiTextResult> OrganizeAsync(NoteContext context, CancellationToken cancellationToken = default) =>
        throw Unavailable();

    public Task<AiTextResult> RewriteAsync(
        NoteContext context,
        RewriteStyle style,
        CancellationToken cancellationToken = default) => throw Unavailable();

    public Task<IReadOnlyList<ExtractedTask>> ExtractTasksAsync(
        NoteContext context,
        CancellationToken cancellationToken = default) => throw Unavailable();

    public Task<IReadOnlyList<SuggestedTag>> SuggestTagsAsync(
        NoteContext context,
        CancellationToken cancellationToken = default) => throw Unavailable();

    public Task<ParsedReminder?> ParseReminderAsync(
        string phrase,
        DateTimeOffset now,
        string languageTag = "ko-KR",
        CancellationToken cancellationToken = default) => throw Unavailable();

    public Task<string> SuggestTitleAsync(
        NoteContext context,
        CancellationToken cancellationToken = default) => throw Unavailable();

    /// <summary>Throws eagerly rather than at first enumeration; callers must probe before streaming.</summary>
    public IAsyncEnumerable<string> StreamAnswerAsync(
        AiQuery query,
        CancellationToken cancellationToken = default) => throw Unavailable();

    private AiUnavailableException Unavailable() => new(_capability.Availability);
}
