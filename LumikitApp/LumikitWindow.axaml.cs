using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO.Ports;

//UpperCase = User defined global variables (settings, preferences etc..)
//camelCase = local temporary variables used in methods
//_thisCase = global variables needed on a class level
namespace LumikitApp
{
    public partial class LumikitWindow : Window
    {
        private static Color spotifyGreen = new Color(255, 30, 215, 96);
        private const int LedCount = 150;
        private const int ColorUpdateIntervalMs = 25;
        
        /// <summary> The music provider to handle audio playback (ex. Spotify, Local Files...) </summary>
        private IMusicProvider _musicProvider;
        private int _lastColorUpdateMs = 0;
        
        /// <summary> The serial output for lighting communications  </summary>
        private SerialHandler SerialHandler;
        private Point _lastPointerPos;
        
        //Variables for scaling playback length and visual size
        private const int _totalModules = 10000;
        private const double _msPerSlot = 50.0;
        private const double _modulesPerSecond = 1000.0 / _msPerSlot;
        private double _slotWidth = 3; // base zoom unit
        private double _minBlockWidth = 0.6; // allow 1/5th of base resolution
        private bool _scrollLock = true;

        /// <summary> User defined bpm variable to visualize bpm lines in playback. 0 == no value </summary>
        private double Bpm = 0;

        /// <summary> The current selected light block available for user editing</summary>
        private List<LightBlock>? _selectedBlocks = new List<LightBlock>();

        //Possible color blocks for lightshow editing, can be redefined by the user
        private readonly List<Color> BlockColors = new()
        {
            Colors.DarkRed, Colors.Red, Colors.Orange, Colors.Yellow, Colors.Green,Colors.Aqua, Colors.Blue,
            Colors.Purple, Colors.Magenta, Colors.White
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

        //Live "Piano Roll" Block Painting Variables
        private bool _isLiveInputActive = false;
        private LightBlock? _liveBlock = null;
        private int _liveStartMs = 0;
        Border? _activeSwatch;

        public LumikitWindow()
        {
            InitializeComponent();
            _timelineCanvas = this.FindControl<Canvas>("TimelineCanvas");
            _scrollViewer = this.FindControl<ScrollViewer>("TimelineScrollViewer");
            _bpmInput = this.FindControl<TextBox>("BpmInput");
            
            var zoomInBtn = this.FindControl<Button>("ZoomInButton");
            if (zoomInBtn != null)
                zoomInBtn.Click += (_, _) => Zoom(1.25);
            var zoomOutBtn = this.FindControl<Button>("ZoomOutButton");
            if (zoomOutBtn != null)
                zoomOutBtn.Click += (_, _) => Zoom(0.8);
            
            PointerMoved += OnPointerMoved;
            
            //Unlock playback viewer when interacted with
            _scrollViewer.PointerPressed += (_, _) => _scrollLock = false;
            
            _bpmInput.LostFocus += (_, _) =>
            {
                if (double.TryParse(_bpmInput.Text, out double _bpm) && _bpm > 0)
                {
                    Bpm = _bpm;
                    DrawBPMLines();
                }
            };

            this.KeyDown += (_, e) =>
            {
                if (e.Key == Key.RightShift)
                {
                    //Relock the scroll viewer
                    _scrollLock = true;
                    e.Handled = true;
                }
            };

            this.KeyDown += OnKeyDown;
            this.KeyUp += OnKeyUp;
            _scrollViewer.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);

//todo: dynamic serialport         

            SerialHandler = new SerialHandler(150,
                new SerialPort("/dev/cu.usbserial-0001", 921600, Parity.None, 8, StopBits.One));

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
            double oldWidth = _slotWidth;
            double newWidth = Math.Clamp(_slotWidth * factor, _minBlockWidth, 30.0);
            if (Math.Abs(newWidth - _slotWidth) < 0.0001) return;

            _slotWidth = newWidth;
            ScaleTimelineChildren(oldWidth, _slotWidth);
            _timelineCanvas.Width = _totalModules * _slotWidth;
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
        //Activate Repeat setting when checked
        private void Effect_Repeat_OnChecked(object? sender, RoutedEventArgs e)
        {
            UpdateEffectSettingVisibility();
        }
        
        /// Deactivate Settings When Unchecked
        private void Effect_Combine_OnUnchecked(object? sender, RoutedEventArgs e)
        {
            UpdateEffectSettingVisibility();
        }
        
        private void Effect_Travel_Unchecked(object? sender, RoutedEventArgs e)
        {
            UpdateEffectSettingVisibility();
        }
        
        private void Effect_Seperate_OnUnchecked(object? sender, RoutedEventArgs e)
        {
            UpdateEffectSettingVisibility();
        }

        private void Effect_Repeat_OnUnchecked(object? sender, RoutedEventArgs e)
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
            if (!_isLiveInputActive && e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                _isLiveInputActive = true;
                _liveStartMs = _playbackHandler?.CurrentProgressMs ?? 0; 
                double caretX = (_liveStartMs / _msPerSlot) * _slotWidth;

                // Create a new block at the caret
                var block = new LightBlock(LightBlocks, _scrollViewer, _slotWidth);
//TODO: make these colors bindable                
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

                block.Container.Width = _slotWidth;
                var snappedX =  Math.Round(caretX / _slotWidth) * _slotWidth;
                Canvas.SetLeft(block.Container, snappedX);
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
                        var selectedBlocksSnapshot = _selectedBlocks;
                        foreach (var selectedBlock in selectedBlocksSnapshot)
                        {
                            selectedBlock.isSelected = false;
                            _timelineCanvas.Children.Remove(selectedBlock.Container);
                            LightBlocks.Remove(selectedBlock);

                        }

                    }
                };
            }

