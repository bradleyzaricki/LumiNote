using SpotifyAPI.Web;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LumikitApp
{
    public class PlaybackHandler : IPlaybackHandler
    {
        private readonly SpotifyProvider _spotifyProvider;
        private readonly Stopwatch _syncStopwatch = new();
        private bool _timerRunning;
        private int _progressMs;

        public int CurrentProgressMs => _progressMs;

        public event Action<int> ProgressUpdated;

        public PlaybackHandler(SpotifyProvider provider)
        {
            _spotifyProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public async Task PauseAsync()
        {
            try
            {
                StopTimer();
                await _spotifyProvider.PausePlaybackAsync();
                
                _progressMs = await _spotifyProvider.GetPlaybackProgressMsAsync();
                await Task.Delay(1000);
                ProgressUpdated?.Invoke(_progressMs);

            }
            catch (Exception ex)
            {
                Debug.WriteLine("Pause failed: " + ex);
            }
        }

        public async Task ResumeAsync()
        {
            var seconds = await _spotifyProvider.GetPlaybackProgressMsAsync() / 1000;
            await _spotifyProvider.SeekToPlaybackTime(seconds*1000);

            Console.WriteLine(seconds);
            _progressMs = 0;
            try
            {
                if (!await _spotifyProvider.IsPlayingAsync())
                {
                    await _spotifyProvider.ResumePlaybackAsync();
                    _syncStopwatch.Restart();
                    StartTimer(_progressMs + seconds*1000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Resume failed: " + ex);
            }
        }

        public async Task SkipAsync()
        {
            try
            {
                StopTimer();
                await _spotifyProvider.SkipTrack();
                await RestartAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Skip failed: " + ex);
            }
        }

        public async Task RestartAsync()
        {
            await PauseAsync();
            _spotifyProvider.SeekToPlaybackTime(0);
            _progressMs = 0;
            await Task.Delay(1000);
            await ResumeAsync();

        }

        public void StartTimer(int initialProgressMs)
        {
            if (_timerRunning) return;

            _progressMs = initialProgressMs;
            _syncStopwatch.Restart();
            _timerRunning = true;

            _ = Task.Run(async () =>
            {
                while (_timerRunning)
                {
                    _progressMs = initialProgressMs + (int)_syncStopwatch.ElapsedMilliseconds;
                    ProgressUpdated?.Invoke(_progressMs);
                    await Task.Delay(10);
                }
            });
        }

        public void StopTimer()
        {
            _timerRunning = false;
            _syncStopwatch.Stop();
        }
    }
}
