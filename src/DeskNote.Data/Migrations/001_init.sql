-- DeskNote initial schema.
--
-- Timestamps are ISO-8601 UTC text ("yyyy-MM-ddTHH:mm:ss.fffffffZ") so they sort lexicographically
-- and stay readable in a SQLite browser. Ids are UUID text.

CREATE TABLE notebooks (
    id          TEXT    NOT NULL PRIMARY KEY,
    name        TEXT    NOT NULL,
    parent_id   TEXT    NULL REFERENCES notebooks(id) ON DELETE CASCADE,
    ordinal     INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL
);

CREATE INDEX idx_notebooks_parent ON notebooks(parent_id);

CREATE TABLE notes (
    id             TEXT    NOT NULL PRIMARY KEY,
    title          TEXT    NOT NULL DEFAULT '',
    content        TEXT    NOT NULL DEFAULT '',
    format         INTEGER NOT NULL DEFAULT 0,
    color_key      TEXT    NOT NULL DEFAULT 'yellow',
    opacity        REAL    NOT NULL DEFAULT 1.0,
    always_on_top  INTEGER NOT NULL DEFAULT 0,
    is_open        INTEGER NOT NULL DEFAULT 1,
    x              INTEGER NOT NULL DEFAULT 0,
    y              INTEGER NOT NULL DEFAULT 0,
    width          INTEGER NOT NULL DEFAULT 320,
    height         INTEGER NOT NULL DEFAULT 260,
    monitor_key    TEXT    NOT NULL DEFAULT '',
    size_preset    INTEGER NOT NULL DEFAULT 1,
    notebook_id    TEXT    NULL REFERENCES notebooks(id) ON DELETE SET NULL,
    created_at     TEXT    NOT NULL,
    updated_at     TEXT    NOT NULL,
    deleted_at     TEXT    NULL,
    rev            INTEGER NOT NULL DEFAULT 1
);

-- Startup restore reads exactly this set and must stay on the <500 ms budget (report p14).
CREATE INDEX idx_notes_open ON notes(is_open) WHERE deleted_at IS NULL;
CREATE INDEX idx_notes_updated ON notes(updated_at DESC);
CREATE INDEX idx_notes_notebook ON notes(notebook_id);
CREATE INDEX idx_notes_deleted ON notes(deleted_at);

-- Full-text search (report p6).
--
-- The trigram tokenizer is chosen over the default unicode61 because of Korean. unicode61 splits
-- on whitespace, so a note containing "수도관 누수" would only match the whole eojeol "수도관" and
-- never the substring "수도". trigram indexes 3-character windows, which gives substring matching
-- for Korean and English alike. Its one limitation is that MATCH needs at least 3 characters;
-- Fts5SearchIndex falls back to LIKE for shorter queries.
CREATE VIRTUAL TABLE notes_fts USING fts5(
    title,
    content,
    content='notes',
    content_rowid='rowid',
    tokenize="trigram case_sensitive 0"
);

CREATE TRIGGER notes_fts_after_insert AFTER INSERT ON notes BEGIN
    INSERT INTO notes_fts(rowid, title, content) VALUES (new.rowid, new.title, new.content);
END;

CREATE TRIGGER notes_fts_after_delete AFTER DELETE ON notes BEGIN
    INSERT INTO notes_fts(notes_fts, rowid, title, content)
    VALUES ('delete', old.rowid, old.title, old.content);
END;

-- Scoped to title and content on purpose: dragging or recoloring a note updates other columns
-- constantly, and must not churn the FTS index.
CREATE TRIGGER notes_fts_after_update AFTER UPDATE OF title, content ON notes BEGIN
    INSERT INTO notes_fts(notes_fts, rowid, title, content)
    VALUES ('delete', old.rowid, old.title, old.content);
    INSERT INTO notes_fts(rowid, title, content) VALUES (new.rowid, new.title, new.content);
END;

CREATE TABLE tags (
    id              TEXT NOT NULL PRIMARY KEY,
    name            TEXT NOT NULL,
    normalized_name TEXT NOT NULL UNIQUE
);

CREATE TABLE note_tags (
    note_id TEXT NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
    tag_id  TEXT NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
    PRIMARY KEY (note_id, tag_id)
);

CREATE INDEX idx_note_tags_tag ON note_tags(tag_id);

CREATE TABLE checklist_items (
    id      TEXT    NOT NULL PRIMARY KEY,
    note_id TEXT    NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL DEFAULT 0,
    text    TEXT    NOT NULL DEFAULT '',
    is_done INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX idx_checklist_note ON checklist_items(note_id, ordinal);

CREATE TABLE reminders (
    id              TEXT NOT NULL PRIMARY KEY,
    note_id         TEXT NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
    due_at          TEXT NOT NULL,
    recurrence_rule TEXT NULL,
    completed_at    TEXT NULL,
    notified_at     TEXT NULL
);

-- The scheduler polls for "due, not yet notified, not completed"; this index serves that directly.
CREATE INDEX idx_reminders_due ON reminders(due_at) WHERE completed_at IS NULL;
CREATE INDEX idx_reminders_note ON reminders(note_id);

-- Written before any destructive content replacement so an AI rewrite can always be undone
-- (report p15: AI rewrite → 원본 version 보존).
CREATE TABLE note_revisions (
    id          TEXT    NOT NULL PRIMARY KEY,
    note_id     TEXT    NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
    content     TEXT    NOT NULL,
    created_at  TEXT    NOT NULL,
    source      INTEGER NOT NULL DEFAULT 0,
    action_name TEXT    NULL
);

CREATE INDEX idx_revisions_note ON note_revisions(note_id, created_at DESC);

CREATE TABLE attachments (
    id           TEXT    NOT NULL PRIMARY KEY,
    note_id      TEXT    NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
    kind         INTEGER NOT NULL DEFAULT 0,
    path         TEXT    NOT NULL,
    display_name TEXT    NULL,
    size_bytes   INTEGER NOT NULL DEFAULT 0,
    sha256       TEXT    NULL,
    created_at   TEXT    NOT NULL
);

CREATE INDEX idx_attachments_note ON attachments(note_id);

-- Non-secret application settings only. Cloud credentials belong in the Windows Credential
-- Locker, never here (report p15).
CREATE TABLE settings (
    key   TEXT NOT NULL PRIMARY KEY,
    value TEXT NOT NULL
);
