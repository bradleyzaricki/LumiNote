using Avalonia.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
namespace LumikitApp
{
    internal class JsonDataHandler
    {
        public static string filePath = "C:\\Users\\bzari\\source\\repos\\SpotifyInformationConsole\\LumikitApp\\TrackInfo.json";
        /// <summary>
        /// Gets the formatted trackData class from json data
        /// </summary>
        /// <param name="trackID"></param>
        /// <returns></returns>
        public static TrackData GetTrack(string trackID)
        {
            var json = File.ReadAllText(filePath);
            Debug.WriteLine(json);

            var tracks = JsonSerializer.Deserialize<List<TrackData>>(json);
            foreach(TrackData track in tracks)
            {
                if(track._trackID != null)
                {
                    if(track._trackID == trackID)
                    {
                        return track;
                    }
                }
            }
            return null;
        }
        /// <summary>
        /// Saves track Name (todo), ID, BPM, and light settings (todo) to the local json file
        /// </summary>
        /// <param name="track"></param>
        public static void SaveTrack(TrackData track)
        {
            List<TrackData> tracks = new();

            // If file exists and is not empty, read existing tracks
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
            {
                string existingJson = File.ReadAllText(filePath);
                tracks = JsonSerializer.Deserialize<List<TrackData>>(existingJson) ?? new List<TrackData>();
            }

            // Add the new track
            tracks.Add(track);

            // Write back the updated list
            string updatedJson = JsonSerializer.Serialize(tracks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, updatedJson);
        }

    }

}
