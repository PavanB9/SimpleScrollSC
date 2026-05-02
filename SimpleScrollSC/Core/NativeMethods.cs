using System.Runtime.InteropServices;
using System.Text;

namespace ScrollShot.Core;

internal static partial class NativeMethods
{
    internal const int WM_HOTKEY = 0x0312;
    internal const uint MOD_NOREPEAT = 0x4000;

    internal const int WM_VSCROLL = 0x0115;
    internal const int WM_MOUSEWHEEL = 0x020A;
    internal const int SB_LINEDOWN = 1;
    internal const int WHEEL_DELTA = 120;
    internal const int MK_CONTROL = 0x0008;
    internal const int MK_SHIFT = 0x0004;
    internal const uint INPUT_MOUSE = 0;
    internal const uint MOUSEEVENTF_WHEEL = 0x0800;
    internal const int GA_ROOT = 2;
    internal const int PW_RENDERFULLCONTENT = 0x00000002;
    internal const uint SRCCOPY = 0x00CC0020;
    internal const uint CAPTUREBLT = 0x40000000;

    internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    internal static partial int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr WindowFromPoint(POINT point);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetAncestor(IntPtr hWnd, int flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

    [LibraryImport("gdi32.dll")]
    internal static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [LibraryImport("gdi32.dll")]
    internal static partial IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(IntPtr ho);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct RECT
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct POINT
    {
        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }

        public readonly int X;
        public readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
