-- ─────────────────────────────────────────────────────────────────────────────
-- Snapshot of the REMOTE D1 schema (database: luminote-track-info,
-- id 162626bd-0de1-45cb-84b6-f61ba678aa89) as exported on 2026-07-05 via:
--   wrangler d1 export luminote-track-info --remote --no-data
--
-- This is a point-in-time reference, NOT a migration. Proper numbered migrations
-- live in ./migrations once we set up the ownership/auth feature.
--
-- LIVE (used by src/worker.js):
--   * tracks         — song metadata; r2_key points at the lightmap JSON in R2
--   * track_sources  — per-provider link + wav_r2_key for that source's audio
--
-- DRAFT (defined but UNUSED by the worker — rough sketches to be superseded):
--   * Lightmaps      — integer ids, no FKs; will be redesigned for ownership
--   * Users          — name/email typed as integer (bug); will be redesigned
-- ─────────────────────────────────────────────────────────────────────────────

PRAGMA defer_foreign_keys=TRUE;

-- ── DRAFT (unused) ───────────────────────────────────────────────────────────
CREATE TABLE [Lightmaps] (
  "lightmap_id" integer PRIMARY KEY,
  "track_id"    integer,
  "name"        text DEFAULT '"Untitled Lightmap"',
  "created_at"  integer DEFAULT 0,
  "edited_at"   integer DEFAULT 0,
  "author"      text,
  "author_id"   integer,
  "likes"       integer
);

CREATE TABLE [Users] (
  "user_id" integer PRIMARY KEY,
  "name"    integer,
  "email"   integer
);

-- ── LIVE ─────────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "tracks" (
  track_id     TEXT PRIMARY KEY,
  bpm          REAL,
  r2_key       TEXT NOT NULL,
  updated_at   TEXT NOT NULL,
  track_name   TEXT,
  artist       TEXT,
  album        TEXT,
  cover_r2_key TEXT,
  duration_ms  INTEGER
);

CREATE TABLE track_sources (
  track_id   TEXT NOT NULL,
  provider   TEXT NOT NULL,
  source_uri TEXT,
  wav_r2_key TEXT,
  PRIMARY KEY (track_id, provider),
  FOREIGN KEY (track_id) REFERENCES tracks(track_id) ON DELETE CASCADE
);

CREATE INDEX idx_track_sources_provider ON track_sources(provider);