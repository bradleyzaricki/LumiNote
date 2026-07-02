using Avalonia.Media;

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
    /// <summary>
    /// Provides the theme color of the provider
    /// (ex. Spotify Green, Soundcloud Orange, Lumanite Purple)
    /// </summary>
    public Color ProviderColor {get; set;}
    
    /// <summary>
    /// Returns whether or not the music provider is local
    /// or an external source 
    /// </summary>
    bool IsProviderLocal {get; set;}
    
    /// <summary>
    /// Returns a trackPOCO of the current track playing
    /// </summary>
    TrackPOCO currentTrack {get; set;}
    
    /// <summary>
    /// Returns the currently playing file path
    /// If music is sourced externally this will return
    /// the id provided by the external source
    /// </summary>
    string currentlyPlayingPath {get; set;}
    
    /// <summary>
    /// The type of the music provider
    /// </summary>
    ProviderType providerName { get; }
    
    /// <summary>
    /// Is track currently playing and not paused
    /// </summary>
    /// <returns></returns>
    Task<bool> IsPlayingAsync();
    
    /// <summary>
    /// Prompt the provider to resume current playback
    /// </summary>
    /// <returns></returns>
    Task ResumePlaybackAsync();
    
    /// <summary>
    /// Prompt the provider to pause current playback
    /// </summary>
    /// <returns></returns>
    Task PausePlaybackAsync();
    
    /// <summary>
    /// Returns the track progress in ms
    /// </summary>
    /// <returns></returns>
    Task<int> GetPlaybackProgressMsAsync();
    
    /// <summary>
    /// Prompt the provider to seek to the select playback time
    /// </summary>
    /// <param name="ms"></param>
    /// <returns></returns>
    internal Task SeekToPlaybackTime(int ms);
    public Task PlayTrackAsync();
    Task<TrackPOCO> GetCurrentlyPlayingTrackAsync();
    string GetCurrentlyPlayingTrackIdAsync();
    public Task InitializeClient();
    public IPlaybackHandler GetPlaybackHandler();

}