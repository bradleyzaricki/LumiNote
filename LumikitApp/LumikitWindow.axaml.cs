using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO.Ports;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Platform;
namespace LumikitApp
{
    public partial class LumikitWindow : Window
    {
        /// <summary>   </summary>
        /// <summary> The music provider to handle audio playback (ex. Spotify, Local Files...) </summary>
        private IMusicProvider musicProvider;

        /// <summary> The serial output for lighting communications  </summary>
        private SerialPort _serialPort;

        private Point _lastPointerPos;
        //Variables for scaling playback length and visual size
        private const int _totalModules = 10000;
        private const double _msPerSlot = 50.0;
        private const double _modulesPerSecond = 1000.0 / _msPerSlot;
        private double slotWidth = 3; // base zoom unit
        private double minBlockWidth = 0.6; // allow 1/5th of base resolution
        private bool scrollLock = true;

        /// <summary> User defined bpm variable to visualize bpm lines in playback. 0 == no value </summary>
        private double bpm = 0;

        /// <summary> The current selected light block available for user editing</summary>
        private List<LightBlock>? _selectedBlocks = new List<LightBlock>();

        //Possible color blocks for lightshow editing max 254 colors
        private readonly List<Color> BlockColors = new()
        {
            Colors.Red, Colors.Orange, Colors.Yellow, Colors.Green, Colors.Blue,
            Colors.Purple, Colors.Magenta, Colors.Aqua, Colors.Lime,
            Colors.HotPink, Colors.DarkRed, Colors.LightGreen, Colors.CornflowerBlue,
            Colors.White
        };

        //Avalonia UI elements
        private Canvas _timelineCanvas;
        private ScrollViewer _scrollViewer;
        private List<LightBlock> LightBlocks = new();
        private TextBlock _playheadCaret;
        private TextBox _bpmInput;
        private TextBox _lightIntensityTextBox;
        private TextBox _endLightTextbox;
        private TextBox _startLightInputBox;

        //Handles unique playback logic depending on the music provider
        private IPlaybackHandler _playbackHandler;

        //Live Piano Roll Variables
        private bool _isLiveInputActive = false;
        private LightBlock? _liveBlock = null;
        private int _liveStartMs = 0;

        public LumikitWindow()
        {
            InitializeComponent();

            _timelineCanvas = this.FindControl<Canvas>("TimelineCanvas");
            _scrollViewer = this.FindControl<ScrollViewer>("TimelineScrollViewer");
            _bpmInput = this.FindControl<TextBox>("BpmInput");
            //_bpmInput.Text = bpm.ToString();
            var zoomInBtn = this.FindControl<Button>("ZoomInButton");
            if (zoomInBtn != null)
                zoomInBtn.Click += (_, _) => Zoom(1.25);
            PointerMoved += OnPointerMoved;

            var zoomOutBtn = this.FindControl<Button>("ZoomOutButton");
            if (zoomOutBtn != null)
                zoomOutBtn.Click += (_, _) => Zoom(0.8);

            _bpmInput.LostFocus += (_, _) =>
            {
                if (double.TryParse(_bpmInput.Text, out double _bpm) && _bpm > 0)
                {
                    bpm = _bpm;
                    DrawBPMLines();
                }
            };

            this.KeyDown += (_, e) =>
            {
                if (e.Key == Key.CapsLock)
                {
                    scrollLock = true;
                    e.Handled = true;
                }
            };

            // === Hook keyboard events for live mode ===
            this.KeyDown += OnKeyDown;
            this.KeyUp += OnKeyUp;
            _scrollViewer.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
//TODO: Create dymamic SerialPort defined by user
            _serialPort = new SerialPort("/dev/cu.usbserial-0001", 115200, Parity.None, 8, StopBits.One);
            try
            {
                _serialPort.Open();
            }
            catch (Exception)
            {
                Console.WriteLine("Failed to open serial port, live serial output feature not available," +
                                  " please refer to Luminote's built in lighting for visual feedback");
            }

            _scrollViewer.PointerPressed += (_, _) => scrollLock = false;
            var applyBtn = this.FindControl<Button>("ApplyBlockChangesButton");
            if (applyBtn != null) applyBtn.Click += OnApplyBlockChangesClicked;


            InitializeColorPalette();
            DrawTimelineSlots();


        }

