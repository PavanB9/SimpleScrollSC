using ScrollShot.Helpers;
using System.Runtime.InteropServices;

namespace ScrollShot.Core;

public static class ScrollCapture
{
    private const int ScrollNotchesPerStep = 1;
    private const int GenericLineScrollsPerStep = 3;
    private const double MinimumMovementDifference = 3.0;

    public static async Task<List<CapturedFrame>> CaptureAsync(
        CaptureOptions options,
        IProgress<CaptureProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateTarget(options.TargetHandle, out NativeMethods.RECT originalBounds);

        progress?.Report(new CaptureProgress("Capturing", 2, 0, "Waiting 500ms before capture"));
        await Task.Delay(500, cancellationToken);

        List<CapturedFrame> frames = [];

        try
        {
            CapturedFrame previous = CaptureFrame(options.TargetHandle, options.IsBrowser, originalBounds, options.CropRect);
            frames.Add(previous);
            progress?.Report(new CaptureProgress("Capturing", 5, frames.Count, "Captured frame 1"));

            int frameWidth = previous.Width;
            int frameHeight = previous.Height;
            long bytesPerFrame = (long)frameWidth * frameHeight * 4;
            double movementThreshold = Math.Max(MinimumMovementDifference, options.DifferenceThreshold + 1);
            int noMovementStreak = 0;

            for (int index = 1; index < options.MaxFrames; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateTargetUnchanged(options.TargetHandle, originalBounds);

                CapturedFrame next = await ScrollAndCaptureNextAsync(
                    options,
                    originalBounds,
                    previous,
                    movementThreshold,
                    progress,
                    cancellationToken);

                double movementDiff = BitmapHelper.AverageDifference(previous, next);
                if (movementDiff < movementThreshold)
                {
                    noMovementStreak++;
                    progress?.Report(new CaptureProgress("Capturing", null, frames.Count, "Scroll did not move; retrying"));

                    if (options.CaptureMode == CaptureMode.AutoUntilBottom)
                    {
                        progress?.Report(new CaptureProgress("Capturing", 90, frames.Count, "Scrolling did not move; stopping"));
                        break;
                    }

                    if (noMovementStreak >= 8)
                    {
                        throw new InvalidOperationException("Scrolling did not move for several steps. Try bringing the target to the foreground or adjusting the scroll speed.");
                    }

                    continue;
                }

                noMovementStreak = 0;

                if (options.CaptureMode == CaptureMode.AutoUntilBottom)
                {
                    if (movementDiff < options.DifferenceThreshold)
                    {
                        progress?.Report(new CaptureProgress("Capturing", 90, frames.Count, "Bottom reached"));
                        break;
                    }

                    previous = next;
                }

                frames.Add(next);

                // Safety caps: prevent runaway RAM and stitched output overflows.
                long estimatedCapturedBytes = bytesPerFrame * frames.Count;
                long estimatedOutputPixels = (long)frameWidth * frameHeight * frames.Count;
                if (estimatedCapturedBytes > options.MaxCapturedBytes || estimatedOutputPixels > options.MaxEstimatedOutputPixels)
                {
                    string reason = estimatedCapturedBytes > options.MaxCapturedBytes
                        ? "Safety cap reached (RAM)"
                        : "Safety cap reached (max output)";

                    progress?.Report(new CaptureProgress("Capturing", 90, frames.Count, reason));
                    if (options.CaptureMode == CaptureMode.ManualStop)
                    {
                        break;
                    }

                    throw new InvalidOperationException(reason);
                }

                double percent = Math.Min(88, 5 + (frames.Count * 85.0 / options.MaxFrames));
                progress?.Report(new CaptureProgress("Capturing", percent, frames.Count, $"Captured frame {frames.Count}"));
            }
        }
        catch (OperationCanceledException)
        {
            if (options.CaptureMode != CaptureMode.ManualStop)
            {
                throw;
            }

            progress?.Report(new CaptureProgress("Capturing", 90, frames.Count, "Stopped"));
        }

        return frames;
    }

    private static async Task<CapturedFrame> ScrollAndCaptureNextAsync(
        CaptureOptions options,
        NativeMethods.RECT originalBounds,
        CapturedFrame previous,
        double movementThreshold,
        IProgress<CaptureProgress>? progress,
        CancellationToken cancellationToken)
    {
        // First attempt: background scroll (no focus stealing).
        ScrollTarget(options.TargetHandle, options.IsBrowser, forceForegroundFallback: false);
        await Task.Delay(options.DelayMilliseconds, cancellationToken);
        ValidateTargetUnchanged(options.TargetHandle, originalBounds);

        CapturedFrame next = CaptureFrame(options.TargetHandle, options.IsBrowser, originalBounds, options.CropRect);
        double diff = BitmapHelper.AverageDifference(previous, next);
        if (diff >= movementThreshold)
        {
            return next;
        }

        // Settle attempt: sometimes the scroll animation is still in-flight.
        int settleDelay = Math.Clamp(options.DelayMilliseconds / 2, 60, 260);
        progress?.Report(new CaptureProgress("Capturing", null, 0, $"Settling {settleDelay}ms"));
        await Task.Delay(settleDelay, cancellationToken);
        ValidateTargetUnchanged(options.TargetHandle, originalBounds);

        CapturedFrame settled = CaptureFrame(options.TargetHandle, options.IsBrowser, originalBounds, options.CropRect);
        double settledDiff = BitmapHelper.AverageDifference(previous, settled);
        if (settledDiff > diff)
        {
            next = settled;
            diff = settledDiff;
        }

        if (diff >= movementThreshold)
        {
            return next;
        }

        // Fallback: allow focus stealing + SendInput when window-directed scrolling is ignored.
        ScrollTarget(options.TargetHandle, options.IsBrowser, forceForegroundFallback: true);
        await Task.Delay(options.DelayMilliseconds, cancellationToken);
        ValidateTargetUnchanged(options.TargetHandle, originalBounds);

        CapturedFrame fallback = CaptureFrame(options.TargetHandle, options.IsBrowser, originalBounds, options.CropRect);
        double fallbackDiff = BitmapHelper.AverageDifference(previous, fallback);
        return fallbackDiff > diff ? fallback : next;
    }

