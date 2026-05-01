namespace ScrollShot.Core;

public sealed class WindowInfo
{
    internal WindowInfo(IntPtr handle, string title, string className, string processName, NativeMethods.RECT bounds)
    {
        Handle = handle;
        Title = title;
        ClassName = className;
        ProcessName = processName;
        Bounds = bounds;
        IsBrowser = IsKnownBrowser(className, processName);
    }

    public IntPtr Handle { get; }
    public string Title { get; }
    public string ClassName { get; }
    public string ProcessName { get; }
    internal NativeMethods.RECT Bounds { get; }
    public bool IsBrowser { get; }
    public override string ToString() => IsBrowser ? $"* {Title}" : Title;

    private static bool IsKnownBrowser(string className, string processName)
    {
        string process = processName.ToLowerInvariant();
        string windowClass = className.ToLowerInvariant();

        return process is "chrome" or "msedge" or "firefox" ||
               windowClass.Contains("chrome_widgetwin") ||
               windowClass.Contains("mozillawindowclass");
    }
}
