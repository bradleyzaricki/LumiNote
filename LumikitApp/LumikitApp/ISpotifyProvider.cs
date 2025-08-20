using System.Threading.Tasks;
using SpotifyAPI.Web;

namespace LumikitApp
{
    public interface ISpotifyProvider
    {
        Task<SpotifyClient> InitializeClient();
        Task<bool> IsPlayingAsync();
        Task ResumePlaybackAsync();
        Task PausePlaybackAsync();
        Task<FullTrack> GetCurrentlyPlayingTrackAsync();
        Task<int> GetPlaybackProgressMsAsync();
        Task SeekToPlaybackTime(int ms);
        Task SkipTrack();
    }
}