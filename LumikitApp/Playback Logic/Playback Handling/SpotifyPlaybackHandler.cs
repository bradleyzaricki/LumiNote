using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LumikitApp
{
    /// <summary>
    /// Drives the lightshow clock for Spotify playback.
    ///
    /// Spotify's Web API only honours whole-second seeks, so every (re)start floors the target
    /// to the previous second and runs PAUSE → SEEK → PLAY, then anchors a local
    /// <see cref="Stopwatch"/> that carries the clock from there. A small fixed
    /// <see cref="PlayLatencyMs"/> delay sits between issuing PLAY and starting the clock, so the
    /// clock begins when audio actually starts rather than when the command is sent.
    /// </summary>
    public class SpotifyPlaybackHandler : IPlaybackHandler
    {
        private readonly IMusicProvider _musicProvider;
        private readonly Stopwatch _syncStopwatch = new();
        private bool _timerRunning;
        private int _progressMs;

        // Playback position that corresponds to _syncStopwatch == 0; reported progress is
        // always _anchorMs + elapsed.
        private int _anchorMs;

        // Identifies the current timer loop. Bumped on every fresh start so a stale loop left
        // over from a prior start exits instead of running in parallel.
        private int _loopGen;

        // Delay between issuing PLAY and starting the clock, to account for the lag before audio
        // actually starts. Bump this up if the lights run ahead of the music.
        private const int PlayLatencyMs = 150;

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
                // _progressMs already holds our precise clock value; keep it (don't overwrite
                // with Spotify's coarse, stale reading).
                StopTimer();
                await _musicProvider.PausePlaybackAsync();
                ProgressUpdated?.Invoke(_progressMs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Pause failed: " + ex);
            }
        }

        /// <summary>Resumes from our own clock position (not Spotify's coarse readback).</summary>
        public async Task ResumeAsync() => await ReanchorAndPlay(_progressMs, loadTrack: false);

        /// <summary>Loads and starts the current track from the beginning.</summary>
        public async Task PlayAsync() => await ReanchorAndPlay(0, loadTrack: true);

        /// <summary>Restarts the (already-loaded) track from the start.</summary>
        public async Task RestartAsync() => await ReanchorAndPlay(0, loadTrack: false);

        /// <summary>Seeks (jumps) to a new position on the already-loaded track.</summary>
        public async Task SeekToPlaybackTime(int ms) => await ReanchorAndPlay(ms, loadTrack: false);

        /// <summary>
        /// Shared (re)start routine for play, resume, restart and seek. Spotify only honours
        /// whole-second seeks, so the target is floored to the previous second. For a fresh play
        /// the track URI is loaded first; then PAUSE parks playback, SEEK moves to the target
        /// while parked, PLAY resumes, and after a small <see cref="PlayLatencyMs"/> wait (for
        /// audio to actually start) the local clock is anchored at that second.
        /// </summary>
        private async Task ReanchorAndPlay(int ms, bool loadTrack)
        {
            try
            {
                if (ms < 0) ms = 0;
                int floored = (ms / 1000) * 1000;

                StopClockSilently();                           // halt our clock without firing PlaybackStopped

                if (loadTrack)
                    await _musicProvider.PlayTrackAsync();     // load the track URI onto the device

                await _musicProvider.PausePlaybackAsync();     // park playback
                await _musicProvider.SeekToPlaybackTime(floored); // move to the target while parked
                await _musicProvider.ResumePlaybackAsync();    // play
                await Task.Delay(PlayLatencyMs);               // let audio actually start
                StartTimer(floored);                           // anchor the clock at that second
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Re-anchor failed: " + ex);
            }
        }

        /// <summary>
        /// (Re)anchors the local clock to <paramref name="initialProgressMs"/>. If the loop
        /// isn't running, starts it under a new generation.
        /// </summary>
        public void StartTimer(int initialProgressMs)
        {
            _anchorMs = initialProgressMs;
            _syncStopwatch.Restart();

            if (_timerRunning) return; // loop already running — just re-anchored
            _timerRunning = true;

            int gen = ++_loopGen;
            _ = Task.Run(() => TimerLoop(gen));
        }

        public void StopTimer()
        {
            _timerRunning = false;
            _syncStopwatch.Stop();
            PlaybackStopped?.Invoke();
        }

        // Stop the clock for an internal transition (e.g. a seek) without notifying listeners.
        private void StopClockSilently()
        {
            _timerRunning = false;
            _syncStopwatch.Stop();
        }

        // Extrapolate position from the anchor every 10ms — no network involved.
        private async Task TimerLoop(int gen)
        {
            while (_timerRunning && gen == _loopGen)
            {
                _progressMs = _anchorMs + (int)_syncStopwatch.ElapsedMilliseconds;
                ProgressUpdated?.Invoke(_progressMs);
                await Task.Delay(10);
            }
        }
    }
}
