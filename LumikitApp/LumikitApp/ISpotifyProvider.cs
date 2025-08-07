using System.Threading.Tasks;
using SpotifyAPI.Web;

namespace LumikitApp
{
    public interface ISpotifyProvider
    {
        Task<SpotifyClient> InitializeClient();
        Task<bool> IsPlaying();
        Task ResumePlayback();
        Task PausePlayback();
        Task<FullTrack> GetCurrentlyPlayingTrack();
        Task<int> GetPlaybackProgressMs();
        Task SeekToPlaybackTime(int ms);
        Task SkipTrack();
    }
}