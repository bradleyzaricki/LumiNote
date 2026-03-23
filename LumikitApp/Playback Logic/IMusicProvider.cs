namespace LumikitApp;
using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Controls;
/// <summary>
/// Music provider allows for manipulation and fetching of the physical music, It is controlled by
/// PlaybackHandler which connects the music provider to the timeline UI element
/// </summary>
public interface IMusicProvider
{
    TrackPOCO currentTrack {get; set;}
    string currentlyPlayingPath {get; set;}
    string providerName { get; }
    Task<bool> IsPlayingAsync();
    Task ResumePlaybackAsync();
    Task PausePlaybackAsync();
    Task<int> GetPlaybackProgressMsAsync();
    Task SeekToPlaybackTime(int ms);
    Task SkipTrack();
    Task<TrackPOCO> GetCurrentlyPlayingTrackAsync();
    public Task InitializeClient();
    public void SetMainWindow(Window mainWindow);

}