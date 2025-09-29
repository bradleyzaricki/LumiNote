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

namespace LumikitApp
{
    public partial class LumikitWindow : Window
    {
        private SpotifyProvider _spotifyProvider;
        private bool scrollLock = true;
        private double _slotWidth = 3;       // base zoom unit
        private double _minBlockWidth = 0.6; // allow 1/5th of base resolution

        private SerialPort _serialPort;

        //Scaling variables for playback
        private const int _totalModules = 10000;
        private const double _msPerSlot = 50.0;
        private const double _modulesPerSecond = 1000.0 / _msPerSlot;

        //bpm variable for better editing
        private double _bpm = 0;

        private LightBlock? _selectedBlock = null;

        //Possible color blocks for lightshow editing
        private readonly List<Color> BlockColors = new()
        {
            Colors.Red, Colors.Orange, Colors.Yellow, Colors.Green, Colors.Blue,
            Colors.Purple, Colors.Magenta, Colors.Aqua, Colors.Lime,
            Colors.HotPink, Colors.DarkRed, Colors.LightGreen, Colors.CornflowerBlue
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

        //POCO object to parse stored json data into
        private TrackData _trackDataLocal;

        //Handles unique playback logic
        private IPlaybackHandler _playbackHandler;

        // === Live Piano Roll Fields ===
        private bool _isLiveInputActive = false;
        private LightBlock? _liveBlock = null;
        private int _liveStartMs = 0;

        public LumikitWindow()
        {
            InitializeComponent();
            _serialPort = new SerialPort("/dev/cu.usbserial-0001", 115200, Parity.None, 8, StopBits.One);
            try
            {
                _serialPort.Open();
            }
            catch (Exception)
            {
                Console.WriteLine("Failed to open serial port, live serial output feature not available");
            }
            _timelineCanvas = this.FindControl<Canvas>("TimelineCanvas");
            _scrollViewer = this.FindControl<ScrollViewer>("TimelineScrollViewer");
            
            _startLightInputBox = this.FindControl<TextBox>("StartLightInput");
            _endLightTextbox = this.FindControl<TextBox>("EndLightInput");
            _lightIntensityTextBox = this.FindControl<TextBox>("IntensityInput");
            
            _scrollViewer.PointerPressed += (_, _) => scrollLock = false;
            this.KeyDown += (_, e) =>
            {
                if (e.Key == Key.LeftShift)
                {
                    scrollLock = true;
                    e.Handled = true; // optional, stops focus from jumping
                }
            };
            _scrollViewer.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);

            _bpmInput = this.FindControl<TextBox>("BpmInput");
            _bpmInput.Text = _bpm.ToString();
            _bpmInput.LostFocus += (_, _) =>
            {
                if (double.TryParse(_bpmInput.Text, out double bpm) && bpm > 0)
                {
                    _bpm = bpm;
                    DrawTimelineSlots();
                }
            };

            InitializeColorPalette();
            DrawTimelineSlots();

            // === Hook keyboard events for live mode ===
            this.KeyDown += OnKeyDown;
            this.KeyUp += OnKeyUp;

            // === Hook zoom buttons ===
            var zoomInBtn = this.FindControl<Button>("ZoomInButton");
            if (zoomInBtn != null)
                zoomInBtn.Click += (_, _) => Zoom(1.25);

            var zoomOutBtn = this.FindControl<Button>("ZoomOutButton");
            if (zoomOutBtn != null)
                zoomOutBtn.Click += (_, _) => Zoom(0.8);
        }

        private void Zoom(double factor)
        {
            double oldWidth = _slotWidth;
            double newWidth = Math.Clamp(_slotWidth * factor, _minBlockWidth, 30.0);
            if (Math.Abs(newWidth - _slotWidth) < 0.0001) return;

            _slotWidth = newWidth;
            ScaleTimelineChildren(oldWidth, _slotWidth);
            _timelineCanvas.Width = _totalModules * _slotWidth;
        }



        /// <summary>
        /// Live block editing to add lights like a piano roll
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            // Start a live block when 1 is pressed
            if (!_isLiveInputActive && (e.Key == Key.D1 ||  e.Key == Key.D2 ||   e.Key == Key.D3 || e.Key == Key.D4 || e.Key == Key.D5  || e.Key == Key.D6 || e.Key == Key.D7  || e.Key == Key.D8 || e.Key == Key.D9))
            {
                _isLiveInputActive = true;
                _liveStartMs = _playbackHandler?.CurrentProgressMs ?? 0; // requires playback handler to expose current ms
                double caretX = (_liveStartMs / _msPerSlot) * _slotWidth;

                // Create a new block at the caret
                var block = new LightBlock(LightBlocks, _scrollViewer, _slotWidth); 
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
                    
                }
                block.StartLight = 0;
                block.EndLight = 100;
                block.Intensity = 255;
                block.Container.Width = _slotWidth;
                Canvas.SetLeft(block.Container, caretX);
                Canvas.SetTop(block.Container, 0);

