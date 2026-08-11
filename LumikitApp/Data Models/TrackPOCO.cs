using System;
using System.Threading;
using Avalonia.Controls;

namespace LumikitApp;

/// <param name="trackUrl">
/// Public URL for this track on the provider's own service, or null if the provider has none
/// (local files). Spotify's Developer Terms require metadata and cover art to be accompanied
/// by a link back to the track on Spotify, so the now-playing UI needs this to build one.
/// </param>
public class TrackPOCO(Guid id, string title, string? artistName, string imageurl, string? trackUrl = null)
{
    public string? artistName = artistName;
    public Guid trackId = id;
    public string trackName = title;
    public string trackCoverImageUrl = imageurl;
    public string? trackUrl = trackUrl;
}
