using DeskNote.Core.Ai;

namespace DeskNote.Core.Abstractions;

/// <summary>
/// The only AI surface the note application is allowed to depend on (report p17-18).
/// </summary>
/// <remarks>
/// <para>
/// Implementations keep model execution outside the DeskNote process. The current implementation
/// talks to the separately running Ollama process over local HTTP; the contract deliberately does
/// not expose that transport, so a dedicated worker can replace it without changing the note app.
/// A failure or resource exhaustion in the model host must be reported as AI unavailability and
/// must never make the note layer depend on model startup (report p10).
/// </para>
/// <para>
/// Every method requires <see cref="ProbeAsync"/> to have reported
/// <see cref="AiCapability.IsAvailable"/>; otherwise they throw
/// <see cref="AiUnavailableException"/>. The note app is fully functional with no model
/// installed, and deleting the model must never damage note data.
/// </para>
/// </remarks>
public interface ILocalAiService
{
    /// <summary>
    /// Reports whether local AI can run right now. Cheap, cancellable, and safe to call at
    /// startup on a background task; it must not load model weights.
    /// </summary>
    Task<AiCapability> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the worker to load the model now, so the next real call does not pay for it.
    /// </summary>
    /// <remarks>
    /// Best effort by contract: it never throws, never reports failure, and callers must work
    /// exactly as well when it does nothing. It is the one method here that may be called before
    /// <see cref="ProbeAsync"/> has said anything.
    /// </remarks>
    Task WarmAsync(CancellationToken cancellationToken = default);

    /// <summary>요약 — condenses the selection, or the whole note when nothing is selected.</summary>
    Task<AiTextResult> SummarizeAsync(NoteContext context, CancellationToken cancellationToken = default);

    /// <summary>정리 — restructures a raw note into headings and lists without inventing content.</summary>
    Task<AiTextResult> OrganizeAsync(NoteContext context, CancellationToken cancellationToken = default);

    /// <summary>재작성 — rewrites the text in the requested tone.</summary>
    Task<AiTextResult> RewriteAsync(
        NoteContext context,
        RewriteStyle style,
        CancellationToken cancellationToken = default);

    /// <summary>할 일 추출 — returns schema-validated tasks; the user confirms before any reminder is created.</summary>
    Task<IReadOnlyList<ExtractedTask>> ExtractTasksAsync(
        NoteContext context,
        CancellationToken cancellationToken = default);

    /// <summary>자동 태그 — proposes tags for the note; nothing is applied without acceptance.</summary>
    Task<IReadOnlyList<SuggestedTag>> SuggestTagsAsync(
        NoteContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 자연어 알림 — reads a moment out of a phrase, or returns null if it could not.
    /// </summary>
    /// <remarks>
    /// Short input and short output, which is why this one is usable interactively where the
    /// text actions are not: the model answers in a second or two rather than in half a minute.
    /// </remarks>
    Task<ParsedReminder?> ParseReminderAsync(
        string phrase,
        DateTimeOffset now,
        string languageTag = "ko-KR",
        CancellationToken cancellationToken = default);

    /// <summary>제목 제안 — one line for a note whose first line makes a poor name.</summary>
    Task<string> SuggestTitleAsync(NoteContext context, CancellationToken cancellationToken = default);

    /// <summary>메모 Q&amp;A — streams an answer so the sidecar shows tokens as they arrive.</summary>
    IAsyncEnumerable<string> StreamAnswerAsync(
        AiQuery query,
        CancellationToken cancellationToken = default);
}
