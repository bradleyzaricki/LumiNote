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
        private Point dragStartCanvas;
        private double originalLeft;
        private double originalWidth;
        private bool isResizingLeft;
        private bool isResizingRight;
        private bool isMoving;
        private List<LightBlock> _siblings;
        private ScrollViewer _scrollViewer;
        private double _slotWidth;

        public LightBlock(Color color, List<LightBlock> siblings, ScrollViewer scrollViewer, double slotWidth)
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
                Cursor = new Avalonia.Input.Cursor(StandardCursorType.SizeWestEast)
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
