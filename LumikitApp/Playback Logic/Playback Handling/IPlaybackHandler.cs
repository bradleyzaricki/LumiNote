using System;
using System.Threading.Tasks;

namespace LumikitApp
{
    public interface IPlaybackHandler
    {
        Task PauseAsync();
        Task ResumeAsync();
        Task PlayAsync();
        Task RestartAsync();
        Task SeekToPlaybackTime(int ms);
        void StartTimer(int initialProgressMs);
        void StopTimer();
        int CurrentProgressMs { get; }
        bool IsTimerRunning { get; }
        event Action<int> ProgressUpdated;
        event Action PlaybackStopped;
    }
}