                _timelineCanvas.Children.Add(block.Container);
                LightBlocks.Add(block);

                _liveBlock = block;
                block.Container.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(block.Container).Properties.IsLeftButtonPressed)
                    {
                        LoadBlockIntoEditor(block);
                        e.Handled = true;
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

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            // Finish the live block when key is released
            if (_isLiveInputActive && e.Key == Key.D1 || e.Key == Key.D2 ||   e.Key == Key.D3 || e.Key == Key.D4 || e.Key == Key.D5  || e.Key == Key.D6 || e.Key == Key.D7  || e.Key == Key.D8 || e.Key == Key.D9)
            {
                _isLiveInputActive = false;
                _liveBlock = null;
            }
        }

public void InitializeWindow(SpotifyProvider provider)
{
    this.FindControl<Button>("SaveTrackDataButton").Click += async (_, _) =>
    {
        var track = await provider.GetCurrentlyPlayingTrackAsync();
        var trackData = new TrackData
        {
            _trackID = track.Id,
            _BPM = double.Parse(_bpmInput.Text),
            _lightBlocks = LightBlocks.Select(b => new LightBlockData
            {
                // ✅ Save in "module units" (time slots) instead of pixels
                X = Canvas.GetLeft(b.Container) / _slotWidth,
                Width = b.Container.Width / _slotWidth,
                Color = ((SolidColorBrush)b.Container.Background).Color.ToString(),
                StartLight = b.StartLight,
                EndLight = b.EndLight,
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

    _spotifyProvider = provider;
}

public async void UpdateCurrentTrack(bool startNewLightShow)
{
    var track = await _spotifyProvider.GetCurrentlyPlayingTrackAsync();

    this.FindControl<TextBlock>("NowPlayingText").Text = track.Name;
    var albumImages = track.Album.Images;
    if (albumImages != null && albumImages.Count > 0)
    {
        var imageUrl = albumImages[1].Url;
        await SetAlbumCover(imageUrl);
    }

    // Clear old blocks
    foreach (var block in LightBlocks) _timelineCanvas.Children.Remove(block.Container);
    LightBlocks.Clear();

    _trackDataLocal = JsonDataHandler.GetTrack(track.Id);
    if (_trackDataLocal != null)
    {
        _bpm = _trackDataLocal._BPM;
        _bpmInput.Text = _bpm.ToString();
        DrawTimelineSlots();

        foreach (var data in _trackDataLocal._lightBlocks)
        {
            if (!Color.TryParse(data.Color, out var color)) continue;
            var block = new LightBlock(LightBlocks, _scrollViewer, _slotWidth);
            block.UpdateColor(color);
            block.StartLight = data.StartLight;
            block.EndLight = data.EndLight;
            block.BlockEffects = data.BlockEffects;
            block.Intensity = data.LightIntensity;

            // ✅ Restore with zoom applied
            block.Container.Width = data.Width * _slotWidth;
            Canvas.SetLeft(block.Container, data.X * _slotWidth);
            Canvas.SetTop(block.Container, 0);

            _timelineCanvas.Children.Add(block.Container);
            LightBlocks.Add(block);

            block.Container.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(block.Container).Properties.IsLeftButtonPressed)
                {
                    LoadBlockIntoEditor(block);
                    e.Handled = true;
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
        _bpm = 0;
        _bpmInput.Text = "0";
        DrawTimelineSlots();
    }
}


        private void LoadBlockIntoEditor(LightBlock block)
        {
            _selectedBlock = block;
            _startLightInputBox.Text = block.StartLight.ToString();
            _endLightTextbox.Text = block.EndLight.ToString();
            _lightIntensityTextBox.Text = block.Intensity.ToString();
                
            // Reset effect selection
            this.FindControl<RadioButton>("Effect_None").IsChecked =
                block.BlockEffects.Contains(LightBlock.Effect.None);
            this.FindControl<RadioButton>("Effect_FadeIn").IsChecked =
                block.BlockEffects.Contains(LightBlock.Effect.FadeIn);
            this.FindControl<RadioButton>("Effect_FadeOut").IsChecked =
                block.BlockEffects.Contains(LightBlock.Effect.FadeOut);
            this.FindControl<RadioButton>("Effect_FadeStrobe").IsChecked =
                block.BlockEffects.Contains(LightBlock.Effect.Strobe);
        
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

            double secondsPerBeat = _bpm > 0 ? 60.0 / _bpm : 0;
            double modulesPerBeat = secondsPerBeat * _modulesPerSecond;

            if (_bpm > 0 && modulesPerBeat > 0)
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

        private void OnCanvasDrop(object? sender, DragEventArgs e)
        {
            if (!e.Data.Contains("block-color")) return;

            var colorString = e.Data.Get("block-color")?.ToString();
            if (colorString == null || !Color.TryParse(colorString, out var color)) return;

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
            block.EndLight = 100;
            block.Container.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(block.Container).Properties.IsLeftButtonPressed)
                {
                    LoadBlockIntoEditor(block); // works here
                    e.Handled = true;
                }

                if (e.GetCurrentPoint(block.Container).Properties.IsRightButtonPressed)
                {
                    _timelineCanvas.Children.Remove(block.Container);
                    LightBlocks.Remove(block);
                    e.Handled = true;
                }
            };

        }


        private void UpdateCaretAndScroll(int ms)
        {
            double slotIndex = ms / _msPerSlot;
            double caretX = slotIndex * _slotWidth;
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
                _liveBlock.Container.Width = Math.Max(_slotWidth, caretX - startX);
            }

            // === existing active block effect logic ===
            var activeBlock = LightBlocks.FirstOrDefault(b =>
            {
                double left = Canvas.GetLeft(b.Container);
                double width = b.Container.Width;
                return caretX >= left && caretX <= left + width;
            });

            if (activeBlock?.Container.Background is SolidColorBrush brush)
            {
                double left = Canvas.GetLeft(activeBlock.Container);
                double width = activeBlock.Container.Width;
                double relPos = Math.Clamp((caretX - left) / width, 0, 1);

                int intensity = activeBlock.Intensity;

                foreach (var blockEffect in activeBlock.BlockEffects)
                {


                    if (blockEffect == LightBlock.Effect.FadeIn)
                    {
                        intensity = (int)(activeBlock.Intensity + relPos * (255 - activeBlock.Intensity));
                        break;
                    }

                    if (blockEffect == LightBlock.Effect.FadeOut)
                    {
                        intensity = (int)(255 - relPos * (255 - activeBlock.Intensity));
                        break;
                    }
                    if (blockEffect == LightBlock.Effect.Strobe)
                    {
                        if (((int)slotIndex % 2) != 0)
                            intensity = 0;
                        Console.WriteLine("strobe");

                        break;
                    }
                }
                byte colorIndex = MapColorToByte(brush.Color);

                byte[] packet = new byte[4];
                packet[0] = (byte)Math.Clamp(activeBlock.StartLight, 0, 255);
                packet[1] = (byte)Math.Clamp(activeBlock.EndLight, 0, 255);
                packet[2] = colorIndex;
                packet[3] = (byte)Math.Clamp(intensity, 0, 255);

                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Write(packet, 0, packet.Length);
                }
                Color displayColor = brush.Color;

// Map StartLight/EndLight (0–100) to actual bar placement
                double total = 100.0;
                double startPct = activeBlock.StartLight / total;
                double endPct   = activeBlock.EndLight   / total;

// Figure out available width of the container
                double fullWidth = _scrollViewer.Viewport.Width;
                if (fullWidth <= 0)
                    fullWidth = _scrollViewer.Bounds.Width;

// Convert percentages to pixel positions
                double l = startPct * fullWidth;
                double w = Math.Max(1, (endPct - startPct) * fullWidth);

// Apply to top/bottom bars
                TopColorBar.HorizontalAlignment = HorizontalAlignment.Left;
                TopColorBar.Margin = new Thickness(l, 0, 0, 0);
                TopColorBar.Width = w;
                TopColorBar.Background = new SolidColorBrush(displayColor);
                TopColorBar.Opacity= intensity/255.0;
                

            }
            else
            {
                TopColorBar.Background = new SolidColorBrush(Colors.Transparent);
            }
            this.FindControl<Button>("ApplyBlockChangesButton").Click += (_, _) =>
            {
                if (_selectedBlock == null) return;

                // Start/End lights
                if (int.TryParse(this.FindControl<TextBox>("StartLightInput").Text, out int start))
                    _selectedBlock.StartLight = start;

                if (int.TryParse(this.FindControl<TextBox>("EndLightInput").Text, out int end))
                    _selectedBlock.EndLight = end;

                // Intensity
                if (int.TryParse(this.FindControl<TextBox>("IntensityInput").Text, out int intensity))
                    _selectedBlock.Intensity = Math.Clamp(intensity, 0, 255);

                // Effect radio buttons
                var rbNone   = this.FindControl<RadioButton>("Effect_None");
                var rbIn     = this.FindControl<RadioButton>("Effect_FadeIn");
                var rbOut    = this.FindControl<RadioButton>("Effect_FadeOut");
                var rbStrobe = this.FindControl<RadioButton>("Effect_FadeStrobe");
                _selectedBlock.BlockEffects = new List<LightBlock.Effect>();
                if (rbIn?.IsChecked == true)
                    _selectedBlock.BlockEffects.Add(LightBlock.Effect.FadeIn);
                
                if (rbOut?.IsChecked == true)
                    _selectedBlock.BlockEffects.Add(LightBlock.Effect.FadeOut);
                if (rbStrobe?.IsChecked == true)
                    _selectedBlock.BlockEffects.Add(LightBlock.Effect.Strobe); 
                else
                    _selectedBlock.BlockEffects.Add(LightBlock.Effect.None); 
            };

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

            return 255; // Unknown
        }


    }
}
