using System;
using System.Threading;
using Avalonia.Controls;

namespace LumikitApp;

public class TrackPOCO(Guid id, string title, string? artistName, string imageurl)
{
    public string? artistName = artistName;
    public Guid trackId = id;
    public string trackName = title;
    public string trackCoverImageUrl = imageurl;
}