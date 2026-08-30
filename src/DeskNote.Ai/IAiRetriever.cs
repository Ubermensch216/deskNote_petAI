namespace DeskNote.Ai;

/// <summary>
/// Supplies the notes 메모 Q&amp;A is allowed to read for one question.
/// </summary>
/// <remarks>
/// Retrieval is deliberately not the AI provider's job: it needs the note store, and this
/// assembly does not reference it. Keeping the seam here means the first implementation can be
/// keyword-based and a later one embedding-based (report p8) without the provider changing.
/// </remarks>
public interface IAiRetriever
{
    Task<IReadOnlyList<RetrievedNote>> RetrieveAsync(
        DeskNote.Core.Ai.AiQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Retrieves nothing. Q&amp;A then answers that it could not find a relevant note.</summary>
public sealed class NoRetrieval : IAiRetriever
{
    public static NoRetrieval Instance { get; } = new();

    public Task<IReadOnlyList<RetrievedNote>> RetrieveAsync(
        DeskNote.Core.Ai.AiQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RetrievedNote>>([]);
}
