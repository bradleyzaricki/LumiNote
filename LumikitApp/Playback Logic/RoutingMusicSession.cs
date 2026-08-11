using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;

namespace LumikitApp
{
    /// <summary>
    /// The active playback session. Holds every provider/handler pair the user enabled
    /// at startup and routes both the <see cref="IMusicProvider"/> and
    /// <see cref="IPlaybackHandler"/> surfaces to whichever pair is currently active.
    ///
    /// This is a delegating composite — it contains no playback logic of its own, it only
    /// forwards to the active concrete pair. Switching sources (<see cref="SwitchToAsync"/>)
    /// pauses the current handler first, then swaps. The window holds stable references to
    /// this single instance, so its event subscriptions survive a switch.
    /// </summary>
    public class RoutingMusicSession : IMusicProvider, IPlaybackHandler
    {
        private readonly Dictionary<ProviderType, IMusicProvider> _providers = new();
        private readonly Dictionary<ProviderType, IPlaybackHandler> _handlers = new();
        private ProviderType _activeName;

        // The handler this session is currently forwarding events from.
        private IPlaybackHandler? _subscribedHandler;

        /// <summary>Raised after the active provider changes (e.g. to refresh theming).</summary>
        public event Action? ProviderSwitched;

        public RoutingMusicSession(IEnumerable<(IMusicProvider provider, IPlaybackHandler handler)> pairs)
        {
            foreach (var (provider, handler) in pairs)
            {
                _providers[provider.providerName] = provider;
                _handlers[provider.providerName]  = handler;
            }

            if (_providers.Count == 0)
                throw new ArgumentException("At least one provider must be enabled.", nameof(pairs));

            _activeName = _providers.Keys.First();
            SubscribeActiveHandler();
        }

        public IReadOnlyCollection<ProviderType> AvailableProviders => _providers.Keys;
        public ProviderType ActiveProviderName => _activeName;
        public bool HasProvider(ProviderType name) => _providers.ContainsKey(name);

        private IMusicProvider  ActiveProvider => _providers[_activeName];
        private IPlaybackHandler ActiveHandler  => _handlers[_activeName];

        /// <summary>
        /// Pauses the current source, then makes <paramref name="name"/> the active source.
        /// Returns false if that provider isn't enabled. A no-op (and true) if already active.
        /// </summary>
        public async Task<bool> SwitchToAsync(ProviderType name)
        {
            if (!_providers.ContainsKey(name)) return false;
            if (name == _activeName) return true;

            // Pause the currently-playing source before handing off.
            await ActiveHandler.PauseAsync();

            _activeName = name;
            SubscribeActiveHandler();

            ProviderSwitched?.Invoke();
            return true;
        }

        // ── Event forwarding ──────────────────────────────────────────────────
        // Re-point our forwarded events at the active handler, dropping the old wiring.
        private void SubscribeActiveHandler()
        {
            if (_subscribedHandler != null)
            {
                _subscribedHandler.ProgressUpdated -= OnInnerProgressUpdated;
                _subscribedHandler.PlaybackStopped -= OnInnerPlaybackStopped;
            }

            _subscribedHandler = ActiveHandler;
            _subscribedHandler.ProgressUpdated += OnInnerProgressUpdated;
            _subscribedHandler.PlaybackStopped += OnInnerPlaybackStopped;
        }

        private void OnInnerProgressUpdated(int ms) => ProgressUpdated?.Invoke(ms);
        private void OnInnerPlaybackStopped()       => PlaybackStopped?.Invoke();

        // ── IMusicProvider (delegates to active provider) ─────────────────────
        public Color ProviderColor
        {
            get => ActiveProvider.ProviderColor;
            set => ActiveProvider.ProviderColor = value;
        }
        public bool IsProviderLocal
        {
            get => ActiveProvider.IsProviderLocal;
            set => ActiveProvider.IsProviderLocal = value;
        }
        public TrackPOCO currentTrack
        {
            get => ActiveProvider.currentTrack;
            set => ActiveProvider.currentTrack = value;
        }
        public string currentlyPlayingPath
        {
            get => ActiveProvider.currentlyPlayingPath;
            set => ActiveProvider.currentlyPlayingPath = value;
        }
        public ProviderType providerName => ActiveProvider.providerName;

        public Task<bool> IsPlayingAsync()             => ActiveProvider.IsPlayingAsync();
        public Task ResumePlaybackAsync()              => ActiveProvider.ResumePlaybackAsync();
        public Task PausePlaybackAsync()               => ActiveProvider.PausePlaybackAsync();
        public Task<int> GetPlaybackProgressMsAsync()  => ActiveProvider.GetPlaybackProgressMsAsync();
        public Task<int> GetTrackDurationMsAsync()     => ActiveProvider.GetTrackDurationMsAsync();
        public Task PlayTrackAsync()                   => ActiveProvider.PlayTrackAsync();
        public Task<TrackPOCO> GetCurrentlyPlayingTrackAsync() => ActiveProvider.GetCurrentlyPlayingTrackAsync();
        public string GetCurrentlyPlayingTrackIdAsync()       => ActiveProvider.GetCurrentlyPlayingTrackIdAsync();
        public IPlaybackHandler GetPlaybackHandler()   => this;

        // Initialize every enabled provider (Spotify login, Bass init, etc.).
        //
        // One provider failing must not stop the others: a user-supplied API key that's wrong or
        // revoked makes sign-in failure routine, and it would otherwise leave a perfectly good
        // local-files source uninitialized. Every provider gets its turn, then the failures are
        // reported together.
        public async Task InitializeClient()
        {
            List<Exception>? failures = null;

            foreach (var provider in _providers.Values)
            {
                try
                {
                    await provider.InitializeClient();
                }
                catch (Exception ex)
                {
                    (failures ??= new()).Add(
                        new InvalidOperationException($"{provider.providerName.DisplayName()}: {ex.Message}", ex));
                }
            }

            if (failures != null) throw new AggregateException(failures);
        }

        // SeekToPlaybackTime exists on both interfaces with the same signature but different
        // behavior, so both are implemented explicitly and routed to the matching surface.
        Task IMusicProvider.SeekToPlaybackTime(int ms) => ActiveProvider.SeekToPlaybackTime(ms);

        // ── IPlaybackHandler (delegates to active handler) ────────────────────
        public Task PauseAsync()   => ActiveHandler.PauseAsync();
        public Task ResumeAsync()  => ActiveHandler.ResumeAsync();
        public Task PlayAsync()    => ActiveHandler.PlayAsync();
        public Task RestartAsync() => ActiveHandler.RestartAsync();
        Task IPlaybackHandler.SeekToPlaybackTime(int ms) => ActiveHandler.SeekToPlaybackTime(ms);
        public void StartTimer(int initialProgressMs) => ActiveHandler.StartTimer(initialProgressMs);
        public void StopTimer()                       => ActiveHandler.StopTimer();
        public int CurrentProgressMs => ActiveHandler.CurrentProgressMs;
        public bool IsTimerRunning   => ActiveHandler.IsTimerRunning;

        public event Action<int> ProgressUpdated;
        public event Action PlaybackStopped;
    }
}
