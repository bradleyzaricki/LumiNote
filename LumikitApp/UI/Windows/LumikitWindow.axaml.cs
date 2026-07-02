    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Input;
    using Avalonia.Interactivity;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Threading;
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
            
            //Lists to store both local track UI items and database track ui items
            private List<TrackItemUI> _allLocalTrackItems = new();
            private List<TrackItemUI> _allDatabaseTrackItems = new();

            //Avalonia UI elements
            private Canvas _blockColorDropBox;
            private Canvas _secondColorDropBox;
            private Canvas _fillColorDropBox;
            private TextBox _bpmInput;
            
            private TextBlock _errorText;
            private TextBlock _hardwareConnectionText; 
            
            /// <summary>
            /// implemented playback handler to control implemented music provider
            /// </summary>
            private IPlaybackHandler _playbackHandler;
            private List<String> _trackQueue = new List<string>();

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
                {
                    var newPopUp = new NewTrackPopup();

                    // 'this' is the parent window; ShowDialog makes it modal
                    bool? result = await newPopUp.ShowDialog<bool?>(this);

                    if (result == true)
                    {
                        string title = newPopUp.TitleText;
                        string authors = newPopUp.AuthorText;

                        var track = await _musicProvider.GetCurrentlyPlayingTrackAsync();
                        if (currentGUID == Guid.Empty.ToString())
                        {
                            currentGUID = Guid.NewGuid().ToString();
                        }
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
                            trackGUID = Guid.Parse(currentGUID),
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
                        _jsonDataHandler
                            .SaveTrack(
                                trackData);
                    }
                };

                                
                
                //Returns custom playbackhandler based on the music provider
                //--This exists because different apps may have different latencies which require a delay
                _playbackHandler.ProgressUpdated += async ms =>
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        // Kill any running block preview the moment real playback ticks.
                        if (_previewRunning && _playbackHandler.IsTimerRunning) StopPreview();

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
                        _musicProvider.currentTrack = s_unknownTrack;
                        //set currently playing path to next track in queue
                        //_musicProvider.currentlyPlayingPath = Nextpath;

                        await _playbackHandler.PlayAsync();
                        UpdateCurrentTrack(true, trackGUID: Guid.Empty); // refresh on next track
                    };
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
                    _blockColorDropBox.AddHandler(DragDrop.DropEvent, OnColorCanvasDrop, RoutingStrategies.Bubble);
                    _secondColorDropBox.AddHandler(DragDrop.DropEvent, OnColorCanvasDrop, RoutingStrategies.Bubble);
                    _fillColorDropBox.AddHandler(DragDrop.DropEvent, OnColorCanvasDrop, RoutingStrategies.Bubble);
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
                if (_musicRouter.HasProvider(ProviderType.LocalFiles)) //local import available — open file manager
                {
                    if (sender is Button b && this.Resources["OpenAudioFlyout"] is Flyout f)
                        f.ShowAt(b);
                }
                else if (!_musicProvider.IsProviderLocal) //set current track to currently playing track
                {
                    var path = _musicProvider.GetCurrentlyPlayingTrackIdAsync();
                    _musicProvider.currentTrack = new TrackPOCO(Guid.Empty, "Unnamed Track", "Unnamed Artists", null);
                    _musicProvider.currentlyPlayingPath = path;
                    _playbackHandler.PlayAsync();
                    UpdateCurrentTrack(true, Guid.Empty); 
                }            
            }
            
            /// <summary>
            /// Select new lightmap track file from local files
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private async void BrowseAudioFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            {
                if (_musicRouter.HasProvider(ProviderType.LocalFiles))
                {
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

                _musicProvider.currentTrack = new TrackPOCO(Guid.Empty, "Unnamed Track", "Unnamed Artists", null);
                _musicProvider.currentlyPlayingPath = path;
                await _playbackHandler.PlayAsync();
                UpdateCurrentTrack(true, trackGUID: Guid.Empty); // refresh on next track

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
                _databaseAccess.SaveTrackAsync(item.TrackId.ToString(), _jsonDataHandler.GetTrack(item.TrackId.ToString()));

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
                
                //Insert and play from top of queue
                _trackQueue.Insert(0, item.TrackId.ToString());
                var track = _jsonDataHandler.GetTrack(_trackQueue.First());

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
                    return;
                }

                // Switch source if needed (router pauses the current source before swapping).
                if (targetProvider != _musicProvider.providerName)
                    await _musicRouter.SwitchToAsync(targetProvider.Value);

                _musicProvider.currentlyPlayingPath = track.GetSource(targetProvider.Value);
                //Set visual information for track that is stored in json
                _musicProvider.currentTrack = new TrackPOCO(track.trackGUID, track._trackName, track.author, null);
                await _playbackHandler.PlayAsync();
                UpdateCurrentTrack(true, track.trackGUID); // refresh on next track

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
