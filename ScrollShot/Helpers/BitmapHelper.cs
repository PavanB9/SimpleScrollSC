using ScrollShot.Core;
using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScrollShot.Helpers;

public enum ImageFormat
{
    Png,
    Jpeg
}

public static class BitmapHelper
{
    public static CapturedFrame FromHBitmap(IntPtr hBitmap)
    {
        BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap,
            IntPtr.Zero,
            System.Windows.Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());

        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int stride = bgra.PixelWidth * 4;
        byte[] pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        return new CapturedFrame(bgra.PixelWidth, bgra.PixelHeight, pixels);
    }

    public static double AverageDifference(CapturedFrame left, CapturedFrame right)
    {
        if (left.Width != right.Width || left.Height != right.Height)
        {
            return double.MaxValue;
        }

        int xStep = Math.Max(1, left.Width / 128);
        int yStep = Math.Max(1, left.Height / 128);
        long total = 0;
        long samples = 0;

        for (int y = 0; y < left.Height; y += yStep)
        {
            int row = y * left.Stride;
            for (int x = 0; x < left.Width; x += xStep)
            {
                int index = row + (x * 4);
                total += Math.Abs(left.Pixels[index] - right.Pixels[index]);
                total += Math.Abs(left.Pixels[index + 1] - right.Pixels[index + 1]);
                total += Math.Abs(left.Pixels[index + 2] - right.Pixels[index + 2]);
                samples += 3;
            }
        }

        return samples == 0 ? double.MaxValue : total / (double)samples;
    }

    public static void Save(CapturedFrame frame, string path, ImageFormat format, int jpegQuality)
    {
        BitmapEncoder encoder = format == ImageFormat.Jpeg
            ? new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) }
            : new PngBitmapEncoder();

        encoder.Frames.Add(BitmapFrame.Create(frame.ToBitmapSource()));

        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }
}
