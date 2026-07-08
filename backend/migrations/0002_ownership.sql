-- 0002_ownership.sql — user accounts, ownership, versioning, song↔lightmap split
--
-- Splits the editable "lightmap" out of the "tracks" (song) row, and adds the
-- user/ownership/versioning columns the sharing feature needs.

-- ── 1. Users (Google-backed accounts) ────────────────────────────────────────
CREATE TABLE users (
  user_id    TEXT PRIMARY KEY,            -- Google 'sub' claim (stable per account)
  email      TEXT UNIQUE,
  name       TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Placeholder owner for pre-auth lightmaps imported below. Reassign these to a real
-- account once you know your Google user_id (a later admin step / first-login claim).
INSERT INTO users (user_id, email, name) VALUES ('legacy', NULL, 'Legacy Import');

-- ── 2. Lightmaps: the editable content, owned + versioned ─────────────────────
CREATE TABLE lightmaps (
  lightmap_id        TEXT PRIMARY KEY,
  track_id           TEXT NOT NULL REFERENCES tracks(track_id) ON DELETE CASCADE,
  owner_id           TEXT NOT NULL REFERENCES users(user_id),
  name               TEXT,
  r2_key             TEXT NOT NULL,        -- lightmaps/{lightmap_id}.json (the blocks)
  version            INTEGER NOT NULL DEFAULT 1,   -- bumped on each owner update
  likes              INTEGER NOT NULL DEFAULT 0,   -- cached count of lightmap_likes
  parent_lightmap_id TEXT REFERENCES lightmaps(lightmap_id),  -- set when this is a remix
  created_at         TEXT NOT NULL DEFAULT (datetime('now')),
  edited_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX idx_lightmaps_track ON lightmaps(track_id);
CREATE INDEX idx_lightmaps_owner ON lightmaps(owner_id);

-- ── 3. Backfill ───────────────────────────────────────────────────────────────
-- Every existing `tracks` row currently *is* a lightmap. Create one per row, reusing
-- the same id and R2 blob, owned by 'legacy'. New lightmaps for the same song will
-- get fresh ids that point back at this song's track_id.
INSERT INTO lightmaps (lightmap_id, track_id, owner_id, name, r2_key, version, created_at, edited_at)
SELECT track_id, track_id, 'legacy', track_name, r2_key, 1, updated_at, updated_at
FROM tracks
WHERE r2_key IS NOT NULL;

-- ── 4. Per-user likes (prevents double-like; lightmaps.likes is the cached total) ─
CREATE TABLE lightmap_likes (
  lightmap_id TEXT NOT NULL REFERENCES lightmaps(lightmap_id) ON DELETE CASCADE,
  user_id     TEXT NOT NULL REFERENCES users(user_id),
  PRIMARY KEY (lightmap_id, user_id)
);

-- ── 5. Retire the lightmap pointer from the song table ────────────────────────
-- Done last so the backfill above could still read tracks.r2_key.
ALTER TABLE tracks DROP COLUMN r2_key;