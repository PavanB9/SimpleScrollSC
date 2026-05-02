using DrawingRectangle = System.Drawing.Rectangle;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ScrollShot.Core;

internal static class RegionSelector
{
    public static Task<DrawingRectangle?> PickCropRectAsync(Window owner, NativeMethods.RECT targetBounds)
    {
        TaskCompletionSource<DrawingRectangle?> completion = new();

        Window overlay = new()
        {
            Owner = owner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)),
            Topmost = true,
            ShowInTaskbar = false,
            Cursor = Cursors.Cross,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight
        };

        Canvas canvas = new() { Background = Brushes.Transparent };
        overlay.Content = canvas;

        TextBlock help = new()
        {
            Text = "Drag to select an area inside the target window (Esc cancels)",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(170, 17, 24, 39)),
            Padding = new Thickness(10, 6, 10, 6)
        };
        Canvas.SetLeft(help, 12);
        Canvas.SetTop(help, 12);
        canvas.Children.Add(help);

        Rectangle targetOutline = new()
        {
            Stroke = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
            StrokeThickness = 2,
            Fill = Brushes.Transparent
        };

        Rectangle selection = new()
        {
            Stroke = new SolidColorBrush(Color.FromArgb(240, 59, 130, 246)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(28, 59, 130, 246))
        };

        bool dragging = false;
        Point startCanvas = default;

        overlay.Loaded += (_, _) =>
        {
            Point topLeft = canvas.PointFromScreen(new Point(targetBounds.Left, targetBounds.Top));
            Point bottomRight = canvas.PointFromScreen(new Point(targetBounds.Right, targetBounds.Bottom));

            Canvas.SetLeft(targetOutline, topLeft.X);
            Canvas.SetTop(targetOutline, topLeft.Y);
            targetOutline.Width = Math.Max(0, bottomRight.X - topLeft.X);
            targetOutline.Height = Math.Max(0, bottomRight.Y - topLeft.Y);

            if (!canvas.Children.Contains(targetOutline))
            {
                canvas.Children.Add(targetOutline);
            }
        };

        overlay.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                completion.TrySetResult(null);
                overlay.Close();
            }
        };

        canvas.MouseLeftButtonDown += (_, e) =>
        {
            dragging = true;
            startCanvas = e.GetPosition(canvas);
            canvas.CaptureMouse();

            if (!canvas.Children.Contains(selection))
            {
                canvas.Children.Add(selection);
            }

            Canvas.SetLeft(selection, startCanvas.X);
            Canvas.SetTop(selection, startCanvas.Y);
            selection.Width = 0;
            selection.Height = 0;
        };

        canvas.MouseMove += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            Point current = e.GetPosition(canvas);
            double left = Math.Min(startCanvas.X, current.X);
            double top = Math.Min(startCanvas.Y, current.Y);
            double width = Math.Abs(current.X - startCanvas.X);
            double height = Math.Abs(current.Y - startCanvas.Y);

            Canvas.SetLeft(selection, left);
            Canvas.SetTop(selection, top);
            selection.Width = width;
            selection.Height = height;
        };

        canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            canvas.ReleaseMouseCapture();

            Point endCanvas = e.GetPosition(canvas);
            Point startScreen = canvas.PointToScreen(startCanvas);
            Point endScreen = canvas.PointToScreen(endCanvas);

            int left = (int)Math.Round(Math.Min(startScreen.X, endScreen.X));
            int top = (int)Math.Round(Math.Min(startScreen.Y, endScreen.Y));
            int right = (int)Math.Round(Math.Max(startScreen.X, endScreen.X));
            int bottom = (int)Math.Round(Math.Max(startScreen.Y, endScreen.Y));

            DrawingRectangle screenRect = DrawingRectangle.FromLTRB(left, top, right, bottom);
            DrawingRectangle windowRect = DrawingRectangle.FromLTRB(targetBounds.Left, targetBounds.Top, targetBounds.Right, targetBounds.Bottom);
            DrawingRectangle intersect = DrawingRectangle.Intersect(screenRect, windowRect);

            if (intersect.Width <= 0 || intersect.Height <= 0)
            {
                completion.TrySetResult(null);
                overlay.Close();
                return;
            }

            DrawingRectangle crop = new(
                x: intersect.Left - targetBounds.Left,
                y: intersect.Top - targetBounds.Top,
                width: intersect.Width,
                height: intersect.Height);

            completion.TrySetResult(crop);
            overlay.Close();
        };

        overlay.Closed += (_, _) => completion.TrySetResult(null);
        overlay.Show();
        overlay.Activate();

        return completion.Task;
    }
}
