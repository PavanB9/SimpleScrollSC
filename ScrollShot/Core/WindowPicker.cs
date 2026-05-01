using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ScrollShot.Core;

public static class WindowPicker
{
    public static Task<WindowInfo?> PickAsync(Window owner)
    {
        TaskCompletionSource<WindowInfo?> completion = new();

        Window overlay = new()
        {
            Owner = owner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(24, 0, 0, 0)),
            Topmost = true,
            ShowInTaskbar = false,
            Cursor = Cursors.Cross,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight,
            Content = new Border
            {
                BorderBrush = Brushes.DeepSkyBlue,
                BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = "Click a window to select it. Press Esc to cancel.",
                    Foreground = Brushes.White,
                    Background = Brushes.Black,
                    Padding = new Thickness(10),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 24, 0, 0)
                }
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

        overlay.MouseDown += async (_, _) =>
        {
            overlay.Hide();
            await Task.Delay(60);

            WindowInfo? picked = null;
            if (NativeMethods.GetCursorPos(out NativeMethods.POINT point))
            {
                IntPtr hit = NativeMethods.WindowFromPoint(point);
                IntPtr root = hit == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT);
                picked = WindowEnumerator.FromHandle(root == IntPtr.Zero ? hit : root);
            }

            completion.TrySetResult(picked);
            overlay.Close();
        };

        overlay.Closed += (_, _) => completion.TrySetResult(null);
        overlay.Show();
        overlay.Activate();
        return completion.Task;
    }
}
