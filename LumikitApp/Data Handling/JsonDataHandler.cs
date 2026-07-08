using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Avalonia.Media;
using LumikitApp.Models;

namespace LumikitApp
{
    public class JsonDataHandler
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        private static string TrackInfoDir => DirectoryPaths.TrackInfoDir;

        private readonly IMusicProvider _provider;


        public string TrackFilePath(string trackId) =>
            Path.Combine(TrackInfoDir, SafeFileName(trackId) + ".json");

        public TrackData GetTrack(string trackID)
        {
            if (string.IsNullOrWhiteSpace(trackID)) return null;

            var path = TrackFilePath(trackID);

            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var track = JsonSerializer.Deserialize<TrackData>(json);
            track?.MigrateLegacyFields();
            return track;
        }

        public JsonDataHandler(IMusicProvider provider)
        {
            _provider = provider;
        }
        public List<TrackData> GetAllTracks()
        {
            if (!Directory.Exists(TrackInfoDir)) return new List<TrackData>();

            var files = Directory.EnumerateFiles(TrackInfoDir, "*.json", SearchOption.TopDirectoryOnly);
            var listFront = new List<TrackData>();
            var listBack = new List<TrackData>();
            foreach (var file in files)
            {
                try
                {
                    //O(n) sort by provider type
                    var json = File.ReadAllText(file);
                    var track = JsonSerializer.Deserialize<TrackData>(json);
                    if (track == null) continue;
                    track.MigrateLegacyFields();
                    if (track.HasSource(_provider.providerName))
                        listFront.Add(track);
                    else
                        listBack.Add(track);
                }
                catch
                {
                }
            }
            listFront.AddRange(listBack);
            return listFront;
        }

        public void SaveTrack(TrackData track)
        {
            if (track == null) return;

            Directory.CreateDirectory(TrackInfoDir);

            var path = TrackFilePath(track.trackGUID.ToString());
            var json = JsonSerializer.Serialize(track, JsonOpts);

            File.WriteAllText(path, json);
        }

        public void DeleteTrack(string trackID)
        {
            if (string.IsNullOrWhiteSpace(trackID)) return;

            var path = TrackFilePath(trackID);
            if (File.Exists(path)) File.Delete(path);
        }

        public string ImportAudioToAppStorage(string sourcePath)
        {
            var appRoot = DirectoryPaths.AudioDir;


            Directory.CreateDirectory(appRoot);

            var hash = ComputeFileHash(sourcePath);
            var ext = Path.GetExtension(sourcePath);

            var destPath = Path.Combine(appRoot, hash + ext);

            if (!File.Exists(destPath))
            {
                File.Copy(sourcePath, destPath);
            }

            return destPath;
        }

        // Saves audio bytes downloaded from the database into the same local store used for
        // imported files, so both sources are indistinguishable to playback afterwards.
        public string SaveAudioBytesToAppStorage(byte[] audioBytes, string extension = ".wav")
        {
            var appRoot = DirectoryPaths.AudioDir;
            Directory.CreateDirectory(appRoot);

            var hash = ComputeHash(audioBytes);
            var destPath = Path.Combine(appRoot, hash + extension);

            if (!File.Exists(destPath))
                File.WriteAllBytes(destPath, audioBytes);

            return destPath;
        }

        private static string ComputeFileHash(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }

        private static string ComputeHash(byte[] data)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            return Convert.ToHexString(hash);
        }

        private static string SafeFileName(string name)
        {
            name = name.Trim();
            if (name.Length == 0) return "unnamed";
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            if (cleaned.Length > 120) cleaned = cleaned.Substring(0, 120);
            return cleaned;
        }
        
        /// <summary>
        /// Get all tracks from the local storage and returns them as a list of TrackItemUI elements.
        /// `myUserId` (the signed-in Google account, or null) decides each row's library group
        /// (Mine / Downloaded / Remixed); groups sort Mine → Remixed → Downloaded.
        /// </summary>
        public List<TrackItemUI> GetAllTrackItems(string? myUserId = null, string? myUserName = null)
        {
            var tracks = GetAllTracks();

            return tracks
                .Where(t => t != null)
                .Select(t =>
                {
                    var group = t.GetLibraryGroup(myUserId);
                    var groupTag = group switch
                    {
                        LibraryGroup.Downloaded => $" • Downloaded (by {t.OwnerName ?? "unknown"})",
                        LibraryGroup.Remixed => " • Remix",
                        _ => ""
                    };
                    return new TrackItemUI
                    {
                        TrackId = t.trackGUID.ToString(),
                        // Card areas: title = lightmap name; SongName/Artist/Author/Status
                        // render in their own aligned columns (see the local list's DataTemplate).
                        TrackName = t.DisplayName ?? "",
                        Subtitle = (t.LightmapName != null ? $"{t._trackName} • " : "") + (t.artist ?? "") + groupTag,
                        SongName = t._trackName ?? "",
                        LightmapName = t.LightmapName,
                        // Artist = the song's artist (always the typed field).
                        Artist = t.artist ?? "",
                        // Author = the Google account that created it. Downloaded maps show the
                        // uploader; your own maps show your name once uploaded (OwnerName), or
                        // your current sign-in while still local-only. Empty if never signed in.
                        Author = group == LibraryGroup.Downloaded
                            ? (t.OwnerName ?? "unknown")
                            : (t.OwnerName ?? myUserName ?? ""),
                        IsMine = group != LibraryGroup.Downloaded,
                        Status = group == LibraryGroup.Remixed ? "Remix" : "",

                        Group = group,
                        OwnerId = t.OwnerId,
                        CloudLightmapId = t.CloudLightmapId,
                        CloudVersion = t.CloudVersion,

                        Color = t.HasSource(_provider.providerName)
                            ? new SolidColorBrush(_provider.ProviderColor)
                            : Brushes.Gray,

                        SourceBadges = TrackItemUI.BuildBadges(t.HasSource)
                    };
                })
                .OrderBy(i => i.Group switch
                {
                    LibraryGroup.Mine => 0,
                    LibraryGroup.Remixed => 1,
                    _ => 2
                })
                .ToList();
        }


    }
}
