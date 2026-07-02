using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using LumikitApp.Models;
using Avalonia.Media;
namespace LumikitApp;

public class DatabaseAccess
{
    private static readonly HttpClient Http = new HttpClient();

    private const string BaseUrl = "https://worker1.bzaricki56.workers.dev";
    private const string ApiKey  = "see-88-paw-583-film";
    private IMusicProvider _provider;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    public  DatabaseAccess(IMusicProvider provider)
    {
        _provider = provider;
    }

    
    /// <summary>
    /// Save track to database with trackID key
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="trackId"></param>
    /// <param name="track"></param>
    /// <exception cref="Exception"></exception>

    public async Task SaveTrackAsync(string trackId, TrackData track)
    {
        var localAudioPath = track.GetSource(ProviderType.LocalFiles) ?? track.filePath;

        string? audioBase64 = null;
        var fileName = track.filePath;
        if (!string.IsNullOrEmpty(localAudioPath) && File.Exists(localAudioPath))
        {
            audioBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(localAudioPath));
            fileName = Path.GetFileName(localAudioPath);
        }

        var payload = new
        {
            _BPM = track._BPM,
            _trackName = track._trackName,
            provider = track.provider,
            filePath = fileName,
            _lightBlocks = track._lightBlocks,
            audioBase64
        };

        var url = new Uri($"{BaseUrl}/tracks/{Uri.EscapeDataString(trackId)}");
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        using var req = new HttpRequestMessage(HttpMethod.Put, url);
        req.Headers.Add("x-api-key", ApiKey);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Worker returned {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");
    }

    /// <summary>
    /// Load track with trackID and optionally provider
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="trackId"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task<TrackData?> LoadTrackAsync(string trackId)
    {
        var url = new Uri($"{BaseUrl}/tracks/{Uri.EscapeDataString(trackId)}");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-api-key", ApiKey);

        using var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Worker returned {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");

        if (resp.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            var trackData = JsonSerializer.Deserialize<TrackData>(body, JsonOptions);
            Console.WriteLine(trackData.filePath);

            return trackData;
        }
        catch
        {
            throw new Exception($"Expected JSON but got:\n{body}");
        }
    }

    /// <summary>
    /// Download a track's stored audio file, or null if the track has none stored.
    /// </summary>
    public async Task<byte[]?> DownloadTrackAudioAsync(string trackId)
    {
        var url = new Uri($"{BaseUrl}/tracks/{Uri.EscapeDataString(trackId)}/audio");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-api-key", ApiKey);

        using var resp = await Http.SendAsync(req);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Worker returned {(int)resp.StatusCode} {resp.ReasonPhrase}");

        return await resp.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// List all database tracks
    /// </summary>
    /// <param name="provider">optional parameter to filter for tracks by provider</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task<List<TrackItemUI>> ListTracksAsync(bool addUnusableTracks = true)
    {
        var url = (addUnusableTracks
            ? new Uri($"{BaseUrl}/tracks")
            : new Uri($"{BaseUrl}/tracks?provider={_provider.providerName}"));


        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-api-key", ApiKey);

        using var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Worker returned {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");

        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("items", out var items))
            throw new Exception($"Unexpected JSON:\n{body}");

        var listStart = new List<TrackItemUI>();
        var listEnd = new List<TrackItemUI>();
        
        foreach (var item in items.EnumerateArray())
        {
            var trackId = item.GetProperty("track_id").GetString() ?? "";
            var trackName = item.GetProperty("track_name").GetString() ?? "(untitled)";
            var p = item.GetProperty("provider").GetString() ?? "";
            var bpm = item.GetProperty("bpm").GetInt32();
            TrackItemUI trackItem = new TrackItemUI
            {
                TrackId = trackId,
                TrackName = trackName,
                Subtitle = $"{p} • {bpm} BPM",
                Provider = p,
                Color = p == _provider.providerName.ToString()
                ? new SolidColorBrush(_provider.ProviderColor)
                : Brushes.Gray,
                SourceBadges = TrackItemUI.BuildBadges(pt => pt.ToString() == p)
            };
            if (p == _provider.providerName.ToString())
            {
                listStart.Add(trackItem);
            }
            else if (addUnusableTracks)
            {
                listEnd.Add(trackItem);
            }

        }
        listStart.AddRange(listEnd);

        return listStart;
    }


    public class TrackListResponse
    {
        public List<TrackItemUI>? items { get; set; }
    }
    /// <summary>
    /// Track summary class to display track information through UI elements
    /// </summary>
    public class TrackSummary
    {
        public string track_id { get; set; }
        public string provider { get; set; }
        public double bpm { get; set; }
        public string track_name { get; set; }
    }

    public async Task<List<TrackSummary>> GetAllTracksAsync()
    {
        var url = new Uri($"{BaseUrl}/tracks");

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-api-key", ApiKey);

        using var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Worker returned {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}");

        using var doc = JsonDocument.Parse(body);

        var list = new List<TrackSummary>();
        foreach (var el in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            list.Add(new TrackSummary
            {
                track_id = el.GetProperty("track_id").GetString(),
                provider = el.GetProperty("provider").GetString(),
                bpm = el.TryGetProperty("bpm", out var bpmEl) ? bpmEl.GetDouble() : 0,
                track_name = el.TryGetProperty("track_name", out var nameEl) ? nameEl.GetString() : null
            });
        }

        return list;
    }
    

}




