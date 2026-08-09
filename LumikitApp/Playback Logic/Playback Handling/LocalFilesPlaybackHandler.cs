using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LumikitApp
{
    /// <summary>
    /// Playback handler to handle local files with minimal latancy
    /// </summary>
    public class LocalFilesPlaybackHandler : IPlaybackHandler
    {
        private readonly IMusicProvider _musicProvider;

        // Serializes pause/resume/seek/play so overlapping calls (e.g. rapid seeks from the
        // timeline) can't interleave and anchor the light clock to a stale position.
        private readonly SemaphoreSlim _gate = new(1, 1);

        // Bumped on every timer start/stop; a progress loop exits as soon as the generation
        // moves past its own, so a retired loop can never keep publishing progress.
        private int _timerGeneration;
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
            await _gate.WaitAsync();
            try
            {
                await PauseCoreAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task ResumeAsync()
        {
            await _gate.WaitAsync();
            try
            {
                await ResumeCoreAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task PlayAsync()
        {
            await _gate.WaitAsync();
            try
            {
                StopTimer();
                await _musicProvider.PlayTrackAsync();
                await RestartCoreAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Play Track failed: " + ex);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task RestartAsync()
        {
            await _gate.WaitAsync();
            try
            {
                await RestartCoreAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SeekToPlaybackTime(int ms)
        {
            await _gate.WaitAsync();
            try
            {
                // Stop timer silently — seeking is not a stop, so don't fire PlaybackStopped
                StopTimerSilently();

                await _musicProvider.PausePlaybackAsync();
                await _musicProvider.SeekToPlaybackTime(ms);
                await Task.Delay(10);
                await _musicProvider.ResumePlaybackAsync();
                StartTimer(ms);
            }
            finally
            {
                _gate.Release();
            }
        }

        // ── Core operations (assume _gate is held) ───────────────────────────

        private async Task PauseCoreAsync()
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

        private async Task ResumeCoreAsync()
        {
            var posMs = await _musicProvider.GetPlaybackProgressMsAsync();

            if (!await _musicProvider.IsPlayingAsync())
            {
                await _musicProvider.ResumePlaybackAsync();
                StartTimer(posMs);
            }
        }

        private async Task RestartCoreAsync()
        {
            await PauseCoreAsync();
            await _musicProvider.SeekToPlaybackTime(0);
            _progressMs = 0;
            await Task.Delay(10);
            await ResumeCoreAsync();
        }

        // ── Timer ─────────────────────────────────────────────────────────────

        public void StartTimer(int initialProgressMs)
        {
            // Retire any previous loop and always re-anchor — an early-return guard here
            // would leave the clock on an old anchor after back-to-back seeks.
            int gen = Interlocked.Increment(ref _timerGeneration);
            _progressMs = initialProgressMs;
            _timerRunning = true;

            var anchor = Stopwatch.StartNew();
            _ = Task.Run(async () =>
            {
                double anchorMs = initialProgressMs;
                while (Volatile.Read(ref _timerGeneration) == gen)
                {
                    int ms = (int)(anchorMs + anchor.ElapsedMilliseconds);
                    if (Volatile.Read(ref _timerGeneration) != gen) break;
                    _progressMs = ms;
                    ProgressUpdated?.Invoke(ms);

                    // Drift correction: the anchor is taken at the call site, but BASS starts
                    // and reports audio some tens of ms away from that moment, and the error
                    // differs between play/resume/seek. The real position is a cheap native
                    // read for local files, so steer the clock toward it. Slewing (rather
                    // than snapping) keeps the lights smooth and tolerates read granularity;
                    // the grace period lets the start-of-playback transient settle first.
                    if (anchor.ElapsedMilliseconds > 150)
                    {
                        int actual = await _musicProvider.GetPlaybackProgressMsAsync();
                        if (actual >= 0)
                        {
                            double err = actual - (anchorMs + anchor.ElapsedMilliseconds);
                            if (Math.Abs(err) > 250)
                                anchorMs += err;       // gross error: snap
                            else
                                anchorMs += err * 0.1; // small error: slew smoothly
                        }
                    }

                    await Task.Delay(10);
                }
            });
        }

        public void StopTimer()
        {
            StopTimerSilently();
            PlaybackStopped?.Invoke();
        }

        private void StopTimerSilently()
        {
            Interlocked.Increment(ref _timerGeneration);
            _timerRunning = false;
        }
    }
}