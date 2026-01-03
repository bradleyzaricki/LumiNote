using System.Threading;
using Avalonia.Controls;

namespace LumikitApp;

public class TrackPOCO(string id, string title, string? artistName, string imageurl)
{
    public string? artistName = artistName;
    public string trackId = id;
    public string trackName = title;
    public string trackCoverImageUrl = imageurl;
}