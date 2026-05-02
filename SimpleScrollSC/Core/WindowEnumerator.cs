using System.Diagnostics;
using System.Text;

namespace ScrollShot.Core;

public static class WindowEnumerator
{
    public static IReadOnlyList<WindowInfo> GetOpenWindows()
    {
        List<WindowInfo> windows = [];

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            {
                return true;
            }

            int titleLength = NativeMethods.GetWindowTextLength(hwnd);
            if (titleLength == 0)
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect) ||
                rect.Width < 80 ||
                rect.Height < 80)
            {
                return true;
            }

            string title = GetWindowText(hwnd, titleLength + 1);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            windows.Add(new WindowInfo(hwnd, title.Trim(), GetClassName(hwnd), GetProcessName(hwnd), rect));
            return true;
        }, IntPtr.Zero);

        return windows
            .OrderByDescending(window => window.IsBrowser)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static WindowInfo? FromHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero ||
            !NativeMethods.IsWindow(handle) ||
            !NativeMethods.GetWindowRect(handle, out NativeMethods.RECT rect))
        {
            return null;
        }

        int titleLength = NativeMethods.GetWindowTextLength(handle);
        string title = titleLength > 0 ? GetWindowText(handle, titleLength + 1) : "(Untitled window)";
        return new WindowInfo(handle, title.Trim(), GetClassName(handle), GetProcessName(handle), rect);
    }

    private static string GetWindowText(IntPtr hwnd, int maxCount)
    {
        StringBuilder builder = new(maxCount);
        _ = NativeMethods.GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClassName(IntPtr hwnd)
    {
        StringBuilder builder = new(256);
        _ = NativeMethods.GetClassName(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetProcessName(IntPtr hwnd)
    {
        try
        {
            _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
