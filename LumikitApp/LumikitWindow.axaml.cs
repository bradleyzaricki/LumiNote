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
using System.Net.Http;
using System.Threading.Tasks;

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

        private readonly List<Color> BlockColors = new()
        {
            Colors.Red, Colors.Green, Colors.Blue, Colors.Yellow,
            Colors.Cyan, Colors.Magenta, Colors.Orange, Colors.Purple,
            Colors.Teal, Colors.Lime, Colors.Pink, Colors.Brown
        };

        private Canvas _timelineCanvas;
        private ScrollViewer _scrollViewer;
        private List<ResizableLightBlock> LightBlocks = new();
        private double slotWidth = 6;

        public LumikitWindow()
        {
            InitializeComponent();
            _timelineCanvas = this.FindControl<Canvas>("TimelineCanvas");
            _scrollViewer = this.FindControl<ScrollViewer>("TimelineScrollViewer");
            InitializeColorPalette();
            DrawTimelineSlots();
        }

        public void InitializeWindow(SpotifyProvider provider, SpotifyClient client)
        {
            this.FindControl<Button>("PauseTrackButton").Click += async (_, _) =>
            {
                StopPlaybackTimer();
                await _spotifyProvider.PausePlayback();
            };
            this.FindControl<Button>("ResumeTrackButton").Click += async (_, _) =>
            {
                StopPlaybackTimer();
                var progressBefore = _spotifyProvider.GetPlaybackProgressMs();
                await _spotifyProvider.ResumePlayback();
                stopwatch.Restart();
                progressAtStart = progressBefore;
                StartPlaybackTimer();
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
                UpdateTrackText(startNewLightShow: true);
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
                    Dispatcher.UIThread.Post(() => StopwatchLabel.Text = playbackTimeInMs.ToString());
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

        public async void UpdateTrackText(bool startNewLightShow)
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

        private void DrawTimelineSlots()
        {
            for (int i = 0; i < 2000; i++)
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
            }
            _timelineCanvas.Width = 2000 * slotWidth;
        }

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

                    foreach (var existing in LightBlocks)
                    {
                        double left = Canvas.GetLeft(existing.Container);
                        double width = existing.Container.Width;
                        if (snappedX < left + width && snappedX + slotWidth > left)
                        {
                            return;
                        }
                    }

                    var block = new ResizableLightBlock(color, LightBlocks, _scrollViewer, slotWidth);
                    Canvas.SetLeft(block.Container, snappedX);
                    Canvas.SetTop(block.Container, 0);
                    _timelineCanvas.Children.Add(block.Container);
                    LightBlocks.Add(block);
                }
            }
        }
    }
}




namespace LumikitApp
{
    public class ResizableLightBlock
    {
        public Border Container { get; }
        private Point dragStartCanvas;
        private double originalLeft;
        private double originalWidth;
        private bool isResizingLeft;
        private bool isResizingRight;
        private bool isMoving;
        private List<ResizableLightBlock> _siblings;
        private ScrollViewer _scrollViewer;
        private double _slotWidth;

        public ResizableLightBlock(Color color, List<ResizableLightBlock> siblings, ScrollViewer scrollViewer, double slotWidth)
        {
            _siblings = siblings;
            _scrollViewer = scrollViewer;
            _slotWidth = slotWidth;
            Container = new Border
            {
                Width = slotWidth,
                Height = 60,
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(0.01f)
            };

            var grid = new Grid();
            var leftHandle = new Border
            {
                Width = 0.05,
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = new Avalonia.Input. Cursor(StandardCursorType.SizeWestEast)
            };
            var rightHandle = new Border
            {
                Width = 0.05,
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = new Avalonia.Input.Cursor(StandardCursorType.SizeWestEast)
            };
            grid.Children.Add(leftHandle);
            grid.Children.Add(rightHandle);
            Container.Child = grid;

            Container.PointerPressed += OnPointerPressed;
            Container.PointerMoved += OnPointerMoved;
            Container.PointerReleased += OnPointerReleased;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var canvas = (Canvas?)Container.Parent;
            if (canvas == null) return;

            dragStartCanvas = e.GetPosition(canvas);
            originalLeft = Canvas.GetLeft(Container);
            originalWidth = Container.Width;

            var local = e.GetPosition(Container);
            isResizingLeft = local.X < 10;
            isResizingRight = local.X > Container.Width - 10;
            isMoving = !isResizingLeft && !isResizingRight;
        }

        private bool Collides(double newLeft, double width)
        {
            foreach (var block in _siblings)
            {
                if (block.Container == Container) continue;
                double left = Canvas.GetLeft(block.Container);
                double right = left + block.Container.Width;
                double thisRight = newLeft + width;
                if (newLeft < right && thisRight > left)
                    return true;
            }
            return false;
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            var canvas = (Canvas?)Container.Parent;
            if (canvas == null || !e.GetCurrentPoint(Container).Properties.IsLeftButtonPressed) return;

            var current = e.GetPosition(canvas);
            double canvasWidth = canvas.Bounds.Width;

            void ScrollIfNeeded(double edge)
            {
                double scrollOffset = _scrollViewer.Offset.X;
                double viewportWidth = _scrollViewer.Viewport.Width;
                if (edge > scrollOffset + viewportWidth - 30)
                {
                    _scrollViewer.Offset = new Vector(scrollOffset + 15, 0);
                }
                else if (edge < scrollOffset + 30)
                {
                    _scrollViewer.Offset = new Vector(Math.Max(scrollOffset - 15, 0), 0);
                }
            }

            if (isResizingLeft)
            {
                double newLeft = originalLeft + (current.X - dragStartCanvas.X);
                double snappedLeft = Math.Round(newLeft / _slotWidth) * _slotWidth;
                double delta = originalLeft - snappedLeft;
                double newWidth = originalWidth + delta;

                if (newWidth >= _slotWidth && snappedLeft >= 0 && !Collides(snappedLeft, newWidth))
                {
                    Canvas.SetLeft(Container, snappedLeft);
                    Container.Width = newWidth;
                    ScrollIfNeeded(snappedLeft);
                }
            }
            else if (isResizingRight)
            {
                double newWidth = originalWidth + (current.X - dragStartCanvas.X);
                double snappedWidth = Math.Round(newWidth / _slotWidth) * _slotWidth;
                double rightEdge = originalLeft + snappedWidth;
                if (snappedWidth >= _slotWidth && rightEdge <= canvasWidth && !Collides(originalLeft, snappedWidth))
                {
                    Container.Width = snappedWidth;
                    ScrollIfNeeded(rightEdge);
                }
            }
            else if (isMoving)
            {
                double newLeft = originalLeft + (current.X - dragStartCanvas.X);
                double snappedLeft = Math.Round(newLeft / _slotWidth) * _slotWidth;
                if (snappedLeft >= 0 && snappedLeft + Container.Width <= canvasWidth && !Collides(snappedLeft, Container.Width))
                {
                    Canvas.SetLeft(Container, snappedLeft);
                    ScrollIfNeeded(snappedLeft + Container.Width);
                }
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            isResizingLeft = false;
            isResizingRight = false;
            isMoving = false;
        }
    }
}

