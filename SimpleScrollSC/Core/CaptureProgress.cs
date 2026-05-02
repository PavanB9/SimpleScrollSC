namespace ScrollShot.Core;

public sealed record CaptureProgress(
    string Status,
    double? ProgressPercent = null,
    int FrameCount = 0,
    string? Message = null);
