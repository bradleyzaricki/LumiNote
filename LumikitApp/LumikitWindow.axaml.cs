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

namespace LumikitApp
{
    public partial class LumikitWindow : Window
    {
        private SpotifyProvider _spotifyProvider;
        private bool scrollLock = true;
        private double _slotWidth = 3;

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
            Colors.Red, Colors.Green, Colors.Blue, Colors.Yellow,
            Colors.Cyan, Colors.Magenta, Colors.Orange, Colors.Purple,
            Colors.Teal, Colors.Lime, Colors.Pink, Colors.Brown
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

        public LumikitWindow()
        {
            InitializeComponent();
            _timelineCanvas = this.FindControl<Canvas>("TimelineCanvas");
            _scrollViewer = this.FindControl<ScrollViewer>("TimelineScrollViewer");
            _startLightInputBox = this.FindControl<TextBox>("StartLightInput");
            _endLightTextbox = this.FindControl<TextBox>("EndLightInput");
            _lightIntensityTextBox = this.FindControl<TextBox>("IntensityInput");
            _scrollViewer.PointerPressed += (_, _) => scrollLock = false;
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
                        X = Canvas.GetLeft(b.Container),
                        Width = b.Container.Width,
                        Color = ((SolidColorBrush)b.Container.Background).Color.ToString(),
                        StartLight = b.StartLight,
                        EndLight = b.EndLight,
                        BlockEffect = b.BlockEffect,
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
            this.FindControl<Button>("ApplyBlockChangesButton").Click += (_, _) =>
            {
                if (_selectedBlock == null) return;

                if (int.TryParse(this.FindControl<TextBox>("StartLightInput").Text, out int start))
                    _selectedBlock.StartLight = start;

                if (int.TryParse(this.FindControl<TextBox>("EndLightInput").Text, out int end))
                    _selectedBlock.EndLight = end;

                if (int.TryParse(this.FindControl<TextBox>("IntensityInput").Text, out int intensity))
                    _selectedBlock.Intensity = Math.Clamp(intensity, 0, 255);

                // Effect radio buttons
                if (this.FindControl<RadioButton>("Effect_FadeIn").IsChecked == true)
                    _selectedBlock.BlockEffect = LightBlock.Effect.FadeIn;
                else if (this.FindControl<RadioButton>("Effect_FadeOut").IsChecked == true)
                    _selectedBlock.BlockEffect = LightBlock.Effect.FadeOut;
                else if (this.FindControl<RadioButton>("Effect_FadeStrobe").IsChecked == true)
                    _selectedBlock.BlockEffect = LightBlock.Effect.Strobe;
                else
                    _selectedBlock.BlockEffect = LightBlock.Effect.None;
            };

            this.FindControl<Button>("PauseTrackButton").Click += async (_, _) => await _playbackHandler.PauseAsync();
            this.FindControl<Button>("ResumeTrackButton").Click += async (_, _) => await _playbackHandler.ResumeAsync();
            this.FindControl<Button>("NextTrackButton").Click += async (_, _) =>
            {
                await _playbackHandler.SkipAsync();
                UpdateCurrentTrack(true); // << refresh on next track
            };
            _spotifyProvider = provider;
        }

        /// <summary>
        /// Retrieve and set track info locally for editing and viewing
        /// </summary>
        /// <param name="startNewLightShow"></param>
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

