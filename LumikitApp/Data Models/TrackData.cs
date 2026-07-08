using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Color = Avalonia.Media.Color;

namespace LumikitApp
{
    /// <summary>Local library shelves for the sharing feature.</summary>
    public enum LibraryGroup
    {
        Mine,       // created locally or uploaded by you
        Downloaded, // someone else's lightmap, unmodified
        Remixed     // a downloaded lightmap you edited (forked into your own)
    }

    public class TrackData
    {
        public Guid trackGUID { get; set; } = Guid.NewGuid();

        public Image? albumCover {get; set;}
        public string artist { get; set; }
        public double _BPM { get; set; }
        [NotNull]
        public string _trackName { get; set; }

        // Keyed by provider name (e.g. "Spotify", "LocalFiles") → track identifier or file path
        public Dictionary<string, string> Sources { get; set; } = new();

        // ── Cloud sharing metadata ────────────────────────────────────────────
        // JSON names match the Worker's /lightmaps API so a GET deserializes straight in;
        // JsonDataHandler persists them locally with the same names.

        // The lightmap's own display name, distinct from the song's _trackName (one song can
        // have many lightmaps). Null on old saves → DisplayName falls back to the track name.
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? LightmapName { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(LightmapName) ? _trackName : LightmapName!;

        // Id of this lightmap in the shared cloud library. Null = never uploaded/downloaded.
        [System.Text.Json.Serialization.JsonPropertyName("lightmap_id")]
        public string? CloudLightmapId { get; set; }

        // Cloud account that owns the lightmap. Null = local-only (treated as yours).
        [System.Text.Json.Serialization.JsonPropertyName("owner_id")]
        public string? OwnerId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("owner_name")]
        public string? OwnerName { get; set; }

        // Set when this lightmap was forked from someone else's (a "remix").
        [System.Text.Json.Serialization.JsonPropertyName("parent_lightmap_id")]
        public string? ParentLightmapId { get; set; }

        // The cloud version this local copy corresponds to (0 = not synced). The cloud list
        // reporting a higher number for CloudLightmapId ⇒ "update available".
        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public int CloudVersion { get; set; }

        /// <summary>Which library shelf this track belongs on for the given signed-in user.</summary>
        public LibraryGroup GetLibraryGroup(string? myUserId) =>
            OwnerId != null && OwnerId != myUserId ? LibraryGroup.Downloaded
            : ParentLightmapId != null ? LibraryGroup.Remixed
            : LibraryGroup.Mine;

        // Legacy fields — kept for backward-compatible deserialization only
        public string? filePath { get; set; }
        public string? provider { get; set; }

        public List<LightBlockData> _lightBlocks { get; set; } = new();
        public TrackData() { }

        // Populates Sources from the old flat fields if this was loaded from a pre-Sources save
        public void MigrateLegacyFields()
        {
            Sources ??= new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(provider) && !string.IsNullOrEmpty(filePath)
                && !Sources.ContainsKey(provider))
                Sources[provider] = filePath;
        }

        public bool HasSource(ProviderType provider) => Sources.ContainsKey(provider.ToString());
        public string? GetSource(ProviderType provider) => Sources.GetValueOrDefault(provider.ToString());
        public void SetSource(ProviderType provider, string value) => Sources[provider.ToString()] = value;


    }
    public class LightBlockData
    {
        public double X { get; set; }//position on timeline
        public double Width { get; set; }//Width of block on timeline
        public string Color { get; set; }//Saved color
        
        public string SecondColor { get; set; }

        public string FillColor { get; set; }

        public string StrobeColor { get; set; }
        [System.Text.Json.Serialization.JsonConverter(typeof(EffectDataListConverter))]
        public List<EffectData> BlockEffects { get; set; }
        
        public int StartLight { get; set; }
        
        public int EndLight { get; set; }
        
        public int SecondaryDualInput1 { get; set; }
        
        public int SecondaryDualInput2 { get; set; }
        
        public int SecondarySingleInput1 { get; set; }
        
        public int SecondarySingleInput2 { get; set; }
        
        public int LightIntensity { get; set; }
    }
}
