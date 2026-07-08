import { jwtVerify, createRemoteJWKSet } from "jose";

// Google's public signing keys (cached by jose). Used to verify the ID token the
// desktop client obtains via its Google OAuth PKCE flow.
const GOOGLE_JWKS = createRemoteJWKSet(new URL("https://www.googleapis.com/oauth2/v3/certs"));
const GOOGLE_ISSUERS = ["https://accounts.google.com", "accounts.google.com"];

export default {
  async fetch(req, env) {
    const json = (obj, status = 200) =>
      new Response(JSON.stringify(obj, null, 2), {
        status,
        headers: { "content-type": "application/json; charset=utf-8" }
      });

    try {
      if (!env) return json({ error: "env missing" }, 500);

      // ── Coarse gate: the shared key is still required on every request (keeps the
      //    API closed while we roll out real auth). Ownership actions ALSO need a user.
      const apiKey = req.headers.get("x-api-key");
      if (!apiKey) return json({ error: "missing x-api-key" }, 401);
      if (apiKey !== env.secret) return json({ error: "unauthorized" }, 401);

      if (!env.trackInfo) return json({ error: "D1 binding env.trackInfo is undefined" }, 500);
      if (!env.lightmaps) return json({ error: "R2 binding env.lightmaps is undefined" }, 500);

      const db = env.trackInfo;
      const r2 = env.lightmaps;
      const url = new URL(req.url);
      const method = req.method;

      const b64ToBytes = (b64) => {
        const bin = atob(b64);
        const arr = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
        return arr;
      };

      // ── Auth: resolve the caller into a users row (or null) ─────────────────
      async function upsertUser(user_id, email, name) {
        await db.prepare(`
          INSERT INTO users (user_id, email, name) VALUES (?, ?, ?)
          ON CONFLICT(user_id) DO UPDATE SET
            email = COALESCE(excluded.email, users.email),
            name  = COALESCE(excluded.name,  users.name)
        `).bind(user_id, email, name).run();
      }

      async function getUser() {
        // Dev-only bypass for local testing — X-Dev-User acts as the user id.
        // Guarded by env.ALLOW_DEV_AUTH (set only in .dev.vars, never in prod).
        if (env.ALLOW_DEV_AUTH === "true") {
          const dev = req.headers.get("x-dev-user");
          if (dev) {
            await upsertUser(dev, `${dev}@dev.local`, dev);
            return { user_id: dev, email: `${dev}@dev.local`, name: dev };
          }
        }
        const m = (req.headers.get("authorization") || "").match(/^Bearer\s+(.+)$/i);
        if (!m) return null;
        let payload;
        try {
          ({ payload } = await jwtVerify(m[1], GOOGLE_JWKS, {
            issuer: GOOGLE_ISSUERS,
            audience: env.GOOGLE_CLIENT_ID
          }));
        } catch {
          return null;
        }
        if (!payload.sub) return null;
        const email = payload.email ?? null;
        const name = payload.name ?? email ?? payload.sub;
        await upsertUser(payload.sub, email, name);
        return { user_id: payload.sub, email, name };
      }

      // A song is identified by any (provider, source_uri) it is linked to, so two
      // lightmaps of the same song share one `tracks` row. Returns an existing song
      // id if one of these sources is already known, else null.
      async function findSongBySources(sources) {
        for (const [provider, uri] of Object.entries(sources || {})) {
          if (!uri) continue;
          const row = await db.prepare(
            "SELECT track_id FROM track_sources WHERE provider = ? AND source_uri = ? LIMIT 1"
          ).bind(provider, uri).first();
          if (row?.track_id) return row.track_id;
        }
        return null;
      }

      // Upsert the shared song row, its per-provider sources, and any cover/audio blobs.
      async function writeSongAndAssets(trackId, payload, now) {
        let coverKey = null;
        if (payload.coverBase64) {
          coverKey = `covers/${trackId}.jpg`;
          await r2.put(coverKey, b64ToBytes(payload.coverBase64), {
            httpMetadata: { contentType: "image/jpeg" }
          });
        }

        const audioProvider = payload.audioProvider ?? "LocalFiles";
        let audioKey = null;
        if (payload.audioBase64) {
          audioKey = `audio_files/${trackId}_${audioProvider}.wav`;
          await r2.put(audioKey, b64ToBytes(payload.audioBase64), {
            httpMetadata: { contentType: "audio/wav" }
          });
        }

        await db.prepare(`
          INSERT INTO tracks (track_id, bpm, track_name, artist, album, duration_ms, cover_r2_key, updated_at)
          VALUES (?, ?, ?, ?, ?, ?, ?, ?)
          ON CONFLICT(track_id) DO UPDATE SET
            bpm=excluded.bpm, track_name=excluded.track_name, artist=excluded.artist,
            album=excluded.album, duration_ms=excluded.duration_ms,
            cover_r2_key=COALESCE(excluded.cover_r2_key, tracks.cover_r2_key),
            updated_at=excluded.updated_at
        `).bind(
          trackId, Number(payload._BPM ?? 0) || 0, payload._trackName ?? null,
          payload.artist ?? null, payload.album ?? null,
          payload.duration_ms ?? payload.durationMs ?? null, coverKey, now
        ).run();

        for (const [provider, uri] of Object.entries(payload.sources ?? {})) {
          const wavKey = provider === audioProvider ? audioKey : null;
          await db.prepare(`
            INSERT INTO track_sources (track_id, provider, source_uri, wav_r2_key)
            VALUES (?, ?, ?, ?)
            ON CONFLICT(track_id, provider) DO UPDATE SET
              source_uri=excluded.source_uri,
              wav_r2_key=COALESCE(excluded.wav_r2_key, track_sources.wav_r2_key)
          `).bind(trackId, provider, uri ?? null, wavKey).run();
        }
      }

      // ── Routes ──────────────────────────────────────────────────────────────
      const listPath = url.pathname === "/lightmaps";
      const idMatch = url.pathname.match(/^\/lightmaps\/([^/]+)$/);
      const assetMatch = url.pathname.match(/^\/lightmaps\/([^/]+)\/(cover|audio)$/);
      const likeMatch = url.pathname.match(/^\/lightmaps\/([^/]+)\/like$/);

      // GET /lightmaps — browse every shareable lightmap
      if (listPath && method === "GET") {
        const rows = await db.prepare(`
          SELECT l.lightmap_id, l.track_id, l.name, l.owner_id, l.version, l.likes,
                 l.parent_lightmap_id, u.name AS owner_name,
                 t.track_name, t.artist, t.bpm,
                 GROUP_CONCAT(DISTINCT s.provider) AS providers
          FROM lightmaps l
          JOIN tracks t ON t.track_id = l.track_id
          LEFT JOIN users u ON u.user_id = l.owner_id
          LEFT JOIN track_sources s ON s.track_id = l.track_id
          GROUP BY l.lightmap_id
          ORDER BY t.track_name
        `).all();
        return json({ items: rows.results ?? [] });
      }

      // POST /lightmaps — create a NEW lightmap (a fresh upload or a remix fork)
      if (listPath && method === "POST") {
        const user = await getUser();
        if (!user) return json({ error: "auth required" }, 401);

        let payload;
        try { payload = JSON.parse(await req.text()); }
        catch (e) { return json({ error: "body is not valid json", message: String(e) }, 400); }

        const blocks = payload._lightBlocks;
        if (!Array.isArray(blocks)) return json({ error: "missing _lightBlocks array" }, 400);
        const sources = payload.sources ?? {};

        const trackId = (await findSongBySources(sources)) ?? payload.track_id ?? crypto.randomUUID();

        // Limit: at most 4 lightmaps per user per song.
        const cnt = await db.prepare(
          "SELECT COUNT(*) AS c FROM lightmaps WHERE track_id = ? AND owner_id = ?"
        ).bind(trackId, user.user_id).first();
        if ((cnt?.c ?? 0) >= 4) {
          return json({ error: "limit reached: 4 lightmaps per song per user", track_id: trackId }, 403);
        }

        const lightmapId = payload.lightmap_id ?? crypto.randomUUID();
        const now = new Date().toISOString();

        const r2Key = `lightmaps/${lightmapId}.json`;
        await r2.put(r2Key, JSON.stringify(blocks), { httpMetadata: { contentType: "application/json" } });

        await writeSongAndAssets(trackId, payload, now);

        await db.prepare(`
          INSERT INTO lightmaps
            (lightmap_id, track_id, owner_id, name, r2_key, version, parent_lightmap_id, created_at, edited_at)
          VALUES (?, ?, ?, ?, ?, 1, ?, ?, ?)
        `).bind(
          lightmapId, trackId, user.user_id,
          payload.name ?? payload._trackName ?? null, r2Key,
          payload.parent_lightmap_id ?? null, now, now
        ).run();

        return json({ ok: true, lightmap_id: lightmapId, track_id: trackId, version: 1 });
      }

      // GET /lightmaps/:id — one lightmap with blocks + song metadata + sources
      if (idMatch && method === "GET") {
        const id = decodeURIComponent(idMatch[1]);
        const row = await db.prepare(`
          SELECT l.lightmap_id, l.track_id, l.owner_id, l.name AS lightmap_name,
                 l.version, l.likes, l.parent_lightmap_id, l.r2_key, l.created_at, l.edited_at,
                 u.name AS owner_name,
                 t.track_name, t.artist, t.album, t.bpm, t.duration_ms
          FROM lightmaps l
          JOIN tracks t ON t.track_id = l.track_id
          LEFT JOIN users u ON u.user_id = l.owner_id
          WHERE l.lightmap_id = ?
        `).bind(id).first();
        if (!row) return json({ error: "not found" }, 404);

        const srcRows = await db.prepare(
          "SELECT provider, source_uri FROM track_sources WHERE track_id = ?"
        ).bind(row.track_id).all();
        const sources = {};
        for (const s of srcRows.results ?? []) sources[s.provider] = s.source_uri;

        let blocks = [];
        const obj = await r2.get(row.r2_key);
        if (obj) {
          try {
            const parsed = JSON.parse(await obj.text());
            blocks = Array.isArray(parsed) ? parsed
              : Array.isArray(parsed?._lightBlocks) ? parsed._lightBlocks
              : Array.isArray(parsed?.lightBlocks) ? parsed.lightBlocks : [];
          } catch { blocks = []; }
        }

        return json({
          lightmap_id: row.lightmap_id, track_id: row.track_id, name: row.lightmap_name,
          owner_id: row.owner_id, owner_name: row.owner_name,
          version: row.version, likes: row.likes, parent_lightmap_id: row.parent_lightmap_id,
          created_at: row.created_at, edited_at: row.edited_at,
          _BPM: row.bpm ?? 0, _trackName: row.track_name ?? null,
          author: row.artist ?? null, album: row.album ?? null, duration_ms: row.duration_ms ?? null,
          _lightBlocks: blocks, sources
        });
      }

      // PUT /lightmaps/:id — update a lightmap you OWN (bumps version)
      if (idMatch && method === "PUT") {
        const id = decodeURIComponent(idMatch[1]);
        const user = await getUser();
        if (!user) return json({ error: "auth required" }, 401);

        const lm = await db.prepare(
          "SELECT track_id, owner_id, r2_key, version FROM lightmaps WHERE lightmap_id = ?"
        ).bind(id).first();
        if (!lm) return json({ error: "not found" }, 404);
        if (lm.owner_id !== user.user_id) {
          return json({ error: "forbidden: not the owner — save as a new remix instead" }, 403);
        }

        let payload;
        try { payload = JSON.parse(await req.text()); }
        catch (e) { return json({ error: "body is not valid json", message: String(e) }, 400); }
        const blocks = payload._lightBlocks;
        if (!Array.isArray(blocks)) return json({ error: "missing _lightBlocks array" }, 400);

        const now = new Date().toISOString();
        await r2.put(lm.r2_key, JSON.stringify(blocks), { httpMetadata: { contentType: "application/json" } });
        await writeSongAndAssets(lm.track_id, payload, now);

        const newVersion = lm.version + 1;
        await db.prepare(
          "UPDATE lightmaps SET name = COALESCE(?, name), version = ?, edited_at = ? WHERE lightmap_id = ?"
        ).bind(payload.name ?? payload._trackName ?? null, newVersion, now, id).run();

        return json({ ok: true, lightmap_id: id, track_id: lm.track_id, version: newVersion });
      }

      // DELETE /lightmaps/:id — owner removes their lightmap (blob + row)
      if (idMatch && method === "DELETE") {
        const id = decodeURIComponent(idMatch[1]);
        const user = await getUser();
        if (!user) return json({ error: "auth required" }, 401);
        const lm = await db.prepare(
          "SELECT owner_id, r2_key FROM lightmaps WHERE lightmap_id = ?"
        ).bind(id).first();
        if (!lm) return json({ error: "not found" }, 404);
        if (lm.owner_id !== user.user_id) return json({ error: "forbidden: not the owner" }, 403);
        await r2.delete(lm.r2_key);
        await db.prepare("DELETE FROM lightmaps WHERE lightmap_id = ?").bind(id).run();
        return json({ ok: true, deleted: id });
      }

      // POST /lightmaps/:id/like — one like per user (idempotent)
      if (likeMatch && method === "POST") {
        const id = decodeURIComponent(likeMatch[1]);
        const user = await getUser();
        if (!user) return json({ error: "auth required" }, 401);
        const ins = await db.prepare(
          "INSERT OR IGNORE INTO lightmap_likes (lightmap_id, user_id) VALUES (?, ?)"
        ).bind(id, user.user_id).run();
        if (ins.meta?.changes > 0) {
          await db.prepare("UPDATE lightmaps SET likes = likes + 1 WHERE lightmap_id = ?").bind(id).run();
        }
        const row = await db.prepare("SELECT likes FROM lightmaps WHERE lightmap_id = ?").bind(id).first();
        return json({ ok: true, likes: row?.likes ?? 0 });
      }

      // GET /lightmaps/:id/(cover|audio) — stream an R2 blob for the lightmap's song
      if (assetMatch && method === "GET") {
        const id = decodeURIComponent(assetMatch[1]);
        const kind = assetMatch[2];
        const lm = await db.prepare("SELECT track_id FROM lightmaps WHERE lightmap_id = ?").bind(id).first();
        if (!lm) return json({ error: "not found" }, 404);

        let key = null;
        if (kind === "cover") {
          const row = await db.prepare("SELECT cover_r2_key FROM tracks WHERE track_id = ?").bind(lm.track_id).first();
          key = row?.cover_r2_key ?? null;
        } else {
          const provider = url.searchParams.get("provider");
          const row = provider
            ? await db.prepare("SELECT wav_r2_key FROM track_sources WHERE track_id = ? AND provider = ?").bind(lm.track_id, provider).first()
            : await db.prepare("SELECT wav_r2_key FROM track_sources WHERE track_id = ? AND wav_r2_key IS NOT NULL LIMIT 1").bind(lm.track_id).first();
          key = row?.wav_r2_key ?? null;
        }
        if (!key) return json({ error: "asset not set", kind }, 404);

        const obj = await r2.get(key);
        if (!obj) return json({ error: "missing r2 object", r2_key: key }, 404);
        return new Response(obj.body, {
          status: 200,
          headers: { "content-type": kind === "cover" ? "image/jpeg" : "audio/wav" }
        });
      }

      return json({
        error: "route not found",
        path: url.pathname,
        routes: ["GET/POST /lightmaps", "GET/PUT/DELETE /lightmaps/:id", "POST /lightmaps/:id/like", "GET /lightmaps/:id/{cover|audio}"]
      }, 404);
    } catch (e) {
      return json({ error: "worker crash", message: e?.message ?? String(e), stack: e?.stack ?? null }, 500);
    }
  }
};