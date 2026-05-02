namespace ScrollShot.Core;

public sealed record CaptureOptions(
    IntPtr TargetHandle,
    bool IsBrowser,
    int DelayMilliseconds,
    int DifferenceThreshold = 2,
    int MaxFrames = 160);
