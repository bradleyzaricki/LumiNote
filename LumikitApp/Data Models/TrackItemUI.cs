using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace LumikitApp.Models;

public class TrackItemUI
{
    public string TrackId { get; set; }
    public string TrackName { get; set; }
    public string Subtitle { get; set; }
    public string Provider { get; set; }
    public IBrush Color { get; set; }

    // ── Structured card areas (local list) ───────────────────────────────
    public string Artist { get; set; } = "";      // the song's artist, as typed in the save dialog
    public string Author { get; set; } = "";       // Google account that created it (set on upload)
    public bool IsMine { get; set; }              // author is the signed-in user → card shows "(you)"
    public string Status { get; set; } = "";      // provenance/sync tags: "Remix", "⬆ Update available"

    // ── Cloud sharing metadata ────────────────────────────────────────────
    public string? OwnerId { get; set; }
    public string? OwnerName { get; set; }        // cloud rows: sort/display key
    public string SongName { get; set; } = "";    // cloud rows: the song's track name (sort key)
    public string? LightmapName { get; set; }     // cloud rows: the lightmap's own name
    public int Likes { get; set; }                // cloud rows: like count (sort key)
    public bool Usable { get; set; } = true;      // cloud rows: playable with the active provider
    public bool CanDelete { get; set; }           // cloud rows: signed-in user owns it → show Delete
    public int Version { get; set; }              // cloud rows: server version
    public string? CloudLightmapId { get; set; }  // local rows: cloud identity (null = local-only)
    public int CloudVersion { get; set; }         // local rows: version this copy holds
    public LibraryGroup Group { get; set; } = LibraryGroup.Mine;
    public bool UpdateAvailable { get; set; }     // local rows: cloud has a newer version

    // Sources this lightmap has linked — drives the row's badges.
    public List<ProviderBadge> SourceBadges { get; set; } = new();

    // Link actions offered this session — drives the row's link buttons.
    public List<TrackLinkAction> LinkActions { get; set; } = new();

    // One badge per provider whose source is present.
    public static List<ProviderBadge> BuildBadges(Func<ProviderType, bool> hasSource) =>
        Enum.GetValues<ProviderType>()
            .Where(hasSource)
            .Select(ProviderBadge.For)
            .ToList();
}

public class ProviderBadge
{
    public string Name { get; init; }
    public IBrush Color { get; init; }

    public static ProviderBadge For(ProviderType p) => new()
    {
        Name = p.DisplayName(),
        Color = new SolidColorBrush(p.BadgeColor())
    };
}

public class TrackLinkAction
{
    public string TrackId { get; init; }
    public ProviderType Provider { get; init; }
    public string Label { get; init; }
}