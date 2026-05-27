using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LumikitApp
{
    /// <summary>
    /// Playback handler to handle local files with minimal latancy
    /// </summary>
    public class LocalFilesPlaybackHandler : IPlaybackHandler
    {
        private readonly IMusicProvider _musicProvider;
        private readonly Stopwatch _syncStopwatch = new();
        private bool _timerRunning;
        private int _progressMs;

        public int CurrentProgressMs => _progressMs;
        public bool IsTimerRunning => _timerRunning;

        public event Action<int> ProgressUpdated;
        public event Action PlaybackStopped;

        public LocalFilesPlaybackHandler(IMusicProvider provider)
        {
            _musicProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public async Task PauseAsync()
        {
            try
            {
                StopTimer();
                await _musicProvider.PausePlaybackAsync();
                
                //adjust playback timer for any potential delay between services
                _progressMs = await _musicProvider.GetPlaybackProgressMsAsync();
                
                ProgressUpdated?.Invoke(_progressMs);

            }
            catch (Exception ex)
            {
                Debug.WriteLine("Pause failed: " + ex);
            }
        }

        public async Task ResumeAsync()
        {
            var seconds = await _musicProvider.GetPlaybackProgressMsAsync() / 1000;
            _progressMs = 0;

                if (!await _musicProvider.IsPlayingAsync())
                {
                    await _musicProvider.ResumePlaybackAsync();
                    _syncStopwatch.Restart();
                    StartTimer(_progressMs + seconds*1000);
                }

        }

        public async Task PlayAsync()
        {
            try
            {
                StopTimer();
                await _musicProvider.PlayTrackAsync();
                await RestartAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Play Track failed: " + ex);
            }
        }

        public async Task RestartAsync()
        {
            await PauseAsync();
            _musicProvider.SeekToPlaybackTime(0);
            _progressMs = 0;
            await Task.Delay(10);
            await ResumeAsync();

        }

        public async Task SeekToPlaybackTime(int ms)
        {
            await PauseAsync();
            ms = (ms / 1000) * 1000;
            _musicProvider.SeekToPlaybackTime(ms);
            await Task.Delay(10);
            StartTimer(ms);
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
            PlaybackStopped?.Invoke();
        }
    }
}