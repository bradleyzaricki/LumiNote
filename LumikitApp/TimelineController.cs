namespace LumikitApp;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
public class TimelineController
{
    //Variables for scaling playback length and visual size
    private const int _totalModules = 10000;
    public const double MsPerSlot = 50.0;
    private const double _modulesPerSecond = 1000.0 / MsPerSlot;
    public double  _slotWidth { get; private set; } = 3; // base zoom unit
    private double _minBlockWidth = 0.6; // allow 1/5th of base resolution
    public bool ScrollLocked { get; private set; }

    /// <summary> User defined bpm variable to visualize bpm lines in playback. 0 == no value </summary>
    public double Bpm = 0;
    
    public Canvas _timelineCanvas;
    public int _lastColorUpdateMs = 0;
    public int _liveStartMs = 0;
    public bool _isLiveInputActive = false;
    public LightBlock? _liveBlock = null;

    /// <summary> The current selected light block available for user editing</summary>
    public List<LightBlock>? _selectedBlocks = new List<LightBlock>();
    
    public List<LightBlock> LightBlocks = new();
    public ScrollViewer _scrollViewer;
    public TextBlock _playheadCaret;
    public event Action<Color[]?>? ActiveBlockChanged;
    public event Action<List<LightBlock>>? BlockSelectionChanged;
    public event Action? BlocksDeselected;
    
    public TimelineController(Canvas canvas, ScrollViewer scrollViewer)
    {
        _timelineCanvas = canvas;
        _scrollViewer = scrollViewer;
        _selectedBlocks = new List<LightBlock>();
    
        _scrollViewer.PointerPressed += (_, _) => ScrollLocked = false;
    }

    public void ClearBlocks()
    {
        foreach (var block in LightBlocks)
            _timelineCanvas.Children.Remove(block.Container);
        LightBlocks.Clear();
        _selectedBlocks.Clear();
    }
    public void DrawTimelineSlots()
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
    public void DrawBPMLines()
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
    public void ScaleTimelineChildren(double oldSlotWidth, double newSlotWidth)
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
    /// Logic for + and - zoom buttons
    /// </summary>
    /// <param name="factor"></param>
    public void Zoom(double factor)
    {
        double oldWidth = _slotWidth;
        double newWidth = Math.Clamp(_slotWidth * factor, _minBlockWidth, 30.0);
        if (Math.Abs(newWidth - _slotWidth) < 0.0001) return;

        _slotWidth = newWidth;
        ScaleTimelineChildren(oldWidth, _slotWidth);
        _timelineCanvas.Width = TimelineController._totalModules * _slotWidth;            
    }
    public void ZoomAtPointer(double deltaY, double mouseX)
    {
        double old = _slotWidth;
        double worldX = _scrollViewer.Offset.X + mouseX;
        double focusModule = worldX / old;

        double step = deltaY > 0 ? 1.1 : 1.0 / 1.1;
        double next = Math.Clamp(_slotWidth * step, 1.0, 30.0);
        if (Math.Abs(next - _slotWidth) < 0.0001) return;

        _slotWidth = next;
        ScaleTimelineChildren(old, _slotWidth);

        double newOffsetX = Math.Max(0, focusModule * _slotWidth - mouseX);
        _scrollViewer.Offset = new Vector(newOffsetX, _scrollViewer.Offset.Y);
    }

    public void ScrollBy(double delta)
    {
        double newX = Math.Max(0, _scrollViewer.Offset.X + delta);
        _scrollViewer.Offset = new Vector(newX, _scrollViewer.Offset.Y);
        ScrollLocked = false;
    }
    /// <summary>
    /// Change Scroll Lock Status
    /// </summary>
    /// <param name="locked"></param>
    public void ChangeScrollLock(bool locked)
    {
        ScrollLocked = locked;
    }


}