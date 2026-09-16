-- Content rewards do not alter either growth ledger or the 30:70 budget.
CREATE TABLE companion_meta_state (
    companion_id TEXT NOT NULL PRIMARY KEY REFERENCES companion_profiles(id) ON DELETE CASCADE,
    activated_date TEXT NOT NULL
);

CREATE TABLE companion_meta_reward_ledger (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    companion_id TEXT NOT NULL REFERENCES companion_profiles(id) ON DELETE CASCADE,
    grant_key TEXT NOT NULL,
    policy_version INTEGER NOT NULL,
    source_event_id TEXT NULL,
    pet_kind TEXT NOT NULL,
    occurred_at TEXT NOT NULL,
    local_date TEXT NOT NULL,
    UNIQUE(companion_id, grant_key)
);

CREATE TABLE companion_memory_album (
    reward_id INTEGER NOT NULL PRIMARY KEY REFERENCES companion_meta_reward_ledger(id) ON DELETE CASCADE,
    template_id TEXT NOT NULL,
    template_version INTEGER NOT NULL
);

CREATE INDEX idx_companion_meta_album
ON companion_meta_reward_ledger(companion_id, local_date DESC, id DESC);

CREATE TABLE companion_unlocks (
    companion_id TEXT NOT NULL REFERENCES companion_profiles(id) ON DELETE CASCADE,
    pet_kind TEXT NOT NULL,
    content_key TEXT NOT NULL,
    unlocked_at TEXT NOT NULL,
    PRIMARY KEY(companion_id, pet_kind, content_key)
);

ALTER TABLE companion_care_ledger ADD COLUMN request_id TEXT NULL;
CREATE UNIQUE INDEX idx_companion_care_request ON companion_care_ledger(companion_id, request_id);
