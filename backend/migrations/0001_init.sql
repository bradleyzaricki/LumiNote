-- 0001_init.sql — BASELINE
--
-- The live schema as it exists in production today (see ../schema.sql for the raw
-- export). Written idempotently (IF [NOT] EXISTS) so applying it against the existing
-- remote DB is a no-op, while a fresh local/dev DB gets built from scratch. Also drops
-- the two half-built draft tables (Lightmaps, Users) that the worker never used —
-- 0002 rebuilds proper versions of them.

CREATE TABLE IF NOT EXISTS tracks (
  track_id     TEXT PRIMARY KEY,
  bpm          REAL,
  r2_key       TEXT,               -- (legacy) pointer to the lightmap blob; removed in 0002
  updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
  track_name   TEXT,
  artist       TEXT,
  album        TEXT,
  cover_r2_key TEXT,
  duration_ms  INTEGER
);

CREATE TABLE IF NOT EXISTS track_sources (
  track_id   TEXT NOT NULL,
  provider   TEXT NOT NULL,
  source_uri TEXT,
  wav_r2_key TEXT,
  PRIMARY KEY (track_id, provider),
  FOREIGN KEY (track_id) REFERENCES tracks(track_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_track_sources_provider ON track_sources(provider);

-- Unused draft tables (rough sketches: integer ids, name/email typed as integer).
DROP TABLE IF EXISTS Lightmaps;
DROP TABLE IF EXISTS Users;
