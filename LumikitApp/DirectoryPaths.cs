using System;
using System.IO;

namespace LumikitApp
{
    internal static class DirectoryPaths
    {
        public static readonly string Root;
        public static readonly string AudioDir;
        public static readonly string TrackInfoDir;
        public static readonly string SettingsDir;

        static DirectoryPaths()
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var rootName = Environment.GetEnvironmentVariable("LUMINOTE_ROOT") ?? "LumiNote";
            var audioName = Environment.GetEnvironmentVariable("LUMINOTE_AUDIO_FOLDER") ?? "Audio";
            var trackName = Environment.GetEnvironmentVariable("LUMINOTE_TRACKINFO_FOLDER") ?? "TrackInfo";
            var settingsName = Environment.GetEnvironmentVariable("LUMINOTE_SETTINGS_FOLDER") ?? "Settings";

            Root = Path.Combine(basePath, rootName);
            AudioDir = Path.Combine(Root, audioName);
            TrackInfoDir = Path.Combine(Root, trackName);
            SettingsDir = Path.Combine(Root, settingsName);

            Directory.CreateDirectory(AudioDir);
            Directory.CreateDirectory(TrackInfoDir);
            Directory.CreateDirectory(SettingsDir);
        }
    }
}