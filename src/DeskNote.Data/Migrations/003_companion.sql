-- Companion data is additive. Older DeskNote builds ignore these tables and keep notes intact.

CREATE TABLE companion_profiles (
    id               TEXT NOT NULL PRIMARY KEY,
    name             TEXT NOT NULL,
    appearance_key   TEXT NOT NULL,
    created_at       TEXT NOT NULL,
    enabled          INTEGER NOT NULL DEFAULT 0,
    rule_version     INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE companion_event_ledger (
    id                TEXT NOT NULL PRIMARY KEY,
    companion_id      TEXT NOT NULL REFERENCES companion_profiles(id) ON DELETE CASCADE,
    source_event_id   TEXT NOT NULL,
    event_type        INTEGER NOT NULL,
    note_id           TEXT NULL REFERENCES notes(id) ON DELETE SET NULL,
    source_entity_id  TEXT NULL,
    curiosity_delta   INTEGER NOT NULL DEFAULT 0,
    insight_delta     INTEGER NOT NULL DEFAULT 0,
    reliability_delta INTEGER NOT NULL DEFAULT 0,
    occurred_at       TEXT NOT NULL,
    local_date        TEXT NOT NULL,
    rule_version      INTEGER NOT NULL,
    payload_json      TEXT NULL,
    UNIQUE(source_event_id, rule_version)
);

CREATE INDEX idx_companion_events_date
ON companion_event_ledger(companion_id, local_date, event_type);

CREATE TABLE companion_daily_progress (
    companion_id      TEXT NOT NULL REFERENCES companion_profiles(id) ON DELETE CASCADE,
    local_date        TEXT NOT NULL,
    capture_count     INTEGER NOT NULL DEFAULT 0,
    recall_count      INTEGER NOT NULL DEFAULT 0,
    resolve_count     INTEGER NOT NULL DEFAULT 0,
    chosen_ritual     INTEGER NULL,
    ritual_completed  INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(companion_id, local_date)
);

CREATE TABLE companion_suggestions (
    id               TEXT NOT NULL PRIMARY KEY,
    companion_id     TEXT NOT NULL REFERENCES companion_profiles(id) ON DELETE CASCADE,
    suggestion_type  INTEGER NOT NULL,
    source_note_id   TEXT NULL REFERENCES notes(id) ON DELETE CASCADE,
    dedupe_key       TEXT NOT NULL,
    status           INTEGER NOT NULL,
    created_at       TEXT NOT NULL,
    expires_at       TEXT NOT NULL,
    acted_at         TEXT NULL,
    payload_json     TEXT NOT NULL,
    UNIQUE(companion_id, dedupe_key)
);

CREATE TABLE companion_preferences (
    companion_id     TEXT NOT NULL REFERENCES companion_profiles(id) ON DELETE CASCADE,
    key              TEXT NOT NULL,
    value            TEXT NOT NULL,
    PRIMARY KEY(companion_id, key)
);
