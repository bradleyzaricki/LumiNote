using System.Diagnostics;

namespace LumikitApp;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
public class TimelineController
{
    //Variables for scaling playback length and visual size
    private const int _totalModules = 10000;
    public const double MsPerSlot = 50.0;
    private const double _modulesPerSecond = 1000.0 / MsPerSlot;
    public double  _slotWidth { get; private set; } = 3; // base zoom unit
    private double _minBlockWidth = 0.6; // allow 1/5th of base resolution
    public bool ScrollLocked { get; private set; }

    ///User defined bpm variable to visualize bpm lines in playback. 0 == no value
    public double Bpm = 0;
    
    public Canvas _timelineCanvas;
    public int _lastColorUpdateMs = 0;
    public int _liveStartMs = 0;
    public bool _isLiveInputActive = false;
    public LightBlock? _liveBlock = null;

    /// <summary> The current selected light block available for user editing</summary>
    public List<LightBlock>? _selectedBlocks;
    
    public List<LightBlock> LightBlocks = new();
    public ScrollViewer _scrollViewer;
    private TextBlock _playheadCaret;
    
    public TimelineController(Canvas canvas, ScrollViewer scrollViewer)
    {
        _timelineCanvas = canvas;
        _scrollViewer = scrollViewer;
        _selectedBlocks = new List<LightBlock>();
        _scrollViewer.PointerPressed += (_, _) => ScrollLocked = false;
        int pid = Environment.ProcessId;
        Console.WriteLine("[PID] " + pid);
    }


    /// <summary>
    /// Clear all lightblocks off the timeline canvas
    /// </summary>
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

    /// <summary>
    /// Update visualization of light playback. Imitates the microcontroller for visualization and development purposes.
    /// Returns computed strip colors for the active block, or null if no block is active.
    /// </summary>
    /// <param name="ms"></param>
    /// <param name="colorUpdateIntervalMs">Visual throttle interval — Tick returns empty when called faster than this.</param>
    /// <param name="brightnessScale"></param>
    /// <param name="serialIntervalMs">Serial hardware update interval forwarded to ComputeBlockEffects for strobe snapping.</param>
    public Color[]? Tick(int ms, int colorUpdateIntervalMs, double brightnessScale, double serialIntervalMs = 50.0)
    {
        if (ms < _lastColorUpdateMs)
            _lastColorUpdateMs = ms;

        if (ms - _lastColorUpdateMs < colorUpdateIntervalMs)
            return Array.Empty<Color>();

        _lastColorUpdateMs = ms;
        double slotIndex = ms / MsPerSlot;
        double caretX = slotIndex * _slotWidth;
        Canvas.SetLeft(_playheadCaret, caretX - 4);

        if (ScrollLocked)
        {
            double viewportWidth = _scrollViewer.Viewport.Width;
            double scrollTo = Math.Max(0, caretX - viewportWidth / 6);
            _scrollViewer.Offset = new Vector(scrollTo, _scrollViewer.Offset.Y);
        }

        if (_isLiveInputActive && _liveBlock != null)
        {
            int pid = Environment.ProcessId;
            Console.WriteLine("[PID] " + pid);

            double startX = Canvas.GetLeft(_liveBlock.Container);
            var snappedWidth = Math.Round(((caretX - startX) / _slotWidth) + 1) * _slotWidth;
            _liveBlock.Container.Width = Math.Max(_slotWidth, snappedWidth);
        }
        
        //Binary Search for next Lightblock
        var activeBlock = LocateCurrentLightBlock(caretX, LightBlocks, 0, LightBlocks.Count - 1); 
        /* var activeBlock = LightBlocks.FirstOrDefault(b =>
                {
                    double left = Canvas.GetLeft(b.Container);
                    double width = b.Container.Width;
                    return caretX >= left && caretX <= left + width;
                });
        */
        if (activeBlock == null)
            return null;

        double blockWidth = activeBlock.Container.Width;
        if (blockWidth <= 0)
            return null;

        double blockLeft    = Canvas.GetLeft(activeBlock.Container);
        double relPos       = Math.Clamp((caretX - blockLeft) / blockWidth, 0, 1);
        double blockElapsedMs = (caretX - blockLeft) / _slotWidth * MsPerSlot;

        return LightEffectsComputer.ComputeBlockEffects(activeBlock, relPos, brightnessScale,
            elapsedMs: blockElapsedMs, serialIntervalMs: serialIntervalMs);
    }

    /// <summary>
    /// Called when changes to lightblock order or values change
    /// (Call on Add, Delete, Save, and Move)
    /// </summary>
    public void ReorderLightBlocks()
    {
        LightBlocks.Sort((a, b) => 
            Canvas.GetLeft(a.Container).CompareTo(Canvas.GetLeft(b.Container)));
    }
    public LightBlock LocateCurrentLightBlock(double currPos, List<LightBlock> blocks, int left, int right)
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
    
    /// <summary>
    /// Add block to _selectedBlocks and sort it
    /// </summary>
    /// <param name="block"></param>
    private void AddSelectedBlock(LightBlock block)
    {
        // Prevent duplicates
        if (_selectedBlocks.Contains(block))
            return;

        _selectedBlocks.Add(block);

        // Order by X position on timeline
        _selectedBlocks = _selectedBlocks
            .OrderBy(b => Canvas.GetLeft(b.Container))
            .ToList();
    }
    
    /// <summary>
    /// Handles block selection logic for left click, shift click, and ctrl click.
    /// Returns the updated selected blocks list so the caller can open the editor.
    /// </summary>
    /// <param name="e"></param>
    /// <param name="blockToAdd"></param>
    public List<LightBlock> HandleBlockSelection(PointerPressedEventArgs e, LightBlock blockToAdd)
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
                }

                if (Canvas.GetLeft(selectedblock.Container) > max)
                {
                    max = Canvas.GetLeft(selectedblock.Container);
                }
            }

            if (min == double.MaxValue) //No blocks selected previously 
            {
                AddSelectedBlock(blockToAdd);
                blockToAdd.isSelected = true;
                return _selectedBlocks;
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

    /// <summary>
    /// Loads light blocks from saved track data onto the timeline canvas.
    /// Caller must provide an onBlockPressed callback to wire up editor/delete interaction.
    /// </summary>
    /// <param name="trackData"></param>
    /// <param name="onBlockPressed"></param>
    public void LoadFromTrackData(TrackData trackData, Action<LightBlock, PointerPressedEventArgs> onBlockPressed)
    {
        foreach (var data in trackData._lightBlocks)
        {
            if (!Color.TryParse(data.Color, out var color)) continue;

            var block = new LightBlock(LightBlocks, _scrollViewer, _slotWidth);
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
            block.Container.Width = data.Width * _slotWidth;
            Canvas.SetLeft(block.Container, data.X * _slotWidth);
            Canvas.SetTop(block.Container, 0);

            _timelineCanvas.Children.Add(block.Container);
            LightBlocks.Add(block);

            //Assign light block keybinds (Lclick edit, Rclick delete)
            block.Container.PointerPressed += (_, e) => onBlockPressed(block, e);
            
        }
    }
    /// <summary>
    /// Converts a canvas X position to milliseconds
    /// </summary>
    public int CanvasXToMs(double x)
    {
        return (int)((x / _slotWidth) * MsPerSlot);
    }
}