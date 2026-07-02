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