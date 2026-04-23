using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using LumikitApp.Models;

namespace LumikitApp
{
    internal class JsonDataHandler
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        private static string TrackInfoDir => DirectoryPaths.TrackInfoDir;


        public static string TrackFilePath(string trackId) =>
            Path.Combine(TrackInfoDir, SafeFileName(trackId) + ".json");

        public static TrackData GetTrack(string trackID)
        {
            if (string.IsNullOrWhiteSpace(trackID)) return null;

            var path = TrackFilePath(trackID);

            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TrackData>(json);
        }

        public static List<TrackData> GetAllTracks()
        {
            if (!Directory.Exists(TrackInfoDir)) return new List<TrackData>();

            var files = Directory.EnumerateFiles(TrackInfoDir, "*.json", SearchOption.TopDirectoryOnly);
            var list = new List<TrackData>();

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var track = JsonSerializer.Deserialize<TrackData>(json);
                    if (track != null) list.Add(track);
                }
                catch
                {
                }
            }

            return list;
        }

        public static void SaveTrack(TrackData track)
        {
            if (track == null) return;

            Directory.CreateDirectory(TrackInfoDir);

            var path = TrackFilePath(track.trackGUID.ToString());
            var json = JsonSerializer.Serialize(track, JsonOpts);

            File.WriteAllText(path, json);
        }

        public static void DeleteTrack(string trackID)
        {
            if (string.IsNullOrWhiteSpace(trackID)) return;

            var path = TrackFilePath(trackID);
            if (File.Exists(path)) File.Delete(path);
        }

        public static string ImportAudioToAppStorage(string sourcePath)
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

        private static string ComputeFileHash(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha.ComputeHash(stream);
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
        public static List<TrackItemUI> GetAllTrackItems()
        {
            var tracks = GetAllTracks();

            return tracks
                .Where(t => t != null)
                .Select(t => new TrackItemUI
                {
                    TrackId = t.trackGUID.ToString(),
                    TrackName = t._trackName ?? "",
                    Subtitle = t.author ?? ""
                })
                .ToList();
        }


    }
}
