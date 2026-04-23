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
        void StartTimer(int initialProgressMs);
        void StopTimer();
        int CurrentProgressMs { get; }
        event Action<int> ProgressUpdated;
    }
}