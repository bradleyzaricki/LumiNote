using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using LumikitApp.Models;
using Avalonia.Media;
namespace LumikitApp;

/// <summary>Thrown when an action needs a signed-in Google account.</summary>
public class AuthRequiredException : Exception
{
    public AuthRequiredException(string message) : base(message) { }
}

/// <summary>
/// Client for the shared cloud library (Cloudflare Worker, see backend/src/worker.js).
/// Speaks the /lightmaps API: every request carries the coarse x-api-key gate, and any
/// mutating call also carries the caller's Google ID token as a Bearer (the Worker
/// verifies it and enforces ownership / the 4-per-song limit server-side).
/// </summary>
public class DatabaseAccess
{
    private static readonly HttpClient Http = new HttpClient();

    private const string BaseUrl = "https://worker1.bzaricki56.workers.dev";
    private const string ApiKey  = "see-88-paw-583-film";
    private readonly IMusicProvider _provider;
    private readonly GoogleAuthService _auth;
    private readonly IAppLog _log;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    public DatabaseAccess(IMusicProvider provider, GoogleAuthService auth, IAppLog log)
    {
        _provider = provider;
        _auth = auth;
        _log = log;
    }

    /// <summary>
    /// Single send path for every Worker call, so each request lands in the console log
    /// with its route, status, and latency. Transport failures log as Warning here — the
    /// UI layer owns the user-facing Error when the operation as a whole fails.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var resp = await Http.SendAsync(req);
            _log.Info($"{req.Method} {req.RequestUri!.AbsolutePath} → {(int)resp.StatusCode} ({sw.ElapsedMilliseconds} ms)", "Cloud");
            return resp;
        }
        catch (Exception ex)
        {
            _log.Warn($"{req.Method} {req.RequestUri!.AbsolutePath} failed after {sw.ElapsedMilliseconds} ms: {ex.Message}", "Cloud");
            throw;
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path, string? idToken = null)
    {
        var req = new HttpRequestMessage(method, new Uri($"{BaseUrl}{path}"));
        req.Headers.Add("x-api-key", ApiKey);
        if (idToken != null)
            req.Headers.Add("Authorization", $"Bearer {idToken}");
        return req;
    }

    private static Exception ApiError(HttpResponseMessage resp, string body)
    {
        // Surface the Worker's structured errors as readable messages.
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var msg = err.GetString() ?? "unknown error";
                if (msg.Contains("limit reached"))
                    return new Exception("Upload limit reached: you already have 4 lightmaps for this song.");
                if (msg.Contains("auth required"))
                    return new AuthRequiredException("Sign in with Google to do this.");
                if (msg.Contains("forbidden"))
                    return new Exception("You don't own this lightmap — your edits save as a new remix instead.");
                return new Exception($"Server error: {msg}");
            }
        }
        catch { }
        return new Exception($"Worker returned {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");
    }

    private async Task<string> RequireIdTokenAsync()
    {
        var token = await _auth.GetIdTokenAsync();
        if (token == null)
            throw new AuthRequiredException("Sign in with Google to upload lightmaps.");
        return token;
    }

    /// <summary>
    /// Upload the given track's lightmap. Updates your own cloud copy (version bump) when
    /// you own it; otherwise creates a new cloud lightmap (fresh upload or remix fork,
    /// carrying ParentLightmapId). Mutates and returns `track` with the resulting cloud
    /// identity/version so the caller can persist it locally.
    /// </summary>
    public async Task<TrackData> UploadLightmapAsync(TrackData track)
    {
        var idToken = await RequireIdTokenAsync();

        var localKey = ProviderType.LocalFiles.ToString();

        // Only ever the LocalFiles source. The legacy `filePath` fallback used to be consulted
        // here, and on a pre-Sources save that field can hold a *Spotify* track id — uploading
        // audio derived from a Spotify source would be stream ripping under the Developer Terms.
        // Spotify is remote-control only so no such bytes exist to read, but keep the path
        // structurally incapable of it rather than relying on that.
        var localAudioPath = track.GetSource(ProviderType.LocalFiles);

        // Upload the source dict; the local link is stored as a bare filename (rebuilt on download).
        var sources = new Dictionary<string, string>(track.Sources);

        string? audioBase64 = null;
        if (!string.IsNullOrEmpty(localAudioPath) && File.Exists(localAudioPath))
        {
            audioBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(localAudioPath));
            sources[localKey] = Path.GetFileName(localAudioPath);
        }

        bool ownedUpdate = track.CloudLightmapId != null && track.OwnerId == _auth.UserId;

        var payload = new
        {
            lightmap_id = track.trackGUID.ToString(),
            parent_lightmap_id = track.ParentLightmapId,
            name = track.LightmapName ?? track._trackName, // the lightmap's own name; song title travels as _trackName

            _BPM = track._BPM,
            _trackName = track._trackName,
            artist = track.artist,
            _lightBlocks = track._lightBlocks,
            sources,
            audioBase64,
            audioProvider = localKey
        };

        using var req = ownedUpdate
            ? NewRequest(HttpMethod.Put, $"/lightmaps/{Uri.EscapeDataString(track.CloudLightmapId!)}", idToken)
            : NewRequest(HttpMethod.Post, "/lightmaps", idToken);
        req.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var resp = await SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw ApiError(resp, body);

        using var doc = JsonDocument.Parse(body);
        track.CloudLightmapId = doc.RootElement.GetProperty("lightmap_id").GetString();
        track.CloudVersion = doc.RootElement.GetProperty("version").GetInt32();
        track.OwnerId = _auth.UserId;
        track.OwnerName = _auth.UserName;
        return track;
    }

    /// <summary>
    /// Load one cloud lightmap (blocks + song metadata + sources + owner/version).
    /// Null if it doesn't exist.
    /// </summary>
    public async Task<TrackData?> LoadTrackAsync(string lightmapId)
    {
        using var req = NewRequest(HttpMethod.Get, $"/lightmaps/{Uri.EscapeDataString(lightmapId)}");
        using var resp = await SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();

        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) throw ApiError(resp, body);
        if (resp.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            // TrackData's cloud fields carry JsonPropertyName attrs matching this response
            // (lightmap_id, owner_id, version, ...) so it deserializes directly.
            return JsonSerializer.Deserialize<TrackData>(body, JsonOptions);
        }
        catch
        {
            throw new Exception($"Expected JSON but got:\n{body}");
        }
    }

    /// <summary>Download a lightmap's stored audio, or null if none is stored.</summary>
    public async Task<byte[]?> DownloadTrackAudioAsync(string lightmapId)
    {
        using var req = NewRequest(HttpMethod.Get, $"/lightmaps/{Uri.EscapeDataString(lightmapId)}/audio");
        using var resp = await SendAsync(req);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Worker returned {(int)resp.StatusCode} {resp.ReasonPhrase}");
        return await resp.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// Delete your own lightmap from the shared library (blob + row). The Worker enforces
    /// ownership — deleting someone else's returns a "forbidden" error.
    /// </summary>
    public async Task DeleteLightmapAsync(string lightmapId)
    {
        var idToken = await RequireIdTokenAsync();
        using var req = NewRequest(HttpMethod.Delete, $"/lightmaps/{Uri.EscapeDataString(lightmapId)}", idToken);
        using var resp = await SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw ApiError(resp, body);
    }

    /// <summary>Like a lightmap (one like per account, idempotent). Returns the new count.</summary>
    public async Task<int> LikeAsync(string lightmapId)
    {
        var idToken = await RequireIdTokenAsync();
        using var req = NewRequest(HttpMethod.Post, $"/lightmaps/{Uri.EscapeDataString(lightmapId)}/like", idToken);
        using var resp = await SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw ApiError(resp, body);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("likes", out var l) ? l.GetInt32() : 0;
    }

    /// <summary>
    /// List every shared lightmap as UI items (owner, version, likes included).
    /// Rows whose song is usable by the active provider sort first.
    /// </summary>
    public async Task<List<TrackItemUI>> ListTracksAsync(bool addUnusableTracks = true)
    {
        using var req = NewRequest(HttpMethod.Get, "/lightmaps");
        using var resp = await SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw ApiError(resp, body);

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("items", out var items))
            throw new Exception($"Unexpected JSON:\n{body}");

        var activeName = _provider.providerName.ToString();
        var listStart = new List<TrackItemUI>();
        var listEnd = new List<TrackItemUI>();

        foreach (var item in items.EnumerateArray())
        {
            string? Str(string prop) =>
                item.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
            int Num(string prop) =>
                item.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : 0;

            var lightmapId = Str("lightmap_id") ?? "";
            var trackName = Str("track_name") ?? "(untitled)";
            var lightmapName = Str("name");
            var artist = Str("artist") ?? "";
            var ownerId = Str("owner_id");
            var ownerName = Str("owner_name") ?? "unknown";
            var providersCsv = Str("providers") ?? "";
            var providers = providersCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var bpm = Num("bpm");
            var version = Num("version");
            var likes = Num("likes");

            bool usable = providers.Contains(activeName);
            bool mine = _auth.UserId != null && ownerId == _auth.UserId;

            var trackItem = new TrackItemUI
            {
                TrackId = lightmapId,
                // Title row = the lightmap's own name; the song it maps lives under it.
                TrackName = string.IsNullOrWhiteSpace(lightmapName) ? trackName : lightmapName,
                Subtitle = $"{trackName} • by {ownerName} • {string.Join(", ", providers)} • {bpm} BPM • ♥ {likes}",
                SongName = trackName,
                LightmapName = lightmapName,
                // Structured card areas (mirror the local list's columns).
                Artist = artist,
                Author = ownerName,
                IsMine = mine,
                Status = $"♥ {likes} • {bpm} BPM",
                Likes = likes,
                Usable = usable,
                Provider = providersCsv,
                OwnerId = ownerId,
                OwnerName = ownerName,
                CanDelete = mine,
                Version = version,
                Color = usable
                    ? new SolidColorBrush(_provider.ProviderColor)
                    : Brushes.Gray,
                SourceBadges = TrackItemUI.BuildBadges(pt => providers.Contains(pt.ToString()))
            };

            if (usable)
                listStart.Add(trackItem);
            else if (addUnusableTracks)
                listEnd.Add(trackItem);
        }
        listStart.AddRange(listEnd);

        return listStart;
    }
}
