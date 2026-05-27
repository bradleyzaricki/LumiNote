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
    using LumikitApp.Models;

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
            /// Last recorded mouse pointer location
            /// </summary>
            private Point _lastPointerPos;

            /// <summary>
            /// Color update interval (determined by the max update interval with current light config)
            /// </summary>
            private const int ColorUpdateIntervalMs = 50;
            
            /// <summary>
            /// The source responsible for playing/pausing/locating a point in a music file
            /// </summary>
            private IMusicProvider _musicProvider;
            
            private JsonDataHandler _jsonDataHandler;
            
            private DatabaseAccess _databaseAccess;
            /// <summary>
            /// The music/lighting timeline 
            /// </summary>
            private TimelineController _timeline;

            /// <summary> The serial output for lighting communications  </summary>
            private SerialPanel _serialPanel;
            
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

            public LumikitWindow()  // designer uses this
            {
                InitializeComponent();
            }
            public LumikitWindow(IMusicProvider provider, IPlaybackHandler playbackHandler, JsonDataHandler jsonDataHandler, DatabaseAccess databaseAccess)
            {
                _musicProvider = provider;
                _playbackHandler = playbackHandler;
                _jsonDataHandler = jsonDataHandler;
                _databaseAccess = databaseAccess;

                InitializeComponent();

                _timeline = new TimelineController(
                    this.FindControl<Canvas>("TimelineCanvas"),
                    this.FindControl<ScrollViewer>("TimelineScrollViewer")
                );
                _errorText = this.FindControl<TextBlock>("ErrorText");
                _hardwareConnectionText = this.FindControl<TextBlock>("HardwareConnectionText");
                _blockColorDropBox = this.FindControl<Canvas>("ColorDropBox");
                _secondColorDropBox = this.FindControl<Canvas>("SecondColorDropBox");
                _blockEditor = new BlockEditorPanel(this, _timeline);
                _serialPanel = new SerialPanel(UpdateErrorText, UpdateHardwareConnectionText);

                _bpmInput = this.FindControl<TextBox>("BpmInput");
                
                LocalTracksListBox.ItemsSource = new ObservableCollection<TrackItemUI>();;

                var serialSettingsButton = this.FindControl<Button>("SerialSettingsButton");

                var zoomInBtn = this.FindControl<Button>("ZoomInButton");
                if (zoomInBtn != null)
                    zoomInBtn.Click += (_, _) => _timeline.Zoom(1.25);
                var zoomOutBtn = this.FindControl<Button>("ZoomOutButton");
                if (zoomOutBtn != null)
                    zoomOutBtn.Click += (_, _) => _timeline.Zoom(0.8);
                
                PointerMoved += OnPointerMoved;
                
                //Unlock playback viewer when interacted with
                _timeline._scrollViewer.PointerPressed += (_, _) => _timeline.ChangeScrollLock(false);
                
                _bpmInput.LostFocus += (_, _) =>
                {
                    if (double.TryParse(_bpmInput.Text, out double _bpm) && _bpm > 0)
                    {
                        _timeline.Bpm = _bpm;
                        _timeline.DrawBPMLines();
                    }
                };

                this.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.RightShift)
                    {
                        //Relock the scroll viewer
                        _timeline.ChangeScrollLock(true);
                        e.Handled = true;
                    }
                };
                var seekBar = this.FindControl<Canvas>("SeekBarCanvas");
                seekBar.PointerPressed += async (_, e) =>
                {
    
                    var pos = e.GetPosition(seekBar);
                    int ms = _timeline.CanvasXToMs(pos.X);
                    await _playbackHandler.SeekToPlaybackTime(ms);
                };
                this.KeyDown += OnKeyDown;
                this.KeyUp += OnKeyUp;
                _timeline._scrollViewer.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
                
                var applyBtn = this.FindControl<Button>("ApplyBlockChangesButton");
                if (applyBtn != null) applyBtn.Click += (_, _) => _blockEditor.ApplyBlockChanges();                
                InitializeColorPalette();
                _timeline.DrawTimelineSlots();
            }
            

            /// Activate and Adjust Settings when travel is checked
            private void Effect_Travel_Checked(object? sender, RoutedEventArgs e)
            {
                Effect_Seperate.IsChecked = false;
                Effect_Combine.IsChecked = false;
                _blockEditor.UpdateEffectSettingVisibility();

            }

            /// Activate and Adjust Settings when combine is checked
            private void Effect_Combine_OnChecked(object? sender, RoutedEventArgs e)
            {
                Effect_Seperate.IsChecked = false;
                Effect_Travel.IsChecked = false;
                _blockEditor.UpdateEffectSettingVisibility();

            }
            
            /// Activate and Adjust Settings when seperate is checked
            private void Effect_Seperate_OnChecked(object? sender, RoutedEventArgs e)
            {
                Effect_Combine.IsChecked = false;
                Effect_Travel.IsChecked = false;
                _blockEditor.UpdateEffectSettingVisibility();

            }
            //Effects Changed
            private void Effect_OnChanged(object? sender, RoutedEventArgs e)
            {
                _blockEditor.UpdateEffectSettingVisibility();

            }
            
                       
             /// <summary>
            /// All key down logic
            /// </summary>
            private void OnKeyDown(object? sender, KeyEventArgs e)
            {
                if (_timeline._isLiveInputActive) return;
                var focused = TopLevel.GetTopLevel(this)?
                    .FocusManager?
                    .GetFocusedElement() is TextBox or ComboBox or AutoCompleteBox;
                
                if (e.Key >= Key.D0 && e.Key <= Key.D9 && focused == false)
                    HandleLiveBlockStart(e.Key);
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V)
                    HandlePaste();
            }

            private void HandleLiveBlockStart(Key key)
            {
                _timeline._isLiveInputActive = true;
                _timeline._liveStartMs = _playbackHandler?.CurrentProgressMs ?? 0;

                double caretX = (_timeline._liveStartMs / TimelineController.MsPerSlot) * _timeline._slotWidth;
                double snappedX = Math.Round(caretX / _timeline._slotWidth) * _timeline._slotWidth;

                int colorIndex = key == Key.D0 ? 9 : (key - Key.D1);
                var block = CreateAndPlaceBlock(BlockColors[colorIndex], snappedX, _timeline._slotWidth);

                _timeline._liveBlock = block;
            }

            private void HandlePaste()
            {
                var snapshot = _timeline._selectedBlocks;
                if (snapshot.Count == 0) return;

                double leftmostX = snapshot.Min(b => Canvas.GetLeft(b.Container));

                foreach (var source in snapshot)
                {
                    double offsetX = _lastPointerPos.X + (Canvas.GetLeft(source.Container) - leftmostX);
                    CreateAndPlaceBlock(new LightBlock(source), offsetX, source.BlockColor, source.Container.Width);
                }
            }

            /// <summary>
            /// Create a basic lightblock and place it on th timeline 
            /// </summary>
            /// <param name="color"></param>
            /// <param name="x"></param>
            /// <param name="width"></param>
            /// <returns></returns>
            private LightBlock CreateAndPlaceBlock(Color color, double x, double width)
            {
                var block = new LightBlock(_timeline.LightBlocks, _timeline._scrollViewer, _timeline._slotWidth);
                return CreateAndPlaceBlock(block, x, color, width);
            }

            /// <summary>
            /// Add an existing lightblock to the timeline and create a container for it with a primary color
            /// </summary>
            /// <param name="block"></param>
            /// <param name="x"></param>
            /// <param name="color"></param>
            /// <param name="width"></param>
            /// <returns></returns>
            private LightBlock CreateAndPlaceBlock(LightBlock block, double x, Color color, double width)
            {
                block.Container.Width = width;
                block.UpdateColor(color);
                Canvas.SetLeft(block.Container, x);
                Canvas.SetTop(block.Container, 0);

                _timeline._timelineCanvas.Children.Add(block.Container);
                _timeline.LightBlocks.Add(block);
                _timeline.ReorderLightBlocks();
                block.Container.PointerPressed += OnBlockPointerPressed;
                block.Container.PointerReleased += (_, _) => _timeline.ReorderLightBlocks();

                return block;
            }

            private void OnBlockPointerPressed(object? sender, PointerPressedEventArgs e)
            {
                var container = (Control)sender!;
                var block = _timeline.LightBlocks.First(b => b.Container == container);
                HandleBlockPressed(block, e);
            }

            private void HandleBlockPressed(LightBlock block, PointerPressedEventArgs e)
            {
                var point = e.GetCurrentPoint(block.Container);

                if (point.Properties.IsLeftButtonPressed)
                {
                    var selected = _timeline.HandleBlockSelection(e, block);
                    _blockEditor.LoadBlockIntoEditor(selected);

                }

                if (point.Properties.IsRightButtonPressed)
                    DeleteSelectedBlocks();
            }

            private void DeleteSelectedBlocks()
            {
                foreach (var block in _timeline._selectedBlocks.ToList())
                {
                    block.isSelected = false;
                    _timeline._timelineCanvas.Children.Remove(block.Container);
                    _timeline.LightBlocks.Remove(block);
                }
            }
            private void OnPointerMoved(object? sender, PointerEventArgs e)
            {
                _lastPointerPos = e.GetPosition(_timeline._timelineCanvas);
            }

            private void OnKeyUp(object? sender, KeyEventArgs e)
            {
                // Finish the live block when key is released
                if (_timeline._isLiveInputActive && e.Key == Key.D1 || e.Key == Key.D2 || e.Key == Key.D3 || e.Key == Key.D4 ||
                    e.Key == Key.D5 || e.Key == Key.D6 || e.Key == Key.D7 || e.Key == Key.D8 || e.Key == Key.D9 ||
                    e.Key == Key.D0)
                {
                    _timeline._isLiveInputActive = false;
                    _timeline._liveBlock = null;
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
                        _timeline.ReorderLightBlocks();
                        var trackData = new TrackData
                        {
                            filePath = _musicProvider.currentlyPlayingPath,
                            _trackName = title,
                            author = authors,
                            trackGUID = Guid.Parse(currentGUID),
                            provider = _musicProvider.providerName,
                            _BPM = double.Parse(_bpmInput.Text),
                            _lightBlocks = _timeline.LightBlocks
                                .Select(b => new LightBlockData
                                {
                                    X = Canvas.GetLeft(b.Container) / _timeline._slotWidth,
                                    Width = b.Container.Width / _timeline._slotWidth,
                                    Color = (b.BlockColor).ToString(),
                                    SecondColor = (b.SecondBlockColor).ToString(),
                                    StartLight = b.StartLight,
                                    EndLight = b.EndLight,
                                    SecondaryDualInput2 = b.SecondaryEndLight,
                                    SecondaryDualInput1 = b.SecondaryStartLight,
                                    SecondarySingleInput1 = b.AdditionalIndividualInput1,
                                    SecondarySingleInput2 = b.AdditionalIndividualInput2,
                                    BlockEffects = b.BlockEffects,
                                    LightIntensity = b.Intensity
                                })
                                .ToList()
                                
                        };
                        JsonDataHandler
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

                        // Tick at 10 ms so visuals and strobe match the preview frame rate.
                        // Serial sends are throttled separately to avoid overwhelming the bus.
                        Color[]? colors = _timeline.Tick(ms, 10, _serialPanel.BrightnessScale, ColorUpdateIntervalMs);

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

                    ChangeAppTheme(); //Changes app colors to match provider (required by spotify TOS)
                    _allLocalTrackItems = _jsonDataHandler.GetAllTrackItems();
                        
                
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


                // Clear old blocks
                _timeline.ClearBlocks();
                
                var trackDataLocal = JsonDataHandler.GetTrack(trackGUID.ToString());// await DatabaseAccess.LoadTrackAsync(_musicProvider.providerName, track.trackId);
                if (trackDataLocal != null) //track detected, filling in track data with track POCO
                {
                    _timeline.Bpm = trackDataLocal._BPM;
                    _bpmInput.Text = _timeline.Bpm.ToString();
                    _timeline.DrawTimelineSlots();

                    
                    _timeline.LoadFromTrackData(trackDataLocal, HandleBlockPressed);
                    
                }
                else
                {
                    //track not detected
                    _timeline.Bpm = 0;
                    _bpmInput.Text = "0";
                    _timeline.DrawTimelineSlots();
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

                    DragDrop.SetAllowDrop(_timeline._timelineCanvas, true);
                    DragDrop.SetAllowDrop(_blockColorDropBox, true);
                    DragDrop.SetAllowDrop(_secondColorDropBox,true);
                    _timeline._timelineCanvas.AddHandler(DragDrop.DropEvent, OnCanvasDrop, RoutingStrategies.Bubble);
                    _blockColorDropBox.AddHandler(DragDrop.DropEvent, OnColorCanvasDrop, RoutingStrategies.Bubble);
                    _secondColorDropBox.AddHandler(DragDrop.DropEvent, OnColorCanvasDrop, RoutingStrategies.Bubble);
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

            private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
            {
                if ((e.KeyModifiers & KeyModifiers.Control) != 0)
                {
                    _timeline.ZoomAtPointer(e.Delta.Y, e.GetPosition(_timeline._scrollViewer).X);
                    e.Handled = true;
                    return;
                }

                _timeline.ScrollBy(e.Delta.Y * -40);
                e.Handled = true;
            }


            /// <summary>
            /// Drag n Drop Functionality for Light Block Color Settings
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void OnColorCanvasDrop(object? sender, DragEventArgs e)
            {
                if (!e.Data.Contains("block-color")) return;

                var colorString = e.Data.Get("block-color")?.ToString();
                if (colorString == null || !Color.TryParse(colorString, out var color)) return;

                if (ReferenceEquals(sender, _blockColorDropBox))
                {
                    foreach (var block in _timeline._selectedBlocks)
                    {
                        block.UpdateColor(color);
                        _blockColorDropBox.Background = new SolidColorBrush(color);
                        _blockColorDropBox.ClipToBounds = true;
                    }

                    return;
                }

                if (ReferenceEquals(sender, _secondColorDropBox))
                {
                    foreach (var block in _timeline._selectedBlocks)
                    {
                        block.SecondBlockColor = color;
                        _secondColorDropBox.Background = new SolidColorBrush(color);
                        _secondColorDropBox.ClipToBounds = true;
                    }

                    return;
                }
            }


            /// <summary>
            ///  drag n drop lightblock drop logic
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void OnCanvasDrop(object? sender, DragEventArgs e)
            {
                if (!e.Data.Contains("block-color")) return;
                var colorString = e.Data.Get("block-color")?.ToString();
                if (colorString == null || !Color.TryParse(colorString, out var color)) return;

                var pos = e.GetPosition(_timeline._timelineCanvas);
                double snappedX = Math.Round(pos.X / _timeline._slotWidth) * _timeline._slotWidth;
                snappedX = Math.Max(0, Math.Min(snappedX, _timeline._timelineCanvas.Width - _timeline._slotWidth));

                double finalWidth = CalculateDropWidth(snappedX);
                if (finalWidth < _timeline._slotWidth) return;

                var block = CreateAndPlaceBlock(color, snappedX, finalWidth);
                block.Intensity = 255;
                block.EndLight = 1000;
            }

            private double CalculateDropWidth(double snappedX)
            {
                double maxWidth = Math.Min(_timeline._slotWidth * 50, _timeline._timelineCanvas.Width - snappedX);
                double finalWidth = maxWidth;

                while (finalWidth >= _timeline._slotWidth)
                {
                    bool collision = _timeline.LightBlocks.Any(existing =>
                    {
                        double left = Canvas.GetLeft(existing.Container);
                        double width = existing.Container.Width;
                        return snappedX < left + width && snappedX + finalWidth > left;
                    });

                    if (!collision) break;
                    finalWidth -= _timeline._slotWidth;
                }

                return finalWidth;
            }
 
            /// <summary>
            /// Calculates the RGB positions and applies them to the simulated color bars
            /// </summary>
            /// <param name="stripColors"></param>
            private void UpdateColorBar(Color[] stripColors)
            {
                var colors = stripColors ?? Array.Empty<Color>();
                int n = colors.Length;

                double fullWidth = _timeline._scrollViewer.Viewport.Width;
                if (fullWidth <= 0)
                    fullWidth = _timeline._scrollViewer.Bounds.Width;

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
                if (_musicProvider.IsProviderLocal) //if track retrieval method is through local files, open file manager
                {
                    if (sender is Button b && this.Resources["OpenAudioFlyout"] is Flyout f) 
                        f.ShowAt(b);
                }

                if (!_musicProvider.IsProviderLocal) //set current track to currently playing track
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
                if (_musicProvider.providerName == "LocalFiles")
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

                    var importedPath = JsonDataHandler.ImportAudioToAppStorage(path);

                    SelectedAudioPathText.Text = importedPath;
                    OnAudioFileSelected(importedPath);  
                }
          
            }
            
            /// <summary>
            /// Update playback with locally selected audio file
            /// </summary>
            /// <param name="path"></param>
            private void OnAudioFileSelected(string path)
            {
            
                _musicProvider.currentTrack = new TrackPOCO(Guid.Empty, "Unnamed Track", "Unnamed Artists", null);
                _musicProvider.currentlyPlayingPath = path;
                _playbackHandler.PlayAsync();
                UpdateCurrentTrack(true, trackGUID: Guid.Empty); // refresh on next track
                
            }
            
            /// <summary>
            /// Refresh the list of local track files
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void RefreshLocalTracks_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            {
                _allLocalTrackItems = _jsonDataHandler.GetAllTrackItems();
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
                DatabaseAccess.SaveTrackAsync(item.TrackId.ToString(), JsonDataHandler.GetTrack(item.TrackId.ToString()));

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
                _trackQueue.Insert(0,item.TrackId.ToString());
                var track = JsonDataHandler.GetTrack(_trackQueue.First());
                _musicProvider.currentlyPlayingPath = track.filePath;
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

                JsonDataHandler.DeleteTrack(item.TrackId.ToString());
                
                _allLocalTrackItems = _jsonDataHandler.GetAllTrackItems();
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
                if (JsonDataHandler.GetTrack(item.TrackId.ToString()) == null)
                {
                    TrackData tdToAdd = await DatabaseAccess.LoadTrackAsync(item.TrackId.ToString());
                    var fileEnd = Path.GetExtension(tdToAdd.filePath).ToLowerInvariant();
                    if (fileEnd == ".wav" || fileEnd == ".mp3")
                    {
                        tdToAdd.filePath = Path.Combine(DirectoryPaths.AudioDir,tdToAdd.filePath);
                    }
                    JsonDataHandler.SaveTrack(tdToAdd);
                }

            }
            
            /// <summary>True while the main music timer is ticking.</summary>
            internal bool IsPlaybackActive => _playbackHandler?.IsTimerRunning == true;

            internal void PlayPreview()
            {
                if (_timeline._selectedBlocks.Count == 0) return;

                _previewRunning = false; // stop any existing loop
                _previewWatch.Restart();
                _previewRunning = true;

                _ = Task.Run(async () =>
                {
                    while (_previewRunning)
                    {
                        double currentMs = _previewWatch.Elapsed.TotalMilliseconds;

                        // Snapshot only the Avalonia-bound values on the UI thread — fast reads
                        double slotWidth = 0;
                        var blocks = new List<(LightBlock Block, double Left, double Width)>();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            slotWidth = _timeline._slotWidth;
                            foreach (var b in _timeline._selectedBlocks)
                                blocks.Add((b, Canvas.GetLeft(b.Container), b.Container.Width));
                        });

                        if (blocks.Count == 0)
                        {
                            await Task.Delay(ColorUpdateIntervalMs);
                            continue;
                        }

                        // Heavy computation stays on the background thread
                        Color[] finalLeds = ComputePreviewFrame(currentMs, blocks, slotWidth);

                        // Loop back when the last block finishes
                        double last = blocks.Max(b => b.Left + b.Width);
                        if (currentMs > (last - blocks[0].Left) * TimelineController.MsPerSlot / slotWidth)
                            _previewWatch.Restart();

                        // Push the finished frame to the UI — just a SetColors call
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

            /// <summary>
            /// Pure computation — no Avalonia calls. Safe to run on any thread.
            /// </summary>
            private static Color[] ComputePreviewFrame(
                double currentMs,
                List<(LightBlock Block, double Left, double Width)> blocks,
                double slotWidth)
            {
                double slotMs = TimelineController.MsPerSlot;
                var finalLeds = new Color[1000];

                foreach (var (block, left, width) in blocks)
                {
                    double blockTimeOffset = (left - blocks[0].Left) * slotMs / slotWidth;
                    double localTime       = currentMs - blockTimeOffset;
                    if (localTime < 0) continue;

                    double relPos = Math.Clamp(localTime / (width * slotMs / slotWidth), 0.0, 1.0);

                    Color[] blockLeds = LightEffectsComputer.ComputeBlockEffects(
                        block, relPos, 100,
                        containerWidth:  width,
                        containerLeft:   left,
                        elapsedMs:       localTime,
                        serialIntervalMs: ColorUpdateIntervalMs);

                    if (blockLeds == null) continue; // strobe off-phase — leave LEDs dark

                    for (int i = 0; i < finalLeds.Length; i++)
                        finalLeds[i] = blockLeds[i];
                }

                return finalLeds;
            }
        }
    }
