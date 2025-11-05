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
/// Music provider allows for manipulation and fetching of the physical music, unlike the
/// Playback handler which provides local track lighting data
/// </summary>
public interface IMusicProvider
{
    Task<bool> IsPlayingAsync();
    Task ResumePlaybackAsync();
    Task PausePlaybackAsync();
    Task<int> GetPlaybackProgressMsAsync();
    Task SeekToPlaybackTime(int ms);
    Task SkipTrack();
    Task<TrackPOCO> GetCurrentlyPlayingTrackAsync();

}