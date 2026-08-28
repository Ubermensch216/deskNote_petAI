using DeskNote.Core.Ai;

namespace DeskNote.Core.Abstractions;

/// <summary>
/// The only AI surface the note application is allowed to depend on (report p17-18).
/// </summary>
/// <remarks>
/// <para>
/// Implementations live behind a named pipe in a separate worker process, so a model that
/// exhausts memory or trips a GPU driver cannot take the note windows down with it (report p10).
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

    /// <summary>메모 Q&amp;A — streams an answer so the sidecar shows tokens as they arrive.</summary>
    IAsyncEnumerable<string> StreamAnswerAsync(
        AiQuery query,
        CancellationToken cancellationToken = default);
}