        /// <summary>
        /// Logic for + and - zoom buttons
        /// </summary>
        /// <param name="factor"></param>
        private void Zoom(double factor)
        {
            double oldWidth = slotWidth;
            double newWidth = Math.Clamp(slotWidth * factor, minBlockWidth, 30.0);
            if (Math.Abs(newWidth - slotWidth) < 0.0001) return;

            slotWidth = newWidth;
            ScaleTimelineChildren(oldWidth, slotWidth);
            _timelineCanvas.Width = _totalModules * slotWidth;
        }

        /// Activate Travel Settings When Toggled
        private void Effect_Travel_Checked(object? sender, RoutedEventArgs e)
        {
            UpdateEffectSettingVisibility();
        }

        /// Deactivate Travel Settings When Toggled
        private void Effect_Travel_Unchecked(object? sender, RoutedEventArgs e)
        {
            UpdateEffectSettingVisibility();
        }

        /// <summary>
        /// Live block editing to add lights like a piano roll
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            // Start a live block when hotkey (num 0-9) is pressed
            if (!_isLiveInputActive && (e.Key == Key.D1 || e.Key == Key.D2 || e.Key == Key.D3 || e.Key == Key.D4 ||
                                        e.Key == Key.D5 || e.Key == Key.D6 || e.Key == Key.D7 || e.Key == Key.D8 ||
                                        e.Key == Key.D9 || e.Key == Key.D0))
            {
                _isLiveInputActive = true;
                _liveStartMs =
                    _playbackHandler?.CurrentProgressMs ?? 0; // requires playback handler to expose current ms
                double caretX = (_liveStartMs / _msPerSlot) * slotWidth;

                // Create a new block at the caret
                var block = new LightBlock(LightBlocks, _scrollViewer, slotWidth);
//TODO: make these colors bindable                
                switch (e.Key)
                {
                    case Key.D1:
                        block.UpdateColor((Colors.Red));
                        break;
                    case Key.D2:
                        block.UpdateColor((Colors.Orange));
                        break;
                    case Key.D3:
                        block.UpdateColor((Colors.Yellow));
                        break;
                    case Key.D4:
                        block.UpdateColor((Colors.Green));
                        break;
                    case Key.D5:
                        block.UpdateColor((Colors.Blue));
                        break;
                    case Key.D6:
                        block.UpdateColor((Colors.Purple));
                        break;
                    case Key.D7:
                        block.UpdateColor((Colors.Magenta));
                        break;
                    case Key.D8:
                        block.UpdateColor((Colors.Aqua));
                        break;
                    case Key.D9:
                        block.UpdateColor((Colors.Lime));
                        break;
                    case Key.D0:
                        block.UpdateColor((Colors.White));
                        break;
                }

                block.Container.Width = slotWidth;
                Canvas.SetLeft(block.Container, caretX);
                Canvas.SetTop(block.Container, 0);
                _timelineCanvas.Children.Add(block.Container);
                LightBlocks.Add(block);
                _liveBlock = block;

                //Assign light block keybinds (Lclick edit, Rclick delete)
                block.Container.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(block.Container).Properties.IsLeftButtonPressed)
                    {
                        AddNewLightBlockToTimeline(e, block);
                    }