            //Copy light blocks
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


                    var addedBlock = new LightBlock(blockToCopy);
                    addedBlock.UpdateColor(blockToCopy.BlockColor);

                    Canvas.SetLeft(addedBlock.Container,
                        _lastPointerPos.X + (Canvas.GetLeft(blockToCopy.Container) - pasteX));
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
                            var selectedBlocksSnapshot = _selectedBlocks;
                            foreach (var selectedBlock in selectedBlocksSnapshot)
                            {
                                selectedBlock.isSelected = false;
                                _timelineCanvas.Children.Remove(selectedBlock.Container);
                                LightBlocks.Remove(selectedBlock);

                            }
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
            BlockEditor.IsVisible = false;
            this.FindControl<Button>("SaveTrackDataButton").Click += async (_, _) =>
            {
                var track = await provider.GetCurrentlyPlayingTrackAsync();
                var trackData = new TrackData
                {
                    _trackID = track.trackId,
                    _BPM = double.Parse(_bpmInput.Text),
                    _lightBlocks = LightBlocks.Select(b => new LightBlockData
                    {
                        X = Canvas.GetLeft(b.Container) / _slotWidth,
                        Width = b.Container.Width / _slotWidth,
                        Color = (b.BlockColor).ToString(),
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

            _musicProvider = provider;
            ChangeAppTheme();

        }

        public async void UpdateCurrentTrack(bool startNewLightShow)
        {

            var track = await _musicProvider.GetCurrentlyPlayingTrackAsync();

            this.FindControl<TextBlock>("NowPlayingTrackText").Text = track.trackName;
            this.FindControl<TextBlock>("NowPlayingArtistText").Text = track.artistName;

            var albumImage = track.trackCoverImageUrl;
            await SetAlbumCover(albumImage);


            // Clear old blocks
            foreach (var block in LightBlocks) _timelineCanvas.Children.Remove(block.Container);
            LightBlocks.Clear();

            var trackDataLocal = JsonDataHandler.GetTrack(track.trackId);
            if (trackDataLocal != null) //track detected, filling in track data with track POCO
            {
                Bpm = trackDataLocal._BPM;
                _bpmInput.Text = Bpm.ToString();
                DrawTimelineSlots();

                foreach (var data in trackDataLocal._lightBlocks)
                {
                    if (!Color.TryParse(data.Color, out var color)) continue;
                    var block = new LightBlock(LightBlocks, _scrollViewer, _slotWidth);
                    block.UpdateColor(color);
                    block.StartLight = data.StartLight;
                    block.EndLight = data.EndLight;
                    block.BlockEffects = data.BlockEffects;
                    block.Intensity = data.LightIntensity;
                    block.SecondaryStartLight = data.SecondaryDualInput1;
                    block.SecondaryEndLight = data.SecondaryDualInput2;
                    block.AdditionalIndividualInput1 = data.SecondarySingleInput1;
                    block.AdditionalIndividualInput2 = data.SecondarySingleInput2;
                    block.Container.Width = data.Width * _slotWidth;
                    Canvas.SetLeft(block.Container, data.X * _slotWidth);
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
                            var selectedBlocksSnapshot = _selectedBlocks;
                            foreach (var selectedBlock in selectedBlocksSnapshot)
                            {
                                selectedBlock.isSelected = false;
                                _timelineCanvas.Children.Remove(selectedBlock.Container);
                                LightBlocks.Remove(selectedBlock);

                            }
                        }
                    };
                }
            }
            else
            {
                //track not detected
                Bpm = 0;
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

        DragDrop.SetAllowDrop(_timelineCanvas, true);
        _timelineCanvas.AddHandler(DragDrop.DropEvent, OnCanvasDrop, RoutingStrategies.Bubble);
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
                e.Handled = true;

                double old = _slotWidth;
                double mouseX = e.GetPosition(_scrollViewer).X;
                double worldX = _scrollViewer.Offset.X + mouseX;
                double focusModule = worldX / old;

                double step = e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1;
                double next = Math.Clamp(_slotWidth * step, 1.0, 30.0);
                if (Math.Abs(next - _slotWidth) < 0.0001) return;

                _slotWidth = next;
                ScaleTimelineChildren(old, _slotWidth);

                double newWorldX = focusModule * _slotWidth;
                double newOffsetX = Math.Max(0, newWorldX - mouseX);
                _scrollViewer.Offset = new Vector(newOffsetX, _scrollViewer.Offset.Y);
                return;
            }

            double delta = e.Delta.Y * -40;
            var currentOffset = _scrollViewer.Offset;
            double newX = Math.Max(0, currentOffset.X + delta);
            _scrollViewer.Offset = new Vector(newX, currentOffset.Y);
            _scrollLock = false;
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
                    Width = _slotWidth,
                    Height = 60,
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(0.5)
                };
                Canvas.SetLeft(slot, i * _slotWidth);
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
                    Canvas.SetLeft(label, i * _slotWidth - 5);
                    Canvas.SetTop(label, -15);
                    _timelineCanvas.Children.Add(label);

                    var caret = new TextBlock
                    {
                        Text = "^",
                        Foreground = Brushes.White,
                        FontSize = 10
                    };
                    Canvas.SetLeft(caret, i * _slotWidth - 2);
                    Canvas.SetTop(caret, 60);
                    _timelineCanvas.Children.Add(caret);
                }
            }

            DrawBPMLines();
            _timelineCanvas.Width = _totalModules * _slotWidth;

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
            double secondsPerBeat = Bpm > 0 ? 60.0 / Bpm : 0;
            double modulesPerBeat = secondsPerBeat * _modulesPerSecond;

            if (Bpm > 0 && modulesPerBeat > 0)
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

                    Canvas.SetLeft(line, i * _slotWidth);
                    Canvas.SetTop(line, -20); // push above block zone
                    _timelineCanvas.Children.Add(line);

                    bpmindicatorNumber++;
                }
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
            var pos = e.GetPosition(_timelineCanvas);
            double snappedX = Math.Round(pos.X / _slotWidth) * _slotWidth;
            snappedX = Math.Max(0, Math.Min(snappedX, _timelineCanvas.Width - _slotWidth));
            double maxWidth = Math.Min(_slotWidth * 50, _timelineCanvas.Width - snappedX);
            double finalWidth = maxWidth;

            while (finalWidth >= _slotWidth)
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
                finalWidth -= _slotWidth;
            }

            if (finalWidth < _slotWidth) return;

            var block = new LightBlock(LightBlocks, _scrollViewer, _slotWidth);
            block.Container.Width = finalWidth;
            Canvas.SetLeft(block.Container, snappedX);
            Canvas.SetTop(block.Container, 0);
            _timelineCanvas.Children.Add(block.Container);
            LightBlocks.Add(block);
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
            if (ms < _lastColorUpdateMs)
                _lastColorUpdateMs = ms;

            if (ms - _lastColorUpdateMs < ColorUpdateIntervalMs)
                return;

            _lastColorUpdateMs = ms;
            double slotIndex = ms / _msPerSlot;
            double caretX = slotIndex * _slotWidth;
            Canvas.SetLeft(_playheadCaret, caretX - 4);

            if (_scrollLock)
            {
                double viewportWidth = _scrollViewer.Viewport.Width;
                double scrollTo = Math.Max(0, caretX - viewportWidth / 6);
                _scrollViewer.Offset = new Vector(scrollTo, _scrollViewer.Offset.Y);
            }

            if (_isLiveInputActive && _liveBlock != null)
            {
                double startX = Canvas.GetLeft(_liveBlock.Container);
                var snappedWidth = Math.Round(((caretX - startX) / _slotWidth ) +1 )* _slotWidth;
                _liveBlock.Container.Width = Math.Max(_slotWidth, snappedWidth);
            }

            var activeBlock = LightBlocks.FirstOrDefault(b =>
            {
                double left = Canvas.GetLeft(b.Container);
                double width = b.Container.Width;
                return caretX >= left && caretX <= left + width;
            });

            if (activeBlock == null)
            {
                
                    SerialHandler.SendFrame(Array.Empty<Color>());

                
                TopColorBar.Background = new SolidColorBrush(Colors.Transparent);
                BottomColorBar.Background = new SolidColorBrush(Colors.Transparent);
                return;
            }

            TopColorBar.Background = new SolidColorBrush(activeBlock.BlockColor);
            BottomColorBar.Background = new SolidColorBrush(activeBlock.BlockColor);


            double width = activeBlock.Container.Width;
            if (width <= 0)
                return;

            double left = Canvas.GetLeft(activeBlock.Container);
            double relPos = Math.Clamp((caretX - left) / width, 0, 1);


            var stripColors = LightEffectsComputer.ComputeBlockEffects(activeBlock, relPos);

            UpdateColorBar(stripColors);

      
            try
            {
               SerialHandler.SendFrame(stripColors);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
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

            double fullWidth = _scrollViewer.Viewport.Width;
            if (fullWidth <= 0)
                fullWidth = _scrollViewer.Bounds.Width;

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
            
            if (_selectedBlocks == null) return;
            //Apply settings made in block editor to every selected block
            foreach (var selectedBlock in _selectedBlocks)  
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
            }
        }
 
        /// <summary>
        /// Visual indicator for selected lightblock
        /// </summary>
        public void UpdateSelectedColorsBackground()
        {
            foreach (var selectedBlock in _selectedBlocks)
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
                AdditionalSingleInput2TextBox.IsVisible = false;
                AdditionalDualInput2TextBox.Text = "";
            }
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
                ResumeTrackButton.Background = new SolidColorBrush(spotifyGreen, 1);
                PauseTrackButton.Background = new SolidColorBrush(spotifyGreen, 1);
                RestartTrackButton.Background = new SolidColorBrush(spotifyGreen, 1);
                NextTrackButton.Background = new SolidColorBrush(spotifyGreen, 1);
            }
            
        }
        
    }
}