    private static void ScrollTarget(IntPtr hwnd, bool isBrowser, bool forceForegroundFallback)
    {
        if (!isBrowser)
        {
            // Traditional Win32 controls often respond to line-down messages without needing focus.
            for (int i = 0; i < GenericLineScrollsPerStep; i++)
            {
                NativeMethods.SendMessage(hwnd, NativeMethods.WM_VSCROLL, new IntPtr(NativeMethods.SB_LINEDOWN), IntPtr.Zero);
            }

            return;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
        {
            return;
        }

        int x = rect.Left + (rect.Width / 2);
        int y = rect.Top + (rect.Height / 2);
        int delta = -NativeMethods.WHEEL_DELTA * ScrollNotchesPerStep;

        if (!forceForegroundFallback)
        {
            // Background wheel message: lParam contains screen coordinates.
            IntPtr wParam = MakeWParam(0, (short)delta);
            IntPtr lParam = MakeLParam(x, y);
            _ = NativeMethods.SendMessage(hwnd, NativeMethods.WM_MOUSEWHEEL, wParam, lParam);
            return;
        }

        // Fallback: synthesize wheel input. IMPORTANT: restore cursor immediately.
        _ = NativeMethods.SetForegroundWindow(hwnd);

        NativeMethods.POINT? restoreCursor = null;
        if (NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            restoreCursor = cursor;
        }

        _ = NativeMethods.SetCursorPos(x, y);

        NativeMethods.INPUT[] inputs =
        [
            new()
            {
                type = NativeMethods.INPUT_MOUSE,
                mi = new NativeMethods.MOUSEINPUT
                {
                    mouseData = unchecked((uint)delta),
                    dwFlags = NativeMethods.MOUSEEVENTF_WHEEL
                }
            }
        ];

        _ = NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());

        if (restoreCursor is not null)
        {
            _ = NativeMethods.SetCursorPos(restoreCursor.Value.X, restoreCursor.Value.Y);
        }
    }

    private static IntPtr MakeWParam(short low, short high)
    {
        int value = (high << 16) | (ushort)low;
        return new IntPtr(value);
    }

    private static IntPtr MakeLParam(int x, int y)
    {
        int value = (y << 16) | (x & 0xFFFF);
        return new IntPtr(value);
    }

    private static CapturedFrame CaptureFrame(IntPtr hwnd, bool isBrowser, NativeMethods.RECT bounds, System.Drawing.Rectangle? cropRect)
    {
        bool hdrEnabled = HdrInfo.IsHdrEnabledForRect(bounds);

        CapturedFrame? fullFrame;
        if (!isBrowser)
        {
            CapturedFrame? print = TryCaptureWithPrintWindow(hwnd, bounds.Width, bounds.Height, hdrEnabled);
            if (print is not null && !LooksLikeBlankPrintWindow(print))
            {
                fullFrame = print;
            }
            else
            {
                fullFrame = TryCaptureWithBitBlt(bounds, hdrEnabled);
            }
        }
        else
        {
            CapturedFrame? bitBltFrame = TryCaptureWithBitBlt(bounds, hdrEnabled);
            fullFrame = bitBltFrame ?? TryCaptureWithPrintWindow(hwnd, bounds.Width, bounds.Height, hdrEnabled);
        }

        if (fullFrame is null)
        {
            throw new InvalidOperationException("Unable to capture the target window.");
        }

        if (cropRect is null)
        {
            return fullFrame;
        }

        return BitmapHelper.Crop(fullFrame, cropRect.Value);
    }

    private static bool LooksLikeBlankPrintWindow(CapturedFrame frame)
    {
        // Common failure mode: PrintWindow returns a uniform black frame for GPU/modern surfaces.
        // Reject only when it's near-uniform and very dark.
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return true;
        }

        int xStep = Math.Max(1, frame.Width / 16);
        int yStep = Math.Max(1, frame.Height / 16);
        byte min = 255;
        byte max = 0;
        long sum = 0;
        int count = 0;

        for (int y = 0; y < frame.Height; y += yStep)
        {
            int row = y * frame.Stride;
            for (int x = 0; x < frame.Width; x += xStep)
            {
                int i = row + (x * 4);
                byte b = frame.Pixels[i];
                byte g = frame.Pixels[i + 1];
                byte r = frame.Pixels[i + 2];
                byte lum = (byte)((r + g + b) / 3);
                min = Math.Min(min, lum);
                max = Math.Max(max, lum);
                sum += lum;
                count++;
            }
        }

        if (count == 0)
        {
            return true;
        }

        double avg = sum / (double)count;
        bool nearUniform = (max - min) <= 2;
        bool veryDark = avg <= 6;
        return nearUniform && veryDark;
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
