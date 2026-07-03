using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LumikitApp
{
    public class LightBlock
    {
        public Border Container { get; }
        public int StartLight { get; set; }
        public int EndLight { get; set; }
        public int SecondaryStartLight { get; set; }
        public int SecondaryEndLight { get; set; }
        public Color SecondBlockColor {get; set;}
        public Color FillColor { get; set; }
        public int Intensity { get; set; }
        public Color BlockColor { get; set; }
        public List<EffectData> BlockEffects { get; set; } = new List<EffectData> { new EffectData { Type = Effect.None } };
        public enum Effect
        {
            None,
            FadeIn,
            FadeOut,
            Strobe,
            Travel,
            Combine,
            Seperate,
            Repeat,
            ChangeColor,
            Twinkle,
            FillColor,
            Scanner,
            Comet,
            Shimmer,
            Sparkle

        }
        /// <summary>
        /// The block's shape effect (Travel/Combine/Seperate), or Effect.None for a static span.
        /// Precedence mirrors the old compute chain (Travel, then Seperate, then Combine) so
        /// legacy blocks that stored more than one shape render unchanged.
        /// </summary>
        public Effect GetShape()
        {
            if (BlockEffects == null) return Effect.None;
            if (BlockEffects.Any(e => e.Type == Effect.Travel)) return Effect.Travel;
            if (BlockEffects.Any(e => e.Type == Effect.Seperate)) return Effect.Seperate;
            if (BlockEffects.Any(e => e.Type == Effect.Combine)) return Effect.Combine;
            if (BlockEffects.Any(e => e.Type == Effect.Scanner)) return Effect.Scanner;
            return Effect.None;
        }

        /// <summary>The EffectData entry backing the active shape, or null for a static span.</summary>
        public EffectData? GetShapeData()
        {
            var shape = GetShape();
            return shape == Effect.None ? null : BlockEffects.FirstOrDefault(e => e.Type == shape);
        }

        /// <summary>
        /// Sets the shape, removing any other shape entries. Keeps the existing entry (and its
        /// params) when the shape is unchanged; Effect.None clears all shapes.
        /// </summary>
        public void SetShape(Effect shape)
        {
            var keep = shape == Effect.None
                ? null
                : BlockEffects.FirstOrDefault(e => e.Type == shape);
            BlockEffects.RemoveAll(e => EffectCatalog.IsShape(e.Type) && e != keep);
            if (shape != Effect.None && keep == null)
                BlockEffects.Add(EffectCatalog.CreateData(shape));
        }

        /// <summary>
        /// The block's texture effect (per-pixel modulation like Twinkle), or Effect.None.
        /// Textures are mutually exclusive; precedence for legacy multi-texture data follows
        /// EffectCatalog order.
        /// </summary>
        public Effect GetTexture()
        {
            if (BlockEffects == null) return Effect.None;
            foreach (var def in EffectCatalog.Textures)
                if (BlockEffects.Any(e => e.Type == def.Type))
                    return def.Type;
            return Effect.None;
        }

        /// <summary>The EffectData entry backing the active texture, or null when there is none.</summary>
        public EffectData? GetTextureData()
        {
            var texture = GetTexture();
            return texture == Effect.None ? null : BlockEffects.FirstOrDefault(e => e.Type == texture);
        }

        /// <summary>
        /// Sets the texture, removing any other texture entries. Keeps the existing entry (and
        /// its params) when the texture is unchanged; Effect.None clears all textures.
        /// </summary>
        public void SetTexture(Effect texture)
        {
            var keep = texture == Effect.None
                ? null
                : BlockEffects.FirstOrDefault(e => e.Type == texture);
            BlockEffects.RemoveAll(e => EffectCatalog.IsTexture(e.Type) && e != keep);
            if (texture != Effect.None && keep == null)
                BlockEffects.Add(EffectCatalog.CreateData(texture));
        }

        private Point dragStartCanvas;
        private double originalLeft;
        private double originalWidth;
        private bool isResizingLeft;
        private bool isResizingRight;
        private bool isMoving;
        private bool _groupDragActive;
        public bool isSelected;
        private List<LightBlock> _siblings;
        private ScrollViewer _scrollViewer;
        private double _slotWidth;


        //Constructor to create a new block + add in all the variables to copy
        public LightBlock(LightBlock blockToMimic)
            : this(blockToMimic._siblings, blockToMimic._scrollViewer, blockToMimic._slotWidth)
        {
            StartLight = blockToMimic.StartLight;
            EndLight = blockToMimic.EndLight;
            Intensity = blockToMimic.Intensity;
            BlockColor = blockToMimic.BlockColor;
            SecondBlockColor = blockToMimic.SecondBlockColor;
            FillColor = blockToMimic.FillColor;
            BlockEffects = blockToMimic.BlockEffects.Select(e => e.DeepCopy()).ToList();
            SecondaryStartLight = blockToMimic.SecondaryStartLight;
            SecondaryEndLight = blockToMimic.SecondaryEndLight;
            
            Container.Width = blockToMimic.Container.Width;
        }
        
        //Base constructor to add a lightblock into a scrollviewer
        public LightBlock(List<LightBlock> siblings, ScrollViewer scrollViewer, double slotWidth)
        {
            StartLight = 0;
            EndLight = 1000;
            Intensity = 255;
            _siblings = siblings;
            _scrollViewer = scrollViewer;
            _slotWidth = slotWidth;
            Container = new Border
            {
                Width = slotWidth,
                Height = 60,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(0.01)
            };



            Container.PointerPressed += OnPointerPressed;
            Container.PointerMoved += OnPointerMoved;
            Container.PointerReleased += OnPointerReleased;
        }
        /// <summary>
        /// Update the output color of the lightblock as well as its visual aid
        /// </summary>
        /// <param name="color"></param>
        public void UpdateColor(Color color)
        {
            BlockColor = color;
            Container.Background = new SolidColorBrush(color);
            var grid = new Grid();
            grid.Children.Add(MakeCorner(HorizontalAlignment.Left, VerticalAlignment.Top));
            grid.Children.Add(MakeCorner(HorizontalAlignment.Right, VerticalAlignment.Top));
            grid.Children.Add(MakeCorner(HorizontalAlignment.Left, VerticalAlignment.Bottom));
            grid.Children.Add(MakeCorner(HorizontalAlignment.Right, VerticalAlignment.Bottom));

            Container.Child = grid;
        }
        /// <summary>
        /// Update ONLY the background color of the lightblock. Visual purposes only no actual functionality
        /// </summary>
        /// <param name="color"></param>
        public void UpdateBackground(Color color)
        {
            Container.Background = new SolidColorBrush(color);
            
        }
        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var canvas = (Canvas?)Container.Parent;
            if (canvas == null) return;

            dragStartCanvas = e.GetPosition(canvas);
            originalLeft = Canvas.GetLeft(Container);
            originalWidth = Container.Width;

            var local = e.GetPosition(Container);
            isResizingLeft = (local.X < 6);
            isResizingRight = (local.X > Container.Width - 6);
            isMoving = !isResizingLeft && !isResizingRight;
            _groupDragActive = false;

            // Capture so the drag keeps tracking even when the pointer outruns the block.
            e.Pointer.Capture(Container);
        }
        //returns true if bordering lightblocks limit space on playback
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
            if(!isMoving && !isResizingLeft && !isResizingRight) return;
            var canvas = (Canvas?)Container.Parent;
            if (canvas == null || !isSelected) return;

            var current = e.GetPosition(canvas);
            double canvasWidth = canvas.Bounds.Width;


            if (isResizingLeft)
            {
                double newLeft = originalLeft + (current.X - dragStartCanvas.X);
                double snappedLeft = Math.Round(newLeft);
                double delta = originalLeft - snappedLeft;
                double newWidth = originalWidth + delta;

                if (newWidth >= 1 && snappedLeft >= 0 && !Collides(snappedLeft, newWidth))
                {
                    Canvas.SetLeft(Container, snappedLeft);
                    Container.Width = newWidth;
                    ScrollIfNeeded(snappedLeft);
                }
            }
            else if (isResizingRight)
            {
                double newWidth = originalWidth + (current.X - dragStartCanvas.X);
                double snappedWidth = Math.Round(newWidth);
                double rightEdge = originalLeft + snappedWidth;
                if (snappedWidth >= 1 && rightEdge <= canvasWidth && !Collides(originalLeft, snappedWidth))
                {
                    Container.Width = snappedWidth;
                    ScrollIfNeeded(rightEdge);
                }
            }
            else if (isMoving)
            {
                MoveSelectedGroup(current);
            }
        }
        private void ScrollIfNeeded(double edge)
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

        /// <summary>
        /// Moves every selected block as one rigid group by a single delta. The delta is clamped
        /// once — against the canvas edges and against non-selected blocks only — so the group
        /// slides until the first block touches an obstacle and never shears apart or self-blocks.
        /// </summary>
        private void MoveSelectedGroup(Point current)
        {
            var canvas = (Canvas?)Container.Parent;
            if (canvas == null) return;
            double canvasWidth = canvas.Bounds.Width;

            var selected = _siblings.Where(b => b.isSelected).ToList();
            if (selected.Count == 0) return;

            // Anchor each block's start position once, at the first move of this drag.
            if (!_groupDragActive)
            {
                foreach (var b in selected)
                    b.originalLeft = Canvas.GetLeft(b.Container);
                _groupDragActive = true;
            }

            double rawDelta = current.X - dragStartCanvas.X;

            // Canvas-edge bounds: the leftmost block can't cross 0, the rightmost can't cross the end.
            double lower = -selected.Min(b => b.originalLeft);
            double upper = canvasWidth - selected.Max(b => b.originalLeft + b.Container.Width);

            // Collision bounds: only non-selected blocks constrain the group. A block to the right
            // caps how far right we can slide; one to the left caps how far left.
            foreach (var s in selected)
            {
                double sLeft = s.originalLeft;
                double sRight = s.originalLeft + s.Container.Width;
                foreach (var o in _siblings)
                {
                    if (o.isSelected) continue;
                    double oLeft = Canvas.GetLeft(o.Container);
                    double oRight = oLeft + o.Container.Width;
                    if (oLeft >= sRight)       upper = Math.Min(upper, oLeft - sRight);
                    else if (oRight <= sLeft)  lower = Math.Max(lower, oRight - sLeft);
                }
            }

            // Snap to whole pixels and clamp within the allowed range. Normally the anchor
            // (delta 0) is valid so lo <= 0 <= hi; bail if a degenerate state inverts them.
            double lo = Math.Ceiling(lower);
            double hi = Math.Floor(upper);
            if (lo > hi) return;
            double delta = Math.Clamp(Math.Round(rawDelta), lo, hi);

            foreach (var s in selected)
                Canvas.SetLeft(s.Container, s.originalLeft + delta);

            ScrollIfNeeded(current.X);
        }
        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            foreach (var block in _siblings)
            {
                block.isMoving=false;
            }
            isResizingLeft = false;
            isResizingRight = false;
            isMoving = false;
            _groupDragActive = false;
        }
        
        
        
        /// <summary>
        /// Helper method to draw UI corners
        /// </summary>
        /// <param name="h"></param>
        /// <param name="v"></param>
        /// <returns></returns>
        private Control MakeCorner(HorizontalAlignment h, VerticalAlignment v)
        {
            IBrush borderBrush;
            if (((BlockColor.R >200) && (BlockColor.G > 200)) && (BlockColor.B > 200))
            { 
                 borderBrush = Brushes.Black;
            }
            else
            {
                borderBrush = Brushes.White;

            }
            return new Border
            {
                Width = 8,
                Height = 8,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(
                    h == HorizontalAlignment.Left ? 2 : 0,
                    v == VerticalAlignment.Top ? 2 : 0,
                    h == HorizontalAlignment.Right ? 2 : 0,
                    v == VerticalAlignment.Bottom ? 2 : 0
                ),
                HorizontalAlignment = h,
                VerticalAlignment = v,
                Cursor = new Cursor(StandardCursorType.SizeWestEast)

            };
        }
        

    }
}
