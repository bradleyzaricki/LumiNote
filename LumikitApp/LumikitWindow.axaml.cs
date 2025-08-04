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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace LumikitApp
{
    public partial class LumikitWindow : Window
    {
        private SpotifyClient _spotify;
        private SpotifyProvider _spotifyProvider;
        private Stopwatch stopwatch = new Stopwatch();
        private int progressAtStart = 0;
        private bool playbackTimerRunning = false;
        private int playbackTimeInMs;
        private bool userScrolled = false;

        private readonly List<Color> BlockColors = new()
        {
            Colors.Red, Colors.Green, Colors.Blue, Colors.Yellow,
            Colors.Cyan, Colors.Magenta, Colors.Orange, Colors.Purple,
            Colors.Teal, Colors.Lime, Colors.Pink, Colors.Brown
        };

        private Canvas _timelineCanvas;
        private ScrollViewer _scrollViewer;
        private List<LightBlock> LightBlocks = new();
        private double slotWidth = 3;
        private TextBlock _playheadCaret;
        private double _bpm = 0;
        private TextBox _bpmInput;
        private TrackData _trackDataLocal;
        public LumikitWindow()
        {
            InitializeComponent();
            _timelineCanvas = this.FindControl<Canvas>("TimelineCanvas");
            _scrollViewer = this.FindControl<ScrollViewer>("TimelineScrollViewer");
            _scrollViewer.PointerPressed += (_, _) => userScrolled = true;
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

        public void InitializeWindow(SpotifyProvider provider, SpotifyClient client)
        {

            this.FindControl<Button>("SaveTrackDataButton").Click += async (_, _) =>
            {
                var track = await provider.GetCurrentlyPlayingTrack();
                var trackData = new TrackData();
                trackData._trackID = track.Id;
                trackData._BPM = double.Parse(BpmInput.Text);
                trackData._lightBlocks = LightBlocks.Select(b => new LightBlockData
                {
                    X = Canvas.GetLeft(b.Container),
                    Width = b.Container.Width,
                    Color = ((SolidColorBrush)b.Container.Background).Color.ToString()
                }).ToList();
                JsonDataHandler.SaveTrack(trackData);
            };
            this.FindControl<Button>("PauseTrackButton").Click += async (_, _) =>
            {
                try
                {
                    Debug.WriteLine("Pause button clicked");
                    StopPlaybackTimer();
                    await _spotifyProvider.PausePlayback();
                    Debug.WriteLine("Pause successful");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Pause failed: " + ex.Message);
                    Debug.WriteLine("StackTrace: " + ex.StackTrace);
                }
            };

            this.FindControl<Button>("ResumeTrackButton").Click += async (_, _) =>
            {
                try
                {
                    StopPlaybackTimer();
                    var progressBefore = _spotifyProvider.GetPlaybackProgressMs();
                    await _spotifyProvider.ResumePlayback();
                    stopwatch.Restart();
                    progressAtStart = progressBefore;
                    StartPlaybackTimer();
                }
                catch
                {

                }

            };
            this.FindControl<Button>("NextTrackButton").Click += async (_, _) =>
            {
                StopPlaybackTimer();
                await _spotifyProvider.SkipTrack();
                int progress = 0;
                for (int i = 0; i < 50; i++)
                {
                    await Task.Delay(5);
                    progress = _spotifyProvider.GetPlaybackProgressMs();
                    if (progress > 0)
                        break;
                }
                progressAtStart = progress;
                stopwatch.Restart();
                StartPlaybackTimer();
                UpdateCurrentTrack(startNewLightShow: true);
            };
            _spotifyProvider = provider;
            _spotify = client;
        }

        public void StartPlaybackTimer()
        {
            if (playbackTimerRunning) return;
            playbackTimerRunning = true;
            _ = Task.Run(async () =>
            {
                while (playbackTimerRunning)
                {
                    await Task.Delay(10);
                    playbackTimeInMs = progressAtStart + (int)stopwatch.ElapsedMilliseconds;
                    Dispatcher.UIThread.Post(() =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            StopwatchLabel.Text = playbackTimeInMs.ToString();

                            double msPerSlot = 50;
                            double slotIndex = playbackTimeInMs / msPerSlot;
                            double caretX = slotIndex * slotWidth;

                            Canvas.SetLeft(_playheadCaret, caretX - 4);
                            if (!userScrolled)
                            {
                                double viewportWidth = _scrollViewer.Viewport.Width;
                                double scrollTo = caretX - viewportWidth / 6;
                                scrollTo = Math.Max(0, scrollTo);
                                _scrollViewer.Offset = new Vector(scrollTo, _scrollViewer.Offset.Y);
                            }
                            var caretMs = playbackTimeInMs;

                            // Find the block under the caret
                            var activeBlock = LightBlocks.FirstOrDefault(b =>
                            {
                                double left = Canvas.GetLeft(b.Container);
                                double width = b.Container.Width;
                                return caretX >= left && caretX <= left + width;
                            });

                            // Update color bars
                            var topBar = this.FindControl<Border>("TopColorBar");
                            var bottomBar = this.FindControl<Border>("BottomColorBar");

                            if (activeBlock != null && activeBlock.Container.Background is SolidColorBrush brush)
                            {
                                topBar.Background = new SolidColorBrush(brush.Color);
                                bottomBar.Background = new SolidColorBrush(brush.Color);
                            }
                            else
                            {
                                topBar.Background = Brushes.Gray;
                                bottomBar.Background = Brushes.Gray;
                            }
                        });
                    });
                }
            });
        }

        public void StopPlaybackTimer()
        {
            playbackTimerRunning = false;
            stopwatch.Stop();
            playbackTimeInMs = _spotifyProvider.GetPlaybackProgressMs();
            StopwatchLabel.Text = playbackTimeInMs.ToString();
        }
        /// <summary>
        /// Updates current track visual and playback settings
        /// </summary>
        /// <param name="startNewLightShow"></param>
        public async void UpdateCurrentTrack(bool startNewLightShow)
        {

            var track = await _spotifyProvider.GetCurrentlyPlayingTrack();
            var trackText = this.FindControl<TextBlock>("NowPlayingText");
            trackText.Text = track.Name;

            var albumImages = track.Album.Images;
            if (albumImages != null && albumImages.Count > 0)
            {
                var imageUrl = albumImages[1].Url;
                await SetAlbumCover(imageUrl);
            }
            foreach (var block in LightBlocks)
            {
                _timelineCanvas.Children.Remove(block.Container);
            }
            LightBlocks.Clear();
            _trackDataLocal = JsonDataHandler.GetTrack(track.Id);
            if( _trackDataLocal != null )
            {
                _bpm = _trackDataLocal._BPM;
                _bpmInput.Text = _bpm.ToString();
                DrawTimelineSlots();

                foreach (var data in _trackDataLocal._lightBlocks)
                {
                    if (!Color.TryParse(data.Color, out var color)) continue;

                    var block = new LightBlock(color, LightBlocks, _scrollViewer, slotWidth);
                    block.Container.Width = data.Width;
                    Canvas.SetLeft(block.Container, data.X);
                    Canvas.SetTop(block.Container, 0);
                    _timelineCanvas.Children.Add(block.Container);
                    LightBlocks.Add(block);

                    block.Container.PointerPressed += (_, e) =>
                    {
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
        /// Initialize the pallet of draggable RGB color swatches and create listener for dropping color swatches
        /// </summary>
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
            // Reverse direction if needed
            double delta = e.Delta.Y * -40;

            // Apply horizontal scroll
            var currentOffset = _scrollViewer.Offset;
            double newX = Math.Max(0, currentOffset.X + delta);
            _scrollViewer.Offset = new Vector(newX, currentOffset.Y);
            userScrolled = true;
            // Prevent default vertical scrolling
            e.Handled = true;
        }

        /// <summary>
        /// Creates playback visualizer with BPM and Current playback indicators
        /// </summary>
        /// 
        private void DrawTimelineSlots()
        {
            _timelineCanvas.Children.Clear();
            int totalSlots = 10000;
            for (int i = 0; i < totalSlots; i++)
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

                if (i % 40 == 0)//every 2 seconds create seconds label (40 * 5ms = 2s)
                {
                    double seconds = (i / 20);
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

            double secondsPerBeat = 60.0 / _bpm;
            double modulesPerSecond = 20;
            double modulesPerBeat = modulesPerSecond * secondsPerBeat;

            //Bpm lines
            for (double i = 0; i < totalSlots; i += modulesPerBeat)
            {
                var line = new Border
                {
                    Width = 1,
                    Height = 60,
                    Background = Brushes.Red
                };
                Canvas.SetLeft(line, i * slotWidth);
                Canvas.SetTop(line, 0);
                _timelineCanvas.Children.Add(line);
            }

            _timelineCanvas.Width = totalSlots * slotWidth;

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
        /// Handles color block being dropped into the playback. Adds light block both visually and it the backend list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnCanvasDrop(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains("block-color"))
            {
                var colorString = e.Data.Get("block-color")?.ToString();
                if (colorString != null && Color.TryParse(colorString, out var color))
                {
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

                        if (!collision)
                            break;

                        finalWidth -= slotWidth;
                    }

                    if (finalWidth < slotWidth)
                        return;

                    var block = new LightBlock(color, LightBlocks, _scrollViewer, slotWidth);
                    block.Container.Width = finalWidth;

                    Canvas.SetLeft(block.Container, snappedX);
                    Canvas.SetTop(block.Container, 0);
                    _timelineCanvas.Children.Add(block.Container);
                    LightBlocks.Add(block);
                    block.Container.PointerPressed += (_, e) =>
                    {
                        if (e.GetCurrentPoint(block.Container).Properties.IsRightButtonPressed)
                        {
                            _timelineCanvas.Children.Remove(block.Container);
                            LightBlocks.Remove(block);
                            e.Handled = true;
                        }
                    };
                }
            }
        }
    }
}






