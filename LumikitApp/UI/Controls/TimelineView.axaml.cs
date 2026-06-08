using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using LumikitApp.Models;

namespace LumikitApp.Controls;

public partial class TimelineView : UserControl
{
    private const int _totalModules = 10000;
    public const double MsPerSlot = 50.0;
    private const double _modulesPerSecond = 1000.0 / MsPerSlot;
    private double _slotWidth = 3;
    private readonly double _minBlockWidth = 0.6;

    public double SlotWidth => _slotWidth;
    public double Bpm = 0;
    public bool ScrollLocked { get; private set; }
    public bool IsLiveInputActive => _isLiveInputActive;

    private bool _isLiveInputActive = false;
    private LightBlock? _liveBlock = null;
    private int _lastColorUpdateMs = 0;

    public List<LightBlock> LightBlocks { get; } = new();
    public List<LightBlock>? SelectedBlocks => _selectedBlocks;
    private List<LightBlock> _selectedBlocks = new();

    private TextBlock? _playheadCaret;
    private Point _lastPointerPos;

    public event Action<int>? SeekRequested;
    public event Action<LightBlock, PointerPressedEventArgs>? BlockPressed;

    public double ViewportWidth
    {
        get
        {
            double w = TimelineScrollViewer.Viewport.Width;
            return w > 0 ? w : TimelineScrollViewer.Bounds.Width;
        }
    }

    public TimelineView()
    {
        InitializeComponent();

        TimelineScrollViewer.PointerPressed += (_, _) => ChangeScrollLock(false);
        TimelineScrollViewer.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        TimelineScrollViewer.PointerMoved += (_, e) => _lastPointerPos = e.GetPosition(TimelineCanvas);
        SeekBarCanvas.PointerPressed += OnSeekBarPointerPressed;

        DragDrop.SetAllowDrop(TimelineCanvas, true);
        TimelineCanvas.AddHandler(DragDrop.DropEvent, OnCanvasDrop, RoutingStrategies.Bubble);
    }

    public void ClearBlocks()
    {
        foreach (var block in LightBlocks)
            TimelineCanvas.Children.Remove(block.Container);
        LightBlocks.Clear();
        _selectedBlocks.Clear();
    }

    public void DrawTimelineSlots()
    {
        TimelineCanvas.Children.Clear();

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
            TimelineCanvas.Children.Add(slot);

            if (i % 40 == 0)
            {
                double seconds = i / _modulesPerSecond;
                var label = new TextBlock
                {
                    Text = $"{seconds:0.0}s",
                    Foreground = Brushes.White,
                    FontSize = 10
                };
                Canvas.SetLeft(label, i * _slotWidth - 5);
                Canvas.SetTop(label, -15);
                TimelineCanvas.Children.Add(label);

                var caret = new TextBlock
                {
                    Text = "^",
                    Foreground = Brushes.White,
                    FontSize = 10
                };
                Canvas.SetLeft(caret, i * _slotWidth - 2);
                Canvas.SetTop(caret, 60);
                TimelineCanvas.Children.Add(caret);
            }
        }

        DrawBPMLines();
        TimelineCanvas.Width = _totalModules * _slotWidth;

