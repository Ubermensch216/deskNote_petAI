-- Note embeddings for semantic retrieval (report p8).
--
-- The vector is a BLOB of little-endian float32, one per dimension, and `dim` records how many.
-- Storing the dimension per row rather than assuming one lets a model change be detected and the
-- stale rows rebuilt, instead of silently comparing vectors from two different models.
--
-- The report expects sqlite-vec here and also flags it as pre-v1 with breaking changes possible.
-- This table is the version that ships: ordinary SQLite, no native extension to load, scanned in
-- process. At the note counts this app is built for that costs a few milliseconds, and everything
-- above IVectorIndex is unaware either way — which is the point of the interface.

CREATE TABLE note_embeddings (
    note_id       TEXT    NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
    chunk_ordinal INTEGER NOT NULL,
    model         TEXT    NOT NULL,
    dim           INTEGER NOT NULL,
    embedding     BLOB    NOT NULL,
    updated_at    TEXT    NOT NULL,
    PRIMARY KEY (note_id, chunk_ordinal)
);

CREATE INDEX idx_note_embeddings_note ON note_embeddings(note_id);
