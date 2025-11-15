using Avalonia.Controls;

namespace LumikitApp;

public class TrackPOCO(string id, string name, string imageurl)
{
    public string trackId = id;
    public string trackName = name;
    public string trackCoverImageUrl = imageurl;
}