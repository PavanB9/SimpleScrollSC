using ScrollShot.Helpers;
using System.Runtime.InteropServices;

namespace ScrollShot.Core;

public static class ScrollCapture
{
    private const int ScrollNotchesPerStep = 5;
    private const int GenericLineScrollsPerStep = 10;

    public static async Task<List<CapturedFrame>> CaptureAsync(
        CaptureOptions options,
        IProgress<CaptureProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateTarget(options.TargetHandle, out NativeMethods.RECT originalBounds);
        NativeMethods.SetForegroundWindow(options.TargetHandle);

        progress?.Report(new CaptureProgress("Capturing", 2, 0, "Waiting 500ms before capture"));
        await Task.Delay(500, cancellationToken);

        List<CapturedFrame> frames = [];
        CapturedFrame previous = CaptureFrame(options.TargetHandle, originalBounds);
        frames.Add(previous);
        progress?.Report(new CaptureProgress("Capturing", 5, frames.Count, "Captured frame 1"));

        for (int index = 1; index < options.MaxFrames; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateTargetUnchanged(options.TargetHandle, originalBounds);

            ScrollTarget(options.TargetHandle, options.IsBrowser);
            await Task.Delay(options.DelayMilliseconds, cancellationToken);
            ValidateTargetUnchanged(options.TargetHandle, originalBounds);

            CapturedFrame next = CaptureFrame(options.TargetHandle, originalBounds);
            double difference = BitmapHelper.AverageDifference(previous, next);
            if (difference < options.DifferenceThreshold)
            {
                progress?.Report(new CaptureProgress("Capturing", 90, frames.Count, "Bottom reached"));
                break;
            }

            frames.Add(next);
            previous = next;

            double percent = Math.Min(88, 5 + (frames.Count * 85.0 / options.MaxFrames));
            progress?.Report(new CaptureProgress("Capturing", percent, frames.Count, $"Captured frame {frames.Count}"));
        }

        return frames;
    }

    private static void ScrollTarget(IntPtr hwnd, bool isBrowser)
    {
        if (!isBrowser)
        {
            // Traditional Win32 controls often respond to line-down messages without needing focus.
            for (int i = 0; i < GenericLineScrollsPerStep; i++)
            {
                NativeMethods.SendMessage(hwnd, NativeMethods.WM_VSCROLL, new IntPtr(NativeMethods.SB_LINEDOWN), IntPtr.Zero);
            }
        }

        // Browsers, Explorer, and many modern controls ignore WM_VSCROLL, so synthesize wheel input at the center.
        if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
        {
            _ = NativeMethods.SetForegroundWindow(hwnd);
            _ = NativeMethods.SetCursorPos(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2));
        }

        NativeMethods.INPUT[] inputs =
        [
            new()
            {
                type = NativeMethods.INPUT_MOUSE,
                mi = new NativeMethods.MOUSEINPUT
                {
                    mouseData = unchecked((uint)(-NativeMethods.WHEEL_DELTA * ScrollNotchesPerStep)),
                    dwFlags = NativeMethods.MOUSEEVENTF_WHEEL
                }
            }
        ];

        _ = NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static CapturedFrame CaptureFrame(IntPtr hwnd, NativeMethods.RECT bounds)
    {
        bool hdrEnabled = HdrInfo.IsHdrEnabledForRect(bounds);

        CapturedFrame? bitBltFrame = TryCaptureWithBitBlt(bounds, hdrEnabled);
        if (bitBltFrame is not null)
        {
            return bitBltFrame;
        }

        CapturedFrame? printWindowFrame = TryCaptureWithPrintWindow(hwnd, bounds.Width, bounds.Height, hdrEnabled);
        return printWindowFrame ?? throw new InvalidOperationException("Unable to capture the target window.");
    }

    private static CapturedFrame? TryCaptureWithBitBlt(NativeMethods.RECT bounds, bool hdrEnabled)
    {
        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
            oldBitmap = NativeMethods.SelectObject(memoryDc, bitmap);

            bool ok = NativeMethods.BitBlt(
                memoryDc,
                0,
                0,
                bounds.Width,
                bounds.Height,
                screenDc,
                bounds.Left,
                bounds.Top,
                NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);

            return ok ? BitmapHelper.FromHBitmap(bitmap, hdrEnabled) : null;
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
            {
                _ = NativeMethods.SelectObject(memoryDc, oldBitmap);
            }

            if (bitmap != IntPtr.Zero)
            {
                _ = NativeMethods.DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                _ = NativeMethods.DeleteDC(memoryDc);
            }

            _ = NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static CapturedFrame? TryCaptureWithPrintWindow(IntPtr hwnd, int width, int height, bool hdrEnabled)
    {
        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
            oldBitmap = NativeMethods.SelectObject(memoryDc, bitmap);

            bool ok = NativeMethods.PrintWindow(hwnd, memoryDc, NativeMethods.PW_RENDERFULLCONTENT);
            return ok ? BitmapHelper.FromHBitmap(bitmap, hdrEnabled) : null;
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
            {
                _ = NativeMethods.SelectObject(memoryDc, oldBitmap);
            }

            if (bitmap != IntPtr.Zero)
            {
                _ = NativeMethods.DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                _ = NativeMethods.DeleteDC(memoryDc);
            }

            _ = NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static void ValidateTarget(IntPtr hwnd, out NativeMethods.RECT bounds)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            throw new InvalidOperationException("The target window is no longer available.");
        }

        if (NativeMethods.IsIconic(hwnd))
        {
            throw new InvalidOperationException("The target window is minimized. Restore it before capturing.");
        }

        if (!NativeMethods.GetWindowRect(hwnd, out bounds) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("The target window bounds could not be read.");
        }
    }

    private static void ValidateTargetUnchanged(IntPtr hwnd, NativeMethods.RECT originalBounds)
    {
        ValidateTarget(hwnd, out NativeMethods.RECT current);
        if (current.Left != originalBounds.Left ||
            current.Top != originalBounds.Top ||
            current.Width != originalBounds.Width ||
            current.Height != originalBounds.Height)
        {
            throw new InvalidOperationException("The target window moved or resized during capture. Capture was aborted.");
        }
    }
}
