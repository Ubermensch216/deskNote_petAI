ALTER TABLE companion_event_ledger
ADD COLUMN pet_kind TEXT NOT NULL DEFAULT 'Rabbit';

CREATE INDEX idx_companion_events_pet
ON companion_event_ledger(companion_id, pet_kind, local_date, event_type);

CREATE TABLE companion_pet_progress (
    companion_id      TEXT NOT NULL REFERENCES companion_profiles(id) ON DELETE CASCADE,
    pet_kind          TEXT NOT NULL,
    curiosity         INTEGER NOT NULL DEFAULT 0,
    insight           INTEGER NOT NULL DEFAULT 0,
    reliability       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(companion_id, pet_kind)
);

-- Existing beta growth belongs to the default rabbit, so upgrading never discards progress.
INSERT INTO companion_pet_progress(companion_id, pet_kind, curiosity, insight, reliability)
SELECT companion_id,
       'Rabbit',
       COALESCE(SUM(curiosity_delta), 0),
       COALESCE(SUM(insight_delta), 0),
       COALESCE(SUM(reliability_delta), 0)
FROM companion_event_ledger
GROUP BY companion_id;
