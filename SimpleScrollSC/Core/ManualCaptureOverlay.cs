using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ScrollShot.Core;

public static class ManualCaptureOverlay
{
    public static Task RunAsync(Window owner, Action onStart, Action onStop, CancellationToken cancellationToken)
    {
        TaskCompletionSource completion = new();

        Window overlay = new()
        {
            Owner = owner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Topmost = true,
            ShowInTaskbar = false,
            Cursor = Cursors.Arrow,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight
        };

        Border panel = new()
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 17, 24, 39)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 20, 0, 0)
        };

        TextBlock text = new()
        {
            Text = "Left click to START capture",
            Foreground = Brushes.White
        };

        panel.Child = text;
        overlay.Content = panel;

        bool started = false;

        void StopAndClose()
        {
            try
            {
                onStop();
            }
            finally
            {
                overlay.Close();
            }
        }

        overlay.MouseLeftButtonDown += (_, _) =>
        {
            if (!started)
            {
                started = true;
                onStart();
                text.Text = "Capturing... left click to STOP";
                return;
            }

            StopAndClose();
        };

        overlay.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                StopAndClose();
            }
        };

        overlay.Closed += (_, _) => completion.TrySetResult();

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                overlay.Dispatcher.Invoke(() =>
                {
                    if (overlay.IsVisible)
                    {
                        overlay.Close();
                    }
                });
            });
        }

        overlay.Show();
        overlay.Activate();

        return completion.Task;
    }
}
