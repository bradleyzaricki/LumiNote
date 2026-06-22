using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LumikitApp
{
    /// <summary>
    /// Drives the lightshow clock for Spotify playback.
    ///
    /// Spotify is remote-controlled over the network and exposes no per-frame position,
    /// so we don't poll it every frame. Instead we anchor a local <see cref="Stopwatch"/>
    /// to an RTT-corrected position reading and extrapolate from there (zero per-frame
    /// latency), while a background loop periodically re-reads the real position and gently
    /// nudges the anchor to cancel drift — snapping only when something large knocks us out
    /// of sync (e.g. the user paused on another device).
    /// </summary>
    public class SpotifyPlaybackHandler : IPlaybackHandler
    {
        private readonly IMusicProvider _musicProvider;
        private readonly Stopwatch _syncStopwatch = new();
        private bool _timerRunning;
        private int _progressMs;

        // Playback position that corresponds to _syncStopwatch == 0. The reported progress
        // is always _anchorMs + elapsed; re-syncing simply shifts this anchor.
        private int _anchorMs;

        // How often the background loop re-reads Spotify's real position.
        private const int ResyncIntervalMs = 1500;
        // Per-correction cap so routine drift is absorbed invisibly (sub-frame at 50ms).
        private const int MaxNudgeMs = 40;
        // Above this error we snap instead of nudging (big external desync / seek miss).
        private const int SnapThresholdMs = 400;

        public int CurrentProgressMs => _progressMs;
        public bool IsTimerRunning => _timerRunning;

        public event Action<int> ProgressUpdated;
        public event Action PlaybackStopped;

        public SpotifyPlaybackHandler(IMusicProvider provider)
        {
            _musicProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public async Task PauseAsync()
        {
            try
            {
                StopTimer();
                await _musicProvider.PausePlaybackAsync();

                // Anchor the display to the real position after pausing.
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
            try
            {
                if (!await _musicProvider.IsPlayingAsync())
                    await _musicProvider.ResumePlaybackAsync();

                // Anchor to the true position (corrected for network round-trip), full ms.
                int anchor = await GetAnchoredPositionMsAsync();
                StartTimer(anchor);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Resume failed: " + ex);
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

        /// <summary>
        /// Restarts the track from the start. A fresh track genuinely needs a moment to load
        /// on Spotify's side before the position read is meaningful.
        /// </summary>
        public async Task RestartAsync()
        {
            await PauseAsync();
            await _musicProvider.SeekToPlaybackTime(0);
            _progressMs = 0;
            await Task.Delay(400);
            await ResumeAsync();
        }

        /// <summary>
        /// Seeks during playback without a pause/stop cycle. Spotify's SeekTo works while
        /// playing, so we seek, re-anchor optimistically to the requested time, and let the
        /// re-sync loop absorb any residual error — no full-second rounding, no fixed delay.
        /// </summary>
        public async Task SeekToPlaybackTime(int ms)
        {
            try
            {
                await _musicProvider.SeekToPlaybackTime(ms);

                if (!await _musicProvider.IsPlayingAsync())
                    await _musicProvider.ResumePlaybackAsync();

                StartTimer(ms);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Seek failed: " + ex);
            }
        }

        /// <summary>
        /// (Re)anchors the local clock to <paramref name="initialProgressMs"/>. If the timer
        /// loop isn't running yet, starts it plus the background re-sync loop.
        /// </summary>
        public void StartTimer(int initialProgressMs)
        {
            _anchorMs = initialProgressMs;
            _syncStopwatch.Restart();

            if (_timerRunning) return; // loops already running — just re-anchored
            _timerRunning = true;

            _ = Task.Run(TimerLoop);
            _ = Task.Run(ResyncLoop);
        }

        public void StopTimer()
        {
            _timerRunning = false;
            _syncStopwatch.Stop();
            PlaybackStopped?.Invoke();
        }

        // Extrapolate position from the anchor every 10ms — no network involved.
        private async Task TimerLoop()
        {
            while (_timerRunning)
            {
                _progressMs = _anchorMs + (int)_syncStopwatch.ElapsedMilliseconds;
                ProgressUpdated?.Invoke(_progressMs);
                await Task.Delay(10);
            }
        }

        // Periodically reconcile the local clock with Spotify's real position.
        private async Task ResyncLoop()
        {
            while (_timerRunning)
            {
                await Task.Delay(ResyncIntervalMs);
                if (!_timerRunning) break;

                try
                {
                    // Don't reconcile while paused/buffering — the reading would be misleading.
                    if (!await _musicProvider.IsPlayingAsync()) continue;

                    int truth    = await GetAnchoredPositionMsAsync();
                    int estimate = _anchorMs + (int)_syncStopwatch.ElapsedMilliseconds;
                    int error    = truth - estimate;

                    if (Math.Abs(error) > SnapThresholdMs)
                        _anchorMs += error;                                   // big desync: snap
                    else
                        _anchorMs += Math.Clamp(error, -MaxNudgeMs, MaxNudgeMs); // drift: nudge
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Resync failed: " + ex);
                }
            }
        }

        /// <summary>
        /// Reads Spotify's reported position and adds an estimate of one-way network latency
        /// (half the measured round-trip), since the value was sampled server-side before it
        /// reached us.
        /// </summary>
        private async Task<int> GetAnchoredPositionMsAsync()
        {
            var rtt = Stopwatch.StartNew();
            int pos = await _musicProvider.GetPlaybackProgressMsAsync();
            rtt.Stop();
            return pos + (int)(rtt.ElapsedMilliseconds / 2);
        }
    }
}
