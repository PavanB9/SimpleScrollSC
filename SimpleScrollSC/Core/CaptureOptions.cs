namespace ScrollShot.Core;

public enum CaptureMode
{
    AutoUntilBottom = 0,
    ManualStop = 1
}

public sealed record CaptureOptions(
    IntPtr TargetHandle,
    bool IsBrowser,
    int DelayMilliseconds,
    int DifferenceThreshold = 2,
    int MaxFrames = 160,
    CaptureMode CaptureMode = CaptureMode.AutoUntilBottom,
    System.Drawing.Rectangle? CropRect = null,
    long MaxCapturedBytes = 512L * 1024 * 1024,
    long MaxEstimatedOutputPixels = 200L * 1024 * 1024);
