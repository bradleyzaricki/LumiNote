using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LumikitApp
{
    public class LightBlock
    {
        public Border Container { get; }
        public int StartLight { get; set; }
        public int EndLight { get; set; }
        public int SecondaryStartLight { get; set; }
        public int SecondaryEndLight { get; set; }
        public int AdditionalIndividualInput1 {get; set;}
        public int AdditionalIndividualInput2 {get; set;}
        
        public Color SecondBlockColor {get; set;}
        public int Intensity { get; set; }
        public Color BlockColor { get; set; }
        public List<Effect> BlockEffects { get; set; } = new List<Effect>(){Effect.None};
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
            Twinkle

        }
        private Point dragStartCanvas;
        private double originalLeft;
        private double originalWidth;
        private bool isResizingLeft;
        private bool isResizingRight;
        private bool isMoving;
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
            BlockEffects = new List<Effect>(blockToMimic.BlockEffects);
            SecondaryStartLight = blockToMimic.SecondaryStartLight;
            SecondaryEndLight = blockToMimic.SecondaryEndLight;
            AdditionalIndividualInput1 = blockToMimic.AdditionalIndividualInput1;
            AdditionalIndividualInput2 = blockToMimic.AdditionalIndividualInput2;

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
                foreach (var block in _siblings)
                {
                    if (!block.isMoving)
                    {
                        block.originalLeft = Canvas.GetLeft(block.Container);
                    }
                    block.MoveBlock(current,dragStartCanvas);

                }
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

        public void MoveBlock(Point current, Point dragStart)
        {
            
            if (!this.isSelected)
            {
                return;
            }
            isMoving = true;
            var canvas = (Canvas?)Container.Parent;
            var canvasWidth = canvas.Bounds.Width;
            double newLeft = originalLeft + (current.X - dragStart.X);
            double snappedLeft = Math.Round(newLeft / _slotWidth) * _slotWidth;
            if (snappedLeft >= 0 && snappedLeft + Container.Width <= canvasWidth &&
                !Collides(snappedLeft, Container.Width))
            {
                Canvas.SetLeft(Container, snappedLeft);
                //ScrollIfNeeded(snappedLeft + Container.Width);
            }
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
