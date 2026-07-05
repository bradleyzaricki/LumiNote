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
            /// Color update interval (determined by the max update interval with current light config)
            /// </summary>
            private const int ColorUpdateIntervalMs = 50;
            
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
            
            private TextBlock _errorText;
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
            private OffsetTapper _offsetTapper;
            public int AudioOffsetMs { get; set; }
            public LumikitWindow()  // designer uses this
            {
                InitializeComponent();
            }
            public LumikitWindow(IMusicProvider provider, IPlaybackHandler playbackHandler, JsonDataHandler jsonDataHandler, DatabaseAccess databaseAccess, BlockEditorViewModel blockEditorViewModel, ISerialPanel serialPanel, OffsetTapper offsetTapper, RoutingMusicSession musicRouter)
            {
                _musicProvider = provider;
                _playbackHandler = playbackHandler;
                _musicRouter = musicRouter;
                _jsonDataHandler = jsonDataHandler;
                _databaseAccess = databaseAccess;
                _viewModel = blockEditorViewModel;
                _offsetTapper = offsetTapper;
                InitializeComponent();
                DataContext = _viewModel;

                _errorText = this.FindControl<TextBlock>("ErrorText");
                _hardwareConnectionText = this.FindControl<TextBlock>("HardwareConnectionText");
                _blockColorDropBox = this.FindControl<Canvas>("ColorDropBox");
                _secondColorDropBox = this.FindControl<Canvas>("SecondColorDropBox");
                _fillColorDropBox = this.FindControl<Canvas>("FillColorDropBox");
                _strobeColorDropBox = this.FindControl<Canvas>("StrobeColorDropBox");
                _blockEditor = new BlockEditorPanel(_viewModel, Timeline);
                _serialPanel = serialPanel;
                _serialPanel.ErrorOccurred           += UpdateErrorText;
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

                        Color[]? colors = Timeline.Tick(ms + AudioOffsetMs, 10, _serialPanel.BrightnessScale, ColorUpdateIntervalMs);

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
                            UpdateErrorText("Queue is empty — add tracks with the ⤒/⤓ buttons.");
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
                string authors;

                if (candidates.Count > 0)
                {
                    var candidateItems = candidates.Select(t => new TrackItemUI
                    {
                        TrackId = t.trackGUID.ToString(),
                        TrackName = t._trackName,
                        Subtitle = t.author,
                        Color = new Avalonia.Media.SolidColorBrush(_musicProvider.ProviderColor)
                    }).ToList();

                    var picker = new TrackSaveTargetWindow(candidateItems, currentGUID);
                    var choice = await picker.ShowDialog<Guid?>(this);
                    if (choice == null) return false; // cancelled

                    if (choice.Value == Guid.Empty)
                    {
                        // Save as new → prompt for a fresh title/author.
                        var newPopUp = new NewTrackPopup();
                        if (await newPopUp.ShowDialog<bool?>(this) != true) return false;
                        title = newPopUp.TitleText;
                        authors = newPopUp.AuthorText;
                        targetGuid = Guid.NewGuid();
                    }
                    else
                    {
                        // Overwrite an existing lightmap → keep its title/author, no prompt.
                        targetGuid = choice.Value;
                        var existing = _jsonDataHandler.GetTrack(targetGuid.ToString());
                        title = existing?._trackName ?? "";
                        authors = existing?.author ?? "";
                    }
                }
                else
                {
                    // No existing lightmap for this song → inherently new, prompt for title/author.
                    var newPopUp = new NewTrackPopup();
                    if (await newPopUp.ShowDialog<bool?>(this) != true) return false;
                    title = newPopUp.TitleText;
                    authors = newPopUp.AuthorText;
                    targetGuid = currentGUID != Guid.Empty.ToString() ? Guid.Parse(currentGUID) : Guid.NewGuid();
                }

                currentGUID = targetGuid.ToString();

                Console.WriteLine("Saving " + title +" lightmap with filepath: " + _musicProvider.currentlyPlayingPath);
                Timeline.ReorderLightBlocks();
                // Preserve any previously linked sources (e.g. a Spotify link added to a local track)
                var existingSources = _jsonDataHandler.GetTrack(currentGUID)?.Sources
                                     ?? new Dictionary<string, string>();

                var trackData = new TrackData
                {
                    filePath = _musicProvider.currentlyPlayingPath,
                    _trackName = title,
                    author = authors,
                    trackGUID = targetGuid,
                    provider = _musicProvider.providerName.ToString(),
                    Sources = existingSources,
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
                currentGUID=trackGUID.ToString();

                var track = await _musicProvider.GetCurrentlyPlayingTrackAsync();
                Console.WriteLine(track.trackName);
                this.FindControl<TextBlock>("NowPlayingTrackText").Text = track.trackName;
                this.FindControl<TextBlock>("NowPlayingArtistText").Text = track.artistName;

                var albumImage = track.trackCoverImageUrl;
                await SetAlbumCover(albumImage);


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
            /// Frontend UI text to signal program did not behave as expected
            /// </summary>
            /// <param name="error"></param>
            public void UpdateErrorText(string error)
            {
                if (_errorText == null) return; //lost cause at this point
                
                if (!_errorText.IsVisible)
                {
                    _errorText.IsVisible = true;
                }
                _errorText.Text = error;
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
                var items = _jsonDataHandler.GetAllTrackItems();
                foreach (var item in items)
                {
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

                var filtered = _allLocalTrackItems
                    .Where(t =>
                        (t.TrackName ?? "").ToLower().Contains(search) ||
                        (t.Subtitle ?? "").ToLower().Contains(search))
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
                    UpdateErrorText($"Can't upload \"{item.TrackName}\" — no local file found.");
                    return;
                }

                try
                {
                    await _databaseAccess.SaveTrackAsync(item.TrackId.ToString(), track);
                    UpdateErrorText($"Uploaded \"{track._trackName}\".");
                }
                catch (Exception ex)
                {
                    UpdateErrorText($"Upload failed: {ex.Message}");
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
                    UpdateErrorText("Track not found in the local library.");
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
                    UpdateErrorText($"No enabled source linked for \"{track._trackName}\". Use the link buttons to add one.");
                    return false;
                }

                // Switch source if needed (router pauses the current source before swapping).
                if (targetProvider != _musicProvider.providerName)
                    await _musicRouter.SwitchToAsync(targetProvider.Value);

                _currentTrackDurationMs = 0; // no auto-advance until the new duration is known

                _musicProvider.currentlyPlayingPath = track.GetSource(targetProvider.Value);
                //Set visual information for track that is stored in json
                _musicProvider.currentTrack = new TrackPOCO(track.trackGUID, track._trackName, track.author, null);
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
                    UpdateErrorText("Switch to the Spotify provider to link a Spotify track.");
                    return;
                }

                var spotifyId = _musicProvider.GetCurrentlyPlayingTrackIdAsync();
                if (string.IsNullOrEmpty(spotifyId))
                {
                    UpdateErrorText("No track currently playing on Spotify.");
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

            /// <summary>
            /// Refresh Database Track List
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private async void DatabaseTrack_Refresh_Click(object? sender, RoutedEventArgs e)
            {
                _allDatabaseTrackItems = await _databaseAccess.ListTracksAsync(false);
                DatabaseTracksListBox.ItemsSource = _allDatabaseTrackItems;
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
                if (_jsonDataHandler.GetTrack(item.TrackId.ToString()) == null)
                {
                    TrackData tdToAdd = await _databaseAccess.LoadTrackAsync(item.TrackId.ToString());

                    // GET returns the id as _trackID, which doesn't map to trackGUID — keep the DB identity.
                    if (Guid.TryParse(item.TrackId, out var dbGuid))
                        tdToAdd.trackGUID = dbGuid;

                    var audioBytes = await _databaseAccess.DownloadTrackAudioAsync(item.TrackId.ToString());
                    if (audioBytes != null)
                    {
                        var localPath = _jsonDataHandler.SaveAudioBytesToAppStorage(audioBytes);
                        tdToAdd.filePath = localPath;
                        tdToAdd.SetSource(ProviderType.LocalFiles, localPath);
                    }

                    _jsonDataHandler.SaveTrack(tdToAdd);
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
