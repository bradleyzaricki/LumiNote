    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Input;
    using Avalonia.Interactivity;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Threading;
    using Avalonia.VisualTree;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.IO.Ports;
    using LumikitApp.Controls;
    using LumikitApp.Models;
    using LumikitApp.UI.Windows;
    using LumikitApp.ViewModels;

    namespace LumikitApp
    {
        public partial class LumikitWindow : Window
        {
            private bool _previewRunning;
            private readonly Stopwatch _previewWatch = new();
            private int _lastSerialSendMs = 0;
            
            /// <summary>
            /// The track fields to send to the music provider when track is unknown, to avoid displaying or editing data for an incorrect class
            /// </summary>
            private static TrackPOCO s_unknownTrack = new TrackPOCO(Guid.Empty, "Unnamed Track", "Unnamed Artists", null);

            /// <summary>
            /// Frame interval for the strip, the on-screen preview and the effect grid.
            ///
            /// 50 ms (20 fps). Briefly ran at 25 ms, which overloaded the receiving MCU: at that
            /// cadence frames arrive faster than the device drains them, and because the OS write
            /// buffer is 64 KB the backlog shows up as the strip lagging the music rather than as
            /// an error. Raise this only alongside a measured check that the device keeps up at
            /// the target LED count — bandwidth on paper is not the binding constraint, the
            /// firmware's per-frame handling is.
            /// </summary>
            private const int ColorUpdateIntervalMs = 50;

            /// <summary>
            /// Link to the currently displayed track on the active provider's own service, or
            /// null when it has none (local files) or Spotify hasn't reported one yet. Backs
            /// the attribution link required alongside displayed Spotify metadata/cover art.
            /// </summary>
            private string? _currentProviderTrackUrl;

            /// <summary>
            /// The user's own API keys per source. Held so the active source's key can be changed
            /// without restarting into the picker.
            /// </summary>
            private readonly ProviderCredentialStore _credentialStore = null!;

            /// <summary>
            /// The source responsible for playing/pausing/locating a point in a music file
            /// </summary>
            private IMusicProvider _musicProvider;

            /// <summary>
            /// Routes between the enabled providers and lets us switch source at runtime.
            /// Same instance as <see cref="_musicProvider"/> / <see cref="_playbackHandler"/>.
            /// </summary>
            private readonly RoutingMusicSession _musicRouter;

            private JsonDataHandler _jsonDataHandler;
            
            private DatabaseAccess _databaseAccess;
            private GoogleAuthService _googleAuth;

            // lightmap_id → version from the last cloud refresh; drives "update available".
            private Dictionary<string, int> _cloudVersions = new();

            // Last lightmap ID the cloud search box auto-downloaded, so retyping/reformatting
            // the same pasted ID doesn't refire the download on every keystroke.
            private Guid? _lastCloudIdFetch;
            /// <summary> The serial output for lighting communications  </summary>
            private ISerialPanel _serialPanel;
            
            /// <summary> Possible color blocks for lightshow editing, can be redefined by the user </summary>
            private readonly List<Color> BlockColors = new()
            {
                Colors.DarkRed, Colors.Red, Colors.Orange, Colors.Yellow, Colors.Green,Colors.Aqua, Colors.Blue,
                Colors.Purple, Colors.Magenta, Colors.White
            };
            
            //Current track data for accurate lightmap/audio matching logic
            private string currentGUID = Guid.Empty.ToString();

            // True once the loaded lightmap has unsaved edits; gates track switching so the user
            // is prompted to save before moving on. Cleared on load and on a successful save.
            private bool _lightmapDirty;
            
            //Lists to store both local track UI items and database track ui items
            private List<TrackItemUI> _allLocalTrackItems = new();
            private List<TrackItemUI> _allDatabaseTrackItems = new();

            //Avalonia UI elements
            private Canvas _blockColorDropBox;
            private Canvas _secondColorDropBox;
            private Canvas _fillColorDropBox;
            private Canvas _strobeColorDropBox;
            private TextBox _bpmInput;
            
            // Debug console overlay (see ErrorConsole in the axaml): renders IAppLog entries;
            // the pill button appears/counts only for Error-level entries while it's closed.
            private IAppLog _log;
            private Border _errorConsole;
            private Button _viewErrorsButton;
            private ScrollViewer _consoleScroll;
            private readonly ObservableCollection<ConsoleRow> _consoleRows = new();
            private int _unseenErrors;

            /// <summary>Presentation row for one AppLogEntry: preformatted text + severity color.</summary>
            private sealed class ConsoleRow
            {
                private static readonly IBrush InfoBrush  = new SolidColorBrush(Color.Parse("#B8B8B8"));
                private static readonly IBrush WarnBrush  = new SolidColorBrush(Color.Parse("#E5C07B"));
                private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FF6B6B"));

                public string Text { get; }
                public IBrush Brush { get; }

                public ConsoleRow(AppLogEntry entry)
                {
                    var level = entry.Level switch
                    {
                        AppLogLevel.Error => "ERR",
                        AppLogLevel.Warning => "WRN",
                        _ => "INF"
                    };
                    var source = entry.Source == "App" ? "" : $" [{entry.Source}]";
                    Text = $"[{entry.Timestamp:HH:mm:ss} {level}]{source} {entry.Message}";
                    Brush = entry.Level switch
                    {
                        AppLogLevel.Error => ErrorBrush,
                        AppLogLevel.Warning => WarnBrush,
                        _ => InfoBrush
                    };
                }
            }

            private TextBlock _hardwareConnectionText;
            
            /// <summary>
            /// implemented playback handler to control implemented music provider
            /// </summary>
            private IPlaybackHandler _playbackHandler;

            // ── Playback queue ────────────────────────────────────────────────
            /// <summary>Upcoming tracks; head plays next. Bound to the Queue tab's list.</summary>
            private readonly ObservableCollection<QueueEntry> _queue = new();

            /// <summary>Duration of the playing track (ms); 0 = unknown → no auto-advance.</summary>
            private int _currentTrackDurationMs;

            /// <summary>Re-entrancy guard: progress ticks keep firing while a switch is underway.</summary>
            private bool _queueAdvancing;

            // Queue drag-reorder state (pointer-based; see QueueRow_* handlers).
            private Border? _queueDragRow;
            private QueueEntry? _queueDragEntry;
            private double _queueDragStartY;
            private bool _queueDragActive;

            //Live "Piano Roll" Block Painting Variables
            Border? _activeSwatch;
            private BlockEditorPanel _blockEditor;
            private BlockEditorViewModel _viewModel;

            // Per-provider audio→light sync offset (ms), applied at the tick site as
            // Timeline.Tick(ms + offset). Kept separate per source because local and Spotify have
            // different latency, and a single lightmap can play on either. Keyed by provider name,
            // set by the Calibrate Sync button (the tap screen) and persisted across sessions.
            private readonly Dictionary<ProviderType, int> _providerOffsets = new();
            private const int DefaultOffsetMs = 20;
            private int ActiveOffsetMs =>
                _providerOffsets.TryGetValue(_musicProvider.providerName, out var v) ? v : DefaultOffsetMs;
            private static string OffsetsPath =>
                Path.Combine(DirectoryPaths.SettingsDir, "sync_offsets.json");

            public LumikitWindow()  // designer uses this
            {
                InitializeComponent();
            }
            public LumikitWindow(IMusicProvider provider, IPlaybackHandler playbackHandler, JsonDataHandler jsonDataHandler, DatabaseAccess databaseAccess, BlockEditorViewModel blockEditorViewModel, ISerialPanel serialPanel, RoutingMusicSession musicRouter, GoogleAuthService googleAuth, IAppLog appLog, ProviderCredentialStore credentialStore)
            {
                _credentialStore = credentialStore;
                _musicProvider = provider;
                _playbackHandler = playbackHandler;
                _musicRouter = musicRouter;
                _jsonDataHandler = jsonDataHandler;
                _databaseAccess = databaseAccess;
                _googleAuth = googleAuth;
                _viewModel = blockEditorViewModel;
                _log = appLog;
                LoadProviderOffsets();
                InitializeComponent();
                DataContext = _viewModel;

                _errorConsole = this.FindControl<Border>("ErrorConsole");
                _viewErrorsButton = this.FindControl<Button>("ViewErrorsButton");
                _consoleScroll = this.FindControl<ScrollViewer>("ConsoleScroll");
                this.FindControl<ItemsControl>("ConsoleItems").ItemsSource = _consoleRows;
                foreach (var entry in _log.Snapshot()) _consoleRows.Add(new ConsoleRow(entry)); // entries logged before the window existed
                _log.EntryAdded += OnLogEntry;
                _hardwareConnectionText = this.FindControl<TextBlock>("HardwareConnectionText");
                _blockColorDropBox = this.FindControl<Canvas>("ColorDropBox");
                _secondColorDropBox = this.FindControl<Canvas>("SecondColorDropBox");
                _fillColorDropBox = this.FindControl<Canvas>("FillColorDropBox");
                _strobeColorDropBox = this.FindControl<Canvas>("StrobeColorDropBox");
                UpdateGoogleSignInButton(); // reflect a persisted sign-in from a previous run
                _blockEditor = new BlockEditorPanel(_viewModel, Timeline);
                _serialPanel = serialPanel;
                _serialPanel.ErrorOccurred           += msg => _log.Error(msg, "Serial");
                _serialPanel.ConnectionStatusChanged += UpdateHardwareConnectionText;

                _viewModel.PreviewRequested += () =>
                {
                    StopPreview();
                    if (!IsPlaybackActive) PlayPreview();
                };

                Timeline.SeekRequested += async ms => await _playbackHandler.SeekToPlaybackTime(ms);
                Timeline.BlockPressed  += HandleBlockPressed;

                _bpmInput = this.FindControl<TextBox>("BpmInput");

                LocalTracksListBox.ItemsSource = new ObservableCollection<TrackItemUI>();

                var zoomInBtn = this.FindControl<Button>("ZoomInButton");
                if (zoomInBtn != null)
                    zoomInBtn.Click += (_, _) => Timeline.Zoom(1.25);
                var zoomOutBtn = this.FindControl<Button>("ZoomOutButton");
                if (zoomOutBtn != null)
                    zoomOutBtn.Click += (_, _) => Timeline.Zoom(0.8);

                _bpmInput.LostFocus += (_, _) =>
                {
                    if (double.TryParse(_bpmInput.Text, out double bpm) && bpm > 0)
                    {
                        Timeline.Bpm = bpm;
                        Timeline.DrawBPMLines();
                    }
                };

                this.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.RightShift)
                    {
                        Timeline.ChangeScrollLock(true);
                        e.Handled = true;
                    }
                };
                this.KeyDown += OnKeyDown;
                this.KeyUp += OnKeyUp;

                InitializeColorPalette();
                Timeline.DrawTimelineSlots();
            }
            

            private void OnKeyDown(object? sender, KeyEventArgs e)
            {
                if (Timeline.IsLiveInputActive) return;
                var focused = TopLevel.GetTopLevel(this)?
                    .FocusManager?
                    .GetFocusedElement() is TextBox or ComboBox or AutoCompleteBox;

                if (e.Key >= Key.D0 && e.Key <= Key.D9 && focused == false)
                {
                    int colorIndex = e.Key == Key.D0 ? 9 : (e.Key - Key.D1);
                    Timeline.StartLiveBlock(BlockColors[colorIndex], _playbackHandler?.CurrentProgressMs ?? 0);
                }
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V)
                    Timeline.Paste();
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z && !focused)
                {
                    if (Timeline.Undo())
                    {
                        StopPreview();
                        _blockEditor.Hide();
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.OemTilde && !focused)
                {
                    SetErrorConsoleVisible(!_errorConsole.IsVisible);
                    e.Handled = true;
                }
            }

            private void HandleBlockPressed(LightBlock block, PointerPressedEventArgs e)
            {
                var point = e.GetCurrentPoint(block.Container);

                if (point.Properties.IsLeftButtonPressed)
                    _blockEditor.LoadBlockIntoEditor(Timeline.HandleBlockSelection(e, block));

                if (point.Properties.IsRightButtonPressed)
                    Timeline.DeleteSelectedBlocks();
            }

            private void OnKeyUp(object? sender, KeyEventArgs e)
            {
                if (Timeline.IsLiveInputActive &&
                    (e.Key == Key.D1 || e.Key == Key.D2 || e.Key == Key.D3 || e.Key == Key.D4 ||
                     e.Key == Key.D5 || e.Key == Key.D6 || e.Key == Key.D7 || e.Key == Key.D8 ||
                     e.Key == Key.D9 || e.Key == Key.D0))
                {
                    Timeline.EndLiveBlock();
                }
            }

            public void InitializeWindow()
            {
                _blockEditor.Hide();
                
                this.FindControl<Button>("SaveTrackDataButton").Click += async (_, _) =>
                    await ShowSaveLightmapDialogAsync();

                // Any edit pushed to the timeline's undo history marks the lightmap dirty, so a
                // track switch (UI or queue) knows to prompt for a save first.
                Timeline.EditPerformed += () => _lightmapDirty = true;

                                
                
                //Returns custom playbackhandler based on the music provider
                //--This exists because different apps may have different latencies which require a delay
                _playbackHandler.ProgressUpdated += async ms =>
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        // Kill any running block preview the moment real playback ticks.
                        if (_previewRunning && _playbackHandler.IsTimerRunning) StopPreview();

                        // End of track (duration from the provider, e.g. Spotify's track
                        // endpoint) → advance to the next queued track, or stop when empty.
                        if (_currentTrackDurationMs > 0 && ms >= _currentTrackDurationMs
                            && !_queueAdvancing && _playbackHandler.IsTimerRunning)
                        {
                            _queueAdvancing = true;
                            _currentTrackDurationMs = 0;
                            _ = AdvanceQueueAsync();
                            return;
                        }

                        StopwatchLabel.Text = ms.ToString();

                        // Reset serial throttle if ms went backwards (new track / seek / restart)
                        if (ms < _lastSerialSendMs) _lastSerialSendMs = 0;

                        // Per-source sync offset: uses the active provider's calibrated value.
                        Color[]? colors = Timeline.Tick(ms + ActiveOffsetMs, 10, _serialPanel.BrightnessScale, ColorUpdateIntervalMs);

                        if (colors == null)
                        {
                            TopColorBar.Background =
                                new SolidColorBrush(Colors.Transparent);
                            BottomColorBar.Background =
                                new SolidColorBrush(Colors.Transparent);
                            if (ms - _lastSerialSendMs >= ColorUpdateIntervalMs)
                            {
                                _lastSerialSendMs = ms;
                                await _serialPanel.TrySendFrameAsync(colors);
                            }
                            return;
                        }

                        if (colors.Length == 0)
                            return;

                        var blockColor = colors.FirstOrDefault(c => c.A > 0);

                        TopColorBar.Background =
                            new SolidColorBrush(blockColor);

                        BottomColorBar.Background =
                            new SolidColorBrush(blockColor);

                        UpdateColorBar(colors);

                        if (ms - _lastSerialSendMs >= ColorUpdateIntervalMs)
                        {
                            _lastSerialSendMs = ms;
                            await _serialPanel.TrySendFrameAsync(colors);
                        }
                    });
                };
                _playbackHandler.PlaybackStopped += () =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                            PlayPreview();
                    });
                };

                    this.FindControl<Button>("PauseTrackButton").Click +=
                        async (_, _) => await _playbackHandler.PauseAsync();
                    this.FindControl<Button>("ResumeTrackButton").Click +=
                        async (_, _) => await _playbackHandler.ResumeAsync();
                    this.FindControl<Button>("NextTrackButton").Click += async (_, _) =>
                    {
                        if (_queue.Count == 0)
                        {
                            _log.Warn("Queue is empty — add tracks with the ⤒/⤓ buttons.");
                            return;
                        }
                        await AdvanceQueueAsync();
                    };

                    // Queue tab: bind the list and keep the header count in sync.
                    QueueList.ItemsSource = _queue;
                    _queue.CollectionChanged += (_, _) => UpdateQueueCountText();
                    this.FindControl<Button>("RestartTrackButton").Click +=
                        async (_, _) => _playbackHandler.RestartAsync();

                    // Re-theme and recolor the track list whenever the active source changes.
                    _musicRouter.ProviderSwitched += () =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            ChangeAppTheme();
                            _allLocalTrackItems = BuildLocalTrackItems();
                            LocalTracksListBox.ItemsSource = _allLocalTrackItems;
                        });
                    };

                    ChangeAppTheme(); //Changes app colors to match provider (required by spotify TOS)
                    _allLocalTrackItems = BuildLocalTrackItems();

                    // Populate the shared-library preview immediately if a prior session's
                    // Google sign-in persisted; otherwise this just leaves it empty.
                    _ = RefreshCloudTracksAsync();
                        
                
            }

            /// <summary>
            /// Shows the save-lightmap flow (overwrite an existing lightmap for this song, or save
            /// as new) and writes the current timeline to disk. Returns true if a save completed,
            /// false if the user cancelled. Shared by the Save button and the pre-switch prompt.
            /// </summary>
            private async Task<bool> ShowSaveLightmapDialogAsync()
            {
                var playbackId = _musicProvider.currentlyPlayingPath;

                // Other local lightmaps already saved for this exact song (same provider source) —
                // let the user pick one to overwrite instead of silently clobbering whatever
                // currentGUID happens to still point at.
                var candidates = _jsonDataHandler.GetAllTracks()
                    .Where(t => t.GetSource(_musicProvider.providerName) == playbackId)
                    .ToList();

                Guid targetGuid;
                string title;
                string artists;
                string? lightmapName;

                // Prompt for names. Track name/author autofill from Spotify's metadata when
                // that's the active source; local files leave them for the user to type.
                async Task<(string lightmap, string title, string artists)?> PromptForNamesAsync()
                {
                    var popup = new NewTrackPopup();
                    if (_musicProvider.providerName == ProviderType.Spotify)
                    {
                        var nowPlaying = await _musicProvider.GetCurrentlyPlayingTrackAsync();
                        popup.Prefill(null, nowPlaying?.trackName, nowPlaying?.artistName);
                    }
                    if (await popup.ShowDialog<bool?>(this) != true) return null;
                    return (popup.LightmapText, popup.TitleText, popup.ArtistText);
                }

                if (candidates.Count > 0)
                {
                    var candidateItems = candidates.Select(t => new TrackItemUI
                    {
                        TrackId = t.trackGUID.ToString(),
                        TrackName = t.DisplayName,
                        Subtitle = t.LightmapName != null ? $"{t._trackName} • {t.artist}" : t.artist,
                        Color = new Avalonia.Media.SolidColorBrush(_musicProvider.ProviderColor)
                    }).ToList();

                    var picker = new TrackSaveTargetWindow(candidateItems, currentGUID);
                    var choice = await picker.ShowDialog<Guid?>(this);
                    if (choice == null) return false; // cancelled

                    if (choice.Value == Guid.Empty)
                    {
                        // Save as new → prompt for fresh names.
                        var names = await PromptForNamesAsync();
                        if (names == null) return false;
                        (lightmapName, title, artists) = names.Value;
                        targetGuid = Guid.NewGuid();
                    }
                    else
                    {
                        // Overwrite an existing lightmap → keep its names, no prompt.
                        targetGuid = choice.Value;
                        var existing = _jsonDataHandler.GetTrack(targetGuid.ToString());
                        lightmapName = existing?.LightmapName;
                        title = existing?._trackName ?? "";
                        artists = existing?.artist ?? "";
                    }
                }
                else
                {
                    // No existing lightmap for this song → inherently new, prompt for names.
                    var names = await PromptForNamesAsync();
                    if (names == null) return false;
                    (lightmapName, title, artists) = names.Value;
                    targetGuid = currentGUID != Guid.Empty.ToString() ? Guid.Parse(currentGUID) : Guid.NewGuid();
                }

                // Saving over someone else's downloaded lightmap forks it into a remix you
                // own (move, not copy — mirrors the server's reupload rule): new identity,
                // provenance in ParentLightmapId, and the downloaded original file removed.
                var prior = _jsonDataHandler.GetTrack(targetGuid.ToString());
                bool foreignOwned = prior?.OwnerId != null && prior.OwnerId != _googleAuth.UserId;
                if (foreignOwned)
                {
                    _jsonDataHandler.DeleteTrack(targetGuid.ToString());
                    targetGuid = Guid.NewGuid();
                }

                currentGUID = targetGuid.ToString();

                Console.WriteLine("Saving " + title +" lightmap with filepath: " + _musicProvider.currentlyPlayingPath);
                Timeline.ReorderLightBlocks();
                // Preserve any previously linked sources (e.g. a Spotify link added to a local track)
                var existingSources = prior?.Sources
                                     ?? new Dictionary<string, string>();

                var trackData = new TrackData
                {
                    filePath = _musicProvider.currentlyPlayingPath,
                    _trackName = title,
                    LightmapName = string.IsNullOrWhiteSpace(lightmapName) ? null : lightmapName,
                    artist = artists,
                    trackGUID = targetGuid,
                    provider = _musicProvider.providerName.ToString(),
                    Sources = existingSources,

                    // Cloud provenance: a fork starts unsynced with a pointer to its parent;
                    // otherwise the prior copy's cloud identity carries through unchanged.
                    CloudLightmapId  = foreignOwned ? null : prior?.CloudLightmapId,
                    ParentLightmapId = foreignOwned
                        ? (prior?.CloudLightmapId ?? prior?.trackGUID.ToString())
                        : prior?.ParentLightmapId,
                    OwnerId     = foreignOwned ? null : prior?.OwnerId,
                    OwnerName   = foreignOwned ? null : prior?.OwnerName,
                    CloudVersion = foreignOwned ? 0 : prior?.CloudVersion ?? 0,
                    _BPM = double.Parse(_bpmInput.Text),
                    _lightBlocks = Timeline.LightBlocks
                        .Select(b => new LightBlockData
                        {
                            X = Canvas.GetLeft(b.Container) / Timeline.SlotWidth,
                            Width = b.Container.Width / Timeline.SlotWidth,
                            Color = (b.BlockColor).ToString(),
                            SecondColor = (b.SecondBlockColor).ToString(),
                            FillColor = (b.FillColor).ToString(),
                            StrobeColor = (b.StrobeColor).ToString(),
                            StartLight = b.StartLight,
                            EndLight = b.EndLight,
                            SecondaryDualInput2 = b.SecondaryEndLight,
                            SecondaryDualInput1 = b.SecondaryStartLight,
                            BlockEffects = b.BlockEffects,
                            LightIntensity = b.Intensity
                        })
                        .ToList()
                };
                trackData.SetSource(_musicProvider.providerName, _musicProvider.currentlyPlayingPath);
                _jsonDataHandler.SaveTrack(trackData);

                _lightmapDirty = false;
                _allLocalTrackItems = BuildLocalTrackItems();
                LocalTracksListBox.ItemsSource = _allLocalTrackItems;
                return true;
            }

            public async void UpdateCurrentTrack(bool startNewLightShow, Guid trackGUID)
            {
                // async void: an unhandled exception here is fatal to the whole app, so the
                // body is fully guarded.
                try
                {
                    currentGUID = trackGUID.ToString();

                    // Spotify's "currently playing" readback is stale for a beat right after a
                    // switch — it returns null until it catches up to the track we asked for.
                    // Poll briefly instead of dereferencing null.
                    TrackPOCO track = null;
                    for (int attempt = 0; attempt < 6 && track == null; attempt++)
                    {
                        track = await _musicProvider.GetCurrentlyPlayingTrackAsync();
                        if (track == null) await Task.Delay(150);
                    }

                    if (track != null)
                    {
                        this.FindControl<TextBlock>("NowPlayingTrackText").Text = track.trackName;
                        this.FindControl<TextBlock>("NowPlayingArtistText").Text = track.artistName;
                        await SetAlbumCover(track.trackCoverImageUrl);

                        // Metadata/cover art just went on screen — refresh the attribution link
                        // that has to accompany it.
                        _currentProviderTrackUrl = track.trackUrl;
                        UpdateProviderAttribution();
                    }
                    else
                    {
                        _log.Warn("Now-playing info unavailable — Spotify readback stayed empty.", "Playback");
                    }

                    Timeline.ClearBlocks();

                    var trackDataLocal = _jsonDataHandler.GetTrack(trackGUID.ToString());
                    if (trackDataLocal != null)
                    {
                        Timeline.Bpm = trackDataLocal._BPM;
                        _bpmInput.Text = Timeline.Bpm.ToString();
                        Timeline.DrawTimelineSlots();
                        Timeline.LoadFromTrackData(trackDataLocal);
                    }
                    else
                    {
                        Timeline.Bpm = 0;
                        _bpmInput.Text = "0";
                        Timeline.DrawTimelineSlots();
                    }

                    // Freshly loaded lightmap — no unsaved edits yet.
                    _lightmapDirty = false;
                }
                catch (Exception ex)
                {
                    _log.Error($"UpdateCurrentTrack failed: {ex.Message}", "Playback");
                }
            }
            
            /// <summary>
            /// Parse and set album cover visual
            /// </summary>
            /// <param name="url"></param>
            private async Task SetAlbumCover(string url)
            {
                try
                {
                    using var client = new HttpClient();
                    var data = await client.GetByteArrayAsync(url);
                    using var stream = new MemoryStream(data);
                    var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
                    var imageControl = this.FindControl<Avalonia.Controls.Image>("AlbumArt");
                    imageControl.Source = bitmap;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to set album cover: " + ex.Message);
                }
            }

            /// <summary>
            /// Renders a new IAppLog entry into the overlay console. Called from whatever
            /// thread logged, so it marshals itself to the UI thread. Only Error-level
            /// entries surface the View Errors pill while the console is closed.
            /// </summary>
            private void OnLogEntry(AppLogEntry entry)
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(() => OnLogEntry(entry));
                    return;
                }

                _consoleRows.Add(new ConsoleRow(entry));
                while (_consoleRows.Count > AppLog.MaxEntries) _consoleRows.RemoveAt(0);

                if (_errorConsole.IsVisible)
                {
                    // Post at Background priority so the new row has been measured first.
                    Dispatcher.UIThread.Post(() => _consoleScroll.ScrollToEnd(), DispatcherPriority.Background);
                }
                else if (entry.Level == AppLogLevel.Error)
                {
                    _unseenErrors++;
                    _viewErrorsButton.Content = _unseenErrors > 1 ? $"⚠ View Errors ({_unseenErrors})" : "⚠ View Errors";
                    _viewErrorsButton.IsVisible = true;
                }
            }

            private void SetErrorConsoleVisible(bool visible)
            {
                _errorConsole.IsVisible = visible;
                if (!visible) return;

                // Opening the console marks all errors as seen; the pill is purely a
                // notification (the ≡ Console button is the persistent way back in).
                _unseenErrors = 0;
                _viewErrorsButton.Content = "⚠ View Errors";
                _viewErrorsButton.IsVisible = false;
                Dispatcher.UIThread.Post(() => _consoleScroll.ScrollToEnd(), DispatcherPriority.Background);
            }

            private void ToggleErrorConsole_Click(object? sender, RoutedEventArgs e) =>
                SetErrorConsoleVisible(!_errorConsole.IsVisible);

            private void CloseErrorConsole_Click(object? sender, RoutedEventArgs e) =>
                SetErrorConsoleVisible(false);

            private void ClearErrorConsole_Click(object? sender, RoutedEventArgs e)
            {
                _log.Clear();
                _consoleRows.Clear();
                _unseenErrors = 0;
            }
            
            /// <summary>
            /// Frontend UI text to indicate hardware connection status
            /// </summary>
            /// <param name="error"></param>
            public void UpdateHardwareConnectionText(string updatedText)
            {
                if (_hardwareConnectionText == null) return; //lost cause at this point
                
                _hardwareConnectionText.Text = updatedText;
            }
            /// <summary>
            /// Create color pallet and dragndrop functionality via avalonia swatches
            /// </summary>
            private void InitializeColorPalette()
            {
                var palette = this.FindControl<WrapPanel>("ColorPalette");
                palette.Children.Clear();

                var picker = this.FindControl<ColorPicker>("SwatchFlyoutPicker");
                picker.PropertyChanged -= SwatchFlyoutPickerOnPropertyChanged;
                picker.PropertyChanged += SwatchFlyoutPickerOnPropertyChanged;

                for (int i = 0; i < BlockColors.Count; i++)
                {
                    int idx = i;
                    var color = BlockColors[idx];

                    var baseSwatch = new Border
                    {
                        Width = 30,
                        Height = 30,
                        Background = new SolidColorBrush(color),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(2),
                        Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand),
                        Tag = idx
                    };

                    void OpenPicker(Control anchor)
                    {
                        _activeSwatch = baseSwatch;

                        var flyout = (Flyout)Resources["SwatchPickerFlyout"]!;
                        if (baseSwatch.Background is SolidColorBrush b)
                            picker.Color = b.Color;

                        flyout.ShowAt(anchor);
                    }

                    Control finalSwatch = baseSwatch;

                    if (idx < 10)
                    {
                        var label = new TextBlock
                        {
                            Text = (idx + 1).ToString().Substring((idx+1).ToString().Length-1,1),
                            Foreground = Brushes.Black,
                            FontSize = 14,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };

                        var grid = new Grid();
                        grid.Children.Add(baseSwatch);
                        grid.Children.Add(label);

                        finalSwatch = new Border
                        {
                            Child = grid,
                            CornerRadius = new CornerRadius(4),
                            Margin = new Thickness(2),
                            Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand),
                            Tag = idx
                        };
                    }

                    finalSwatch.PointerPressed += async (_, e) =>
                    {
                        if (e.GetCurrentPoint(finalSwatch).Properties.IsRightButtonPressed)
                        {
                            e.Handled = true;
                            OpenPicker(finalSwatch);
                            return;
                        }

                        if (e.GetCurrentPoint(finalSwatch).Properties.IsLeftButtonPressed)
                        {
                            var data = new DataObject();
                            data.Set("block-color", BlockColors[idx].ToString());
                            DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
                        }
                    };

                        palette.Children.Add(finalSwatch);
                    }

                    DragDrop.SetAllowDrop(_blockColorDropBox, true);
                    DragDrop.SetAllowDrop(_secondColorDropBox, true);
                    DragDrop.SetAllowDrop(_fillColorDropBox, true);
                    DragDrop.SetAllowDrop(_strobeColorDropBox, true);
                    _blockColorDropBox.AddHandler(DragDrop.DropEvent, OnColorCanvasDrop, RoutingStrategies.Bubble);
                    _secondColorDropBox.AddHandler(DragDrop.DropEvent, OnColorCanvasDrop, RoutingStrategies.Bubble);
                    _fillColorDropBox.AddHandler(DragDrop.DropEvent, OnColorCanvasDrop, RoutingStrategies.Bubble);
                    _strobeColorDropBox.AddHandler(DragDrop.DropEvent, OnColorCanvasDrop, RoutingStrategies.Bubble);
                }

            
            private void SwatchFlyoutPickerOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property != ColorPicker.ColorProperty) return;
                if (_activeSwatch == null) return;
                if (_activeSwatch.Tag is not int idx) return;
                if (idx < 0 || idx >= BlockColors.Count) return;

                var picker = (ColorPicker)sender!;
                var newColor = picker.Color;

                _activeSwatch.Background = new SolidColorBrush(newColor);
                BlockColors[idx] = newColor;
            }

            private void OnColorCanvasDrop(object? sender, DragEventArgs e)
            {
                if (!e.Data.Contains("block-color")) return;

                var colorString = e.Data.Get("block-color")?.ToString();
                if (colorString == null || !Color.TryParse(colorString, out var color)) return;

                if ((Timeline.SelectedBlocks?.Count ?? 0) > 0)
                    Timeline.PushUndo();

                if (ReferenceEquals(sender, _blockColorDropBox))
                {
                    foreach (var block in Timeline.SelectedBlocks ?? new List<LightBlock>())
                        block.UpdateColor(color);
                    _viewModel.BlockColorBrush = new SolidColorBrush(color);
                    return;
                }

                if (ReferenceEquals(sender, _secondColorDropBox))
                {
                    foreach (var block in Timeline.SelectedBlocks ?? new List<LightBlock>())
                        block.SecondBlockColor = color;
                    _viewModel.SecondBlockColorBrush = new SolidColorBrush(color);
                    return;
                }

                if (ReferenceEquals(sender, _fillColorDropBox))
                {
                    foreach (var block in Timeline.SelectedBlocks ?? new List<LightBlock>())
                        block.FillColor = color;
                    _viewModel.FillColorBrush = new SolidColorBrush(color);
                    return;
                }

                if (ReferenceEquals(sender, _strobeColorDropBox))
                {
                    foreach (var block in Timeline.SelectedBlocks ?? new List<LightBlock>())
                        block.StrobeColor = color;
                    _viewModel.StrobeColorBrush = new SolidColorBrush(color);
                    return;
                }
            }


            /// <summary>
            /// Calculates the RGB positions and applies them to the simulated color bars
            /// </summary>
            /// <param name="stripColors"></param>
            private void UpdateColorBar(Color[] stripColors)
            {
                var colors = stripColors ?? Array.Empty<Color>();
                int n = colors.Length;

                double fullWidth = Timeline.ViewportWidth;

                TopColorBar.HorizontalAlignment = HorizontalAlignment.Left;
                TopColorBar.Margin = new Thickness(0);
                TopColorBar.Width = Math.Max(1, fullWidth);
                TopColorBar.Opacity = 1.0;

                if (n == 0)
                {
                    TopColorBar.Background = Brushes.Transparent;
                    BottomColorBar.Background = Brushes.Transparent;

                    return;
                }

                if (n == 1)
                {
                    TopColorBar.Background = new SolidColorBrush(colors[0]);
                    BottomColorBar.Background = TopColorBar.Background;

                    return;
                }

                var stops = new GradientStops();
                for (int i = 0; i < n; i++)
                {
                    double a = (double)i / n;
                    double b = (double)(i + 1) / n;

                    stops.Add(new GradientStop(colors[i], a));
                    stops.Add(new GradientStop(colors[i], b));
                }

                TopColorBar.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops = stops
                };
                BottomColorBar.Background = TopColorBar.Background;
            }
            
          

            public void RefreshPorts(object sender, RoutedEventArgs e)
            {
                RefreshPorts();
            }
            public void RefreshPorts()
            {
                var ports =  SerialPort.GetPortNames().OrderBy(p => p).ToArray();

                var previous = PortComboBox.SelectedItem as string;

                PortComboBox.Items.Clear();
                foreach (var p in ports)
                    PortComboBox.Items.Add(p);

                if (previous != null && ports.Contains(previous))
                    PortComboBox.SelectedItem = previous;
                else if (ports.Length > 0)
                    PortComboBox.SelectedIndex = 0;
            
            }
            
            private void SaveHardwareSettings(object? sender, RoutedEventArgs e)
            {
                _serialPanel.Connect(
                    port: PortComboBox.SelectedItem as string,
                    ledCount: Int32.Parse(ActiveLightsTextBox.Text),
                    hardwareCurrent: (int)HardwareCurrentSlider.Value);
            }
            
            /// <summary>
            /// Changes app theme depending on current music provider source (ex. Spotify green vs Lumanite purple)
            /// </summary>
            private void ChangeAppTheme()
            {
                ResumeTrackButton.Background = new SolidColorBrush(_musicProvider.ProviderColor, 1);
                PauseTrackButton.Background = new SolidColorBrush(_musicProvider.ProviderColor, 1);
                RestartTrackButton.Background = new SolidColorBrush(_musicProvider.ProviderColor, 1);
                NextTrackButton.Background = new SolidColorBrush(_musicProvider.ProviderColor, 1);

                UpdateProviderAttribution();
            }

            /// <summary>
            /// Shows the Spotify logo + "PLAY ON SPOTIFY" link only while Spotify is the active
            /// source. Spotify's Developer Terms require displayed metadata and cover art to be
            /// attributed to Spotify and accompanied by a link back to the track — and equally
            /// require that the mark is not shown against content from anywhere else, so this
            /// hides the whole panel for local files rather than leaving it permanently on.
            /// </summary>
            private void UpdateProviderAttribution()
            {
                bool spotifyActive = _musicRouter.ActiveProviderName == ProviderType.Spotify;

                // Switching away drops the link, so coming back can't briefly offer the
                // previous track's URL against the newly loaded one.
                if (!spotifyActive) _currentProviderTrackUrl = null;

                SpotifyAttributionPanel.IsVisible = spotifyActive;
                // No resolved track link yet (readback still catching up) → no dead button.
                OpenInSpotifyButton.IsVisible = spotifyActive
                                                && !string.IsNullOrWhiteSpace(_currentProviderTrackUrl);

                // The key button is meaningful only for sources the user supplies a key for.
                var active = _musicRouter.ActiveProviderName;
                MusicSourceKeyButton.IsVisible = active.RequiresUserCredentials();
                MusicSourceKeyButton.Content   = $"{active.DisplayName()} Key";
            }

            /// <summary>
            /// Lets the user replace the active source's developer key mid-session — the common
            /// case being a key that was mistyped or revoked. The new key is picked up on the
            /// next launch, since the provider was constructed with the old one.
            /// </summary>
            private async void MusicSourceKey_Click(object? sender, RoutedEventArgs e)
            {
                var active = _musicRouter.ActiveProviderName;
                if (!active.RequiresUserCredentials()) return;

                var before = _credentialStore.Get(active)?.ClientId;

                var window = new ProviderCredentialsWindow(active, _credentialStore);
                await window.ShowDialog(this);
                await window.Completed;

                var after = _credentialStore.Get(active)?.ClientId;
                if (before != after)
                    _log.Info($"{active.DisplayName()} key updated — restart LumiNote for it to take effect.");

                UpdateProviderAttribution();
            }

            /// <summary>Opens the current track on Spotify — the required link back to the service.</summary>
            private void OpenInSpotify_Click(object? sender, RoutedEventArgs e)
            {
                if (string.IsNullOrWhiteSpace(_currentProviderTrackUrl)) return;

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _currentProviderTrackUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    _log.Error($"Couldn't open Spotify link: {ex.Message}");
                }
            }
            
            private void HardwareSettingsOnClick(object? sender, RoutedEventArgs e)
            {
                RefreshPorts();
            }
            
            /// <summary>
            /// New Lightmap Button (Either prompts user for file or uses currentlyPLayign Track) 
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void OpenAudioFileButton_OnClick(object? sender, RoutedEventArgs e)
            {
                if (sender is not Button b) return;

                bool hasLocal   = _musicRouter.HasProvider(ProviderType.LocalFiles);
                bool hasSpotify = _musicRouter.HasProvider(ProviderType.Spotify);

                // Multiple sources enabled → let the user pick which one the new lightmap is for.
                if (hasLocal && hasSpotify)
                {
                    if (this.Resources["NewLightmapSourceFlyout"] is Flyout picker)
                        picker.ShowAt(b);
                }
                // Single source → go straight to its flow (unchanged behavior).
                else if (hasLocal)
                {
                    if (this.Resources["OpenAudioFlyout"] is Flyout f)
                        f.ShowAt(b);
                }
                else if (hasSpotify)
                {
                    StartSpotifyLightmap();
                }
            }

            /// <summary>Picker: start a new lightmap from a local audio file.</summary>
            private async void PickLocalSource_Click(object? sender, RoutedEventArgs e)
            {
                (this.Resources["NewLightmapSourceFlyout"] as Flyout)?.Hide();
                await PromptAndImportLocalAsync();
            }

            /// <summary>Picker: start a new lightmap from the currently-playing Spotify track.</summary>
            private void PickSpotifySource_Click(object? sender, RoutedEventArgs e)
            {
                (this.Resources["NewLightmapSourceFlyout"] as Flyout)?.Hide();
                StartSpotifyLightmap();
            }

            /// <summary>
            /// Starts a new lightmap from whatever is currently playing on Spotify. Switches the
            /// active source to Spotify first, since in mixed mode Local may be active.
            /// </summary>
            private async void StartSpotifyLightmap()
            {
                if (_musicProvider.providerName != ProviderType.Spotify)
                    await _musicRouter.SwitchToAsync(ProviderType.Spotify);

                var path = _musicProvider.GetCurrentlyPlayingTrackIdAsync();
                _currentTrackDurationMs = 0;
                _musicProvider.currentTrack = new TrackPOCO(Guid.Empty, "Unnamed Track", "Unnamed Artists", null);
                _musicProvider.currentlyPlayingPath = path;
                await _playbackHandler.PlayAsync();
                UpdateCurrentTrack(true, Guid.Empty);
                _currentTrackDurationMs = await _musicProvider.GetTrackDurationMsAsync();
            }
            
            /// <summary>
            /// Select new lightmap track file from local files
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private async void BrowseAudioFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            {
                await PromptAndImportLocalAsync();
            }

            /// <summary>
            /// Prompts for a local audio file, imports it into app storage, and starts a new
            /// lightmap for it. Shared by the browse flyout and the source picker.
            /// </summary>
            private async Task PromptAndImportLocalAsync()
            {
                if (!_musicRouter.HasProvider(ProviderType.LocalFiles)) return;

                var dlg = new OpenFileDialog
                {
                    Title = "Select an audio file",
                    AllowMultiple = false,
                    Filters =
                    {
                        new FileDialogFilter
                        {
                            Name = "Audio",
                            Extensions = { "mp3", "wav", "flac", "m4a", "aac", "ogg" }
                        }
                    }
                };

                var result = await dlg.ShowAsync(this);
                var path = result?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(path)) return;

                var importedPath = _jsonDataHandler.ImportAudioToAppStorage(path);

                SelectedAudioPathText.Text = importedPath;
                OnAudioFileSelected(importedPath);
            }
            
            /// <summary>
            /// Update playback with locally selected audio file
            /// </summary>
            /// <param name="path"></param>
            private async void OnAudioFileSelected(string path)
            {
                // A local file was chosen — make sure the LocalFiles source is active.
                if (_musicProvider.providerName != ProviderType.LocalFiles)
                    await _musicRouter.SwitchToAsync(ProviderType.LocalFiles);

                _currentTrackDurationMs = 0;
                _musicProvider.currentTrack = new TrackPOCO(Guid.Empty, "Unnamed Track", "Unnamed Artists", null);
                _musicProvider.currentlyPlayingPath = path;
                await _playbackHandler.PlayAsync();
                UpdateCurrentTrack(true, trackGUID: Guid.Empty); // refresh on next track
                _currentTrackDurationMs = await _musicProvider.GetTrackDurationMsAsync();
            }
            
            /// <summary>
            /// Loads local track items and stamps which link buttons each row should offer,
            /// based on the providers enabled for this session.
            /// </summary>
            private List<TrackItemUI> BuildLocalTrackItems()
            {
                // Grouping (Mine / Remixed / Downloaded) depends on who's signed in.
                var items = _jsonDataHandler.GetAllTrackItems(_googleAuth.UserId, _googleAuth.UserName ?? _googleAuth.Email);
                foreach (var item in items)
                {
                    // Downloaded/synced tracks that fell behind the cloud get a badge.
                    if (item.CloudLightmapId != null
                        && _cloudVersions.TryGetValue(item.CloudLightmapId, out var cloudV)
                        && cloudV > item.CloudVersion)
                    {
                        item.UpdateAvailable = true;
                        item.Status = (item.Status.Length > 0 ? item.Status + " • " : "") + "⬆ Update available";
                    }

                    item.LinkActions = _musicRouter.AvailableProviders
                        .Select(p => new TrackLinkAction
                        {
                            TrackId  = item.TrackId,
                            Provider = p,
                            Label    = p.LinkLabel()
                        })
                        .ToList();
                }
                return items;
            }

            /// <summary>
            /// Refresh the list of local track files
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void RefreshLocalTracks_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            {
                _allLocalTrackItems = BuildLocalTrackItems();
                LocalTracksListBox.ItemsSource = _allLocalTrackItems;

            }

            /// <summary>
            /// Local track search box changed
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void LocalTrackSearchBox_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
            {
                var search = LocalTrackSearchBox.Text?.ToLower() ?? "";

                // One box matches lightmap name, track name, and author together.
                var filtered = _allLocalTrackItems
                    .Where(t =>
                        (t.TrackName ?? "").ToLower().Contains(search) ||
                        (t.SongName ?? "").ToLower().Contains(search) ||
                        (t.Artist ?? "").ToLower().Contains(search))
                    .ToList();

                LocalTracksListBox.ItemsSource = filtered;
            }
            
            /// <summary>
            /// Upload a local lightmapped track to the database
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private async void LocalTrack_Upload_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            {
                if (sender is not Avalonia.Controls.Control c) return;
                if (c.DataContext is not TrackItemUI item) return;

                var track = _jsonDataHandler.GetTrack(item.TrackId.ToString());
                if (track == null)
                {
                    _log.Error($"Can't upload \"{item.TrackName}\" — no local file found.");
                    return;
                }

                try
                {
                    // Uploading needs an account (ownership + the 4-per-song limit live server-side).
                    if (!_googleAuth.IsSignedIn)
                    {
                        _log.Info("Opening Google sign-in in your browser…");
                        if (!await _googleAuth.SignInAsync())
                        {
                            _log.Warn("Sign-in cancelled — uploading needs a Google account.");
                            return;
                        }
                        UpdateGoogleSignInButton();
                    }

                    // Updates your own cloud copy (version bump) or creates a new
                    // lightmap/remix; comes back with the cloud identity to persist.
                    var updated = await _databaseAccess.UploadLightmapAsync(track);
                    _jsonDataHandler.SaveTrack(updated);

                    _allLocalTrackItems = BuildLocalTrackItems();
                    LocalTracksListBox.ItemsSource = _allLocalTrackItems;
                    _log.Info($"Uploaded \"{track._trackName}\" (v{updated.CloudVersion}).");
                }
                catch (Exception ex)
                {
                    _log.Error($"Upload failed: {ex.Message}");
                }
            }

           
            /// <summary>
            /// Play selected track from local selection
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private async void LocalTrack_Play_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            {
                if (sender is not Avalonia.Controls.Control c) return;
                if (c.DataContext is not TrackItemUI item) return;

                await PlayTrackByIdAsync(item.TrackId.ToString());
            }

            /// <summary>
            /// Resolves a library track's best source, switches provider if needed, and plays it.
            /// Shared by the Play button and the queue (auto-advance + Next).
            /// </summary>
            private async Task<bool> PlayTrackByIdAsync(string trackId)
            {
                // If the current lightmap has unsaved edits, postpone the switch and ask what to do.
                if (_lightmapDirty)
                {
                    // Nullable: closing via the title-bar X yields null, treated as Cancel.
                    var choice = await new UnsavedChangesPrompt().ShowDialog<UnsavedChoice?>(this);
                    if (choice is null or UnsavedChoice.Cancel)
                        return false; // abort the switch — stay on the edited lightmap
                    if (choice == UnsavedChoice.Save && !await ShowSaveLightmapDialogAsync())
                        return false; // backed out of the save target picker — abort the switch
                    // Save (completed) or Don't Save → discard the dirty state and switch.
                    _lightmapDirty = false;
                }

                var track = _jsonDataHandler.GetTrack(trackId);
                if (track == null)
                {
                    _log.Error("Track not found in the local library.");
                    return false;
                }

                // Pick a source the track has that is also an enabled provider.
                // Always prefer local files when available (lowest latency / most reliable),
                // then the currently active provider, then any other enabled source.
                ProviderType? targetProvider = null;
                if (_musicRouter.HasProvider(ProviderType.LocalFiles) && track.HasSource(ProviderType.LocalFiles))
                    targetProvider = ProviderType.LocalFiles;
                else if (track.HasSource(_musicProvider.providerName))
                    targetProvider = _musicProvider.providerName;
                else
                {
                    foreach (var type in _musicRouter.AvailableProviders)
                    {
                        if (track.HasSource(type))
                        {
                            targetProvider = type;
                            break;
                        }
                    }
                }

                if (targetProvider == null)
                {
                    _log.Warn($"No enabled source linked for \"{track._trackName}\". Use the link buttons to add one.");
                    return false;
                }

                // Switch source if needed (router pauses the current source before swapping).
                if (targetProvider != _musicProvider.providerName)
                    await _musicRouter.SwitchToAsync(targetProvider.Value);

                _currentTrackDurationMs = 0; // no auto-advance until the new duration is known

                _musicProvider.currentlyPlayingPath = track.GetSource(targetProvider.Value);
                //Set visual information for track that is stored in json
                _musicProvider.currentTrack = new TrackPOCO(track.trackGUID, track._trackName, track.artist, null);
                await _playbackHandler.PlayAsync();
                UpdateCurrentTrack(true, track.trackGUID); // refresh on next track

                _currentTrackDurationMs = await _musicProvider.GetTrackDurationMsAsync();
                return true;
            }

            /// <summary>
            /// Plays the next queued track (dequeuing it); pauses playback when the queue is empty.
            /// </summary>
            private async Task AdvanceQueueAsync()
            {
                try
                {
                    if (_queue.Count > 0)
                    {
                        // Peek, not pop: only consume the queue entry once the switch actually
                        // happens. If a pending-save prompt is cancelled the track stays queued.
                        var next = _queue[0];
                        if (await PlayTrackByIdAsync(next.TrackId))
                            _queue.Remove(next);
                    }
                    else
                    {
                        await _playbackHandler.PauseAsync();
                    }
                }
                finally
                {
                    _queueAdvancing = false;
                }
            }

            // ── Queue add/remove ──────────────────────────────────────────────

            private void LocalTrack_QueueTop_Click(object? sender, RoutedEventArgs e)
            {
                if (sender is not Control c || c.DataContext is not TrackItemUI item) return;
                _queue.Insert(0, QueueEntry.From(item));
            }

            private void LocalTrack_QueueBottom_Click(object? sender, RoutedEventArgs e)
            {
                if (sender is not Control c || c.DataContext is not TrackItemUI item) return;
                _queue.Add(QueueEntry.From(item));
            }

            private void QueueRemove_Click(object? sender, RoutedEventArgs e)
            {
                if (sender is not Control c || c.DataContext is not QueueEntry entry) return;
                _queue.Remove(entry);
            }

            private void QueueClear_Click(object? sender, RoutedEventArgs e) => _queue.Clear();

            private void UpdateQueueCountText()
            {
                QueueCountText.Text = _queue.Count switch
                {
                    0 => "Queue is empty",
                    1 => "1 track",
                    var n => $"{n} tracks"
                };
            }

            // ── Queue drag-reorder ────────────────────────────────────────────
            // Pointer-based: the pressed row is captured and glued to the cursor via a
            // translate transform; when it crosses a neighbour's midpoint the collection
            // Move() swaps them and the anchor shifts one row, so the row stays under the
            // pointer while the others slide into place.

            private double QueueRowPitch => (_queueDragRow?.Bounds.Height ?? 60) + 6; // row + margin

            private void QueueRow_PointerPressed(object? sender, PointerPressedEventArgs e)
            {
                if (sender is not Border row || row.DataContext is not QueueEntry entry) return;
                // Presses on the row's buttons (✕) are theirs, not a drag.
                if (e.Source is Visual v && v.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;

                _queueDragRow = row;
                _queueDragEntry = entry;
                _queueDragStartY = e.GetPosition(QueueList).Y;
                _queueDragActive = false;
                e.Pointer.Capture(row);
            }

            private void QueueRow_PointerMoved(object? sender, PointerEventArgs e)
            {
                if (_queueDragRow == null || _queueDragEntry == null || !ReferenceEquals(sender, _queueDragRow))
                    return;

                double dy = e.GetPosition(QueueList).Y - _queueDragStartY;

                if (!_queueDragActive)
                {
                    if (Math.Abs(dy) < 5) return; // dead zone so clicks don't wiggle rows
                    _queueDragActive = true;
                    _queueDragRow.Opacity = 0.75;
                    if (_queueDragRow.Parent is Control presenter) presenter.ZIndex = 100;
                }

                int index = _queue.IndexOf(_queueDragEntry);
                if (index < 0) return;

                // Crossed a neighbour's midpoint → swap and re-anchor one row over.
                int target = Math.Clamp(index + (int)Math.Round(dy / QueueRowPitch), 0, _queue.Count - 1);
                if (target != index)
                {
                    _queue.Move(index, target);
                    _queueDragStartY += (target - index) * QueueRowPitch;
                    dy = e.GetPosition(QueueList).Y - _queueDragStartY;
                }

                _queueDragRow.RenderTransform = new TranslateTransform(0, dy);
            }

            private void QueueRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
            {
                if (_queueDragRow == null) return;

                _queueDragRow.RenderTransform = null;
                _queueDragRow.Opacity = 1.0;
                if (_queueDragRow.Parent is Control presenter) presenter.ZIndex = 0;

                _queueDragRow = null;
                _queueDragEntry = null;
                _queueDragActive = false;
            }
            
            /// <summary>
            /// Delete selected local track lightmap file
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void LocalTrack_Delete_Click(object? sender, RoutedEventArgs e)
            {
                if (sender is not Control c) return;
                if (c.DataContext is not TrackItemUI item) return;

                Console.WriteLine("Deleting file for: " + item.TrackName);

                _jsonDataHandler.DeleteTrack(item.TrackId.ToString());
                
                _allLocalTrackItems = BuildLocalTrackItems();
                LocalTracksListBox.ItemsSource = _allLocalTrackItems;
                
            }
            
            /// <summary>
            /// Routes a link button to the matching provider's link flow.
            /// </summary>
            private async void LocalTrack_Link_Click(object? sender, RoutedEventArgs e)
            {
                if (sender is not Control c) return;
                if (c.DataContext is not TrackLinkAction action) return;

                switch (action.Provider)
                {
                    case ProviderType.Spotify:
                        LinkSpotifySource(action.TrackId);
                        break;
                    case ProviderType.LocalFiles:
                        await LinkLocalFileSource(action.TrackId);
                        break;
                }
            }

            // Links the currently playing Spotify track as this lightmap's Spotify source.
            private void LinkSpotifySource(string trackId)
            {
                if (_musicProvider.providerName != ProviderType.Spotify)
                {
                    _log.Warn("Switch to the Spotify provider to link a Spotify track.");
                    return;
                }

                var spotifyId = _musicProvider.GetCurrentlyPlayingTrackIdAsync();
                if (string.IsNullOrEmpty(spotifyId))
                {
                    _log.Warn("No track currently playing on Spotify.");
                    return;
                }

                var track = _jsonDataHandler.GetTrack(trackId);
                if (track == null) return;

                track.SetSource(ProviderType.Spotify, spotifyId);
                _jsonDataHandler.SaveTrack(track);

                _allLocalTrackItems = BuildLocalTrackItems();
                LocalTracksListBox.ItemsSource = _allLocalTrackItems;
            }

            // Opens a file browser and links the chosen audio file as this lightmap's LocalFiles source.
            private async Task LinkLocalFileSource(string trackId)
            {
                var dialog = new OpenFileDialog
                {
                    AllowMultiple = false,
                    Filters = new List<FileDialogFilter>
                    {
                        new FileDialogFilter
                        {
                            Name       = "Audio Files",
                            Extensions = new List<string> { "mp3", "wav", "flac" }
                        }
                    }
                };

                var result = await dialog.ShowAsync(this);
                if (result == null || result.Length == 0) return;

                var importedPath = _jsonDataHandler.ImportAudioToAppStorage(result[0]);

                var track = _jsonDataHandler.GetTrack(trackId);
                if (track == null) return;

                track.SetSource(ProviderType.LocalFiles, importedPath);
                _jsonDataHandler.SaveTrack(track);

                _allLocalTrackItems = BuildLocalTrackItems();
                LocalTracksListBox.ItemsSource = _allLocalTrackItems;
            }

            // ── Per-provider sync offset (tap calibration) ─────────────────────

            // Opens the tap screen for whichever provider is currently active and stores the
            // result as that provider's sync offset. Note: the tap measures tap-timing against a
            // metronome, not the true audio→light delay, and it always plays a local beep — so
            // the value is a hand-tunable starting point, not a measured per-source latency.
            private async void CalibrateSync_Click(object? sender, RoutedEventArgs e)
            {
                var provider = _musicProvider.providerName;
                var tapper = new OffsetTapper();
                await tapper.ShowDialog(this);

                _providerOffsets[provider] = tapper.ComputedOffsetMs;
                SaveProviderOffsets();
                _log.Info($"Sync offset for {provider} set to {tapper.ComputedOffsetMs} ms.");
            }

            private void LoadProviderOffsets()
            {
                try
                {
                    if (!File.Exists(OffsetsPath)) return;
                    var byName = System.Text.Json.JsonSerializer
                        .Deserialize<Dictionary<string, int>>(File.ReadAllText(OffsetsPath));
                    if (byName == null) return;
                    foreach (var (name, ms) in byName)
                        if (Enum.TryParse<ProviderType>(name, out var type))
                            _providerOffsets[type] = ms;
                }
                catch
                {
                    // Corrupt/unreadable offsets file — fall back to defaults.
                }
            }

            private void SaveProviderOffsets()
            {
                try
                {
                    var byName = _providerOffsets.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
                    File.WriteAllText(OffsetsPath, System.Text.Json.JsonSerializer.Serialize(
                        byName, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex)
                {
                    _log.Warn("Couldn't save sync offsets: " + ex.Message);
                }
            }

            /// <summary>
            /// (Re)loads the shared-library preview: signed out shows nothing (there's no
            /// browsing UI without an account driving it), signed in fetches the full list and
            /// keeps the first 10 as the preview. Called on startup (if a prior sign-in
            /// persisted) and whenever sign-in state changes — there's no manual refresh button;
            /// this is the only path that repopulates the list.
            /// </summary>
            private async Task RefreshCloudTracksAsync()
            {
                if (!_googleAuth.IsSignedIn)
                {
                    _allDatabaseTrackItems = new List<TrackItemUI>();
                    _cloudVersions = new Dictionary<string, int>();
                    ApplyCloudView();
                    return;
                }

                try
                {
                    var fetched = await _databaseAccess.ListTracksAsync(false);
                    _allDatabaseTrackItems = fetched.Take(10).ToList();
                    ApplyCloudView();

                    // Remember each cloud lightmap's version (off the full fetch, not just the
                    // 10-item preview) so downloaded tracks that fell behind show their
                    // "update available" badge even if they scrolled out of the preview.
                    _cloudVersions = fetched
                        .Where(i => !string.IsNullOrEmpty(i.TrackId))
                        .GroupBy(i => i.TrackId)
                        .ToDictionary(g => g.Key, g => g.Max(i => i.Version));

                    _allLocalTrackItems = BuildLocalTrackItems();
                    LocalTracksListBox.ItemsSource = _allLocalTrackItems;
                }
                catch (Exception ex)
                {
                    _log.Error($"Loading shared tracks failed: {ex.Message}");
                }
            }

            /// <summary>
            /// Rebuilds the cloud list from the last fetch: filters by name/artist/owner or an
            /// exact/partial lightmap ID, usable-with-current-provider rows sort ahead of gray ones.
            /// </summary>
            private void ApplyCloudView()
            {
                if (DatabaseTracksListBox == null) return; // fired during XAML load

                var search = this.FindControl<TextBox>("CloudSearchBox")?.Text?.Trim().ToLowerInvariant() ?? "";
                IEnumerable<TrackItemUI> view = _allDatabaseTrackItems;

                if (search.Length > 0)
                    view = view.Where(t =>
                        (t.TrackName ?? "").ToLowerInvariant().Contains(search) ||
                        (t.SongName ?? "").ToLowerInvariant().Contains(search) ||
                        (t.Subtitle ?? "").ToLowerInvariant().Contains(search) ||
                        (t.TrackId ?? "").ToLowerInvariant().Contains(search));

                DatabaseTracksListBox.ItemsSource = view
                    .OrderByDescending(t => t.Usable)
                    .ThenBy(t => t.SongName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            /// <summary>
            /// Filters the preview list as you type. A full lightmap ID that isn't already in
            /// the preview (e.g. one a friend copied with a row's ⧉ ID button) is downloaded
            /// straight into your local library, the same convenience the old dedicated
            /// "Get by ID" box gave — just driven from this one box now.
            /// </summary>
            private async void CloudSearchBox_TextChanged(object? sender, TextChangedEventArgs e)
            {
                ApplyCloudView();

                var raw = (sender as TextBox)?.Text?.Trim();
                if (!Guid.TryParse(raw, out var id))
                {
                    _lastCloudIdFetch = null;
                    return;
                }
                if (_lastCloudIdFetch == id) return; // already fetched this exact ID
                if (_allDatabaseTrackItems.Any(t => string.Equals(t.TrackId, id.ToString(), StringComparison.OrdinalIgnoreCase)))
                    return; // already visible in the preview — let the row's own Download button handle it

                _lastCloudIdFetch = id;
                await DownloadLightmapAsync(id.ToString(), knownCloudVersion: null, displayName: id.ToString());
            }

            /// <summary>Copy a cloud lightmap's ID so it can be sent to a friend (paste it into the search box to fetch it).</summary>
            private async void DatabaseTrack_CopyId_Click(object? sender, RoutedEventArgs e)
            {
                if (sender is not Control c || c.DataContext is not TrackItemUI item) return;

                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard == null) return;

                await clipboard.SetTextAsync(item.TrackId);
                _log.Info($"Copied the lightmap ID for \"{item.TrackName}\" — send it to a friend to share.");
            }

            /// <summary>
            /// Delete your own lightmap from the shared cloud library (button only shows on
            /// rows you own; the Worker enforces ownership regardless). The local copy, if
            /// any, is untouched — it just becomes local-only.
            /// </summary>
            private async void DatabaseTrack_Delete_Click(object? sender, RoutedEventArgs e)
            {
                if (sender is not Control c || c.DataContext is not TrackItemUI item) return;

                try
                {
                    await _databaseAccess.DeleteLightmapAsync(item.TrackId);

                    _allDatabaseTrackItems.Remove(item);
                    ApplyCloudView();
                    _log.Info($"Deleted \"{item.TrackName}\" from the shared library.");
                }
                catch (Exception ex)
                {
                    _log.Error($"Delete failed: {ex.Message}");
                }
            }

            /// <summary>
            /// Google sign-in/out toggle for the shared library (uploading, remixing, likes).
            /// Either direction repopulates the cloud preview: signing in loads the 10-track
            /// preview, signing out clears it (see <see cref="RefreshCloudTracksAsync"/>).
            /// </summary>
            private async void GoogleSignIn_Click(object? sender, RoutedEventArgs e)
            {
                try
                {
                    if (_googleAuth.IsSignedIn)
                    {
                        _googleAuth.SignOut();
                    }
                    else
                    {
                        _log.Info("Opening Google sign-in in your browser…");
                        if (await _googleAuth.SignInAsync())
                            _log.Info($"Signed in as {_googleAuth.UserName ?? _googleAuth.Email}.");
                        else
                            _log.Warn("Sign-in cancelled.");
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"Sign-in failed: {ex.Message}");
                }

                UpdateGoogleSignInButton();
                await RefreshCloudTracksAsync();

                // Grouping depends on who's signed in — rebuild the local shelves.
                // (RefreshCloudTracksAsync already does this when it fetches, but not on the
                // sign-out path, so do it unconditionally here too.)
                _allLocalTrackItems = BuildLocalTrackItems();
                LocalTracksListBox.ItemsSource = _allLocalTrackItems;
            }

            private void UpdateGoogleSignInButton()
            {
                var btn = this.FindControl<Button>("GoogleSignInButton");
                if (btn != null)
                    btn.Content = _googleAuth.IsSignedIn
                        ? $"Sign out ({_googleAuth.UserName ?? _googleAuth.Email})"
                        : "Sign in with Google";
            }

            /// <summary>
            /// Download Track and Lightmap Data to Local JSON Files
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private async void DatabaseTrack_Download_Click(object? sender, RoutedEventArgs e)
            {
                if (sender is not Control c) return;
                if (c.DataContext is not TrackItemUI item) return;

                await DownloadLightmapAsync(item.TrackId.ToString(), item.Version, item.TrackName);
            }

            /// <summary>
            /// Shared download core for both the list rows and Get-by-ID. knownCloudVersion
            /// short-circuits before the fetch when the list already told us the version;
            /// the by-ID path passes null and version-checks after loading instead.
            /// </summary>
            private async Task DownloadLightmapAsync(string lightmapId, int? knownCloudVersion, string displayName)
            {
                var existing = _jsonDataHandler.GetTrack(lightmapId);

                // Already have this cloud version (or newer) → nothing to do. A stale copy
                // falls through and is overwritten by the fresh download ("update available").
                if (knownCloudVersion != null && existing != null && existing.CloudVersion >= knownCloudVersion)
                {
                    _log.Info($"\"{displayName}\" is already up to date (v{existing.CloudVersion}).");
                    return;
                }

                try
                {
                    TrackData tdToAdd = await _databaseAccess.LoadTrackAsync(lightmapId);
                    if (tdToAdd == null)
                    {
                        _log.Error($"No shared lightmap found for ID {lightmapId}.");
                        return;
                    }
                    displayName = tdToAdd.DisplayName ?? displayName;

                    if (knownCloudVersion == null && existing != null && existing.CloudVersion >= tdToAdd.CloudVersion)
                    {
                        _log.Info($"\"{displayName}\" is already up to date (v{existing.CloudVersion}).");
                        return;
                    }

                    // GET carries lightmap_id/owner/version via TrackData's cloud fields, but the
                    // local file identity (trackGUID) still needs to be pinned to the cloud id.
                    if (Guid.TryParse(lightmapId, out var dbGuid))
                        tdToAdd.trackGUID = dbGuid;
                    tdToAdd.CloudLightmapId = lightmapId;

                    // Audio: reuse the local copy from a previous download when we have one
                    // (lightmap updates don't change the song), else fetch it.
                    var existingAudio = existing?.GetSource(ProviderType.LocalFiles);
                    if (existingAudio != null && System.IO.File.Exists(existingAudio))
                    {
                        tdToAdd.filePath = existingAudio;
                        tdToAdd.SetSource(ProviderType.LocalFiles, existingAudio);
                    }
                    else
                    {
                        var audioBytes = await _databaseAccess.DownloadTrackAudioAsync(lightmapId);
                        if (audioBytes != null)
                        {
                            var localPath = _jsonDataHandler.SaveAudioBytesToAppStorage(audioBytes);
                            tdToAdd.filePath = localPath;
                            tdToAdd.SetSource(ProviderType.LocalFiles, localPath);
                        }
                    }

                    _jsonDataHandler.SaveTrack(tdToAdd);

                    _allLocalTrackItems = BuildLocalTrackItems();
                    LocalTracksListBox.ItemsSource = _allLocalTrackItems;
                    _log.Info($"Downloaded \"{displayName}\" (v{tdToAdd.CloudVersion}).");
                }
                catch (Exception ex)
                {
                    _log.Error($"Download failed: {ex.Message}");
                }
            }
            
            /// <summary>True while the main music timer is ticking.</summary>
            internal bool IsPlaybackActive => _playbackHandler?.IsTimerRunning == true;

            internal void PlayPreview()
            {
                if ((Timeline.SelectedBlocks?.Count ?? 0) == 0) return;

                _previewRunning = false;
                _previewWatch.Restart();
                _previewRunning = true;

                _ = Task.Run(async () =>
                {
                    while (_previewRunning)
                    {
                        double currentMs = _previewWatch.Elapsed.TotalMilliseconds;

                        double slotWidth = 0;
                        var blocks = new List<(LightBlock Block, double Left, double Width)>();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            slotWidth = Timeline.SlotWidth;
                            foreach (var b in Timeline.SelectedBlocks ?? new List<LightBlock>())
                                blocks.Add((b, Canvas.GetLeft(b.Container), b.Container.Width));
                        });

                        if (blocks.Count == 0)
                        {
                            await Task.Delay(ColorUpdateIntervalMs);
                            continue;
                        }

                        Color[] finalLeds = LightEffectsComputer.ComputePreviewFrame(currentMs, blocks, slotWidth, ColorUpdateIntervalMs);

                        double last = blocks.Max(b => b.Left + b.Width);
                        if (currentMs > (last - blocks[0].Left) * TimelineView.MsPerSlot / slotWidth)
                            _previewWatch.Restart();

                        Dispatcher.UIThread.Post(() => LedPreview.SetColors(finalLeds));

                        await Task.Delay(ColorUpdateIntervalMs);
                    }
                });
            }

            internal void StopPreview()
            {
                _previewRunning = false;
                _previewWatch.Stop();
            }

        }
    }
