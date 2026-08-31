using DeskNote.Core.Models;

namespace DeskNote.Data.Tests;

/// <summary>
/// The vector store behind semantic search. The vectors here are hand-made rather than produced by
/// a model: what is under test is storage, scoping and ranking, none of which should depend on an
/// embedding model being installed on the machine running the tests.
/// </summary>
public class VectorIndexTests
{
    private const string Model = "bge-m3:latest";

    private static float[] Vector(params float[] values) => values;

    private static Note NewNote(string content = "본문") => new()
    {
        Id = Guid.NewGuid(),
        Content = content,
    };

    [Fact]
    public async Task The_nearest_vector_comes_first()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var near = NewNote();
        var far = NewNote();
        await db.Notes.AddAsync(near);
        await db.Notes.AddAsync(far);

        await index.UpsertAsync(near.Id, 0, Vector(1, 0, 0));
        await index.UpsertAsync(far.Id, 0, Vector(0, 1, 0));

        var hits = await index.SearchAsync(Vector(0.9f, 0.1f, 0));

        Assert.Equal(near.Id, hits[0].NoteId);
        Assert.True(hits[0].Distance < hits[1].Distance);
    }

    /// <summary>Direction is what a cosine compares; a longer vector of the same direction is the same meaning.</summary>
    [Fact]
    public async Task Magnitude_does_not_change_the_ranking()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var note = NewNote();
        await db.Notes.AddAsync(note);
        await index.UpsertAsync(note.Id, 0, Vector(10, 0, 0));

        var hits = await index.SearchAsync(Vector(0.01f, 0, 0));

        Assert.Equal(0, Assert.Single(hits).Distance, 5);
    }

    [Fact]
    public async Task A_note_matching_on_several_chunks_is_returned_once()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var note = NewNote();
        await db.Notes.AddAsync(note);

        await index.UpsertAsync(note.Id, 0, Vector(1, 0, 0));
        await index.UpsertAsync(note.Id, 1, Vector(0.99f, 0.01f, 0));

        var hits = await index.SearchAsync(Vector(1, 0, 0));

        Assert.Equal(0, Assert.Single(hits).ChunkOrdinal);
    }

    [Fact]
    public async Task Re_embedding_a_chunk_replaces_the_old_vector()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var note = NewNote();
        await db.Notes.AddAsync(note);

        await index.UpsertAsync(note.Id, 0, Vector(1, 0, 0));
        await index.UpsertAsync(note.Id, 0, Vector(0, 1, 0));

        var hits = await index.SearchAsync(Vector(1, 0, 0));

        Assert.Equal(1.0, Assert.Single(hits).Distance, 5);
    }

    /// <summary>A shortened note leaves chunks behind that would go on matching text it lost.</summary>
    [Fact]
    public async Task Trimming_drops_the_chunks_a_shrunken_note_no_longer_has()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var note = NewNote();
        await db.Notes.AddAsync(note);

        await index.UpsertAsync(note.Id, 0, Vector(1, 0, 0));
        await index.UpsertAsync(note.Id, 1, Vector(0, 1, 0));
        await index.TrimAsync(note.Id, keepBelow: 1);

        var hits = await index.SearchAsync(Vector(0, 1, 0), topK: 10);

        Assert.Equal(0, Assert.Single(hits).ChunkOrdinal);
    }

    [Fact]
    public async Task A_scoped_search_reads_only_the_named_notes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var wanted = NewNote();
        var other = NewNote();
        await db.Notes.AddAsync(wanted);
        await db.Notes.AddAsync(other);

        await index.UpsertAsync(wanted.Id, 0, Vector(0, 1, 0));
        await index.UpsertAsync(other.Id, 0, Vector(1, 0, 0));

        var hits = await index.SearchAsync(Vector(1, 0, 0), scopedNoteIds: [wanted.Id]);

        Assert.Equal(wanted.Id, Assert.Single(hits).NoteId);
    }

    [Fact]
    public async Task A_deleted_note_stops_being_retrievable()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var note = NewNote();
        await db.Notes.AddAsync(note);
        await index.UpsertAsync(note.Id, 0, Vector(1, 0, 0));
        await db.Notes.SoftDeleteAsync(note.Id);

        Assert.Empty(await index.SearchAsync(Vector(1, 0, 0)));
    }

    /// <summary>Purging a note must not leave its vectors behind as orphans.</summary>
    [Fact]
    public async Task Purging_a_note_removes_its_vectors()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var note = NewNote();
        await db.Notes.AddAsync(note);
        await index.UpsertAsync(note.Id, 0, Vector(1, 0, 0));

        await db.Notes.SoftDeleteAsync(note.Id);
        await db.Notes.PurgeAsync(note.Id);

        Assert.Empty(await index.FindUnindexedAsync(10));
        Assert.Empty(await index.SearchAsync(Vector(1, 0, 0)));
    }

    /// <summary>
    /// Vectors from two models are not comparable, so a model change must read as "not indexed"
    /// rather than as a set of nonsense distances.
    /// </summary>
    [Fact]
    public async Task Vectors_from_another_model_are_ignored()
    {
        await using var db = await TestDatabase.CreateAsync();

        var note = NewNote();
        await db.Notes.AddAsync(note);

        await new SqliteVectorIndex(db.Factory, "old-model").UpsertAsync(note.Id, 0, Vector(1, 0, 0));

        var current = new SqliteVectorIndex(db.Factory, Model);

        Assert.Empty(await current.SearchAsync(Vector(1, 0, 0)));
        Assert.Contains(note.Id, await current.FindUnindexedAsync(10));
    }

    [Fact]
    public async Task A_vector_of_another_length_is_skipped_rather_than_compared()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var note = NewNote();
        await db.Notes.AddAsync(note);
        await index.UpsertAsync(note.Id, 0, Vector(1, 0, 0, 0));

        Assert.Empty(await index.SearchAsync(Vector(1, 0, 0)));
    }

    [Fact]
    public async Task An_indexed_note_is_no_longer_reported_as_unindexed()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var note = NewNote("찾을 수 있는 본문");
        await db.Notes.AddAsync(note);

        Assert.Contains(note.Id, await index.FindUnindexedAsync(10));

        await index.UpsertAsync(note.Id, 0, Vector(1, 0, 0));

        Assert.DoesNotContain(note.Id, await index.FindUnindexedAsync(10));
    }

    /// <summary>An empty note has nothing to embed, so it is not work waiting to be done.</summary>
    [Fact]
    public async Task An_empty_note_is_not_queued_for_indexing()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        await db.Notes.AddAsync(NewNote(string.Empty));

        Assert.Empty(await index.FindUnindexedAsync(10));
    }

    /// <summary>
    /// The source note has to be absent from its own neighbours, or every list would open with
    /// the note the user is already looking at.
    /// </summary>
    [Fact]
    public async Task A_note_is_not_its_own_neighbour()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var source = NewNote();
        var other = NewNote();
        await db.Notes.AddAsync(source);
        await db.Notes.AddAsync(other);

        await index.UpsertAsync(source.Id, 0, Vector(1, 0, 0));
        await index.UpsertAsync(other.Id, 0, Vector(0.9f, 0.1f, 0));

        var hits = await index.FindSimilarAsync(source.Id);

        Assert.Equal(other.Id, Assert.Single(hits).NoteId);
    }

    [Fact]
    public async Task Neighbours_come_back_nearest_first()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var source = NewNote();
        var near = NewNote();
        var far = NewNote();
        await db.Notes.AddAsync(source);
        await db.Notes.AddAsync(near);
        await db.Notes.AddAsync(far);

        await index.UpsertAsync(source.Id, 0, Vector(1, 0, 0));
        await index.UpsertAsync(near.Id, 0, Vector(0.95f, 0.05f, 0));
        await index.UpsertAsync(far.Id, 0, Vector(0, 1, 0));

        var hits = await index.FindSimilarAsync(source.Id);

        Assert.Equal([near.Id, far.Id], hits.Select(h => h.NoteId));
    }

    /// <summary>
    /// A long note that shares one paragraph with another is related through that paragraph.
    /// Scoring by the closest pair is what keeps the rest of it from voting the match away.
    /// </summary>
    [Fact]
    public async Task A_note_is_scored_by_its_closest_chunk_not_its_average()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var source = NewNote();
        var shares = NewNote();
        await db.Notes.AddAsync(source);
        await db.Notes.AddAsync(shares);

        await index.UpsertAsync(source.Id, 0, Vector(1, 0, 0));

        // One chunk matches exactly; the other two are unrelated and would drag an average down.
        await index.UpsertAsync(shares.Id, 0, Vector(0, 1, 0));
        await index.UpsertAsync(shares.Id, 1, Vector(1, 0, 0));
        await index.UpsertAsync(shares.Id, 2, Vector(0, 0, 1));

        var hit = Assert.Single(await index.FindSimilarAsync(source.Id));

        Assert.Equal(1, hit.ChunkOrdinal);
        Assert.True(hit.Distance < 0.001);
    }

    [Fact]
    public async Task A_deleted_note_is_not_offered_as_a_neighbour()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var source = NewNote();
        var removed = NewNote();
        await db.Notes.AddAsync(source);
        await db.Notes.AddAsync(removed);

        await index.UpsertAsync(source.Id, 0, Vector(1, 0, 0));
        await index.UpsertAsync(removed.Id, 0, Vector(1, 0, 0));
        await db.Notes.SoftDeleteAsync(removed.Id);

        Assert.Empty(await index.FindSimilarAsync(source.Id));
    }

    /// <summary>A note written before the index existed has no vectors, and so has no neighbours.</summary>
    [Fact]
    public async Task A_note_with_no_vectors_has_no_neighbours()
    {
        await using var db = await TestDatabase.CreateAsync();
        var index = new SqliteVectorIndex(db.Factory, Model);

        var source = NewNote();
        var other = NewNote();
        await db.Notes.AddAsync(source);
        await db.Notes.AddAsync(other);

        await index.UpsertAsync(other.Id, 0, Vector(1, 0, 0));

        Assert.Empty(await index.FindSimilarAsync(source.Id));
    }

    /// <summary>Vectors from another model are not comparable, so they are not compared.</summary>
    [Fact]
    public async Task Neighbours_from_another_model_are_ignored()
    {
        await using var db = await TestDatabase.CreateAsync();

        var source = NewNote();
        var other = NewNote();
        await db.Notes.AddAsync(source);
        await db.Notes.AddAsync(other);

        await new SqliteVectorIndex(db.Factory, Model).UpsertAsync(source.Id, 0, Vector(1, 0, 0));
        await new SqliteVectorIndex(db.Factory, "other-model").UpsertAsync(other.Id, 0, Vector(1, 0, 0));

        Assert.Empty(await new SqliteVectorIndex(db.Factory, Model).FindSimilarAsync(source.Id));
    }
}
