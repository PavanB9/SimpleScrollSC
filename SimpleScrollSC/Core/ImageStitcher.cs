namespace ScrollShot.Core;

public static class ImageStitcher
{
    private const int MinimumOverlapRows = 24;
    private const double AcceptableOverlapScore = 10.0;

    public static CapturedFrame Stitch(
        IReadOnlyList<CapturedFrame> frames,
        IProgress<CaptureProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required.", nameof(frames));
        }

        int width = frames[0].Width;
        List<(CapturedFrame Frame, int CropTop)> segments = [(frames[0], 0)];
        int totalHeight = frames[0].Height;

        for (int i = 1; i < frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CapturedFrame previous = frames[i - 1];
            CapturedFrame current = frames[i];

            if (current.Width != width)
            {
                throw new InvalidOperationException("Captured frame widths changed during stitching.");
            }

            int overlap = FindOverlap(previous, current);
            int cropTop = Math.Clamp(overlap, 0, current.Height - 1);

            segments.Add((current, cropTop));
            totalHeight += current.Height - cropTop;
            progress?.Report(new CaptureProgress("Stitching", 90 + (i * 8.0 / frames.Count), i + 1, $"Matched overlap {overlap}px"));
        }

        byte[] output = new byte[width * 4 * totalHeight];
        int destinationY = 0;

        foreach ((CapturedFrame frame, int cropTop) in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int copyHeight = frame.Height - cropTop;
            for (int row = 0; row < copyHeight; row++)
            {
                Buffer.BlockCopy(
                    frame.Pixels,
                    (cropTop + row) * frame.Stride,
                    output,
                    (destinationY + row) * width * 4,
                    width * 4);
            }

            destinationY += copyHeight;
        }

        progress?.Report(new CaptureProgress("Done", 100, frames.Count, "Stitching complete"));
        return new CapturedFrame(width, totalHeight, output);
    }

    private static int FindOverlap(CapturedFrame previous, CapturedFrame current)
    {
        int maxOverlap = Math.Min(Math.Min(previous.Height, current.Height), previous.Height * 3 / 4);
        if (maxOverlap < MinimumOverlapRows)
        {
            return 0;
        }

        int bestRows = 0;
        double bestScore = double.MaxValue;

        for (int rows = maxOverlap; rows >= MinimumOverlapRows; rows--)
        {
            double score = ScoreOverlap(previous, current, rows);
            if (score < bestScore)
            {
                bestScore = score;
                bestRows = rows;
            }

            if (score <= AcceptableOverlapScore)
            {
                return rows;
            }
        }

        return bestScore <= AcceptableOverlapScore * 1.8 ? bestRows : 0;
    }

    private static double ScoreOverlap(CapturedFrame previous, CapturedFrame current, int rows)
    {
        int xStep = Math.Max(1, previous.Width / 96);
        int yStep = Math.Max(1, rows / 48);
        long total = 0;
        long samples = 0;

        for (int y = 0; y < rows; y += yStep)
        {
            int previousOffset = (previous.Height - rows + y) * previous.Stride;
            int currentOffset = y * current.Stride;

            for (int x = 0; x < previous.Width; x += xStep)
            {
                int p = previousOffset + (x * 4);
                int c = currentOffset + (x * 4);
                total += Math.Abs(previous.Pixels[p] - current.Pixels[c]);
                total += Math.Abs(previous.Pixels[p + 1] - current.Pixels[c + 1]);
                total += Math.Abs(previous.Pixels[p + 2] - current.Pixels[c + 2]);
                samples += 3;
            }
        }

        return samples == 0 ? double.MaxValue : total / (double)samples;
    }
}
