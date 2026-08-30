namespace DeskNote.Core.Abstractions;

/// <summary>
/// Turns text into vectors for semantic retrieval (report p8).
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="ILocalAiService"/> on purpose. Embedding is a different model with a
/// different cost — milliseconds per chunk rather than tens of seconds per answer — and it runs on
/// a schedule of its own, whenever a note is saved. Tying it to the chat model would mean a note
/// could not be indexed unless the chat model was also installed.
/// </para>
/// <para>
/// Vectors are as sensitive as the text they came from: they stay inside the same database and the
/// same encryption boundary as the notes (report p16).
/// </para>
/// </remarks>
public interface IEmbeddingService
{
    /// <summary>Dimension of the vectors this service produces, or 0 when it has not been asked yet.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Embeds a batch of texts, in order.
    /// </summary>
    /// <remarks>
    /// Returns an empty list when embedding is unavailable rather than throwing. Indexing runs in
    /// the background on every save, and a missing model must degrade search, not raise errors
    /// behind the user's back.
    /// </remarks>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);
}