            //Start playback editor setup
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
                    var block = new LightBlock(color, LightBlocks, _scrollViewer, _slotWidth);
                    block.StartLight = data.StartLight;
                    block.EndLight = data.EndLight;
                    block.BlockEffect = data.BlockEffect;
                    block.Intensity = data.LightIntensity;
                    block.Container.Width = data.Width;
                    Canvas.SetLeft(block.Container, data.X);
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
            this.FindControl<RadioButton>("Effect_None").IsChecked = block.BlockEffect == LightBlock.Effect.None;
            this.FindControl<RadioButton>("Effect_FadeIn").IsChecked = block.BlockEffect == LightBlock.Effect.FadeIn;
            this.FindControl<RadioButton>("Effect_FadeOut").IsChecked = block.BlockEffect == LightBlock.Effect.FadeOut;
            this.FindControl<RadioButton>("Effect_FadeStrobe").IsChecked =
                block.BlockEffect == LightBlock.Effect.Strobe;


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
            foreach (var color in BlockColors)
            {
                var swatch = new Border
                {
                    Width = 30,
                    Height = 30,
                    Background = new SolidColorBrush(color),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(2),
                    Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand)
                };
                swatch.PointerPressed += (_, e) =>
                {
                    var data = new DataObject();
                    data.Set("block-color", color.ToString());
                    DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
                };
                palette.Children.Add(swatch);
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
                        Height = 60,
                        Background = color


                    };
                    Canvas.SetLeft(line, i * _slotWidth);
                    Canvas.SetTop(line, 0);
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

            var block = new LightBlock(color, LightBlocks, _scrollViewer, _slotWidth);
            block.Container.Width = finalWidth;

            Canvas.SetLeft(block.Container, snappedX);
            Canvas.SetTop(block.Container, 0);
            _timelineCanvas.Children.Add(block.Container);
            LightBlocks.Add(block);
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

            var activeBlock = LightBlocks.FirstOrDefault(b =>
            {
                double left = Canvas.GetLeft(b.Container);
                double width = b.Container.Width;
                return caretX >= left && caretX <= left + width;
            });

            var topBar = this.FindControl<Border>("TopColorBar");
            var bottomBar = this.FindControl<Border>("BottomColorBar");

            if (activeBlock?.Container.Background is SolidColorBrush brush)
            {
                // Calculate effect-modified color first
                Color displayColor = brush.Color;
                double left = Canvas.GetLeft(activeBlock.Container);
                double width = activeBlock.Container.Width;
                double relPos = Math.Clamp((caretX - left) / width, 0, 1);

                switch (activeBlock.BlockEffect)
                {
                    case LightBlock.Effect.Strobe:
                        if (((int)slotIndex % 2) != 0)
                            displayColor = Colors.Gray;
                        break;

                    case LightBlock.Effect.FadeIn:
                        int fadeInValue = (int)(activeBlock.Intensity + relPos * (255 - activeBlock.Intensity));
                        displayColor = Color.FromArgb(
                            255,
                            (byte)(brush.Color.R * fadeInValue / 255),
                            (byte)(brush.Color.G * fadeInValue / 255),
                            (byte)(brush.Color.B * fadeInValue / 255));
                        break;

                    case LightBlock.Effect.FadeOut:
                        int fadeOutValue = (int)(255 - relPos * (255 - activeBlock.Intensity));
                        displayColor = Color.FromArgb(
                            255,
                            (byte)(brush.Color.R * fadeOutValue / 255),
                            (byte)(brush.Color.G * fadeOutValue / 255),
                            (byte)(brush.Color.B * fadeOutValue / 255));
                        break;
                }

                // Map StartLight/EndLight to percentage of the bar
                double totalLights = 100.0; // change if your max LED count is different
                double startPercent = activeBlock.StartLight / totalLights;
                double endPercent = activeBlock.EndLight / totalLights;

                var brushFill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop(Colors.Gray, 0),
                        new GradientStop(Colors.Gray, startPercent),
                        new GradientStop(displayColor, startPercent),
                        new GradientStop(displayColor, endPercent),
                        new GradientStop(Colors.Gray, endPercent),
                        new GradientStop(Colors.Gray, 1)
                    }
                };

                topBar.Background = brushFill;
                bottomBar.Background = brushFill;
            }
            else
            {
                topBar.Background = Brushes.Gray;
                bottomBar.Background = Brushes.Gray;
            }
        }
    }
}
