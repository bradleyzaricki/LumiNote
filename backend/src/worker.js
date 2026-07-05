export default {
    async fetch(req, env) {
      const json = (obj, status = 200) =>
        new Response(JSON.stringify(obj, null, 2), {
          status,
          headers: { "content-type": "application/json; charset=utf-8" }
        });

      try {
        if (!env) return json({ error: "env missing" }, 500);

        const apiKey = req.headers.get("x-api-key");
        if (!apiKey) return json({ error: "missing x-api-key" }, 401);
        if (apiKey !== env.secret) return json({ error: "unauthorized" }, 401);

        if (!env.trackInfo) return json({ error: "D1 binding env.trackInfo is undefined" }, 500);
        if (!env.lightmaps) return json({ error: "R2 binding env.lightmaps is undefined" }, 500);

        const url = new URL(req.url);

        function base64ToUint8Array(b64) {
          const bin = atob(b64);
          const len = bin.length;
          const arr = new Uint8Array(len);
          for (let i = 0; i < len; i++) arr[i] = bin.charCodeAt(i);
          return arr;
        }

        // GET /tracks — list, providers rolled up per song
        if (req.method === "GET" && url.pathname === "/tracks") {
          let rows;
          try {
            rows = await env.trackInfo.prepare(`
              SELECT t.track_id, t.bpm, t.track_name,
                     GROUP_CONCAT(s.provider) AS providers
              FROM tracks t
              LEFT JOIN track_sources s ON s.track_id = t.track_id
              GROUP BY t.track_id
              ORDER BY t.track_name
            `).all();
          } catch (e) {
            return json({ error: "D1 query failed", message: e?.message ?? String(e), stack: e?.stack ?? null }, 500);
          }
          return json({ items: rows.results ?? [] });
        }

        const mainMatch = url.pathname.match(/^\/tracks\/([^/]+)$/);
        const assetMatch = url.pathname.match(/^\/tracks\/([^/]+)\/(cover|audio)$/);

        if (!mainMatch && !assetMatch) {
          return json({ error: "route not found", path: url.pathname }, 404);
        }

        // GET /tracks/:id/(cover|audio)
        if (assetMatch) {
          const trackId = decodeURIComponent(assetMatch[1]);
          const kind = assetMatch[2];

          if (req.method !== "GET") {
            return json({ error: "method not allowed", method: req.method }, 405);
          }

          let key = null;
          try {
            if (kind === "cover") {
              const row = await env.trackInfo
                .prepare("SELECT cover_r2_key FROM tracks WHERE track_id = ?")
                .bind(trackId).first();
              key = row?.cover_r2_key ?? null;
            } else {
              // audio is per-source; ?provider= picks one, else first source that has audio
              const provider = url.searchParams.get("provider");
              const row = provider
                ? await env.trackInfo
                    .prepare("SELECT wav_r2_key FROM track_sources WHERE track_id = ? AND provider = ?")
                    .bind(trackId, provider).first()
                : await env.trackInfo
                    .prepare("SELECT wav_r2_key FROM track_sources WHERE track_id = ? AND wav_r2_key IS NOT NULL LIMIT 1")
                    .bind(trackId).first();
              key = row?.wav_r2_key ?? null;
            }
          } catch (e) {
            return json({ error: "D1 query failed", message: e?.message ?? String(e), stack: e?.stack ?? null }, 500);
          }

          if (!key) return json({ error: "asset not set", kind }, 404);

          const obj = await env.lightmaps.get(key);
          if (!obj) return json({ error: "missing r2 object", r2_key: key }, 404);

          const ct = kind === "cover" ? "image/jpeg" : "audio/wav";
          return new Response(obj.body, { status: 200, headers: { "content-type": ct } });
        }

        const trackId = decodeURIComponent(mainMatch[1]);

        // GET /tracks/:id
        if (req.method === "GET") {
          let row;
          try {
            row = await env.trackInfo.prepare(`
              SELECT track_id, bpm, track_name, artist, album, duration_ms,
                     r2_key, cover_r2_key, updated_at
              FROM tracks WHERE track_id = ?
            `).bind(trackId).first();
          } catch (e) {
            return json({ error: "D1 query failed", message: e?.message ?? String(e), stack: e?.stack ?? null }, 500);
          }

          if (!row) return json({ error: "not found" }, 404);

          let sourceRows;
          try {
            sourceRows = await env.trackInfo
              .prepare("SELECT provider, source_uri FROM track_sources WHERE track_id = ?")
              .bind(trackId).all();
          } catch (e) {
            return json({ error: "D1 query failed", message: e?.message ?? String(e), stack: e?.stack ?? null }, 500);
          }

          const sources = {};
          for (const s of sourceRows.results ?? []) sources[s.provider] = s.source_uri;

          const obj = await env.lightmaps.get(row.r2_key);
          let blocks = [];
          if (obj) {
            const blocksText = await obj.text();
            try {
              const parsed = JSON.parse(blocksText);
              blocks = Array.isArray(parsed) ? parsed
                : Array.isArray(parsed?._lightBlocks) ? parsed._lightBlocks
                : Array.isArray(parsed?.lightBlocks) ? parsed.lightBlocks : [];
            } catch { blocks = []; }
          }

          return json({
            _BPM: row.bpm ?? 0,
            _trackID: row.track_id,
            _trackName: row.track_name ?? null,
            author: row.artist ?? null,
            album: row.album ?? null,
            duration_ms: row.duration_ms ?? null,
            _lightBlocks: blocks,
            sources
          });
        }

        // PUT /tracks/:id
        if (req.method === "PUT") {
          const text = await req.text();
          if (!text || !text.trim()) return json({ error: "empty body" }, 400);

          let payload;
          try { payload = JSON.parse(text); }
          catch (e) { return json({ error: "body is not valid json", message: e?.message ?? String(e) }, 400); }

          const now = new Date().toISOString();
          const bpm = Number(payload._BPM ?? 0) || 0;
          const trackName = payload._trackName ?? null;
          const blocks = payload._lightBlocks;
          const sources = payload.sources ?? {};

          if (!Array.isArray(blocks)) return json({ error: "missing _lightBlocks array" }, 400);

          // lightmap blocks → R2
          const r2Key = `lightmaps/${trackId}.json`;
          await env.lightmaps.put(r2Key, JSON.stringify(blocks), {
            httpMetadata: { contentType: "application/json" }
          });

          // cover → R2
          let coverKey = null;
          if (payload.coverBase64) {
            coverKey = `covers/${trackId}.jpg`;
            await env.lightmaps.put(coverKey, base64ToUint8Array(payload.coverBase64), {
              httpMetadata: { contentType: "image/jpeg" }
            });
          }

          // audio → R2 (belongs to one source; default LocalFiles)
          let audioKey = null;
          const audioProvider = payload.audioProvider ?? "LocalFiles";
          if (payload.audioBase64) {
            audioKey = `audio_files/${trackId}.wav`;
            await env.lightmaps.put(audioKey, base64ToUint8Array(payload.audioBase64), {
              httpMetadata: { contentType: "audio/wav" }
            });
          }

          // shared song metadata
          try {
            await env.trackInfo.prepare(`
              INSERT INTO tracks (
                track_id, bpm, track_name, artist, album, duration_ms,
                r2_key, cover_r2_key, updated_at
              ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
              ON CONFLICT(track_id) DO UPDATE SET
                bpm=excluded.bpm,
                track_name=excluded.track_name,
                artist=excluded.artist,
                album=excluded.album,
                duration_ms=excluded.duration_ms,
                r2_key=excluded.r2_key,
                cover_r2_key=COALESCE(excluded.cover_r2_key, tracks.cover_r2_key),
                updated_at=excluded.updated_at
            `).bind(
              trackId, bpm, trackName,
              payload.artist ?? null, payload.album ?? null,
              payload.duration_ms ?? payload.durationMs ?? null,
              r2Key, coverKey, now
            ).run();
          } catch (e) {
            return json({ error: "D1 write failed (tracks)", message: e?.message ?? String(e), stack: e?.stack ?? null }, 500);
          }

          // one row per provider link; COALESCE keeps existing audio when this save has none
          try {
            for (const [provider, uri] of Object.entries(sources)) {
              const wavKey = provider === audioProvider ? audioKey : null;
              await env.trackInfo.prepare(`
                INSERT INTO track_sources (track_id, provider, source_uri, wav_r2_key)
                VALUES (?, ?, ?, ?)
                ON CONFLICT(track_id, provider) DO UPDATE SET
                  source_uri=excluded.source_uri,
                  wav_r2_key=COALESCE(excluded.wav_r2_key, track_sources.wav_r2_key)
              `).bind(trackId, provider, uri ?? null, wavKey).run();
            }
          } catch (e) {
            return json({ error: "D1 write failed (track_sources)", message: e?.message ?? String(e), stack: e?.stack ?? null }, 500);
          }

          return json({ ok: true, r2_key: r2Key, cover_r2_key: coverKey, wav_r2_key: audioKey });
        }

        return json({ error: "method not allowed", method: req.method }, 405);
      } catch (e) {
        return json({ error: "worker crash", message: e?.message ?? String(e), stack: e?.stack ?? null }, 500);
      }
    }
  };