                    if (e.GetCurrentPoint(block.Container).Properties.IsRightButtonPressed)
                    {
                        _timelineCanvas.Children.Remove(block.Container);
                        LightBlocks.Remove(block);
                        e.Handled = true;
                        
                    }
                };
            }

            if (!_isLiveInputActive && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V)
            {
                var selectedBlocksSnapshot = _selectedBlocks;
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


                    var addedBlock = new LightBlock(LightBlocks, _scrollViewer, slotWidth);
                    addedBlock.UpdateColor(blockToCopy.BlockColor);
                    addedBlock.StartLight = blockToCopy.StartLight;
                    addedBlock.EndLight = blockToCopy.EndLight;
                    addedBlock.BlockEffects = blockToCopy.BlockEffects;
                    addedBlock.Intensity = blockToCopy.Intensity;
                    addedBlock.DeltaStartLight = blockToCopy.DeltaStartLight;
                    addedBlock.DeltaEndLight = blockToCopy.DeltaEndLight;
                    addedBlock.Container.Width = blockToCopy.Container.Width;
                    Canvas.SetLeft(addedBlock.Container, _lastPointerPos.X + (Canvas.GetLeft(blockToCopy.Container)-pasteX));
                    Canvas.SetTop(addedBlock.Container, 0);
                    
                    _timelineCanvas.Children.Add(addedBlock.Container);
                    LightBlocks.Add(addedBlock);
                    addedBlock.Container.PointerPressed += (_, e) =>
                    {
                        if (e.GetCurrentPoint(addedBlock.Container).Properties.IsLeftButtonPressed)
                        {
                            AddNewLightBlockToTimeline(e, addedBlock);
                        }

                        if (e.GetCurrentPoint(addedBlock.Container).Properties.IsRightButtonPressed)
                        {
                            _timelineCanvas.Children.Remove(addedBlock.Container);
                            LightBlocks.Remove(addedBlock);
                            e.Handled = true;
                        
                        }
                    };
                }
            }
        }
        
        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            _lastPointerPos = e.GetPosition(_timelineCanvas);
        }
        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            // Finish the live block when key is released
            if (_isLiveInputActive && e.Key == Key.D1 || e.Key == Key.D2 || e.Key == Key.D3 || e.Key == Key.D4 ||
                e.Key == Key.D5 || e.Key == Key.D6 || e.Key == Key.D7 || e.Key == Key.D8 || e.Key == Key.D9 ||
                e.Key == Key.D0)
            {
                _isLiveInputActive = false;
                _liveBlock = null;
            }
        }

        public void InitializeWindow(IMusicProvider provider)
        {
            this.FindControl<Button>("SaveTrackDataButton").Click += async (_, _) =>
            {
                var track = await provider.GetCurrentlyPlayingTrackAsync();
                var trackData = new TrackData
                {
                    _trackID = track.trackId,
                    _BPM = double.Parse(_bpmInput.Text),
                    _lightBlocks = LightBlocks.Select(b => new LightBlockData
                    {
                        X = Canvas.GetLeft(b.Container) / slotWidth,
                        Width = b.Container.Width / slotWidth,
                        Color = (b.BlockColor).ToString(),
                        StartLight = b.StartLight,
                        EndLight = b.EndLight,
                        DeltaEndLight = b.DeltaEndLight,
                        DeltaStartLight = b.DeltaStartLight,
                        BlockEffects = b.BlockEffects,
                        LightIntensity = b.Intensity
                    }).ToList()
                };
                JsonDataHandler.SaveTrack(trackData);
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
                await _playbackHandler.SkipAsync();
                UpdateCurrentTrack(true); // refresh on next track
            };
            this.FindControl<Button>("RestartTrackButton").Click += async (_, _) => _playbackHandler.RestartAsync();

            musicProvider = provider;
        }

        public async void UpdateCurrentTrack(bool startNewLightShow)
        {
            var track = await musicProvider.GetCurrentlyPlayingTrackAsync();

            this.FindControl<TextBlock>("NowPlayingText").Text = track.trackName;
            var albumImage = track.trackCoverImageUrl;
            await SetAlbumCover(albumImage);


            // Clear old blocks
            foreach (var block in LightBlocks) _timelineCanvas.Children.Remove(block.Container);
            LightBlocks.Clear();

            var trackDataLocal = JsonDataHandler.GetTrack(track.trackId);
            if (trackDataLocal != null) //track detected, filling in track data with track POCO
            {
                bpm = trackDataLocal._BPM;
                _bpmInput.Text = bpm.ToString();
                DrawTimelineSlots();

                foreach (var data in trackDataLocal._lightBlocks)
                {
                    if (!Color.TryParse(data.Color, out var color)) continue;
                    var block = new LightBlock(LightBlocks, _scrollViewer, slotWidth);
                    block.UpdateColor(color);
                    block.StartLight = data.StartLight;
                    block.EndLight = data.EndLight;
                    block.BlockEffects = data.BlockEffects;
                    block.Intensity = data.LightIntensity;
                    block.DeltaStartLight = data.DeltaStartLight;
                    block.DeltaEndLight = data.DeltaEndLight;
                    block.Container.Width = data.Width * slotWidth;
                    Canvas.SetLeft(block.Container, data.X * slotWidth);
                    Canvas.SetTop(block.Container, 0);

                    _timelineCanvas.Children.Add(block.Container);
                    LightBlocks.Add(block);

                    //Assign light block keybinds (Lclick edit, Rclick delete)
                    block.Container.PointerPressed += (_, e) =>
                    {
                        if (e.GetCurrentPoint(block.Container).Properties.IsLeftButtonPressed)
                        {
                            AddNewLightBlockToTimeline(e, block);
                        }

                        if (e.GetCurrentPoint(block.Container).Properties.IsRightButtonPressed)
                        {
                            _timelineCanvas.Children.Remove(block.Container);
                            LightBlocks.Remove(block);
                            e.Handled = true;
                        }
                    };
                }
            }
            else
            {
                //track not detected
                bpm = 0;
                _bpmInput.Text = "0";
                DrawTimelineSlots();
            }
        }

        private void AddNewLightBlockToTimeline(PointerPressedEventArgs e, LightBlock blockToAdd)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                var min = double.MaxValue;
                var max = -1.00;
                foreach (var selectedblock in _selectedBlocks)
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
                    _selectedBlocks.Add(blockToAdd);
                    blockToAdd.isSelected = true;
                    LoadBlockIntoEditor(_selectedBlocks);
                    return;

                }

                foreach (var lightblock in LightBlocks)
                {
                    if (_selectedBlocks.Contains(lightblock))
                        continue;

                    var blockLeft = Canvas.GetLeft(lightblock.Container);
                    if ((blockLeft > min && blockLeft <= Canvas.GetLeft(blockToAdd.Container))
                        || (blockLeft < max && blockLeft >= Canvas.GetLeft(blockToAdd.Container)))
                    {
                        //Add all selected blocks including block that was shift clicked
                        _selectedBlocks.Add(lightblock);
                        lightblock.isSelected = true;
                        blockToAdd.isSelected = true;

                        LoadBlockIntoEditor(_selectedBlocks);

                    }
                }

            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                _selectedBlocks.Add(blockToAdd);
                blockToAdd.isSelected = true;

                LoadBlockIntoEditor(_selectedBlocks);

            }

            else
            {
                foreach (var blockToRemove in _selectedBlocks)
                {
                    blockToRemove.UpdateBackground(blockToRemove.BlockColor);
                    blockToRemove.isSelected = false;

                }

                _selectedBlocks.Clear();
                _selectedBlocks.Add(blockToAdd);
                blockToAdd.isSelected = true;
                
                LoadBlockIntoEditor(_selectedBlocks);

            }

            e.Handled = true;
        }

    

        /// <summary>
        /// Loads sidebar editor with pre-existing lightblock values
        /// </summary>
        /// <param name="block"></param>
        private void LoadBlockIntoEditor(List<LightBlock> selectedBlocks)
        {
            UpdateSelectedColors();
            UpdateEffectSettingVisibility();
            foreach (var block in selectedBlocks)
            {
                StartLightInput.Text = block.StartLight.ToString();
                EndLightInput.Text = block.EndLight.ToString();
                IntensityInput.Text = block.Intensity.ToString();
                TravelEndLightInput.Text = block.DeltaEndLight.ToString();
                TravelStartLightInput.Text =  block.DeltaStartLight.ToString();
                
                // Reset effect selection
                this.FindControl<CheckBox>("Effect_None").IsChecked =
                    block.BlockEffects.Contains(LightBlock.Effect.None);
                this.FindControl<CheckBox>("Effect_FadeIn").IsChecked =
                    block.BlockEffects.Contains(LightBlock.Effect.FadeIn);
                this.FindControl<CheckBox>("Effect_FadeOut").IsChecked =
                    block.BlockEffects.Contains(LightBlock.Effect.FadeOut);
                this.FindControl<CheckBox>("Effect_FadeStrobe").IsChecked =
                    block.BlockEffects.Contains(LightBlock.Effect.Strobe);
                this.FindControl<CheckBox>("Effect_Travel").IsChecked =
                    block.BlockEffects.Contains(LightBlock.Effect.Travel);

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
        /// Create color pallet and dragndrop functionality via avalonia swatches
        /// </summary>
        private void InitializeColorPalette()
        {
            var palette = this.FindControl<WrapPanel>("ColorPalette");
            palette.Children.Clear();

            for (int i = 0; i < BlockColors.Count; i++)
            {
                var color = BlockColors[i]; // capture early

                // Create base swatch
                var baseSwatch = new Border
                {
                    Width = 30,
                    Height = 30,
                    Background = new SolidColorBrush(color),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(2),
                    Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand)
                };

                Control finalSwatch = baseSwatch;

                // Add number overlay for first 9 colors
                if (i < 9)
                {
                    var label = new TextBlock
                    {
                        Text = (i + 1).ToString(),
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
                        Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand)
                    };
                }

                // Attach drag handler
                finalSwatch.PointerPressed += (_, e) =>
                {
                    var data = new DataObject();
                    Console.WriteLine(color.ToString());
                    data.Set("block-color", color.ToString());
                    DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
                };

                palette.Children.Add(finalSwatch);
            }

            DragDrop.SetAllowDrop(_timelineCanvas, true);
            _timelineCanvas.AddHandler(DragDrop.DropEvent, OnCanvasDrop, RoutingStrategies.Bubble);
        }

        private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                e.Handled = true;

                double old = slotWidth;
                double mouseX = e.GetPosition(_scrollViewer).X;
                double worldX = _scrollViewer.Offset.X + mouseX;
                double focusModule = worldX / old;

                double step = e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1;
                double next = Math.Clamp(slotWidth * step, 1.0, 30.0);
                if (Math.Abs(next - slotWidth) < 0.0001) return;

                slotWidth = next;
                ScaleTimelineChildren(old, slotWidth);

                double newWorldX = focusModule * slotWidth;
                double newOffsetX = Math.Max(0, newWorldX - mouseX);
                _scrollViewer.Offset = new Vector(newOffsetX, _scrollViewer.Offset.Y);
                return;
            }

            double delta = e.Delta.Y * -40;
            var currentOffset = _scrollViewer.Offset;
            double newX = Math.Max(0, currentOffset.X + delta);
            _scrollViewer.Offset = new Vector(newX, currentOffset.Y);
            scrollLock = false;
            e.Handled = true;
        }

        private void ScaleTimelineChildren(double oldSlotWidth, double newSlotWidth)
        {
            double factor = newSlotWidth / oldSlotWidth;

            foreach (var child in _timelineCanvas.Children)
            {
                if (ReferenceEquals(child, _playheadCaret)) continue;

                double left = Canvas.GetLeft((Control)child);
                if (!double.IsNaN(left))
                    Canvas.SetLeft((Control)child, left * factor);

                if (child is Border b && !double.IsNaN(b.Width) && b.Width > 0)
                    b.Width *= factor;

                if (child is TextBlock tb && tb.Text == "^")
                {
                    double caretLeft = Canvas.GetLeft(tb);
                    Canvas.SetLeft(tb, caretLeft);
                }
            }

            _timelineCanvas.Width = _totalModules * newSlotWidth;
        }

        /// <summary>
        /// Draw timeline slots based on global scaling data
        /// </summary>
        private void DrawTimelineSlots()
        {
            _timelineCanvas.Children.Clear();

            for (int i = 0; i < _totalModules; i++)
            {
                var slot = new Border
                {
                    Width = slotWidth,
                    Height = 60,
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(0.5)
                };
                Canvas.SetLeft(slot, i * slotWidth);
                Canvas.SetTop(slot, 0);
                _timelineCanvas.Children.Add(slot);

                if (i % 40 == 0)
                {
                    double seconds = (i / _modulesPerSecond);
                    var label = new TextBlock
                    {
                        Text = $"{seconds:0.0}s",
                        Foreground = Brushes.White,
                        FontSize = 10
                    };
                    Canvas.SetLeft(label, i * slotWidth - 5);
                    Canvas.SetTop(label, -15);
                    _timelineCanvas.Children.Add(label);

                    var caret = new TextBlock
                    {
                        Text = "^",
                        Foreground = Brushes.White,
                        FontSize = 10
                    };
                    Canvas.SetLeft(caret, i * slotWidth - 2);
                    Canvas.SetTop(caret, 60);
                    _timelineCanvas.Children.Add(caret);
                }
            }
            
            DrawBPMLines();
            _timelineCanvas.Width = _totalModules * slotWidth;

            _playheadCaret = new TextBlock
            {
                Text = "▲",
                Foreground = Brushes.Red,
                FontSize = 14
            };
            Canvas.SetLeft(_playheadCaret, 0);
            Canvas.SetTop(_playheadCaret, 72);
            _timelineCanvas.Children.Add(_playheadCaret);
        }

        /// <summary>
        /// Draw BPM lines based on global bpm data
        /// </summary>
        private void DrawBPMLines()
        {
            double secondsPerBeat = bpm > 0 ? 60.0 / bpm : 0;
            double modulesPerBeat = secondsPerBeat * _modulesPerSecond;

            if (bpm > 0 && modulesPerBeat > 0)
            {
                var bpmindicatorNumber = 0;
                for (double i = 0; i < _totalModules; i += modulesPerBeat)
                {
                    var color = Brushes.Brown;
                    if (bpmindicatorNumber % 4 == 0)
                        color = Brushes.Red;

                    var line = new Border
                    {
                        Width = 1,
                        Height = 60, // shorter tick mark
                        Background = color
                    };

                    Canvas.SetLeft(line, i * slotWidth);
                    Canvas.SetTop(line, -20); // push above block zone
                    _timelineCanvas.Children.Add(line);

                    bpmindicatorNumber++;
                }
            }
        }
        private void OnCanvasDrop(object? sender, DragEventArgs e)
        {
            if (!e.Data.Contains("block-color")) return;

            var colorString = e.Data.Get("block-color")?.ToString();
            if (colorString == null || !Color.TryParse(colorString, out var color)) return;

            var pos = e.GetPosition(_timelineCanvas);
            double snappedX = Math.Round(pos.X / slotWidth) * slotWidth;
            snappedX = Math.Max(0, Math.Min(snappedX, _timelineCanvas.Width - slotWidth));
            double maxWidth = Math.Min(slotWidth * 50, _timelineCanvas.Width - snappedX);
            double finalWidth = maxWidth;

            while (finalWidth >= slotWidth)
            {
                bool collision = false;
                foreach (var existing in LightBlocks)
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
                finalWidth -= slotWidth;
            }

            if (finalWidth < slotWidth) return;

            var block = new LightBlock(LightBlocks, _scrollViewer, slotWidth);
            block.Container.Width = finalWidth;
            Canvas.SetLeft(block.Container, snappedX);
            Canvas.SetTop(block.Container, 0);
            _timelineCanvas.Children.Add(block.Container);
            LightBlocks.Add(block);
            block.UpdateColor(color);
            block.Intensity = 255;
            block.EndLight = 100;
            //Assign light block keybinds (Lclick edit, Rclick delete)
            block.Container.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(block.Container).Properties.IsLeftButtonPressed)
                {
                    AddNewLightBlockToTimeline(e, block);
                }

                if (e.GetCurrentPoint(block.Container).Properties.IsRightButtonPressed)
                {
                    var selectedBlocksSnapshot = _selectedBlocks;
                    foreach (var selectedBlock in selectedBlocksSnapshot)
                    {
                        selectedBlock.isSelected = false;
                        _timelineCanvas.Children.Remove(selectedBlock.Container);
                        LightBlocks.Remove(selectedBlock); 
                        
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
            double slotIndex = ms / _msPerSlot;
            double caretX = slotIndex * slotWidth;
            Canvas.SetLeft(_playheadCaret, caretX - 4);

            if (scrollLock)
            {
                double viewportWidth = _scrollViewer.Viewport.Width;
                double scrollTo = Math.Max(0, caretX - viewportWidth / 6);
                _scrollViewer.Offset = new Vector(scrollTo, _scrollViewer.Offset.Y);
            }

            // === Expand live block if active ===
            if (_isLiveInputActive && _liveBlock != null)
            {
                double startX = Canvas.GetLeft(_liveBlock.Container);
                _liveBlock.Container.Width = Math.Max(slotWidth, caretX - startX);
            }

            // === existing active block effect logic ===
            var activeBlock = LightBlocks.FirstOrDefault(b =>
            {
                double left = Canvas.GetLeft(b.Container);
                double width = b.Container.Width;
                return caretX >= left && caretX <= left + width;
            });

            if (activeBlock == null)
            {
                TopColorBar.Background = new SolidColorBrush(Colors.Transparent);
                return;
            }

            TopColorBar.Background = new SolidColorBrush(activeBlock.BlockColor);


            double width = activeBlock.Container.Width;
            if (width <= 0)
                return;

            double left = Canvas.GetLeft(activeBlock.Container);
            double relPos = Math.Clamp((caretX - left) / width, 0, 1);

            int intensity = ComputeIntensity(activeBlock, relPos, slotIndex);

            var (startLight, endLight) = ComputeTravelRange(activeBlock, relPos);

            int start = (int)Math.Clamp(startLight, 0, 255);
            int end = (int)Math.Clamp(endLight, 0, 255);
            SendPacket(start, end, activeBlock.BlockColor, intensity);
            UpdateColorBar(start, end, activeBlock.BlockColor, intensity);
        }

        private void SendPacket(int startLight, int endLight, Color color, int intensity)
        {
            var packet = new byte[4];
            packet[0] = (byte)startLight;
            packet[1] = (byte)endLight;
            packet[2] = MapColorToByte(color);
            packet[3] = (byte)Math.Clamp(intensity, 0, 255);

            if (_serialPort is { IsOpen: true })
                _serialPort.Write(packet, 0, packet.Length);
        }

        private void UpdateColorBar(int startLight, int endLight, Color color, int intensity)
        {
            double totalLights = 100.0;

            double startPct = Math.Clamp(startLight / totalLights, 0, 1);
            double endPct = Math.Clamp(endLight / totalLights, startPct, 1);

            double fullWidth = _scrollViewer.Viewport.Width;
            if (fullWidth <= 0)
                fullWidth = _scrollViewer.Bounds.Width;

            double l = startPct * fullWidth;
            double w = Math.Max(1, (endPct - startPct) * fullWidth);

            TopColorBar.HorizontalAlignment = HorizontalAlignment.Left;
            TopColorBar.Margin = new Thickness(l, 0, 0, 0);
            TopColorBar.Width = w;
            TopColorBar.Background = new SolidColorBrush(color);
            TopColorBar.Opacity = intensity / 255.0;
        }
        private (double start, double end) ComputeTravelRange(LightBlock block, double relPos)
        {
            bool hasTravel = block.BlockEffects != null &&
                             block.BlockEffects.Contains(LightBlock.Effect.Travel);

            if (!hasTravel)
                return (block.StartLight, block.EndLight);

            double s0 = block.StartLight;
            double e0 = block.EndLight;

            double s1 = block.DeltaStartLight; // interpreted as final start position
            double e1 = block.DeltaEndLight;   // interpreted as final end position

            double start = s0 + (s1 - s0) * relPos;
            double end = e0 + (e1 - e0) * relPos;

            if (end < start)
            {
                double t = start;
                start = end;
                end = t;
            }

            return (start, end);
        }
    private int ComputeIntensity(LightBlock block, double relPos, double slotIndex)
    {
        int intensity = Math.Clamp(block.Intensity, 0, 255);

        foreach (var effect in block.BlockEffects)
        {
            switch (effect)
            {
                case LightBlock.Effect.FadeIn:
                    if (relPos <= 0.5)
                        intensity = (int)(block.Intensity * (relPos / 0.5));
                    break;

                case LightBlock.Effect.FadeOut:
                    if (relPos >= 0.5)
                        intensity = (int)(block.Intensity * ((1.0 - relPos) / 0.5));
                    break;

                case LightBlock.Effect.Strobe:
                    if (((int)slotIndex %2) != 0)
                        intensity = 0;
                    break;
            }
        }

        return Math.Clamp(intensity, 0, 255);
    }

        private void OnApplyBlockChangesClicked(object? s, RoutedEventArgs e)
        {
        
            if (_selectedBlocks == null) return;
            foreach (var _selectedBlock in _selectedBlocks)  
            {
            // Start/End lights
            if (int.TryParse(this.FindControl<TextBox>("StartLightInput").Text, out int start))
                _selectedBlock.StartLight = start;

            if (int.TryParse(this.FindControl<TextBox>("EndLightInput").Text, out int end))
                _selectedBlock.EndLight = end;

            // Intensity
            if (int.TryParse(this.FindControl<TextBox>("IntensityInput").Text, out int intensity))
                _selectedBlock.Intensity = Math.Clamp(intensity, 0, 255);

            //OPTIONAL Travel Light
            if (int.TryParse(this.FindControl<TextBox>("TravelStartLightInput").Text, out int travelStart))
                _selectedBlock.DeltaStartLight = travelStart;
            
            if (int.TryParse(this.FindControl<TextBox>("TravelEndLightInput").Text, out int travelEnd))
                _selectedBlock.DeltaEndLight = travelEnd;
            
            // Effect radio buttons
            var rbNone   = this.FindControl<CheckBox>("Effect_None");
            var rbIn     = this.FindControl<CheckBox>("Effect_FadeIn");
            var rbOut    = this.FindControl<CheckBox>("Effect_FadeOut");
            var rbStrobe = this.FindControl<CheckBox>("Effect_FadeStrobe");
            var rbTravel     = this.FindControl<CheckBox>("Effect_Travel");
            var rbCombine    = this.FindControl<CheckBox>("Effect_Combine");
            var rbBuild = this.FindControl<CheckBox>("Effect_Build");
            _selectedBlock.BlockEffects = new List<LightBlock.Effect>();
            if (rbIn?.IsChecked == true)
                _selectedBlock.BlockEffects.Add(LightBlock.Effect.FadeIn);
            if (rbOut?.IsChecked == true)
                _selectedBlock.BlockEffects.Add(LightBlock.Effect.FadeOut);
            if (rbStrobe?.IsChecked == true)
                _selectedBlock.BlockEffects.Add(LightBlock.Effect.Strobe); 
            if(rbTravel?.IsChecked == true)
                _selectedBlock.BlockEffects.Add(LightBlock.Effect.Travel);
            if (rbCombine?.IsChecked == true)
                _selectedBlock.BlockEffects.Add(LightBlock.Effect.Combine);
            else
                _selectedBlock.BlockEffects.Add(LightBlock.Effect.None); 
            }
    
        }

        private byte MapColorToByte(Color color)
        {
            if (color == Colors.Red) return 0;
            if (color == Colors.Orange) return 1;
            if (color == Colors.Yellow) return 2;
            if (color == Colors.Green) return 3;
            if (color == Colors.Blue) return 4;
            if (color == Colors.Purple) return 5;
            if (color == Colors.Magenta) return 6;
            if (color == Colors.Aqua) return 7;
            if (color == Colors.Lime) return 8;
            if (color == Colors.HotPink) return 9;
            if (color == Colors.DarkRed) return 10;
            if (color == Colors.LightGreen) return 11;
            if (color == Colors.CornflowerBlue) return 12;
            if (color == Colors.White) return 13;
            return 255; // Unknown
        }

        public void UpdateSelectedColors()
        {
            foreach (var selectedBlock in _selectedBlocks)
            {
                var color = selectedBlock.BlockColor;
                var newcolor = new Color(100, color.R, color.G, color.B);
                selectedBlock.UpdateBackground(newcolor);
            }

        }

        private void UpdateEffectSettingVisibility()
        {
            var isOn = Effect_Travel?.IsChecked == true;
            TravelInputsPanel.IsVisible = isOn;
            if (!isOn)
            {
                TravelStartLightInput.Text = "";
                TravelEndLightInput.Text = "";

            }
        }

    }
}