        _playheadCaret = new TextBlock
        {
            Text = "▲",
            Foreground = Brushes.Red,
            FontSize = 14
        };
        Canvas.SetLeft(_playheadCaret, 0);
        Canvas.SetTop(_playheadCaret, 72);
        TimelineCanvas.Children.Add(_playheadCaret);
    }

    public void DrawBPMLines()
    {
        double secondsPerBeat = Bpm > 0 ? 60.0 / Bpm : 0;
        double modulesPerBeat = secondsPerBeat * _modulesPerSecond;

        if (Bpm > 0 && modulesPerBeat > 0)
        {
            int bpmIndicatorNumber = 0;
            for (double i = 0; i < _totalModules; i += modulesPerBeat)
            {
                var color = Brushes.Brown;
                if (bpmIndicatorNumber % 4 == 0)
                    color = Brushes.Red;

                var line = new Border
                {
                    Width = 1,
                    Height = 60,
                    Background = color
                };
                Canvas.SetLeft(line, i * _slotWidth);
                Canvas.SetTop(line, -20);
                TimelineCanvas.Children.Add(line);

                bpmIndicatorNumber++;
            }
        }
    }

    private void ScaleTimelineChildren(double oldSlotWidth, double newSlotWidth)
    {
        double factor = newSlotWidth / oldSlotWidth;

        foreach (var child in TimelineCanvas.Children)
        {
            if (ReferenceEquals(child, _playheadCaret)) continue;

            double left = Canvas.GetLeft((Control)child);
            if (!double.IsNaN(left))
                Canvas.SetLeft((Control)child, left * factor);

            if (child is Border b && !double.IsNaN(b.Width) && b.Width > 0)
                b.Width *= factor;
        }

        TimelineCanvas.Width = _totalModules * newSlotWidth;
    }

    public void Zoom(double factor)
    {
        double oldWidth = _slotWidth;
        double newWidth = Math.Clamp(_slotWidth * factor, _minBlockWidth, 30.0);
        if (Math.Abs(newWidth - _slotWidth) < 0.0001) return;

        _slotWidth = newWidth;
        ScaleTimelineChildren(oldWidth, _slotWidth);
        TimelineCanvas.Width = _totalModules * _slotWidth;
    }

    private void ZoomAtPointer(double deltaY, double mouseX)
    {
        double old = _slotWidth;
        double worldX = TimelineScrollViewer.Offset.X + mouseX;
        double focusModule = worldX / old;

        double step = deltaY > 0 ? 1.1 : 1.0 / 1.1;
        double next = Math.Clamp(_slotWidth * step, 1.0, 30.0);
        if (Math.Abs(next - _slotWidth) < 0.0001) return;

        _slotWidth = next;
        ScaleTimelineChildren(old, _slotWidth);

        double newOffsetX = Math.Max(0, focusModule * _slotWidth - mouseX);
        TimelineScrollViewer.Offset = new Vector(newOffsetX, TimelineScrollViewer.Offset.Y);
    }

    public void ScrollBy(double delta)
    {
        double newX = Math.Max(0, TimelineScrollViewer.Offset.X + delta);
        TimelineScrollViewer.Offset = new Vector(newX, TimelineScrollViewer.Offset.Y);
        ScrollLocked = false;
    }

    public void ChangeScrollLock(bool locked)
    {
        ScrollLocked = locked;
    }

    public Color[]? Tick(int ms, int colorUpdateIntervalMs, double brightnessScale, double serialIntervalMs = 50.0)
    {
        if (ms < _lastColorUpdateMs)
            _lastColorUpdateMs = ms;

        if (ms - _lastColorUpdateMs < colorUpdateIntervalMs)
            return Array.Empty<Color>();

        _lastColorUpdateMs = ms;
        double slotIndex = ms / MsPerSlot;
        double caretX = slotIndex * _slotWidth;
        Canvas.SetLeft(_playheadCaret!, caretX - 4);

        if (ScrollLocked)
        {
            double viewportWidth = TimelineScrollViewer.Viewport.Width;
            double scrollTo = Math.Max(0, caretX - viewportWidth / 6);
            TimelineScrollViewer.Offset = new Vector(scrollTo, TimelineScrollViewer.Offset.Y);
        }

        if (_isLiveInputActive && _liveBlock != null)
        {
            double startX = Canvas.GetLeft(_liveBlock.Container);
            var snappedWidth = Math.Round((caretX - startX) / _slotWidth + 1) * _slotWidth;
            _liveBlock.Container.Width = Math.Max(_slotWidth, snappedWidth);
        }

        var activeBlock = LocateCurrentLightBlock(caretX, LightBlocks, 0, LightBlocks.Count - 1);
        if (activeBlock == null)
            return null;

        double blockWidth = activeBlock.Container.Width;
        if (blockWidth <= 0)
            return null;

        double blockLeft = Canvas.GetLeft(activeBlock.Container);
        double relPos = Math.Clamp((caretX - blockLeft) / blockWidth, 0, 1);
        double blockElapsedMs = (caretX - blockLeft) / _slotWidth * MsPerSlot;

        return LightEffectsComputer.ComputeBlockEffects(activeBlock, relPos, brightnessScale,
            elapsedMs: blockElapsedMs, serialIntervalMs: serialIntervalMs);
    }

    public void ReorderLightBlocks()
    {
        LightBlocks.Sort((a, b) =>
            Canvas.GetLeft(a.Container).CompareTo(Canvas.GetLeft(b.Container)));
    }

    private LightBlock? LocateCurrentLightBlock(double currPos, List<LightBlock> blocks, int left, int right)
    {
        if (left > right) return null;

        int midIndex = (left + right) / 2;
        var mid = blocks[midIndex];

        double start = Canvas.GetLeft(mid.Container);
        double end = start + mid.Container.Width;

        if (currPos < start)
            return LocateCurrentLightBlock(currPos, blocks, left, midIndex - 1);

        if (currPos > end)
            return LocateCurrentLightBlock(currPos, blocks, midIndex + 1, right);

        return mid;
    }

    private void AddSelectedBlock(LightBlock block)
    {
        if (_selectedBlocks.Contains(block)) return;
        _selectedBlocks.Add(block);
        _selectedBlocks = _selectedBlocks
            .OrderBy(b => Canvas.GetLeft(b.Container))
            .ToList();
    }

    public List<LightBlock> HandleBlockSelection(PointerPressedEventArgs e, LightBlock blockToAdd)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var min = double.MaxValue;
            var max = -1.00;
            foreach (var selected in _selectedBlocks)
            {
                double left = Canvas.GetLeft(selected.Container);
                if (left < min) min = left;
                if (left > max) max = left;
            }

            if (min == double.MaxValue)
            {
                AddSelectedBlock(blockToAdd);
                blockToAdd.isSelected = true;
                return _selectedBlocks;
            }

            foreach (var lightblock in LightBlocks)
            {
                if (_selectedBlocks.Contains(lightblock)) continue;

                var blockLeft = Canvas.GetLeft(lightblock.Container);
                if ((blockLeft > min && blockLeft <= Canvas.GetLeft(blockToAdd.Container))
                    || (blockLeft < max && blockLeft >= Canvas.GetLeft(blockToAdd.Container)))
                {
                    AddSelectedBlock(lightblock);
                    lightblock.isSelected = true;
                    blockToAdd.isSelected = true;
                }
            }
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            AddSelectedBlock(blockToAdd);
            blockToAdd.isSelected = true;
        }
        else
        {
            foreach (var blockToRemove in _selectedBlocks)
            {
                blockToRemove.UpdateBackground(blockToRemove.BlockColor);
                blockToRemove.isSelected = false;
            }
            _selectedBlocks.Clear();
            AddSelectedBlock(blockToAdd);
            blockToAdd.isSelected = true;
        }

        e.Handled = true;
        return _selectedBlocks;
    }

    public void LoadFromTrackData(TrackData trackData)
    {
        foreach (var data in trackData._lightBlocks)
        {
            if (!Color.TryParse(data.Color, out var color)) continue;

            var block = new LightBlock(LightBlocks, TimelineScrollViewer, _slotWidth);
            block.UpdateColor(color);
            block.SecondBlockColor = Color.TryParse(data.SecondColor, out var color2) ? color2 : new Color();
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

            TimelineCanvas.Children.Add(block.Container);
            LightBlocks.Add(block);

            block.Container.PointerPressed += (_, e) => BlockPressed?.Invoke(block, e);
            block.Container.PointerReleased += (_, _) => ReorderLightBlocks();
        }
    }

    public LightBlock CreateAndPlaceBlock(Color color, double x, double width)
    {
        var block = new LightBlock(LightBlocks, TimelineScrollViewer, _slotWidth);
        return CreateAndPlaceBlock(block, x, color, width);
    }

    public LightBlock CreateAndPlaceBlock(LightBlock block, double x, Color color, double width)
    {
        block.Container.Width = width;
        block.UpdateColor(color);
        Canvas.SetLeft(block.Container, x);
        Canvas.SetTop(block.Container, 0);

        TimelineCanvas.Children.Add(block.Container);
        LightBlocks.Add(block);
        ReorderLightBlocks();
        block.Container.PointerPressed += (_, e) => BlockPressed?.Invoke(block, e);
        block.Container.PointerReleased += (_, _) => ReorderLightBlocks();

        return block;
    }

    public void DeleteSelectedBlocks()
    {
        foreach (var block in _selectedBlocks.ToList())
        {
            block.isSelected = false;
            TimelineCanvas.Children.Remove(block.Container);
            LightBlocks.Remove(block);
        }
        _selectedBlocks.Clear();
    }

    public void StartLiveBlock(Color color, int currentMs)
    {
        _isLiveInputActive = true;
        double caretX = (currentMs / MsPerSlot) * _slotWidth ;
        double snappedX = Math.Round(caretX);
        _liveBlock = CreateAndPlaceBlock(color, snappedX, _slotWidth);
    }

    public void EndLiveBlock()
    {
        _isLiveInputActive = false;
        _liveBlock = null;
    }

    public void Paste()
    {
        if (_selectedBlocks.Count == 0) return;

        double leftmostX = _selectedBlocks.Min(b => Canvas.GetLeft(b.Container));
        foreach (var source in _selectedBlocks)
        {
            double offsetX = _lastPointerPos.X + (Canvas.GetLeft(source.Container) - leftmostX);
            CreateAndPlaceBlock(new LightBlock(source), offsetX, source.BlockColor, source.Container.Width);
        }
    }

    public int CanvasXToMs(double x)
    {
        return (int)(x / _slotWidth * MsPerSlot);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            ZoomAtPointer(e.Delta.Y, e.GetPosition(TimelineScrollViewer).X);
            e.Handled = true;
            return;
        }
        ScrollBy(e.Delta.Y * -40);
        e.Handled = true;
    }

    private void OnSeekBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(SeekBarCanvas);
        SeekRequested?.Invoke(CanvasXToMs(pos.X));
    }

    private void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("block-color")) return;
        var colorString = e.Data.Get("block-color")?.ToString();
        if (colorString == null || !Color.TryParse(colorString, out var color)) return;

        var pos = e.GetPosition(TimelineCanvas);
        double snappedX = Math.Round(pos.X / _slotWidth) * _slotWidth;
        snappedX = Math.Max(0, Math.Min(snappedX, TimelineCanvas.Width - _slotWidth));

        var (resolvedX, finalWidth) = ResolveDropPosition(snappedX);
        if (finalWidth < _slotWidth) return;

        var block = CreateAndPlaceBlock(color, resolvedX, finalWidth);
        block.Intensity = 255;
        block.EndLight = 1000;
    }

    private (double resolvedX, double width) ResolveDropPosition(double snappedX)
    {
        // If the drop lands inside an existing block, push start to immediately after it
        var blockUnder = LightBlocks.FirstOrDefault(b =>
        {
            double left = Canvas.GetLeft(b.Container);
            return snappedX >= left && snappedX < left + b.Container.Width;
        });

        if (blockUnder != null)
            snappedX = Math.Round(
                (Canvas.GetLeft(blockUnder.Container) + blockUnder.Container.Width) / _slotWidth
            ) * _slotWidth;

        // Available space = distance to the nearest block starting at or after snappedX
        double spaceEnd = LightBlocks
            .Select(b => Canvas.GetLeft(b.Container))
            .Where(left => left >= snappedX)
            .DefaultIfEmpty(TimelineCanvas.Width)
            .Min();

        double finalWidth = Math.Min(_slotWidth * 50, Math.Max(0, spaceEnd - snappedX));
        return (snappedX, finalWidth);
    }
}
