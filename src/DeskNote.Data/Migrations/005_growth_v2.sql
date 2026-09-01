-- Growth rules V2: experience and care days replace the three axes as the growth gate.
-- The axes stay as lifetime counters so no existing history is discarded.

ALTER TABLE companion_pet_progress ADD COLUMN experience INTEGER NOT NULL DEFAULT 0;
ALTER TABLE companion_pet_progress ADD COLUMN care_days INTEGER NOT NULL DEFAULT 0;

ALTER TABLE companion_event_ledger ADD COLUMN experience_delta INTEGER NOT NULL DEFAULT 0;

CREATE TABLE companion_care_ledger (
    id            TEXT NOT NULL PRIMARY KEY,
    companion_id  TEXT NOT NULL REFERENCES companion_profiles(id) ON DELETE CASCADE,
    pet_kind      TEXT NOT NULL,
    care_action   INTEGER NOT NULL,
    play_kind     INTEGER NULL,
    points        INTEGER NOT NULL,
    occurred_at   TEXT NOT NULL,
    local_date    TEXT NOT NULL,
    rule_version  INTEGER NOT NULL
);

CREATE INDEX idx_companion_care_day
ON companion_care_ledger(companion_id, pet_kind, local_date);

CREATE INDEX idx_companion_care_recent
ON companion_care_ledger(companion_id, pet_kind, occurred_at);

-- Existing progress keeps its stage, and its position inside that stage is rescaled onto the
-- new experience band. Care days are credited only up to what the already-earned stage required,
-- so nobody is demoted for days they had no way of knowing to spend.
UPDATE companion_pet_progress
SET experience = CASE
        WHEN curiosity + insight + reliability >= 150 THEN 2200
        WHEN curiosity + insight + reliability >= 90
            THEN 1200 + CAST((curiosity + insight + reliability - 90) * 1000 / 60 AS INTEGER)
        WHEN curiosity + insight + reliability >= 45
            THEN 600 + CAST((curiosity + insight + reliability - 45) * 600 / 45 AS INTEGER)
        WHEN curiosity + insight + reliability >= 15
            THEN 200 + CAST((curiosity + insight + reliability - 15) * 400 / 30 AS INTEGER)
        ELSE CAST((curiosity + insight + reliability) * 200 / 15 AS INTEGER)
    END,
    care_days = CASE
        WHEN curiosity + insight + reliability >= 150 THEN 28
        WHEN curiosity + insight + reliability >= 90 THEN 14
        WHEN curiosity + insight + reliability >= 45 THEN 7
        ELSE 0
    END;

-- Daily ritual counts are superseded by the app and care ledgers, which carry the same facts
-- with the points attached.
DROP TABLE companion_daily_progress;
