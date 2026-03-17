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
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.IO.Ports;
    using LumikitApp.Models;

    //UpperCase = User defined global variables (settings, preferences etc..)
    //camelCase = local temporary variables used in methods
    //_thisCase = global variables needed on a class level
    namespace LumikitApp
    {
        public partial class LumikitWindow : Window
        {
            private static Color _spotifyGreen = new Color(255, 30, 215, 96);
            
            private int ActiveLedCount = 0;
            private int ColorUpdateIntervalMs = 50;
            private int HardwareCurrent = 0;
            private double BrightnessScale = 1;
            /// <summary> The music provider to handle audio playback (ex. Spotify, Local Files...) </summary>
            private IMusicProvider _musicProvider;
            
            /// <summary> The serial output for lighting communications  </summary>
            private SerialHandler SerialHandler;
            private Point _lastPointerPos;
            
            private TimelineController _timeline;


            //Possible color blocks for lightshow editing, can be redefined by the user
            private readonly List<Color> BlockColors = new()
            {
                Colors.DarkRed, Colors.Red, Colors.Orange, Colors.Yellow, Colors.Green,Colors.Aqua, Colors.Blue,
                Colors.Purple, Colors.Magenta, Colors.White
            };

            private ObservableCollection<TrackItemUI> _tracks 
                = new ObservableCollection<TrackItemUI>();
            
            //This is used for local file mode 
            private string currentGUID = Guid.Empty.ToString();
            private List<TrackItemUI> _allLocalTrackItems = new();
            private List<TrackItemUI> _allDatabaseTrackItems = new();

            //Avalonia UI elements
            private Canvas _blockColorDropBox;
            private Canvas _secondColorDropBox;

            private TextBox _bpmInput;

            //Handles unique playback logic depending on the music provider
            private IPlaybackHandler _playbackHandler;
            private List<String> _trackQueue = new List<string>();

            //Live "Piano Roll" Block Painting Variables
            Border? _activeSwatch;

            public LumikitWindow()
            {
                InitializeComponent();
                _timeline = new TimelineController(
                    this.FindControl<Canvas>("TimelineCanvas"),
                    this.FindControl<ScrollViewer>("TimelineScrollViewer")
                );
                _blockColorDropBox = this.FindControl<Canvas>("ColorDropBox");
                _secondColorDropBox = this.FindControl<Canvas>("SecondColorDropBox");
                _bpmInput = this.FindControl<TextBox>("BpmInput");
                
                LocalTracksListBox.ItemsSource = _tracks;

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

                this.KeyDown += OnKeyDown;
                this.KeyUp += OnKeyUp;
                _timeline._scrollViewer.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
                
                var applyBtn = this.FindControl<Button>("ApplyBlockChangesButton");
                if (applyBtn != null) applyBtn.Click += OnApplyBlockChangesClicked;
                
                InitializeColorPalette();
                _timeline.DrawTimelineSlots();

            }
            

            /// Activate and Adjust Settings when travel is checked
            private void Effect_Travel_Checked(object? sender, RoutedEventArgs e)
            {
                Effect_Seperate.IsChecked = false;
                Effect_Combine.IsChecked = false;
                UpdateEffectSettingVisibility();
            }

            /// Activate and Adjust Settings when combine is checked
            private void Effect_Combine_OnChecked(object? sender, RoutedEventArgs e)
            {
                Effect_Seperate.IsChecked = false;
                Effect_Travel.IsChecked = false;
                UpdateEffectSettingVisibility();

            }
            
            /// Activate and Adjust Settings when seperate is checked
            private void Effect_Seperate_OnChecked(object? sender, RoutedEventArgs e)
            {
                Effect_Combine.IsChecked = false;
                Effect_Travel.IsChecked = false;
                UpdateEffectSettingVisibility();
            }
            //Effects Changed
            private void Effect_OnChanged(object? sender, RoutedEventArgs e)
            {
                UpdateEffectSettingVisibility();
            }
            
           
            /// <summary>
            /// All key down logic
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void OnKeyDown(object? sender, KeyEventArgs e)
            {
                // Start a live block when hotkey (num 0-9) is pressed
                if (!_timeline._isLiveInputActive && e.Key >= Key.D0 && e.Key <= Key.D9)
                {
                    _timeline._isLiveInputActive = true;
                    _timeline._liveStartMs = _playbackHandler?.CurrentProgressMs ?? 0; 
                    double caretX = (_timeline._liveStartMs / TimelineController.MsPerSlot) * _timeline._slotWidth;

                    // Create a new block at the caret
                    var block = new LightBlock(_timeline.LightBlocks, _timeline._scrollViewer, _timeline._slotWidth);
                    switch (e.Key)
                    {
                        case Key.D1:
                            block.UpdateColor(BlockColors[0]);
                            break;
                        case Key.D2:
                            block.UpdateColor(BlockColors[1]);
                            break;
                        case Key.D3:
                            block.UpdateColor(BlockColors[2]);
                            break;
                        case Key.D4:
                            block.UpdateColor(BlockColors[3]);
                            break;
                        case Key.D5:
                            block.UpdateColor(BlockColors[4]);
                            break;
                        case Key.D6:
                            block.UpdateColor(BlockColors[5]);
                            break;
                        case Key.D7:
                            block.UpdateColor(BlockColors[6]);
                            break;
                        case Key.D8:
                            block.UpdateColor(BlockColors[7]);
                            break;
                        case Key.D9:
                            block.UpdateColor(BlockColors[8]);
                            break;
                        case Key.D0:
                            block.UpdateColor(BlockColors[9]);
                            break;
                    }

                    block.Container.Width = _timeline._slotWidth;
                    var snappedX =  Math.Round(caretX / _timeline._slotWidth) * _timeline._slotWidth;
                    Canvas.SetLeft(block.Container, snappedX);
                    Canvas.SetTop(block.Container, 0);
                    _timeline._timelineCanvas.Children.Add(block.Container);
                    _timeline.LightBlocks.Add(block);
                    _timeline._liveBlock = block;

                    //Assign light block keybinds (Lclick edit, Rclick delete)
                    block.Container.PointerPressed += (_, e) =>
                    {
                        if (e.GetCurrentPoint(block.Container).Properties.IsLeftButtonPressed)
                        {
                            AddNewLightBlockToTimeline(e, block);
                        }

                        if (e.GetCurrentPoint(block.Container).Properties.IsRightButtonPressed)
                        {
                            var selectedBlocksSnapshot = _timeline._selectedBlocks;
                            foreach (var selectedBlock in selectedBlocksSnapshot)
                            {
                                selectedBlock.isSelected = false;
                                _timeline._timelineCanvas.Children.Remove(selectedBlock.Container);
                                _timeline.LightBlocks.Remove(selectedBlock);

                            }

                        }
                    };
                }

                //Copy light blocks
                if (!_timeline._isLiveInputActive && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V)
                {
                    var selectedBlocksSnapshot = _timeline._selectedBlocks;
                    var pasteX = double.MaxValue;
                    foreach (var block in selectedBlocksSnapshot)
                    {
                        if (Canvas.GetLeft(block.Container) < pasteX)
                        {
                            pasteX = Canvas.GetLeft(block.Container);
                        }
                    }

                    foreach (LightBlock blockToCopy in selectedBlocksSnapshot)
                    {
                        var addedBlock = new LightBlock(blockToCopy);
                        addedBlock.UpdateColor(blockToCopy.BlockColor);

                        Canvas.SetLeft(addedBlock.Container,
                            _lastPointerPos.X + (Canvas.GetLeft(blockToCopy.Container) - pasteX));
                        Canvas.SetTop(addedBlock.Container, 0);

                        _timeline._timelineCanvas.Children.Add(addedBlock.Container);
                        _timeline.LightBlocks.Add(addedBlock);
                        addedBlock.Container.PointerPressed += (_, e) =>
                        {
                            if (e.GetCurrentPoint(addedBlock.Container).Properties.IsLeftButtonPressed)
                            {
                                AddNewLightBlockToTimeline(e, addedBlock);
                            }

                            if (e.GetCurrentPoint(addedBlock.Container).Properties.IsRightButtonPressed)
                            {
                                var selectedBlocksSnapshot = _timeline._selectedBlocks;
                                foreach (var selectedBlock in selectedBlocksSnapshot)
                                {
                                    selectedBlock.isSelected = false;
                                    _timeline._timelineCanvas.Children.Remove(selectedBlock.Container);
                                    _timeline.LightBlocks.Remove(selectedBlock);

                                }
                            }
                        };
                    }
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

            public void InitializeWindow(IMusicProvider provider)
            {
                BlockEditor.IsVisible = false;
                this.FindControl<Button>("SaveTrackDataButton").Click += async (_, _) =>
                {
                    var newPopUp = new NewTrackPopup();

                    // 'this' is the parent window; ShowDialog makes it modal
                    bool? result = await newPopUp.ShowDialog<bool?>(this);

                    if (result == true)
                    {
                        string title = newPopUp.TitleText;
                        string authors = newPopUp.AuthorText;

                        var track = await provider.GetCurrentlyPlayingTrackAsync();
                        if (currentGUID == Guid.Empty.ToString())
                        {
                            currentGUID = Guid.NewGuid().ToString();
                        }
                        var trackData = new TrackData
                        {

                            filePath = _musicProvider.currentlyPlayingPath,
                            _trackName = title,
                            author = authors,
                            trackGUID = Guid.Parse(currentGUID),
                            provider = _musicProvider.providerName,
                            _BPM = double.Parse(_bpmInput.Text),
                            _lightBlocks = _timeline.LightBlocks.Select(b => new LightBlockData
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
                            }).ToList()
                        };
                        JsonDataHandler
                            .SaveTrack(
                                trackData); 
                    }
                };

                _playbackHandler = new PlaybackHandler(provider);
                _playbackHandler.ProgressUpdated += ms =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        StopwatchLabel.Text = ms.ToString();
                        UpdateCaretAndScroll(ms);
                    });
                };
                this.FindControl<Button>("PauseTrackButton").Click += async (_, _) => await _playbackHandler.PauseAsync();
                this.FindControl<Button>("ResumeTrackButton").Click += async (_, _) => await _playbackHandler.ResumeAsync();
                this.FindControl<Button>("NextTrackButton").Click += async (_, _) =>
                {
                    _musicProvider.currentTrack = new TrackPOCO(Guid.Empty, "Unnamed Track", "Unnamed Artists", null);
                    await _playbackHandler.SkipAsync();
                    UpdateCurrentTrack(true, trackGUID:Guid.Empty); // refresh on next track
                };
                this.FindControl<Button>("RestartTrackButton").Click += async (_, _) => _playbackHandler.RestartAsync();

                _musicProvider = provider;
                ChangeAppTheme(); //Changes app colors to match provider (required by spotify TOS)
                _allLocalTrackItems = JsonDataHandler.GetAllTrackItems();
                LocalTracksListBox.ItemsSource = _allLocalTrackItems;

            }

            public async void UpdateCurrentTrack(bool startNewLightShow, Guid trackGUID)
            {
                currentGUID=trackGUID.ToString();
                var track = await _musicProvider.GetCurrentlyPlayingTrackAsync();

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

                    foreach (var data in trackDataLocal._lightBlocks)
                    {
                        if (!Color.TryParse(data.Color, out var color)) continue;
                        var block = new LightBlock(_timeline.LightBlocks, _timeline._scrollViewer, _timeline._slotWidth);
                        block.UpdateColor(color);
                        block.SecondBlockColor = (Color.TryParse(data.SecondColor, out var color2) ? color2 : new Color());
                        block.StartLight = data.StartLight;
                        block.EndLight = data.EndLight;
                        block.BlockEffects = data.BlockEffects;
                        block.Intensity = data.LightIntensity;
                        block.SecondaryStartLight = data.SecondaryDualInput1;
                        block.SecondaryEndLight = data.SecondaryDualInput2;
                        block.AdditionalIndividualInput1 = data.SecondarySingleInput1;
                        block.AdditionalIndividualInput2 = data.SecondarySingleInput2;
                        block.Container.Width = data.Width * _timeline._slotWidth;
                        Canvas.SetLeft(block.Container, data.X * _timeline._slotWidth);
                        Canvas.SetTop(block.Container, 0);

                        _timeline._timelineCanvas.Children.Add(block.Container);
                        _timeline.LightBlocks.Add(block);

                        //Assign light block keybinds (Lclick edit, Rclick delete)
                        block.Container.PointerPressed += (_, e) =>
                        {
                            if (e.GetCurrentPoint(block.Container).Properties.IsLeftButtonPressed)
                            {
                                AddNewLightBlockToTimeline(e, block);
                            }

                            if (e.GetCurrentPoint(block.Container).Properties.IsRightButtonPressed)
                            {
                                var selectedBlocksSnapshot = _timeline._selectedBlocks;
                                foreach (var selectedBlock in selectedBlocksSnapshot)
                                {
                                    selectedBlock.isSelected = false;
                                    _timeline._timelineCanvas.Children.Remove(selectedBlock.Container);
                                    _timeline.LightBlocks.Remove(selectedBlock);

                                }
                            }
                        };
                    }
                }
                else
                {
                    //track not detected
                    _timeline.Bpm = 0;
                    _bpmInput.Text = "0";
                    _timeline.DrawTimelineSlots();
                }
            }

            private void AddNewLightBlockToTimeline(PointerPressedEventArgs e, LightBlock blockToAdd)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    var min = double.MaxValue;
                    var max = -1.00;
                    foreach (var selectedblock in _timeline._selectedBlocks)
                    {
                        if (Canvas.GetLeft(selectedblock.Container) < min)
                        {
                            min = Canvas.GetLeft(selectedblock.Container);
                            Console.WriteLine("MIN = " + min);
                        }

                        if (Canvas.GetLeft(selectedblock.Container) > max)
                        {
                            max = Canvas.GetLeft(selectedblock.Container);
                        }
                    }

                    if (min == double.MaxValue) //No blocks selected previously 
                    {
                        _timeline._selectedBlocks.Add(blockToAdd);
                        blockToAdd.isSelected = true;
                        LoadBlockIntoEditor(_timeline._selectedBlocks);
                        return;

                    }

                    foreach (var lightblock in _timeline.LightBlocks)
                    {
                        if (_timeline._selectedBlocks.Contains(lightblock))
                            continue;

                        var blockLeft = Canvas.GetLeft(lightblock.Container);
                        if ((blockLeft > min && blockLeft <= Canvas.GetLeft(blockToAdd.Container))
                            || (blockLeft < max && blockLeft >= Canvas.GetLeft(blockToAdd.Container)))
                        {
                            //Add all selected blocks including block that was shift clicked
                            _timeline._selectedBlocks.Add(lightblock);
                            lightblock.isSelected = true;
                            blockToAdd.isSelected = true;

                            LoadBlockIntoEditor(_timeline._selectedBlocks);

                        }
                    }

                }
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    _timeline._selectedBlocks.Add(blockToAdd);
                    blockToAdd.isSelected = true;

                    LoadBlockIntoEditor(_timeline._selectedBlocks);

                }

                else
                {
                    foreach (var blockToRemove in _timeline._selectedBlocks)
                    {
                        blockToRemove.UpdateBackground(blockToRemove.BlockColor);
                        blockToRemove.isSelected = false;

                    }

                    _timeline._selectedBlocks.Clear();
                    _timeline._selectedBlocks.Add(blockToAdd);
                    blockToAdd.isSelected = true;

                    LoadBlockIntoEditor(_timeline._selectedBlocks);

                }

                e.Handled = true;
            }



            /// <summary>
            /// Loads sidebar editor with pre-existing lightblock values
            /// </summary>
            /// <param name="block"></param>
            private void LoadBlockIntoEditor(List<LightBlock> selectedBlocks)
            {
                if(selectedBlocks == null || selectedBlocks.Count == 0)return;
                BlockEditor.IsVisible = true;
                UpdateSelectedColorsBackground();
                UpdateEffectSettingVisibility();
                foreach (var block in selectedBlocks)
                {
                    StartLightInput.Text = block.StartLight.ToString();
                    EndLightInput.Text = block.EndLight.ToString();
                    IntensityInput.Text = block.Intensity.ToString();
                    AdditionalDualInput2TextBox.Text = block.SecondaryEndLight.ToString();
                    AdditionalDualInput1TextBox.Text = block.SecondaryStartLight.ToString();
                    AdditionalSingleInput1TextBox.Text = block.AdditionalIndividualInput1.ToString();
                    AdditionalSingleInput2TextBox.Text = block.AdditionalIndividualInput2.ToString();
                    _blockColorDropBox.Background = new SolidColorBrush(block.BlockColor);
                    _secondColorDropBox.Background = new SolidColorBrush(block.SecondBlockColor);
                    // Reset effect selection

                    this.FindControl<CheckBox>("Effect_FadeIn").IsChecked =
                        block.BlockEffects.Contains(LightBlock.Effect.FadeIn);
                    this.FindControl<CheckBox>("Effect_FadeOut").IsChecked =
                        block.BlockEffects.Contains(LightBlock.Effect.FadeOut);
                    this.FindControl<CheckBox>("Effect_FadeStrobe").IsChecked =
                        block.BlockEffects.Contains(LightBlock.Effect.Strobe);
                    this.FindControl<CheckBox>("Effect_Travel").IsChecked =
                        block.BlockEffects.Contains(LightBlock.Effect.Travel);
                    this.FindControl<CheckBox>("Effect_Combine").IsChecked =
                        block.BlockEffects.Contains(LightBlock.Effect.Combine);
                    this.FindControl<CheckBox>("Effect_Seperate").IsChecked =
                        block.BlockEffects.Contains(LightBlock.Effect.Seperate);
                    this.FindControl<CheckBox>("Effect_Repeat").IsChecked =
                        block.BlockEffects.Contains((LightBlock.Effect.Repeat));
                    this.FindControl<CheckBox>("Effect_ChangeColor").IsChecked =
                        block.BlockEffects.Contains((LightBlock.Effect.ChangeColor));
                    this.FindControl<CheckBox>("Effect_Twinkle").IsChecked =
                        block.BlockEffects.Contains(LightBlock.Effect.Twinkle);

                }
                UpdateEffectSettingVisibility();


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
            /// Create color pallet and dragndrop functionality via avalonia swatches
            /// </summary>
            private void InitializeColorPalette()
            {
                var palette = this.FindControl<WrapPanel>("ColorPalette");
                palette.Children.Clear();

                var picker = this.FindControl<ColorPicker>("SwatchFlyoutPicker");
                picker.PropertyChanged -= SwatchFlyoutPickerOnPropertyChanged;
                picker.PropertyChanged += SwatchFlyoutPickerOnPropertyChanged;

                var hardwareSettingsButton = this.FindControl<Button>("HardwareSettingsButton");
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
                            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
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

                //Drop selected R G B color value to the playback track
                var pos = e.GetPosition(_timeline._timelineCanvas);
                double snappedX = Math.Round(pos.X / _timeline._slotWidth) * _timeline._slotWidth;
                snappedX = Math.Max(0, Math.Min(snappedX, _timeline._timelineCanvas.Width - _timeline._slotWidth));
                double maxWidth = Math.Min(_timeline._slotWidth * 50, _timeline._timelineCanvas.Width - snappedX);
                double finalWidth = maxWidth;

                while (finalWidth >= _timeline._slotWidth)
                {
                    bool collision = false;
                    foreach (var existing in _timeline.LightBlocks)
                    {
                        double left = Canvas.GetLeft(existing.Container);
                        double width = existing.Container.Width;
                        if (snappedX < left + width && snappedX + finalWidth > left)
                        {
                            collision = true;
                            break;
                        }
                    }

                    if (!collision) break;
                    finalWidth -= _timeline._slotWidth;
                }

                if (finalWidth < _timeline._slotWidth) return;

                var block = new LightBlock(_timeline.LightBlocks, _timeline._scrollViewer, _timeline._slotWidth);
                block.Container.Width = finalWidth;
                Canvas.SetLeft(block.Container, snappedX);
                Canvas.SetTop(block.Container, 0);
                _timeline._timelineCanvas.Children.Add(block.Container);
                _timeline.LightBlocks.Add(block);
                block.UpdateColor(color);
                block.Intensity = 255;
                block.EndLight = 1000;
                
                //Assign light block keybinds (Lclick edit, Rclick delete)
                block.Container.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(block.Container).Properties.IsLeftButtonPressed)
                    {
                        AddNewLightBlockToTimeline(e, block);
                    }

                    if (e.GetCurrentPoint(block.Container).Properties.IsRightButtonPressed)
                    {
                        var selectedBlocksSnapshot = _timeline._selectedBlocks;
                        foreach (var selectedBlock in selectedBlocksSnapshot)
                        {
                            selectedBlock.isSelected = false;
                            _timeline._timelineCanvas.Children.Remove(selectedBlock.Container);
                            _timeline.LightBlocks.Remove(selectedBlock);

                        }

                        e.Handled = true;
                    }
                };

            }

            /// <summary>
            /// Update visualization of light playback. Imitates the microcontroller for visualization and development purposes
            /// </summary>
            /// <param name="ms"></param>
            private void UpdateCaretAndScroll(int ms)
            {
                if (ms < _timeline._lastColorUpdateMs)
                    _timeline._lastColorUpdateMs = ms;

                if (ms - _timeline._lastColorUpdateMs < ColorUpdateIntervalMs)
                    return;

                _timeline._lastColorUpdateMs = ms;
                double slotIndex = ms / TimelineController.MsPerSlot;
                double caretX = slotIndex * _timeline._slotWidth;
                Canvas.SetLeft(_timeline._playheadCaret, caretX - 4);

                if (_timeline.ScrollLocked)
                {
                    double viewportWidth = _timeline._scrollViewer.Viewport.Width;
                    double scrollTo = Math.Max(0, caretX - viewportWidth / 6);
                    _timeline._scrollViewer.Offset = new Vector(scrollTo, _timeline._scrollViewer.Offset.Y);
                }

                if (_timeline._isLiveInputActive && _timeline._liveBlock != null)
                {
                    double startX = Canvas.GetLeft(_timeline._liveBlock.Container);
                    var snappedWidth = Math.Round(((caretX - startX) / _timeline._slotWidth ) +1 )* _timeline._slotWidth;
                    _timeline._liveBlock.Container.Width = Math.Max(_timeline._slotWidth, snappedWidth);
                }

                var activeBlock = _timeline.LightBlocks.FirstOrDefault(b =>
                {
                    double left = Canvas.GetLeft(b.Container);
                    double width = b.Container.Width;
                    return caretX >= left && caretX <= left + width;
                });

                if (activeBlock == null)
                {
                    if (SerialHandler != null)
                    {
                        SerialHandler.SendFrame(Array.Empty<Color>());

                    }
                    
                    TopColorBar.Background = new SolidColorBrush(Colors.Transparent);
                    BottomColorBar.Background = new SolidColorBrush(Colors.Transparent);
                    return;
                }

                var blockColor = activeBlock.BlockColor;
                TopColorBar.Background = new SolidColorBrush(blockColor);
                BottomColorBar.Background = new SolidColorBrush(blockColor);

                double width = activeBlock.Container.Width;
                if (width <= 0)
                    return;

                double left = Canvas.GetLeft(activeBlock.Container);
                double relPos = Math.Clamp((caretX - left) / width, 0, 1);


                var stripColors = LightEffectsComputer.ComputeBlockEffects(activeBlock, relPos, BrightnessScale);

                UpdateColorBar(stripColors);

                if (SerialHandler != null)
                {
                    try
                    {
                        SerialHandler.SendFrame(stripColors);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
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
            
            private void OnApplyBlockChangesClicked(object? s, RoutedEventArgs e)
            {
                
                if (_timeline._selectedBlocks == null) return;
                //Apply settings made in block editor to every selected block
                foreach (var selectedBlock in _timeline._selectedBlocks)  
                {
                    // Start/End lights
                    if (int.TryParse(this.FindControl<TextBox>("StartLightInput").Text, out int start))
                        selectedBlock.StartLight = start;
                    if (int.TryParse(this.FindControl<TextBox>("EndLightInput").Text, out int end))
                        selectedBlock.EndLight = end;
                    
                    // Intensity
                    if (int.TryParse(this.FindControl<TextBox>("IntensityInput").Text, out int intensity))
                        selectedBlock.Intensity = Math.Clamp(intensity, 0, 255);
                    
                    //2 Additional Text Boxes
                    if (int.TryParse(this.FindControl<TextBox>("AdditionalDualInput1TextBox").Text, out int travelStart))
                        selectedBlock.SecondaryStartLight = travelStart;
                    
                    if (int.TryParse(this.FindControl<TextBox>("AdditionalDualInput2TextBox").Text, out int travelEnd))
                        selectedBlock.SecondaryEndLight = travelEnd;
                    
                    //Additional Single Text Box
                    if(int.TryParse(this.Find<TextBox>("AdditionalSingleInput1TextBox").Text, out int single1))
                        selectedBlock.AdditionalIndividualInput1 = single1;
                    //Additional Single Text Box
                    if(int.TryParse(this.Find<TextBox>("AdditionalSingleInput2TextBox").Text, out int single2))
                        selectedBlock.AdditionalIndividualInput2 = single2;
                    
                    // Effect radio buttons
                    var rbIn     = this.FindControl<CheckBox>("Effect_FadeIn");
                    var rbOut    = this.FindControl<CheckBox>("Effect_FadeOut");
                    var rbStrobe = this.FindControl<CheckBox>("Effect_FadeStrobe");
                    var rbTravel     = this.FindControl<CheckBox>("Effect_Travel");
                    var rbCombine    = this.FindControl<CheckBox>("Effect_Combine");
                    var rbSeperate = this.FindControl<CheckBox>("Effect_Seperate");
                    var rbRepeat    = this.FindControl<CheckBox>("Effect_Repeat");
                    var colorChange = this.FindControl<CheckBox>("Effect_ChangeColor");
                    var rbTwinkle = this.FindControl<CheckBox>("Effect_Twinkle");


                    selectedBlock.BlockEffects = new List<LightBlock.Effect>();
                    if (rbIn?.IsChecked == true)
                        selectedBlock.BlockEffects.Add(LightBlock.Effect.FadeIn);
                    if (rbOut?.IsChecked == true)
                        selectedBlock.BlockEffects.Add(LightBlock.Effect.FadeOut);
                    if (rbStrobe?.IsChecked == true)
                        selectedBlock.BlockEffects.Add(LightBlock.Effect.Strobe); 
                    if(rbTravel?.IsChecked == true)
                        selectedBlock.BlockEffects.Add(LightBlock.Effect.Travel);
                    if (rbCombine?.IsChecked == true) 
                        selectedBlock.BlockEffects.Add(LightBlock.Effect.Combine);
                    if (rbSeperate?.IsChecked == true)
                        selectedBlock.BlockEffects.Add(LightBlock.Effect.Seperate);
                    if (rbRepeat?.IsChecked == true)
                        selectedBlock.BlockEffects.Add(LightBlock.Effect.Repeat);
                    if(colorChange.IsChecked ==true)
                        selectedBlock.BlockEffects.Add((LightBlock.Effect.ChangeColor));
                    if (rbTwinkle?.IsChecked == true)
                        selectedBlock.BlockEffects.Add(LightBlock.Effect.Twinkle);
                }
            }
     
            /// <summary>
            /// Visual indicator for selected lightblock
            /// </summary>
            public void UpdateSelectedColorsBackground()
            {
                foreach (var selectedBlock in _timeline._selectedBlocks)
                {
                    var color = selectedBlock.BlockColor;
                    var newcolor = new Color((byte)(color.A * 0.5), color.R, color.G, color.B);
                    selectedBlock.UpdateBackground(newcolor);
                }
            }

            /// <summary>
            /// Updates the visibility of the lightblock effect variables to ensure proper light effect combinations
            /// </summary>
            private void UpdateEffectSettingVisibility()
            {
                var travelEffectActive = Effect_Travel?.IsChecked == true;
                var combineEffectActive = Effect_Combine?.IsChecked == true;
                var seperateEffectActive = Effect_Seperate?.IsChecked == true;
                var repeatEffectActive = Effect_Repeat?.IsChecked == true;
                var changeColorActive = Effect_ChangeColor?.IsChecked == true;
                //No Positional Effects
                if (!(travelEffectActive || combineEffectActive || seperateEffectActive))
                {
                    //Deactivate Dual Text Boxes
                    AdditionalDualInputsPanel.IsVisible = false;
                    AdditionalDualInput1TextBox.Text = "";
                    AdditionalDualInput2TextBox.Text = "";
                    
                    //Deactivate Single Text Box 1
                    AdditionalSingleInputLabel1.Text = "";
                    AdditionalSingleInput1TextBox.Text = "";
                    AdditionalSingleInput1TextBox.IsVisible = false;

                }

                //Travel
                if (travelEffectActive)
                {
                    //Dual Input Boxes
                    AdditionalDualInputsPanel.IsVisible = true;
                    AdditionalDualInput1Label.Text = "Final Start Light (0-1000)";
                    AdditionalDualInput2Label.Text = "Final End Light (0-1000)";
                    
                    
                    //Deactivate Single Text Box 1
                    AdditionalSingleInputLabel1.Text = "";
                    AdditionalSingleInput1TextBox.Text = "";
                    AdditionalSingleInput1TextBox.IsVisible = false;

                }
                
                //Combine/Seperate
                if ((combineEffectActive || seperateEffectActive))
                {
                    //Activate Dual Boxes (DualInput 1 & 2)
                    AdditionalDualInputsPanel.IsVisible = true;
                    AdditionalDualInput1Label.Text = "Second Start Light (0-1000)";
                    AdditionalDualInput2Label.Text = "Second End Light (0-1000)";
                    
                    //Activate Width Box (Single Input 1)
                    AdditionalSingleInput1TextBox.IsVisible = true;
                    AdditionalSingleInputLabel1.Text = "Combined Width (0-1000)";

                }

                //Repeatable
                if (repeatEffectActive)
                {
                    AdditionalSingleInputLabel2.Text = "Repeat Number";
                    AdditionalSingleInput2TextBox.IsVisible = true;
                }
                else
                {
                    AdditionalSingleInputLabel2.Text = "";
                    AdditionalSingleInput2TextBox.Text = "";
                    AdditionalSingleInput2TextBox.IsVisible = false;
                }

                if (changeColorActive)
                {
                    
                }

            }
            public void RefreshPorts(object sender, RoutedEventArgs e)
            {
                var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();

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
                if (SerialHandler != null)
                {
                    SerialHandler.ClosePort();
                }
                ActiveLedCount = Int32.Parse(ActiveLightsTextBox.Text);
                HardwareCurrent = (int)(HardwareCurrentSlider.Value);
                BrightnessScale = HardwareCurrent / (ActiveLedCount * 0.06);
                SerialHandler = new SerialHandler(ActiveLedCount,
                    new SerialPort(PortComboBox.SelectedItem as string, 460800, Parity.None, 8, StopBits.One));
            }
            /// <summary>
            /// Changes app theme depending on current music provider source (Spotify green vs Luminote purple)
            /// </summary>
            private void ChangeAppTheme()
            {
                if (_musicProvider.providerName == "LocalFiles")
                {
                    ResumeTrackButton.Background = Brushes.BlueViolet;
                    PauseTrackButton.Background = Brushes.BlueViolet;
                    RestartTrackButton.Background = Brushes.BlueViolet;
                    NextTrackButton.Background = Brushes.BlueViolet;
                }

                if (_musicProvider.providerName == "Spotify")
                {
                    ResumeTrackButton.Background = new SolidColorBrush(_spotifyGreen, 1);
                    PauseTrackButton.Background = new SolidColorBrush(_spotifyGreen, 1);
                    RestartTrackButton.Background = new SolidColorBrush(_spotifyGreen, 1);
                    NextTrackButton.Background = new SolidColorBrush(_spotifyGreen, 1);
                }
                
            }


            private void HardwareSettingsOnClick(object? sender, RoutedEventArgs e)
            {
                RefreshPorts(null, null);
            }

            /// <summary>
            /// Select new lightmap track file from local files
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private async void BrowseAudioFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            {
                if (_musicProvider is MusicFileProvider musicFileProvider)
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
                _playbackHandler.SkipAsync();
                UpdateCurrentTrack(true, trackGUID: Guid.Empty); // refresh on next track
                
            }
            
            /// <summary>
            /// Refresh the list of local track files
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void RefreshLocalTracks_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            {
                _allLocalTrackItems = JsonDataHandler.GetAllTrackItems();
                LocalTracksListBox.ItemsSource = _allLocalTrackItems;
            }

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
                Console.WriteLine("LOOKING 4");

                var track = JsonDataHandler.GetTrack(_trackQueue.First());
                _musicProvider.currentlyPlayingPath = track.filePath;
                //Set visual information for track that is stored in json
                _musicProvider.currentTrack = new TrackPOCO(track.trackGUID, track._trackName, track.author, null);
                await _playbackHandler.SkipAsync();
                UpdateCurrentTrack(true, track.trackGUID); // refresh on next track

            }

            private async void LocalTrack_Upload_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            {
                if (sender is not Avalonia.Controls.Control c) return;
                if (c.DataContext is not TrackItemUI item) return;
                DatabaseAccess.SaveTrackAsync(item.TrackId.ToString(), JsonDataHandler.GetTrack(item.TrackId.ToString()));
                Console.WriteLine(JsonDataHandler.GetTrack(item.TrackId.ToString()).provider);

            }


            
            /// <summary>
            /// New Lightmap Button (Either prompts user for file or uses currentlyPLayign Track) 
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void OpenAudioFileButton_OnClick(object? sender, RoutedEventArgs e)
            {
                if (_musicProvider is MusicFileProvider musicFileProvider)
                {
                    if (sender is Button b && this.Resources["OpenAudioFlyout"] is Flyout f) 
                        f.ShowAt(b);
                }

                if (_musicProvider is SpotifyProvider spotifyProvider)
                {
                    Console.WriteLine("syncing spotify");
                    var path = spotifyProvider.GetCurrentlyPlayingTrackIdAsync();
                    _musicProvider.currentTrack = new TrackPOCO(Guid.Empty, "Unnamed Track", "Unnamed Artists", null);
                    _musicProvider.currentlyPlayingPath = path;
                    _playbackHandler.SkipAsync();
                    UpdateCurrentTrack(true, Guid.Empty); 
                }            
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
                
                _allLocalTrackItems = JsonDataHandler.GetAllTrackItems();
                LocalTracksListBox.ItemsSource = _allLocalTrackItems;
                
            }
            
            /// <summary>
            /// Refresh Database Track List
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private async void RefreshDatabaseTracks_Click(object? sender, RoutedEventArgs e)
            {
                _allDatabaseTrackItems = await DatabaseAccess.ListTracksAsync(_musicProvider.providerName, false);
                DatabaseTracksListBox.ItemsSource = _allDatabaseTrackItems;
                Console.WriteLine(_allDatabaseTrackItems.Count);
                Console.WriteLine("searching " + "|" + _allDatabaseTrackItems.First().TrackId + "|");
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
        }
    }
